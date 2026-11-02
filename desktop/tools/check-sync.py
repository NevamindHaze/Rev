"""End-to-end check of the sync between Rev for Windows, Rev on iPhone and Anki. Never touches real data.

Everything happens in a temporary folder:
  - Anki:      a brand-new throw-away collection, synced by the REAL Rev Sync add-on code (core.py);
  - Windows:   the REAL Rev for Windows sync, run through tools/Rev.Demo on a test data folder;
  - iPhone:    played by this script, which writes rev.json exactly as Rev on iPhone does.

    venv\\Scripts\\python -I tools\\check-sync.py <path to anki-addon\\rev_sync> <path to Rev.Demo.exe>
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

ADDON, DEMO = sys.argv[1], sys.argv[2]
sys.path.insert(0, ADDON)
from anki.collection import Collection  # noqa: E402
from anki.scheduler.v3 import CardAnswer  # noqa: E402

import core  # noqa: E402

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


work = tempfile.mkdtemp(prefix="rev-sync-check-")
folder = os.path.join(work, "Rev Sync")
windows_data = os.path.join(work, "Windows data")
os.makedirs(folder)
os.makedirs(windows_data)

# ---- Anki: a small collection with a few studied cards
col = Collection(os.path.join(work, "collection.anki2"))
col.set_config("fsrs", True)
deck = col.decks.id("Vet::Pharmacology")
for i in range(8):
    note = col.new_note(col.models.by_name("Basic"))
    note["Front"] = "Question %d" % i
    note["Back"] = "Answer <b>%d</b>" % i
    note.tags = ["pharm"]
    col.add_note(note, deck)
cloze = col.new_note(col.models.by_name("Cloze"))
cloze["Text"] = "{{c1::Fasciola}} lives in the {{c2::liver}}"
col.add_note(cloze, col.decks.id("Vet::Parasitology"))
col.decks.select(deck)
for _ in range(3):
    qc = col.sched.get_queued_cards(fetch_limit=1).cards[0]
    card = col.get_card(qc.card.id)
    card.start_timer()
    col.sched.answer_card(col.sched.build_answer(card=card, states=qc.states, rating=CardAnswer.EASY))
state = {}


def anki_sync():
    return core.sync(col, folder, state)


def anki_cards():
    with open(os.path.join(folder, "anki.json"), encoding="utf-8") as f:
        snapshot = json.load(f)
    return {c["key"]: c for c in snapshot["cards"]}, snapshot


def note_of(key):
    guid, ord_ = key.rsplit(":", 1)
    nid = col.db.scalar("select id from notes where guid = ?", guid)
    return col.get_note(nid) if nid else None


def card_of(key):
    guid, ord_ = key.rsplit(":", 1)
    cid = col.db.scalar("select c.id from cards c join notes n on c.nid = n.id where n.guid = ? and c.ord = ?", guid, int(ord_))
    return col.get_card(cid) if cid else None


def windows(*args):
    result = subprocess.run([DEMO, windows_data, *args], capture_output=True, text=True, encoding="utf-8")
    if result.returncode != 0:
        raise RuntimeError("Rev.Demo %s failed: %s %s" % (args, result.stdout, result.stderr))
    return result.stdout.strip()


def windows_sync():
    return json.loads(windows("--sync", folder))


def windows_cards():
    return {c["key"]: c for c in json.loads(windows("--dump"))}


iphone = {}         # the iPhone's copy of every card, as it would send them in rev.json
iphone_reviews = []


def iphone_write():
    """Writes rev.json the way Rev on iPhone does: plain text, the Anki fields as HTML, its own times."""
    body = {"format": 1, "writer": "rev", "writtenAt": time.time(), "cards": list(iphone.values()),
            "deleted": [], "reviews": iphone_reviews}
    with open(os.path.join(folder, "rev.json"), "w", encoding="utf-8") as f:
        json.dump(body, f)


def zero(report):
    return not any(report.values()) if isinstance(report, dict) else False


print("1. Anki writes its file; Rev for Windows joins and gets every card")
anki_sync()
base, _ = anki_cards()
report = windows_sync()
check(report["Added"] == len(base), "Windows added all %d cards (%s)" % (len(base), report))
win = windows_cards()
basic = sorted(k for k, c in base.items() if c["kind"] == "basic")
a, b, c_, d_, e, f = basic[:6]
check(win[a]["back"] == base[a]["back"].replace("<b>", "**").replace("</b>", "**"), "bold came across as **bold**")
check(sum(1 for w in win.values() if w["phase"] == "review") == 3, "the 3 cards studied in Anki are studied in Windows")

print("2. The add-on reads windows.json for the first time: nothing in Anki changes")
report = anki_sync()
check(zero(report), "no change in Anki %s" % report)

print("3. The iPhone (already in sync) writes rev.json: still nothing changes")
for key, card in base.items():
    iphone[key] = dict(card, front=card["front"], back=card["back"], fields=None)
iphone_write()
report = anki_sync()
check(zero(report), "no change in Anki %s" % report)

print("4. An edit on the iPhone reaches Windows through Anki")
time.sleep(1.2)
iphone[a] = dict(iphone[a], fields={"Front": "Question edited on iPhone", "Back": "Answer"}, editedAt=time.time())
iphone_write()
report = anki_sync()
check(report["updated"] == 1 and note_of(a)["Front"] == "Question edited on iPhone", "Anki has the iPhone's edit")
report = windows_sync()
check(report["Updated"] == 1 and windows_cards()[a]["front"] == "Question edited on iPhone", "Windows has it too (%s)" % report)

print("5. Windows edits, studies, adds and deletes; Anki and the iPhone get all of it")
time.sleep(1.2)
win = windows_cards()
windows("--edit", win[b]["front"], "Answer edited on Windows")
windows("--study", win[c_]["front"])
windows("--add", "Vet › Surgery", "Approach for a bovine caesarean?", "Left flank, standing")
windows("--delete", win[e]["front"])
windows_sync()
report = anki_sync()
check(note_of(b)["Back"] == "Answer edited on Windows", "Anki has Windows' edit %s" % report)
check(card_of(c_).type == 2 and card_of(c_).ivl >= 1, "Anki has Windows' new schedule")
new_note = col.find_notes('"Front:Approach for a bovine caesarean?"')
check(len(new_note) == 1 and col.get_note(new_note[0]).note_type()["name"] == "Rev Basic", "Anki has Windows' new card")
check(any(d.name == "Vet::Surgery" for d in col.decks.all_names_and_ids()), "...in its new deck")
check(note_of(e) is None, "the card deleted on Windows is gone from Anki")
cards, snapshot = anki_cards()
win = windows_cards()
check(abs(cards[b]["editedAt"] - json.load(open(os.path.join(folder, "windows.json"), encoding="utf-8"))
          ["cards"][[x["key"] for x in json.load(open(os.path.join(folder, "windows.json"), encoding="utf-8"))["cards"]].index(b)]["editedAt"]) < 0.01,
      "anki.json reports Windows' own edit time, so the iPhone takes it and Windows doesn't re-apply it")
check(any(d["key"] == e for d in snapshot["deleted"]), "anki.json tells the iPhone about the deletion")
check(col.db.scalar("select count() from revlog where cid = ?", card_of(c_).id) >= 1, "Windows' review is in Anki's history")

print("6. Both apps edit the same card: the newer edit wins everywhere")
time.sleep(1.2)
iphone[f] = dict(iphone[f], fields={"Front": base[f]["front"], "Back": "iPhone, older"}, editedAt=time.time())
iphone_write()
time.sleep(1.2)
windows("--edit", windows_cards()[f]["front"], "Windows, newer")
windows_sync()
anki_sync()
windows_sync()
check(note_of(f)["Back"] == "Windows, newer", "Anki has the newer (Windows) edit")
check(windows_cards()[f]["back"] == "Windows, newer", "Windows keeps it")
cards, _ = anki_cards()
check(cards[f]["editedAt"] > iphone[f]["editedAt"], "anki.json's time is newer than the iPhone's, so the iPhone takes it")

print("7. A review done on the iPhone reaches Windows")
reviews_before = windows_cards()[d_]["reviews"]
ms = int(time.time() * 1000)
iphone_reviews.append({"key": d_, "at": ms, "rating": 3, "interval": 4.0})
iphone_write()
anki_sync()
report = windows_sync()
check(report["Reviews"] == 1 and windows_cards()[d_]["reviews"] == reviews_before + 1, "Windows has the iPhone's review, once (%s)" % report)

print("8. A deck deleted in one app is gone everywhere, not left behind empty")
windows("--delete-deck", "Vet › Surgery")
windows_sync()
report = anki_sync()
check(not any(d.name == "Vet::Surgery" for d in col.decks.all_names_and_ids()),
      "the deck deleted on Windows is gone from Anki %s" % report)
check(any(d.name == "Vet::Pharmacology" for d in col.decks.all_names_and_ids()), "the other decks stay")
parasitology = col.decks.id_for_name("Vet::Parasitology")
col.remove_notes(list({col.get_card(cid).nid for cid in col.decks.cids(parasitology)}))
col.decks.remove([parasitology])
anki_sync()
report = windows_sync()
decks = json.loads(windows("--decks"))
check("Vet › Parasitology" not in decks and report["Deleted"] == 2,
      "the deck deleted in Anki is gone from Windows (%s)" % report)
check("Vet › Pharmacology" in decks, "...and Windows keeps the others (%s)" % decks)
for key in [k for k, c in iphone.items() if c["deck"] == "Vet::Parasitology"]:
    del iphone[key]   # the iPhone deletes them too once it reads anki.json
iphone_write()
check(zero(anki_sync()), "the iPhone catching up changes nothing in Anki")

print("8b. Rev for Windows notices within seconds when Anki rewrites its file")
watch = subprocess.Popen([DEMO, windows_data, "--watch", folder, "30"], stdout=subprocess.PIPE, text=True, encoding="utf-8")
check(watch.stdout.readline().strip() == "watching", "Windows is watching the folder")
time.sleep(1.2)
iphone[a] = dict(iphone[a], fields={"Front": "Edited on iPhone, watched", "Back": "Answer"}, editedAt=time.time())
iphone_write()
anki_sync()   # (over Wi-Fi this happens the moment the iPhone syncs)
started = time.time()
line = watch.stdout.readline().strip()
took = time.time() - started
watch.kill()
watch.wait()
check(line.startswith("synced") and json.loads(line[7:])["Updated"] == 1, "Windows synced by itself: %s" % line[:90])
check(took < 10, "...%.1f seconds after Anki wrote anki.json" % took)
check(windows_cards()[a]["front"] == "Edited on iPhone, watched", "and has the iPhone's edit")

print("9. Syncing again and again changes nothing anywhere (no echoes)")
quiet = True
for _ in range(3):
    r1, r2 = anki_sync(), windows_sync()
    print("     add-on:", r1, "| Windows:", r2["Summary"])
    quiet = quiet and zero(r1) and r2["Any"] is False
check(quiet, "every further sync is empty")
windows_file = os.path.join(folder, "windows.json")
before = os.path.getmtime(windows_file)
time.sleep(1.1)
windows_sync()
check(os.path.getmtime(windows_file) == before, "windows.json isn't rewritten when nothing changed")

print("10. Taking Windows out of the sync leaves iPhone <-> Anki working")
os.remove(windows_file)
time.sleep(1.2)
iphone[a] = dict(iphone[a], fields={"Front": "iPhone only again", "Back": "Answer"}, editedAt=time.time())
iphone_write()
report = anki_sync()
check(note_of(a)["Front"] == "iPhone only again", "the iPhone's edit still reaches Anki %s" % report)

col.close()
shutil.rmtree(work, ignore_errors=True)
print("\n%s" % ("ALL PASSED" if not failures else "%d FAILED" % len(failures)))
sys.exit(1 if failures else 0)
