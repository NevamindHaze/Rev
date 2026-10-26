"""End-to-end test of the sync logic on a COPY of an Anki collection.

Usage: python test_core.py <path to collection.anki2>
Needs the `anki` package (pip install anki). Never touches the original collection.
"""

import json
import os
import shutil
import sys
import tempfile
import time
import uuid

# Import core.py on its own (the package's __init__ needs the Anki app, which tests don't have).
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "rev_sync"))
from anki.collection import Collection  # noqa: E402

import core  # noqa: E402

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


def main(source):
    work = tempfile.mkdtemp(prefix="revsync-")
    shutil.copy(source, os.path.join(work, "collection.anki2"))
    folder = os.path.join(work, "Rev Sync")
    col = Collection(os.path.join(work, "collection.anki2"))
    state = {}

    print("1. First export")
    core.sync(col, folder, state)
    anki = json.load(open(os.path.join(folder, "anki.json"), encoding="utf-8"))
    check(len(anki["cards"]) == col.card_count(), "every card exported (%d)" % len(anki["cards"]))
    size = os.path.getsize(os.path.join(folder, "anki.json"))
    print("     anki.json is %.0f KB" % (size / 1024))
    kinds = {}
    for c in anki["cards"]:
        kinds[c["kind"]] = kinds.get(c["kind"], 0) + 1
    print("     kinds:", kinds)
    sample = next(c for c in anki["cards"] if c["sched"]["phase"] == "review")
    print("     sample:", json.dumps({k: sample[k] for k in ("key", "deck", "kind", "front", "back")}, ensure_ascii=False)[:300])

    print("2. Unchanged second run doesn't rewrite the file")
    before = os.path.getmtime(os.path.join(folder, "anki.json"))
    time.sleep(1.1)
    core.sync(col, folder, state)
    check(os.path.getmtime(os.path.join(folder, "anki.json")) == before, "anki.json not rewritten")

    print("3. Apply changes made in Rev")
    now = time.time() + 5
    rev_cards = [dict(c, front="", back="") for c in anki["cards"]]
    by_key = {c["key"]: c for c in rev_cards}
    basic = [c for c in rev_cards if c["kind"] == "basic"]
    reviewed = next(c for c in basic[2:] if c["sched"]["phase"] == "review")
    reviewed["sched"] = dict(reviewed["sched"], due=now + 10 * 86400, interval=10.0, reps=reviewed["sched"]["reps"] + 1)
    reviewed["schedAt"] = now
    edited = basic[0]
    edited["fields"] = {"Front": "Edited in Rev", "Back": "New answer"}
    edited["tags"] = ["from_rev"]
    edited["editedAt"] = now
    deleted = basic[1]
    del by_key[deleted["key"]]
    new_id = str(uuid.uuid4())
    cloze_id = str(uuid.uuid4())
    sched_new = {"phase": "new", "due": 0, "interval": 0, "ease": 2.5, "reps": 0, "lapses": 0,
                 "stability": None, "difficulty": None, "suspended": False}
    sched_learn = dict(sched_new, phase="learning", due=now + 600, reps=1)
    rev_new = [
        {"key": new_id + ":0", "noteId": new_id, "ord": 0, "deck": "Rev test", "kind": "basic",
         "front": "Q", "back": "A", "fields": {"Front": "Q", "Back": "A"}, "tags": ["t1"], "leech": False,
         "editedAt": now, "sched": sched_learn, "schedAt": now},
        {"key": cloze_id + ":0", "noteId": cloze_id, "ord": 0, "deck": "Rev test", "kind": "cloze",
         "front": "", "back": "", "fields": {"Text": "{{c1::Dogs}} have {{c2::42}} teeth", "Back Extra": ""},
         "tags": [], "leech": True, "editedAt": now, "sched": sched_new, "schedAt": now},
        {"key": cloze_id + ":1", "noteId": cloze_id, "ord": 1, "deck": "Rev test", "kind": "cloze",
         "front": "", "back": "", "fields": {"Text": "{{c1::Dogs}} have {{c2::42}} teeth", "Back Extra": ""},
         "tags": [], "leech": True, "editedAt": now, "sched": dict(sched_new, suspended=True), "schedAt": now},
    ]
    rev = {"format": 1, "writer": "rev", "writtenAt": now,
           "cards": list(by_key.values()) + rev_new,
           "deleted": [{"key": deleted["key"], "at": now}],
           "reviews": [{"key": reviewed["key"], "at": int(now * 1000), "rating": 3, "interval": 10.0}]}
    json.dump(rev, open(os.path.join(folder, "rev.json"), "w", encoding="utf-8"))
    report = core.sync(col, folder, state)
    print("     report:", report)

    cid_of = {"%s:%d" % (g, o): cid for cid, g, o, _ in col.db.all(
        "select c.id, n.guid, c.ord, n.id from cards c join notes n on c.nid = n.id")}
    card = col.get_card(cid_of[reviewed["key"]])
    check(card.ivl == 10 and card.due == col.sched.today + 10, "reviewed card rescheduled (ivl %d, due %d)" % (card.ivl, card.due - col.sched.today))
    note = col.get_card(cid_of[edited["key"]]).note()
    check(note["Front"] == "Edited in Rev" and "from_rev" in note.tags, "edited card's text and tags updated")
    check(deleted["key"] not in cid_of, "deleted card removed")
    check(new_id + ":0" in cid_of, "new basic note added")
    new_card = col.get_card(cid_of[new_id + ":0"])
    check(new_card.type == 1 and new_card.queue == 1, "new card is in learning")
    check(cloze_id + ":0" in cid_of and cloze_id + ":1" in cid_of, "cloze note made 2 cards")
    check(col.get_card(cid_of[cloze_id + ":1"]).queue == -1, "suspended cloze card is suspended")
    check("leech" in col.get_card(cid_of[cloze_id + ":0"]).note().tags, "leech tag added")
    check(col.db.scalar("select count() from revlog where id = ?", int(now * 1000)) == 1, "review added to history")

    print("4. Anki's file reflects Rev's changes without echoing them back as newer")
    anki2 = json.load(open(os.path.join(folder, "anki.json"), encoding="utf-8"))
    by_key2 = {c["key"]: c for c in anki2["cards"]}
    check(by_key2[reviewed["key"]]["schedAt"] == now, "rescheduled card reports Rev's time")
    check(by_key2[edited["key"]]["editedAt"] == now, "edited card reports Rev's time")
    check(by_key2[edited["key"]]["front"] == "Edited in Rev", "rendered front updated")
    check(any(d["key"] == deleted["key"] for d in anki2["deleted"]), "deletion listed as tombstone")
    c0 = by_key2[cloze_id + ":0"]
    print("     cloze card:", c0["front"], "|", c0["back"])

    print("5. Same Rev content written again changes nothing")
    rev["writtenAt"] = now + 1
    json.dump(rev, open(os.path.join(folder, "rev.json"), "w", encoding="utf-8"))
    report = core.sync(col, folder, state)
    check(not any(report.values()), "nothing applied twice %s" % report)

    col.close()
    shutil.rmtree(work, ignore_errors=True)
    print("\n%s" % ("ALL PASSED" if not failures else "%d FAILED" % len(failures)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
