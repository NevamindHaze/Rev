"""Two-way sync between Anki and the Rev apps (Rev on iPhone, and optionally Rev for Windows).

The apps never talk to each other directly. They share a folder (iCloud Drive / "Rev Sync"):
  anki.json     - written by this add-on: every Anki card, its schedule, and recent reviews
  rev.json      - written by Rev on iPhone: the same for its cards
  windows.json  - written by Rev for Windows (only if you use it): the same for its cards
  media/        - images used by cards
Each Rev app reads anki.json and writes its own file; this add-on reads the Rev apps' files, applies
whatever is newer, then writes anki.json. So Anki is the hub: a change in one Rev app reaches the other
through Anki. With only rev.json in the folder, everything works exactly as it did before Windows.

A card is identified by "<note guid>:<card ordinal>", which is the same in both apps.
For each card, content (text, tags, deck) and schedule are merged separately:
whichever side changed it most recently wins. Review history from both sides is kept.

This module only uses the collection object, so it can be tested outside Anki.
"""

import html as htmllib
import json
import os
import re
import shutil
import time

FORMAT = 1
REVIEW_DAYS = 60
TOMBSTONE_DAYS = 60
# The Rev apps' files, and where the add-on remembers which version of each it has already applied.
PEERS = (
    ("rev.json", "lastRevWrittenAt"),
    ("windows.json", "lastWindowsWrittenAt"),
)
# Rev's own card kinds and the Anki note types used for cards created in Rev.
REV_MODELS = {
    "basic": "Rev Basic",
    "reversed": "Rev Reversed",
    "cloze": "Rev Cloze",
    "typeIn": "Rev Type-in",
}
CSS = ".card { font-family: arial; font-size: 20px; text-align: center; color: black; background-color: white; }\n.cloze { font-weight: bold; color: blue; }\n"


# ---------------------------------------------------------------- files

def read_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def write_json_atomic(path, data):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
    os.replace(tmp, path)


# ---------------------------------------------------------------- text

def html_from_plain(text):
    """Rev's plain text -> simple HTML for an Anki field."""
    s = htmllib.escape(text or "", quote=False).replace("\n", "<br>")
    return re.sub(r"\[img:([^\]]+)\]", lambda m: '<img src="%s">' % m.group(1), s)


def clean_rendered(html, is_answer):
    """Anki's rendered card HTML -> the part Rev needs (Rev turns it into plain text)."""
    html = re.sub(r"(?is)<style.*?</style>|<script.*?</script>", "", html)
    if is_answer:
        # The answer side usually repeats the question first; keep only what follows the line.
        html = re.split(r'(?i)<hr[^>]*id="?answer"?[^>]*>', html, maxsplit=1)[-1]
        # Revealed cloze answers are marked [like this], as Rev does.
        html = re.sub(r'(?is)<span class="?cloze"?(?=[\s>])[^>]*>(.*?)</span>', r"[\1]", html)
    html = re.sub(r'(?is)<code id="?typeans"?>.*?</code>', "", html)
    html = re.sub(r"(?is)<input[^>]*>", "", html)
    return html.strip()


def image_names(html):
    names = re.findall(r'(?i)<img[^>]*?src="([^"]+)"', html or "")
    names += re.findall(r"\[img:([^\]]+)\]", html or "")
    return {os.path.basename(n) for n in names if n and not n.startswith(("http:", "https:", "data:"))}


def kind_of(model):
    """Which Rev card kind an Anki note type matches; "other" means Rev can't edit its text."""
    for kind, name in REV_MODELS.items():
        if model["name"] == name:
            return kind
    fields = [f["name"] for f in model["flds"]]
    templates = model["tmpls"]
    if model["type"] == 1:
        return "cloze" if "Text" in fields else "other"
    if fields == ["Front", "Back"]:
        if len(templates) == 1:
            return "typeIn" if "{{type:" in templates[0]["qfmt"] else "basic"
        if len(templates) == 2:
            return "reversed"
    return "other"


# ---------------------------------------------------------------- schedule

def day_info(col):
    today = col.sched.today
    day_start = col.sched.day_cutoff - 86400
    return today, day_start


def schedule_of(card, today, day_start):
    due_raw = card.odue if card.odid else card.due
    if card.type == 0:
        phase, due = "new", 0  # new cards have no date; a fixed value keeps the file unchanged
    elif card.type == 2:
        phase, due = "review", day_start + (due_raw - today) * 86400
    else:
        phase = "learning" if card.type == 1 else "relearning"
        due = due_raw if due_raw > 1_000_000_000 else day_start + (due_raw - today) * 86400
    memory = getattr(card, "memory_state", None)
    return {
        "phase": phase,
        "due": float(due),
        "interval": float(max(card.ivl, 0)),
        "ease": card.factor / 1000 if card.factor else 2.5,
        "reps": card.reps,
        "lapses": card.lapses,
        "stability": memory.stability if memory else None,
        "difficulty": memory.difficulty if memory else None,
        "suspended": card.queue == -1,
    }


def apply_schedule(card, s, today, day_start):
    if card.odid:
        return False  # in a temporary filtered deck; leave it alone
    phase = s["phase"]
    if phase == "review":
        card.type = 2
        card.queue = 2
        card.due = today + round((s["due"] - day_start) / 86400)
        card.ivl = max(1, round(s["interval"]))
        card.left = 0
    elif phase in ("learning", "relearning"):
        card.type = 1 if phase == "learning" else 3
        card.queue = 1
        card.due = int(s["due"])
        card.left = 1
        if phase == "relearning":
            card.ivl = max(1, round(s["interval"]))
    elif card.type != 0:  # "new" again (e.g. reset in Rev)
        card.type = 0
        card.queue = 0
        card.ivl = 0
        card.due = 0
    if phase != "new":
        card.factor = int(round(s["ease"] * 1000))
    card.reps = s["reps"]
    card.lapses = s["lapses"]
    if s.get("stability") and s.get("difficulty"):
        try:
            from anki.cards import FSRSMemoryState
            card.memory_state = FSRSMemoryState(stability=s["stability"], difficulty=s["difficulty"])
        except Exception:
            pass
    if s.get("suspended"):
        card.queue = -1
    elif card.queue == -1:
        card.queue = {0: 0, 2: 2}.get(card.type, 1)
    return True


# ---------------------------------------------------------------- note types for cards made in Rev

def ensure_model(col, kind):
    name = REV_MODELS.get(kind, REV_MODELS["basic"])
    model = col.models.by_name(name)
    if model:
        return model
    mm = col.models
    model = mm.new(name)
    if kind == "cloze":
        model["type"] = 1
        fields = ["Text", "Back Extra"]
        templates = [("Cloze", "{{cloze:Text}}", "{{cloze:Text}}<br>\n{{Back Extra}}")]
    else:
        fields = ["Front", "Back"]
        answer = "{{FrontSide}}\n\n<hr id=answer>\n\n{{Back}}"
        if kind == "typeIn":
            templates = [("Card 1", "{{Front}}\n\n{{type:Back}}", "{{Front}}\n\n<hr id=answer>\n\n{{type:Back}}")]
        elif kind == "reversed":
            templates = [("Card 1", "{{Front}}", answer),
                         ("Card 2", "{{Back}}", "{{FrontSide}}\n\n<hr id=answer>\n\n{{Front}}")]
        else:
            templates = [("Card 1", "{{Front}}", answer)]
    for field in fields:
        mm.add_field(model, mm.new_field(field))
    for tname, qfmt, afmt in templates:
        t = mm.new_template(tname)
        t["qfmt"] = qfmt
        t["afmt"] = afmt
        mm.add_template(model, t)
    model["css"] = CSS
    mm.add(model)
    return mm.by_name(name)


# ---------------------------------------------------------------- when a card really changed

def changed_at(applied, key, mod):
    """When this part of a card (text or schedule) really last changed.

    Applying a change from a Rev app stamps the Anki note or card with the time of the sync, not the time
    the change was made. We remember both ([Anki's mod after applying, the Rev app's own time]), so while
    nothing else has touched it since, the Rev app's own time is the true one.
    """
    echo = applied.get(key)
    return echo[1] if echo and echo[0] == mod else mod


# ---------------------------------------------------------------- export (Anki -> anki.json)

def _all_cards(col):
    """(card id, note guid, ord, note id) for every card."""
    return col.db.all("select c.id, n.guid, c.ord, n.id from cards c join notes n on c.nid = n.id")


def export_anki(col, state):
    now = time.time()
    today, day_start = day_info(col)
    applied_content = state.setdefault("appliedContent", {})
    applied_sched = state.setdefault("appliedSched", {})
    render_cache = state.setdefault("render", {})
    last_review = {cid: rid for cid, rid in col.db.all("select cid, max(id) from revlog group by cid")}

    cards_out = []
    key_of_cid = {}
    notes = {}
    models = {}
    for cid, guid, ord_, nid in _all_cards(col):
        card = col.get_card(cid)
        note = notes.get(nid)
        if note is None:
            note = notes[nid] = col.get_note(nid)
        model = models.get(note.mid)
        if model is None:
            model = models[note.mid] = col.models.get(note.mid)
        kind = kind_of(model)
        key = "%s:%d" % (guid, ord_)
        key_of_cid[cid] = key

        stamp = "%s:%s" % (note.mod, model["mod"])
        cached = render_cache.get(key)
        if not cached or cached["stamp"] != stamp:
            cached = render_cache[key] = {
                "stamp": stamp,
                "front": clean_rendered(card.question(reload=True), False),
                "back": clean_rendered(card.answer(), True),
            }

        # If our last change came from a Rev app, report that app's own timestamp so it doesn't re-apply it.
        edited_at = changed_at(applied_content, key, note.mod)
        sched_at = changed_at(applied_sched, key, card.mod)

        entry = {
            "key": key,
            "noteId": guid,
            "ord": ord_,
            "deck": col.decks.name(card.odid or card.did),
            "kind": kind,
            "front": cached["front"],
            "back": cached["back"],
            "fields": None,
            "tags": [t for t in note.tags if t.lower() != "leech"],
            "leech": any(t.lower() == "leech" for t in note.tags),
            "editedAt": float(edited_at),
            "sched": schedule_of(card, today, day_start),
            "schedAt": float(sched_at),
            "lastReview": last_review[cid] / 1000 if cid in last_review else None,
        }
        if kind == "cloze":
            entry["clozeSource"] = note["Text"]
            entry["clozeExtra"] = note["Back Extra"] if "Back Extra" in note.keys() else ""
        cards_out.append(entry)

    # Cards that disappeared since last time were deleted in Anki.
    current = set(key_of_cid.values())
    tombstones = state.setdefault("tombstones", {})
    for key in state.get("exportedKeys", []):
        if key not in current and key not in tombstones:
            tombstones[key] = now
    for key in list(tombstones):
        if key in current or tombstones[key] < now - TOMBSTONE_DAYS * 86400:
            del tombstones[key]
    state["exportedKeys"] = sorted(current)

    cutoff = int((now - REVIEW_DAYS * 86400) * 1000)
    reviews = [
        {"key": key_of_cid[cid], "at": rid, "rating": ease, "interval": float(max(ivl, 0))}
        for rid, cid, ease, ivl in col.db.all(
            "select id, cid, ease, ivl from revlog where id > ? and ease > 0", cutoff)
        if cid in key_of_cid
    ]

    return {
        "format": FORMAT,
        "writer": "anki",
        "writtenAt": now,
        "cards": cards_out,
        "deleted": [{"key": k, "at": v} for k, v in sorted(tombstones.items())],
        "reviews": reviews,
    }


# ---------------------------------------------------------------- import (rev.json / windows.json -> Anki)

def import_rev(col, rev, state):
    report = {"added": 0, "updated": 0, "rescheduled": 0, "deleted": 0, "reviews": 0}
    today, day_start = day_info(col)
    applied_content = state.setdefault("appliedContent", {})
    applied_sched = state.setdefault("appliedSched", {})
    exported = set(state.get("exportedKeys", []))
    anki_tombstones = state.get("tombstones", {})   # cards gone from Anki in the last 60 days, and when

    by_key = {}
    cards_of_note = {}
    nid_of_guid = {}
    for cid, guid, ord_, nid in _all_cards(col):
        by_key["%s:%d" % (guid, ord_)] = cid
        cards_of_note.setdefault(nid, []).append(cid)
        nid_of_guid[guid] = nid

    # 1. Deletions made in Rev: remove a note once all of its cards were deleted there,
    #    unless it was changed (in Anki, or by another Rev app) after the deletion.
    tombstones = {d["key"]: d["at"] for d in rev.get("deleted", [])}
    doomed = {}
    for key, at in tombstones.items():
        cid = by_key.get(key)
        if not cid:
            continue
        card = col.get_card(cid)
        if changed_at(applied_sched, key, card.mod) < at and changed_at(applied_content, key, card.note().mod) < at:
            doomed.setdefault(card.nid, set()).add(cid)
    remove = [nid for nid, cids in doomed.items() if cids == set(cards_of_note.get(nid, []))]
    if remove:
        col.remove_notes(remove)
        report["deleted"] += len(remove)

    # 2. Changes and new cards.
    new_notes = {}
    for rc in rev.get("cards", []):
        key = rc["key"]
        cid = by_key.get(key)
        if cid and any(cid in cards_of_note.get(nid, []) for nid in remove):
            continue
        if not cid:
            if key in exported or rc["noteId"] in nid_of_guid:
                continue  # deleted in Anki (or a card Anki can't make); Anki's deletion wins
            # Deleted recently (in Anki, or by a Rev app): an app that hasn't heard yet may still list it.
            # It only comes back if that app changed it after it was deleted.
            newest = max(rc["editedAt"], rc["schedAt"])
            if key in tombstones or newest <= anki_tombstones.get(key, -1):
                continue
            new_notes.setdefault(rc["noteId"], []).append(rc)
            continue

        card = col.get_card(cid)
        note = card.note()
        # Skip Rev changes we already applied (their time is remembered), even if clocks differ a little.
        content_done = applied_content.get(key, [0, 0])[1] >= rc["editedAt"]
        sched_done = applied_sched.get(key, [0, 0])[1] >= rc["schedAt"]
        # Compare with when the card really last changed (see changed_at), so the newest change wins
        # even when it reaches Anki after an older change from another Rev app was applied.
        if rc["editedAt"] > changed_at(applied_content, key, note.mod) + 1 and not content_done:
            changed = False
            for name, value in (rc.get("fields") or {}).items():
                if name in note.keys() and note[name] != value:
                    note[name] = value
                    changed = True
            tags = list(rc.get("tags", [])) + (["leech"] if rc.get("leech") else [])
            if {t.lower() for t in tags} != {t.lower() for t in note.tags}:
                note.tags = tags
                changed = True
            if changed:
                col.update_note(note)
                report["updated"] += 1
            if not card.odid and rc.get("deck"):
                did = col.decks.id(rc["deck"])
                if did != card.did:
                    col.set_deck([cid], did)
            applied_content[key] = [col.get_note(note.id).mod, rc["editedAt"]]
            card = col.get_card(cid)

        if rc["schedAt"] > changed_at(applied_sched, key, card.mod) + 1 and not sched_done:
            if apply_schedule(card, rc["sched"], today, day_start):
                col.update_card(card)
                report["rescheduled"] += 1
                applied_sched[key] = [col.get_card(cid).mod, rc["schedAt"]]

    # 3. Notes created in Rev.
    for note_id, rcs in new_notes.items():
        rcs.sort(key=lambda r: r["ord"])
        first = rcs[0]
        kind = first["kind"] if first["kind"] in REV_MODELS else "basic"
        model = ensure_model(col, kind)
        note = col.new_note(model)
        note.guid = note_id
        fields = first.get("fields") or {"Front": html_from_plain(first["front"]), "Back": html_from_plain(first["back"])}
        for name, value in fields.items():
            if name in note.keys():
                note[name] = value
        note.tags = list(first.get("tags", [])) + (["leech"] if first.get("leech") else [])
        did = col.decks.id(first.get("deck") or "Default")
        col.add_note(note, did)
        report["added"] += 1
        note = col.get_note(note.id)
        for card in note.cards():
            rc = next((r for r in rcs if r["ord"] == card.ord), None)
            key = "%s:%d" % (note.guid, card.ord)
            by_key[key] = card.id
            applied_content[key] = [note.mod, rc["editedAt"] if rc else first["editedAt"]]
            if rc and apply_schedule(card, rc["sched"], today, day_start):
                col.update_card(card)
                applied_sched[key] = [col.get_card(card.id).mod, rc["schedAt"]]

    # 4. Reviews done in Rev (kept for Anki's stats; duplicates are skipped).
    cutoff = int((time.time() - REVIEW_DAYS * 86400) * 1000)
    existing = set(col.db.list("select id from revlog where id > ?", cutoff))
    for r in rev.get("reviews", []):
        cid = by_key.get(r["key"])
        if not cid or r["at"] in existing:
            continue
        ivl = int(round(r["interval"])) if r["interval"] >= 1 else -600
        col.db.execute(
            "insert or ignore into revlog (id, cid, usn, ease, ivl, lastIvl, factor, time, type) "
            "values (?, ?, -1, ?, ?, 0, 2500, 0, 1)", r["at"], cid, r["rating"], ivl)
        existing.add(r["at"])
        report["reviews"] += 1
    return report


# ---------------------------------------------------------------- media

def copy_media_to_anki(col, folder, rev):
    media_dir = col.media.dir()
    copied = 0
    for rc in rev.get("cards", []):
        text = " ".join(list((rc.get("fields") or {}).values()) + [rc.get("front", ""), rc.get("back", "")])
        for name in image_names(text):
            src = os.path.join(folder, "media", name)
            dst = os.path.join(media_dir, name)
            if os.path.exists(src) and not os.path.exists(dst):
                shutil.copy2(src, dst)
                copied += 1
    return copied


def copy_media_from_anki(col, folder, snapshot):
    media_dir = col.media.dir()
    os.makedirs(os.path.join(folder, "media"), exist_ok=True)
    for card in snapshot["cards"]:
        for name in image_names(card["front"] + " " + card["back"]):
            src = os.path.join(media_dir, name)
            dst = os.path.join(folder, "media", name)
            if os.path.exists(src) and not os.path.exists(dst):
                shutil.copy2(src, dst)


# ---------------------------------------------------------------- one full sync

def _content_signature(snapshot):
    body = dict(snapshot)
    body["writtenAt"] = 0
    return json.dumps(body, sort_keys=True, ensure_ascii=False)


def merge_peers(snapshots):
    """Several Rev apps' files as one, so their changes are applied to Anki together.

    For each card the newest text (editedAt) and the newest schedule (schedAt) win, possibly from different
    apps. A deletion in one app loses to a newer change of the same card in another app. Reviews from all
    apps are kept once each. Applying them together matters: applying one app's file first would stamp the
    cards with the time of the sync, and the other app's newer change would then look older.
    """
    content, schedule, latest = {}, {}, {}
    for snapshot in snapshots:
        for rc in snapshot.get("cards", []):
            key = rc["key"]
            if key not in content or rc["editedAt"] > content[key]["editedAt"]:
                content[key] = rc
            if key not in schedule or rc["schedAt"] > schedule[key]["schedAt"]:
                schedule[key] = rc
            latest[key] = max(latest.get(key, 0), rc["editedAt"], rc["schedAt"])
    cards = [dict(rc, sched=schedule[key]["sched"], schedAt=schedule[key]["schedAt"]) for key, rc in content.items()]

    deleted = {}
    for snapshot in snapshots:
        for d in snapshot.get("deleted", []):
            deleted[d["key"]] = max(deleted.get(d["key"], 0), d["at"])
    tombstones = [{"key": key, "at": at} for key, at in sorted(deleted.items()) if latest.get(key, 0) <= at]

    seen, reviews = set(), []
    for snapshot in snapshots:
        for r in snapshot.get("reviews", []):
            if (r["key"], r["at"]) not in seen:
                seen.add((r["key"], r["at"]))
                reviews.append(r)

    return {
        "format": FORMAT,
        "writer": "+".join(s.get("writer", "?") for s in snapshots),
        "writtenAt": max(s.get("writtenAt") or 0 for s in snapshots),
        "cards": cards,
        "deleted": tombstones,
        "reviews": reviews,
    }


def sync(col, folder, state):
    """Read the Rev apps' files, apply them, then write Anki's file. Returns a short report."""
    os.makedirs(os.path.join(folder, "media"), exist_ok=True)
    report = {}
    peers = []
    for name, mark in PEERS:
        snapshot = read_json(os.path.join(folder, name))
        if snapshot and snapshot.get("format") == FORMAT:
            peers.append((snapshot, mark))
    # Only when a Rev app wrote something new. With just rev.json, this is exactly the original sync.
    if any(snapshot.get("writtenAt") != state.get(mark) for snapshot, mark in peers):
        incoming = peers[0][0] if len(peers) == 1 else merge_peers([s for s, _ in peers])
        report = import_rev(col, incoming, state)
        for snapshot, mark in peers:
            copy_media_to_anki(col, folder, snapshot)
            state[mark] = snapshot.get("writtenAt")

    snapshot = export_anki(col, state)
    signature = _content_signature(snapshot)
    import hashlib
    digest = hashlib.sha256(signature.encode("utf-8")).hexdigest()
    if digest != state.get("lastExportDigest") or not os.path.exists(os.path.join(folder, "anki.json")):
        write_json_atomic(os.path.join(folder, "anki.json"), snapshot)
        state["lastExportDigest"] = digest
    copy_media_from_anki(col, folder, snapshot)
    return report
