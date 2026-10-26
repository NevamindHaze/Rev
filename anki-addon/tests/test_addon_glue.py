"""The add-on's Anki side (__init__.py) with a stand-in for Anki's window, around a throw-away collection:
starting and stopping the Wi-Fi server with the profile, syncing on Anki's main thread, the "busy"
answers, the pairing window's text, and a new pairing code locking out the old one.

Usage: python test_addon_glue.py
Needs the `anki` package (pip install anki), not Anki itself. Never touches a real collection, and
everything the add-on would save is redirected to a temporary folder.
"""

import json
import os
import shutil
import sys
import tempfile
import threading
import time
import types
import urllib.error
import urllib.request

from anki.collection import Collection

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


# ---------------------------------------------------------------- a stand-in for Anki's window (aqt)

class Signal:
    def __init__(self):
        self.slots = []

    def connect(self, slot):
        self.slots.append(slot)


class Widget:
    """Enough of a Qt widget for the add-on's window: remembers text, ticks and on/off."""

    def __init__(self, *args, **kwargs):
        self.text = args[0] if args and isinstance(args[0], str) else ""
        self.checked = False
        self.enabled = True
        for name in ("toggled", "clicked", "triggered", "timeout"):
            setattr(self, name, Signal())

    def setText(self, text):
        self.text = text

    def setChecked(self, on):
        self.checked = on

    def isChecked(self):
        return self.checked

    def setEnabled(self, on):
        self.enabled = on

    def __getattr__(self, name):
        return lambda *args, **kwargs: None


class Timer(Widget):
    @staticmethod
    def singleShot(ms, fn):
        pass


work = tempfile.mkdtemp(prefix="revsync-glue-")
folder = os.path.join(work, "Rev Sync")
os.makedirs(os.path.join(work, "backups"))   # Anki always has its backup folder
col = Collection(os.path.join(work, "collection.anki2"))
for i in range(3):
    note = col.new_note(col.models.by_name("Basic"))
    note["Front"], note["Back"] = "Question %d" % i, "Answer %d" % i
    col.add_note(note, col.decks.id("Vet"))

config = {"sync_folder": folder, "interval_minutes": 5, "wifi_port": 0}
tooltips, main_threads = [], []


def run_on_main(fn):
    # Anki runs it on its main thread later; here a separate thread stands in for that.
    def run():
        main_threads.append(threading.current_thread().name)
        fn()
    threading.Thread(target=run, name="anki-main").start()


mw = types.SimpleNamespace(
    col=col, state="deckBrowser", reset=lambda: None,
    pm=types.SimpleNamespace(backupFolder=lambda: os.path.join(work, "backups")),
    addonManager=types.SimpleNamespace(getConfig=lambda name: config),
    taskman=types.SimpleNamespace(run_on_main=run_on_main),
    form=types.SimpleNamespace(menuTools=types.SimpleNamespace(addAction=lambda action: menu.append(action))),
)
menu = []
hooks = types.SimpleNamespace(profile_did_open=[], profile_will_close=[], sync_will_start=[], sync_did_finish=[])
qt = types.SimpleNamespace(QAction=Widget, QCheckBox=Widget, QDialog=Widget, QHBoxLayout=Widget, QLabel=Widget,
                           QPushButton=Widget, QTimer=Timer, QVBoxLayout=Widget,
                           Qt=types.SimpleNamespace(TextInteractionFlag=types.SimpleNamespace(TextSelectableByMouse=1)))
utils = types.SimpleNamespace(showWarning=lambda text: tooltips.append("WARNING " + text), tooltip=tooltips.append)
sys.modules["aqt"] = types.SimpleNamespace(gui_hooks=hooks, mw=mw)
sys.modules["aqt.qt"] = qt
sys.modules["aqt.utils"] = utils

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import rev_sync as addon  # noqa: E402

# Everything the add-on saves goes to the temporary folder, never into the add-on's own folder.
addon.USER_FILES = os.path.join(work, "user_files")
addon.STATE_PATH = os.path.join(addon.USER_FILES, "state.json")
addon.WIFI_PATH = os.path.join(addon.USER_FILES, "wifi.json")
addon.WIFI_REV_PATH = os.path.join(addon.USER_FILES, "rev-wifi.json")


def call(method, path, code, body=None):
    request = urllib.request.Request("http://127.0.0.1:%d/rev-sync/v1%s" % (addon._server.port, path), data=body, method=method)
    request.add_header("X-Rev-Code", code)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, json.loads(response.read() or b"{}")
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read() or b"{}")


def phone_file(written_at, cards=()):
    return json.dumps({"format": 1, "writer": "rev", "writtenAt": written_at, "cards": list(cards),
                       "deleted": [], "reviews": []}).encode("utf-8")


def main():
    print("1. The add-on adds its two menu items, and Wi-Fi stays off until switched on")
    check([a.text for a in menu] == ["Rev Sync now", "Rev Sync Wi-Fi..."], "Tools menu: %s" % [a.text for a in menu])
    addon._on_profile_open()
    check(addon._server is None, "no server when Wi-Fi sync is off")
    dialog = addon.WifiDialog()
    check(dialog.details.text == "Wi-Fi sync is off." and not dialog.new_code.enabled, "the window says it's off")

    print("2. Switching it on in the window starts the server and shows what to type")
    dialog.enabled.setChecked(True)
    dialog._toggled(True)
    code = addon._wifi_settings()["code"]
    check(addon._server is not None and addon._server.running, "the server is listening (port %s)" % (addon._server and addon._server.port))
    check(code in dialog.details.text and ".local:" in dialog.details.text, "the window shows the code and the computer's name")
    print("     " + dialog.details.text.replace("\n", "\n     "))

    print("3. A sync from the iPhone runs on Anki's main thread and saves everything")
    status, answer = call("POST", "/sync", code, phone_file(time.time()))
    check(status == 200 and len(answer.get("anki", {}).get("cards", [])) == 3, "Anki's 3 cards come back (%d %s)" % (status, answer.get("error", "")))
    check(main_threads == ["anki-main"], "the sync ran on Anki's main thread %s" % main_threads)
    state = json.load(open(addon.STATE_PATH, encoding="utf-8"))
    check(state.get("backedUp") is True and os.listdir(os.path.join(work, "backups")), "a backup was made before the first sync")
    check(os.path.exists(addon.WIFI_REV_PATH), "the iPhone's file is kept for later syncs")
    check(not os.path.exists(os.path.join(os.path.dirname(addon.__file__), "user_files", "rev-wifi.json")),
          "nothing was saved in the add-on's own folder")

    print("4. Anki says when it's busy, so the iPhone uses iCloud instead")
    mw.state = "review"
    status, answer = call("POST", "/sync", code, phone_file(time.time()))
    check(status == 503 and "review" in answer["error"], "in a review: \"%s\"" % answer.get("error"))
    mw.state = "deckBrowser"
    addon._running = True
    status, answer = call("POST", "/sync", code, phone_file(time.time()))
    check(status == 503 and "already" in answer["error"], "during another sync: \"%s\"" % answer.get("error"))
    addon._running = False
    config["sync_folder"] = os.path.join(work, "missing", "Rev Sync")
    status, answer = call("POST", "/sync", code, phone_file(time.time()))
    check(status == 503 and "folder" in answer["error"], "no sync folder: \"%s\"" % answer.get("error"))
    config["sync_folder"] = folder

    print("5. The regular sync (timer, menu) still works")
    tooltips.clear()
    addon.run_sync("menu", show_result=True)
    check(tooltips == ["Rev Sync: everything up to date"], "Tools > Rev Sync now: %s" % tooltips)

    print("6. A new pairing code: the old one no longer works")
    dialog._new_code()
    new = addon._wifi_settings()["code"]
    status, _ = call("GET", "/hello", code)
    check(new != code and status == 401, "old code refused (%d)" % status)
    status, _ = call("GET", "/hello", new)
    check(status == 200 and new in dialog.details.text, "new code works and is shown")

    print("7. Closing the profile stops the server; switching off keeps it off")
    port = addon._server.port
    addon._on_profile_close()
    check(addon._server is None, "stopped with the profile")
    try:
        urllib.request.urlopen("http://127.0.0.1:%d/rev-sync/v1/hello" % port, timeout=5)
        stopped = False
    except (urllib.error.URLError, ConnectionError):
        stopped = True
    check(stopped, "nothing listens any more")
    addon._on_profile_open()
    check(addon._server is not None, "opening the profile again starts it again")
    dialog.enabled.setChecked(False)
    dialog._toggled(False)
    check(addon._server is None and dialog.details.text == "Wi-Fi sync is off.", "switched off: stopped")
    addon._on_profile_open()
    check(addon._server is None, "...and stays off next time")
    addon._on_profile_close()

    print("8. A port another program uses: the window explains instead of failing")
    blocker = __import__("socket").socket()
    blocker.bind(("0.0.0.0", 0))
    blocker.listen()
    config["wifi_port"] = blocker.getsockname()[1]
    dialog.enabled.setChecked(True)
    dialog._toggled(True)
    check(addon._server is None and "port" in dialog.details.text, "\"%s...\"" % dialog.details.text[:60])
    blocker.close()
    dialog.enabled.setChecked(False)
    dialog._toggled(False)


try:
    main()
finally:
    addon._stop_server()
    col.close()
    shutil.rmtree(work, ignore_errors=True)
print("\n%s" % ("ALL PASSED" if not failures else "%d FAILED" % len(failures)))
sys.exit(1 if failures else 0)
