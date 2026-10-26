"""Sync over Wi-Fi: the add-on's real server and sync code, a throw-away Anki collection, and a pretend
Rev on iPhone that talks to the server the way the app will.

Usage: python test_wifi.py
Needs the `anki` package (pip install anki). Never touches a real collection or sync folder.
"""

import json
import os
import shutil
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid
from http.client import HTTPConnection

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "rev_sync"))
from anki.collection import Collection  # noqa: E402

import core  # noqa: E402
import wifi  # noqa: E402

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


class Setup:
    """A collection, a sync folder and a running server, all in a temporary folder."""

    def __init__(self):
        self.work = tempfile.mkdtemp(prefix="revsync-wifi-")
        self.folder = os.path.join(self.work, "Rev Sync")
        self.col = Collection(os.path.join(self.work, "collection.anki2"))
        deck = self.col.decks.id("Vet")
        for i in range(4):
            note = self.col.new_note(self.col.models.by_name("Basic"))
            note["Front"], note["Back"] = "Question %d" % i, "Answer %d" % i
            self.col.add_note(note, deck)
        self.state = {}
        self.busy = False
        self.handlers = wifi.AddonHandlers(lambda fn: fn(), self.sync, self.col.media.dir(),
                                           os.path.join(self.work, "user_files", "rev-wifi.json"))
        self.code = wifi.new_code()
        self.server = wifi.WifiServer(self.handlers, self.code, port=0, host="127.0.0.1")
        self.server.start()
        self.base = "http://127.0.0.1:%d%s" % (self.server.port, wifi.API)

    def sync(self, rev):
        if self.busy:
            raise wifi.Busy("Anki is in the middle of a review")
        return core.sync_and_export(self.col, self.folder, self.state, rev)

    def timer_sync(self):
        """The add-on's regular sync (timer, menu), which also uses the stored Wi-Fi file."""
        return core.sync(self.col, self.folder, self.state, self.handlers.stored_rev())

    def call(self, method, path, body=None, code="right"):
        request = urllib.request.Request(self.base + path, data=body, method=method)
        if code is not None:
            request.add_header("X-Rev-Code", self.code if code == "right" else code)
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return response.status, response.read()
        except urllib.error.HTTPError as error:
            return error.code, error.read()

    def iphone_sync(self, cards, written_at, deleted=()):
        body = {"format": 1, "writer": "rev", "writtenAt": written_at, "cards": list(cards),
                "deleted": list(deleted), "reviews": []}
        status, answer = self.call("POST", "/sync", json.dumps(body).encode("utf-8"))
        return status, (json.loads(answer) if answer else {})

    def note(self, key):
        nid = self.col.db.scalar("select id from notes where guid = ?", key.rsplit(":", 1)[0])
        return self.col.get_note(nid) if nid else None

    def close(self):
        self.server.stop()
        self.col.close()
        shutil.rmtree(self.work, ignore_errors=True)


def as_rev(entry, **changes):
    rc = dict(entry, front="", back="", fields={"Front": entry["front"], "Back": entry["back"]})
    rc.update(changes)
    return rc


def main():
    s = Setup()
    try:
        run(s)
    finally:
        s.close()
    print("\n%s" % ("ALL PASSED" if not failures else "%d FAILED" % len(failures)))
    return 1 if failures else 0


def run(s):
    print("1. Pairing: only the right code gets an answer")
    status, _ = s.call("GET", "/hello", code=None)
    check(status == 401, "no code: refused (%d)" % status)
    status, _ = s.call("GET", "/hello", code="AAAA-AAAA")
    check(status == 401, "wrong code: refused (%d)" % status)
    status, body = s.call("GET", "/hello", code=s.code.lower().replace("-", " "))
    check(status == 200 and json.loads(body)["app"] == "rev-sync", "right code (typed in lower case, with a space): answered")
    connection = HTTPConnection("127.0.0.1", s.server.port, timeout=30)
    connection.request("GET", "/collection.anki2", headers={"X-Rev-Code": s.code})
    status = connection.getresponse().status
    connection.close()
    check(status == 404, "anything outside /rev-sync/v1 doesn't exist, even with the code (%d)" % status)
    status, _ = s.call("GET", "/../../collection.anki2")
    check(status == 404, "...nor can a path climb out of it (%d)" % status)

    print("2. The first Wi-Fi sync gets every Anki card back at once")
    status, answer = s.iphone_sync([], time.time())
    anki = {c["key"]: c for c in answer.get("anki", {}).get("cards", [])}
    check(status == 200 and len(anki) == 4, "Anki's 4 cards in the answer (%d)" % status)
    check(os.path.exists(os.path.join(s.folder, "anki.json")), "anki.json is still written for iCloud and Rev for Windows")

    print("3. A new card and an edit from the iPhone reach Anki in the same request")
    time.sleep(1.1)
    t1 = time.time()
    keys = sorted(anki)
    phone = {k: as_rev(anki[k]) for k in keys}
    phone[keys[0]] = as_rev(anki[keys[0]], fields={"Front": "Edited over Wi-Fi", "Back": "A"}, editedAt=t1)
    new_id = str(uuid.uuid4())
    sched = {"phase": "new", "due": 0, "interval": 0, "ease": 2.5, "reps": 0, "lapses": 0,
             "stability": None, "difficulty": None, "suspended": False}
    phone[new_id + ":0"] = {"key": new_id + ":0", "noteId": new_id, "ord": 0, "deck": "Nematodes", "kind": "basic",
                            "front": "Ascaris host?", "back": "Pig", "tags": [], "leech": False,
                            "fields": {"Front": 'Ascaris host? <img src="ascaris.png">', "Back": "Pig"},
                            "editedAt": t1, "sched": sched, "schedAt": t1}
    picture = b"\x89PNG pretend picture"
    status, _ = s.call("PUT", "/media/ascaris.png", picture)
    check(status == 204, "the card's picture is accepted (%d)" % status)
    status, answer = s.iphone_sync(phone.values(), t1)
    report = answer.get("report", {})
    check(status == 200 and report.get("added") == 1 and report.get("updated") == 1, "report: 1 added, 1 updated %s" % report)
    check(s.note(keys[0])["Front"] == "Edited over Wi-Fi", "the edit is in Anki")
    check(s.note(new_id + ":0") is not None, "the new card is in Anki")
    with open(os.path.join(s.col.media.dir(), "ascaris.png"), "rb") as f:
        check(f.read() == picture, "the picture is in Anki's media folder")
    returned = {c["key"]: c for c in answer["anki"]["cards"]}
    check(abs(returned[keys[0]]["editedAt"] - t1) < 0.01, "the answer reports the iPhone's own time (no echo)")
    first_written = answer["anki"]["writtenAt"]

    print("4. Pictures: the iPhone can fetch Anki's, and nothing outside the media folder")
    with open(os.path.join(s.col.media.dir(), "liver.png"), "wb") as f:
        f.write(b"liver picture")
    status, body = s.call("GET", "/media/liver.png")
    check(status == 200 and body == b"liver picture", "an Anki picture can be fetched")
    status, _ = s.call("GET", "/media/missing.png")
    check(status == 404, "a missing picture: not found (%d)" % status)
    for bad in ("..%2F..%2Fcollection.anki2", "..%5Ccollection.anki2", "%2E%2E", ".hidden", "C%3A%5Cx.png"):
        status, _ = s.call("GET", "/media/" + bad)
        check(status == 400, "%s is refused (%d)" % (bad, status))
    status, _ = s.call("PUT", "/media/..%2Fevil.png", b"x")
    check(status == 400 and not os.path.exists(os.path.join(s.work, "evil.png")), "...also when sending one (%d)" % status)
    status, _ = s.call("PUT", "/media/ascaris.png", b"something else")
    with open(os.path.join(s.col.media.dir(), "ascaris.png"), "rb") as f:
        check(status == 204 and f.read() == picture, "a picture already in Anki is never replaced")

    print("5. Syncing the same again changes nothing, and Anki's file keeps its time")
    status, answer = s.iphone_sync(phone.values(), t1)
    check(status == 200 and not any(answer["report"].values()), "nothing applied twice %s" % answer.get("report"))
    check(answer["anki"]["writtenAt"] == first_written, "Anki's file has the same time, so the iPhone skips it")

    print("6. An older rev.json that iCloud delivers late doesn't undo the Wi-Fi sync")
    old = {"format": 1, "writer": "rev", "writtenAt": t1 - 100, "cards": [as_rev(anki[k]) for k in keys],
           "deleted": [], "reviews": []}
    core.write_json_atomic(os.path.join(s.folder, "rev.json"), old)
    report = s.timer_sync()
    check(not any(report.values()) and s.note(keys[0])["Front"] == "Edited over Wi-Fi", "Anki keeps the Wi-Fi edit %s" % report)
    check(s.note(new_id + ":0") is not None, "and the card added over Wi-Fi")

    print("7. Away from home: a newer rev.json through iCloud is used as before")
    time.sleep(1.1)
    t2 = time.time()
    phone[keys[1]] = as_rev(anki[keys[1]], fields={"Front": "Edited away from home", "Back": "A"}, editedAt=t2)
    core.write_json_atomic(os.path.join(s.folder, "rev.json"), {
        "format": 1, "writer": "rev", "writtenAt": t2, "cards": list(phone.values()), "deleted": [], "reviews": []})
    report = s.timer_sync()
    check(report.get("updated") == 1 and s.note(keys[1])["Front"] == "Edited away from home", "the iCloud change is applied %s" % report)
    check(s.note(keys[0])["Front"] == "Edited over Wi-Fi", "the Wi-Fi edit stays")

    print("8. An older file sent over Wi-Fi doesn't replace a newer one")
    status, answer = s.iphone_sync([as_rev(anki[k]) for k in keys], t1 - 50)
    check(status == 200 and not any(answer["report"].values()), "nothing changes %s" % answer.get("report"))
    check(s.note(keys[0])["Front"] == "Edited over Wi-Fi", "the newer edit stays")

    print("9. When Anki is busy, or the request is wrong, the iPhone is told (and uses iCloud)")
    s.busy = True
    status, answer = s.iphone_sync(phone.values(), time.time())
    check(status == 503 and "review" in answer.get("error", ""), "busy: 503, \"%s\"" % answer.get("error"))
    s.busy = False
    status, _ = s.call("POST", "/sync", b"not json")
    check(status == 400, "not a Rev file: refused (%d)" % status)
    connection = HTTPConnection("127.0.0.1", s.server.port, timeout=30)
    connection.putrequest("POST", wifi.API + "/sync")
    connection.putheader("X-Rev-Code", s.code)
    connection.putheader("Content-Length", str(wifi.MAX_BODY + 1))
    connection.endheaders()
    status = connection.getresponse().status
    connection.close()
    check(status == 413, "far too large: refused before reading it (%d)" % status)
    status, _ = s.call("DELETE", "/media/ascaris.png")
    check(status in (404, 501), "anything else isn't possible (%d)" % status)

    print("10. Guessing the code: after 5 wrong codes the server pauses for a minute")
    for _ in range(wifi.MAX_FAILURES):
        s.call("GET", "/hello", code="WRONG-CODE")
    status, _ = s.call("GET", "/hello")
    check(status == 429, "even the right code waits now (%d)" % status)
    s.server._paused_until = 0   # (instead of waiting a minute in the test)
    status, _ = s.call("GET", "/hello")
    check(status == 200, "afterwards the right code works again (%d)" % status)

    print("11. Pairing codes and addresses")
    codes = {wifi.new_code() for _ in range(200)}
    check(len(codes) == 200 and all(len(c) == 9 and c[4] == "-" for c in codes), "codes look like K7QM-3XPA and differ")
    check(not any(ch in c for c in codes for ch in "01IO"), "no 0/O or 1/I in codes")
    print("     this computer's addresses:", wifi.local_addresses())


if __name__ == "__main__":
    sys.exit(main())
