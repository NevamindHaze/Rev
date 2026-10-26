"""End-to-end test of the sync with two Rev apps (iPhone and Windows) and Anki, on a throw-away collection.

Usage: python test_windows.py
Needs the `anki` package (pip install anki). Builds a brand-new collection in a temporary folder;
never touches a real one. rev.json stands in for Rev on iPhone and windows.json for Rev for Windows.
"""

import json
import os
import shutil
import sys
import tempfile
import time
import uuid

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "rev_sync"))
from anki.collection import Collection  # noqa: E402

import core  # noqa: E402

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


class Setup:
    def __init__(self):
        self.work = tempfile.mkdtemp(prefix="revsync-windows-")
        self.folder = os.path.join(self.work, "Rev Sync")
        self.col = Collection(os.path.join(self.work, "collection.anki2"))
        self.state = {}
        deck = self.col.decks.id("Vet")
        for i in range(8):
            note = self.col.new_note(self.col.models.by_name("Basic"))
            note["Front"] = "Question %d" % i
            note["Back"] = "Answer %d" % i
            self.col.add_note(note, deck)
        self.created = time.time()
        self.written = {}

    def sync(self):
        return core.sync(self.col, self.folder, self.state)

    def anki(self):
        with open(os.path.join(self.folder, "anki.json"), encoding="utf-8") as f:
            return json.load(f)

    def anki_cards(self):
        return {c["key"]: c for c in self.anki()["cards"]}

    def write(self, name, writer, cards, deleted=(), reviews=()):
        """A Rev app's file. writtenAt always moves on, like a real app writing a new version."""
        at = max(time.time(), self.written.get(name, 0) + 1)
        self.written[name] = at
        body = {"format": 1, "writer": writer, "writtenAt": at, "cards": list(cards),
                "deleted": list(deleted), "reviews": list(reviews)}
        with open(os.path.join(self.folder, name), "w", encoding="utf-8") as f:
            json.dump(body, f)

    def note(self, key):
        guid, ord_ = key.rsplit(":", 1)
        nid = self.col.db.scalar("select id from notes where guid = ?", guid)
        return self.col.get_note(nid) if nid else None

    def card(self, key):
        guid, ord_ = key.rsplit(":", 1)
        cid = self.col.db.scalar("select c.id from cards c join notes n on c.nid = n.id where n.guid = ? and c.ord = ?",
                                 guid, int(ord_))
        return self.col.get_card(cid) if cid else None

    def close(self):
        self.col.close()
        shutil.rmtree(self.work, ignore_errors=True)


def as_rev(entry, **changes):
    """An entry from anki.json as a Rev app would send it back (Rev sends plain text plus `fields`)."""
    rc = dict(entry, front="", back="", fields={"Front": "", "Back": ""})
    rc["fields"] = {"Front": entry["front"], "Back": entry["back"]}
    rc.update(changes)
    return rc


def main():
    s = Setup()
    s.sync()
    base = s.anki_cards()
    keys = sorted(base)
    k = {name: keys[i] for i, name in enumerate(["late", "both", "split", "keep", "gone", "plain", "rev", "win"])}

    # Let a few seconds pass so changes are clearly newer than the cards (Anki times are whole seconds).
    time.sleep(2.5)
    t1 = time.time()

    print("1. A change from Windows alone reaches Anki, and Anki reports Windows' own time")
    win = {key: as_rev(base[key]) for key in keys}
    win[k["win"]] = as_rev(base[k["win"]], fields={"Front": "Edited on Windows", "Back": "Answer"}, editedAt=t1)
    s.write("windows.json", "windows", win.values())
    report = s.sync()
    check(s.note(k["win"])["Front"] == "Edited on Windows", "Windows' edit is in Anki %s" % report)
    check(s.anki_cards()[k["win"]]["editedAt"] == t1, "anki.json reports Windows' time (no echo)")

    print("2. A newer change that arrives after an older one was applied still wins")
    win[k["late"]] = as_rev(base[k["late"]], fields={"Front": "Windows, older", "Back": "A"}, editedAt=t1)
    s.write("windows.json", "windows", win.values())
    s.sync()
    time.sleep(2.5)   # the iPhone's change (made at t1 + 1) reaches Anki only now
    rev = {key: as_rev(base[key]) for key in keys}
    rev[k["late"]] = as_rev(base[k["late"]], fields={"Front": "iPhone, newer", "Back": "A"}, editedAt=t1 + 1.5)
    s.write("rev.json", "rev", rev.values())
    s.sync()
    check(s.note(k["late"])["Front"] == "iPhone, newer", "the newer iPhone change wins although it arrived later")

    print("3. Both apps change the same card before a sync: the newer one wins")
    t2 = time.time()
    win[k["both"]] = as_rev(base[k["both"]], fields={"Front": "Windows, newest", "Back": "A"}, editedAt=t2 + 1)
    rev[k["both"]] = as_rev(base[k["both"]], fields={"Front": "iPhone, older", "Back": "A"}, editedAt=t2)
    s.write("windows.json", "windows", win.values())
    s.write("rev.json", "rev", rev.values())
    s.sync()
    check(s.note(k["both"])["Front"] == "Windows, newest", "Windows' newer edit wins")
    check(s.anki_cards()[k["both"]]["editedAt"] == t2 + 1, "anki.json reports the winner's time")

    print("4. Text from one app and schedule from the other, for the same card")
    t3 = time.time()
    day_start = s.col.sched.day_cutoff - 86400
    studied = dict(base[k["split"]]["sched"], phase="review", due=day_start + 6 * 86400, interval=6.0, reps=1)
    rev[k["split"]] = as_rev(base[k["split"]], fields={"Front": "Text from iPhone", "Back": "A"}, editedAt=t3)
    win[k["split"]] = as_rev(base[k["split"]], sched=studied, schedAt=t3 + 1)
    s.write("rev.json", "rev", rev.values())
    s.write("windows.json", "windows", win.values())
    s.sync()
    card = s.card(k["split"])
    check(s.note(k["split"])["Front"] == "Text from iPhone", "the iPhone's text is in Anki")
    check(card.type == 2 and card.ivl == 6 and card.due == s.col.sched.today + 6, "Windows' schedule is in Anki")

    print("5. A deletion on Windows loses to a newer change on the iPhone")
    t4 = time.time()
    del win[k["keep"]]
    rev[k["keep"]] = as_rev(base[k["keep"]], fields={"Front": "Changed on iPhone after", "Back": "A"}, editedAt=t4 + 1)
    s.write("windows.json", "windows", win.values(), deleted=[{"key": k["keep"], "at": t4}])
    s.write("rev.json", "rev", rev.values())
    s.sync()
    check(s.note(k["keep"]) is not None and s.note(k["keep"])["Front"] == "Changed on iPhone after", "the card survives")

    print("6. A deletion on Windows wins over the iPhone's older copy, and stays deleted")
    time.sleep(1.1)
    t5 = time.time()
    del win[k["gone"]]
    s.write("windows.json", "windows", win.values(),
            deleted=[{"key": k["keep"], "at": t4}, {"key": k["gone"], "at": t5}])
    report = s.sync()
    check(s.note(k["gone"]) is None, "the card is deleted in Anki %s" % report)
    check(any(d["key"] == k["gone"] for d in s.anki()["deleted"]), "anki.json tells the iPhone to delete it too")
    s.write("rev.json", "rev", rev.values())   # the iPhone hasn't heard yet and still lists it
    s.sync()
    check(s.note(k["gone"]) is None, "the iPhone's old copy doesn't bring it back")
    # Windows writes again while the iPhone's old file is still there (both files are read every time).
    s.write("windows.json", "windows", win.values(),
            deleted=[{"key": k["keep"], "at": t4}, {"key": k["gone"], "at": t5}])
    s.sync()
    check(s.note(k["gone"]) is None, "...not even when Windows writes again meanwhile")
    # Once the iPhone has read anki.json, it deletes the card too and stops listing it.
    del rev[k["gone"]]

    print("6b. A card deleted in one app comes back if the other app changed it after the deletion")
    t5b = time.time()
    del win[k["plain"]]
    s.write("windows.json", "windows", win.values(),
            deleted=[{"key": k["gone"], "at": t5}, {"key": k["plain"], "at": t5b}])
    s.sync()
    check(s.note(k["plain"]) is None, "deleted on Windows")
    time.sleep(1.1)
    restored = as_rev(base[k["plain"]], fields={"Front": "Studied on iPhone after", "Back": "A"}, editedAt=time.time())
    rev[k["plain"]] = restored
    s.write("rev.json", "rev", rev.values())
    s.sync()
    check(s.note(k["plain"]) is not None and s.note(k["plain"])["Front"] == "Studied on iPhone after",
          "the iPhone's later change brings it back")
    win[k["plain"]] = as_rev(s.anki_cards()[k["plain"]])   # Windows gets it back from anki.json

    print("7. New notes from both apps are added once")
    t6 = time.time()
    sched_new = {"phase": "new", "due": 0, "interval": 0, "ease": 2.5, "reps": 0, "lapses": 0,
                 "stability": None, "difficulty": None, "suspended": False}
    def new_note(writer):
        note_id = str(uuid.uuid4()).upper()
        return {"key": note_id + ":0", "noteId": note_id, "ord": 0, "deck": "Vet::From " + writer, "kind": "basic",
                "front": "New on " + writer, "back": "A", "fields": {"Front": "New on " + writer, "Back": "A"},
                "tags": [writer], "leech": False, "editedAt": t6, "sched": sched_new, "schedAt": t6}
    from_win, from_rev = new_note("Windows"), new_note("iPhone")
    win[from_win["key"]] = from_win
    rev[from_rev["key"]] = from_rev
    s.write("windows.json", "windows", win.values(), deleted=[{"key": k["gone"], "at": t5}, {"key": k["plain"], "at": t5b}])
    s.write("rev.json", "rev", rev.values())
    before = s.col.note_count()
    s.sync()
    check(s.col.note_count() == before + 2, "two notes added")
    names = s.col.decks.all_names_and_ids()
    check(any(d.name == "Vet::From Windows" for d in names), "Windows' note is in its deck")
    cards = s.anki_cards()
    check(from_win["key"] in cards and from_rev["key"] in cards, "both new notes are in anki.json for the other app")
    s.write("windows.json", "windows", win.values(), deleted=[{"key": k["gone"], "at": t5}, {"key": k["plain"], "at": t5b}])
    s.write("rev.json", "rev", rev.values())
    s.sync()
    check(s.col.note_count() == before + 2, "writing them again adds nothing")

    print("8. Reviews from both apps are kept once")
    ms = int(time.time() * 1000)
    reviews_win = [{"key": k["plain"], "at": ms, "rating": 3, "interval": 1.0}]
    reviews_rev = [{"key": k["plain"], "at": ms + 7, "rating": 1, "interval": 0.0},
                   {"key": k["plain"], "at": ms, "rating": 3, "interval": 1.0}]   # the same review, seen by both
    s.write("windows.json", "windows", win.values(), deleted=[{"key": k["gone"], "at": t5}], reviews=reviews_win)
    s.write("rev.json", "rev", rev.values(), reviews=reviews_rev)
    s.sync()
    count = s.col.db.scalar("select count() from revlog where id in (?, ?)", ms, ms + 7)
    check(count == 2, "two reviews in Anki's history (%d)" % count)

    print("9. Nothing changes when both apps write the same again")
    s.write("windows.json", "windows", win.values(), deleted=[{"key": k["gone"], "at": t5}], reviews=reviews_win)
    s.write("rev.json", "rev", rev.values(), reviews=reviews_rev)
    report = s.sync()
    check(not any(report.values()), "nothing applied twice %s" % report)

    print("10. Without windows.json, the iPhone-only sync carries on")
    os.remove(os.path.join(s.folder, "windows.json"))
    rev[k["rev"]] = as_rev(base[k["rev"]], fields={"Front": "iPhone only again", "Back": "A"}, editedAt=time.time())
    s.write("rev.json", "rev", rev.values(), reviews=reviews_rev)
    report = s.sync()
    check(s.note(k["rev"])["Front"] == "iPhone only again", "the iPhone's change is applied %s" % report)

    s.close()
    print("\n%s" % ("ALL PASSED" if not failures else "%d FAILED" % len(failures)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
