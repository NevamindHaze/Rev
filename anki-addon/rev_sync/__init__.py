"""Rev Sync: keeps Anki and the Rev apps (Rev on iPhone, and Rev for Windows if you use it) in sync through a shared iCloud Drive folder.

When it runs:
  - a few seconds after your profile opens
  - every few minutes while Anki is open (not while you're reviewing)
  - a few seconds after Rev for Windows (or iCloud, for the iPhone) writes its file into the folder
  - just before and just after Anki syncs with AnkiWeb
  - when you close your profile
  - Tools > Rev Sync now
  - when Rev on iPhone syncs over Wi-Fi (if you switched that on in Tools > Rev Sync Wi-Fi...)
Before its very first sync it makes a backup of your collection.
"""

import json
import os
import socket
import threading
import traceback

from aqt import gui_hooks, mw
from aqt.qt import QAction, QCheckBox, QDialog, QHBoxLayout, QLabel, QPushButton, QTimer, QVBoxLayout, Qt
from aqt.utils import showWarning, tooltip

from . import core, wifi

ADDON = __name__
USER_FILES = os.path.join(os.path.dirname(__file__), "user_files")
STATE_PATH = os.path.join(USER_FILES, "state.json")
WIFI_PATH = os.path.join(USER_FILES, "wifi.json")          # Wi-Fi on/off and the pairing code
WIFI_REV_PATH = os.path.join(USER_FILES, "rev-wifi.json")  # the newest file Rev on iPhone sent over Wi-Fi
_timer = None
_watch_timer = None
_seen_files = {}
_running = False
_server = None
_server_error = ""


def _config():
    return mw.addonManager.getConfig(ADDON) or {}


def _folder():
    folder = _config().get("sync_folder") or ""
    return os.path.expandvars(os.path.expanduser(folder))


def _folder_ok(folder):
    return bool(folder) and os.path.isdir(os.path.dirname(folder.rstrip("\\/")))


def _load_json(path):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def _load_state():
    return _load_json(STATE_PATH)


def _save_state(state):
    os.makedirs(USER_FILES, exist_ok=True)
    core.write_json_atomic(STATE_PATH, state)


def _handlers():
    return wifi.AddonHandlers(_on_main, _sync_for_wifi, mw.col.media.dir(), WIFI_REV_PATH)


def _sync_now(wifi_rev):
    """One sync (on Anki's main thread). Returns (report, Anki's file)."""
    state = _load_state()
    if not state.get("backedUp"):
        mw.col.create_backup(backup_folder=mw.pm.backupFolder(), force=True, wait_for_completion=True)
        state["backedUp"] = True
        _save_state(state)
    report, snapshot = core.sync_and_export(mw.col, _folder(), state, wifi_rev)
    _save_state(state)
    if any(report.values()):
        mw.reset()
    return report, snapshot


def _summary(report):
    parts = ["%d %s" % (n, label) for label, n in report.items() if n]
    return ", ".join(parts) if parts else "everything up to date"


def run_sync(reason="", show_result=False):
    global _running
    if _running or not mw.col:
        return
    folder = _folder()
    if not _folder_ok(folder):
        if show_result:
            showWarning("Rev Sync: the sync folder isn't set or doesn't exist.\n\nSet it in Tools > Add-ons > Rev Sync > Config.")
        return
    _running = True
    try:
        # Also uses the newest file Rev on iPhone sent over Wi-Fi, so a late iCloud copy can't undo it.
        report, _ = _sync_now(_handlers().stored_rev())
        if show_result or any(report.values()):
            tooltip("Rev Sync: " + _summary(report))
    except Exception:
        traceback.print_exc()
        if show_result:
            showWarning("Rev Sync failed:\n\n" + traceback.format_exc())
    finally:
        _running = False


# ---------------------------------------------------------------- Wi-Fi

def _on_main(fn, timeout=120):
    """Runs fn on Anki's main thread and waits for its result (the server answers on its own threads)."""
    done = threading.Event()
    box = {}

    def run():
        try:
            box["value"] = fn()
        except BaseException as error:
            box["error"] = error
        finally:
            done.set()

    mw.taskman.run_on_main(run)
    if not done.wait(timeout):
        raise wifi.Busy("Anki took too long to answer")
    if "error" in box:
        raise box["error"]
    return box["value"]


def _sync_for_wifi(rev):
    """A sync asked for by Rev on iPhone over Wi-Fi (runs on Anki's main thread)."""
    global _running
    if not mw.col:
        raise wifi.Busy("no Anki profile is open")
    if mw.state == "review":
        raise wifi.Busy("Anki is in the middle of a review")
    if _running:
        raise wifi.Busy("Anki is already syncing")
    if not _folder_ok(_folder()):
        raise wifi.Busy("the Rev Sync folder isn't set in Anki")
    _running = True
    try:
        report, snapshot = _sync_now(rev)
        if any(report.values()):
            tooltip("Rev Sync (Wi-Fi): " + _summary(report))
        return report, snapshot
    finally:
        _running = False


def _wifi_settings():
    settings = _load_json(WIFI_PATH)
    if not settings.get("code"):
        settings["code"] = wifi.new_code()
        _save_wifi_settings(settings)
    return settings


def _save_wifi_settings(settings):
    os.makedirs(USER_FILES, exist_ok=True)
    core.write_json_atomic(WIFI_PATH, settings)


def _port():
    try:
        return int(_config().get("wifi_port", wifi.DEFAULT_PORT))
    except (TypeError, ValueError):
        return wifi.DEFAULT_PORT


def _start_server():
    """Starts listening if Wi-Fi sync is switched on (and stops first if it already was)."""
    global _server, _server_error
    _stop_server()
    _server_error = ""
    settings = _wifi_settings()
    if not settings.get("enabled") or not mw.col:
        return
    server = wifi.WifiServer(_handlers(), settings["code"], port=_port())
    try:
        server.start()
        _server = server
    except OSError as error:
        _server_error = "Couldn't start on port %d (%s). Another program may be using it: set another \"wifi_port\" in the add-on's Config." % (_port(), error)


def _stop_server():
    global _server
    if _server:
        _server.stop()
        _server = None


class WifiDialog(QDialog):
    """Tools > Rev Sync Wi-Fi...: switch it on, and see what to type into Rev on iPhone."""

    def __init__(self):
        super().__init__(mw)
        self.setWindowTitle("Rev Sync Wi-Fi")
        self.setMinimumWidth(460)
        layout = QVBoxLayout(self)
        intro = QLabel("Rev on iPhone can sync with Anki directly over your home Wi-Fi, in seconds instead of "
                       "waiting for iCloud. When the iPhone isn't on the same Wi-Fi, or Anki is closed, it "
                       "uses iCloud as before.")
        intro.setWordWrap(True)
        layout.addWidget(intro)
        self.enabled = QCheckBox("Let Rev on iPhone sync with Anki over Wi-Fi")
        self.enabled.setChecked(bool(_wifi_settings().get("enabled")))
        self.enabled.toggled.connect(self._toggled)
        layout.addWidget(self.enabled)
        self.details = QLabel()
        self.details.setWordWrap(True)
        self.details.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        layout.addWidget(self.details)
        buttons = QHBoxLayout()
        self.new_code = QPushButton("New pairing code")
        self.new_code.clicked.connect(self._new_code)
        buttons.addWidget(self.new_code)
        buttons.addStretch()
        close = QPushButton("Close")
        close.clicked.connect(self.accept)
        buttons.addWidget(close)
        layout.addLayout(buttons)
        self._refresh()

    def _toggled(self, on):
        settings = _wifi_settings()
        settings["enabled"] = on
        _save_wifi_settings(settings)
        _start_server()
        self._refresh()

    def _new_code(self):
        settings = _wifi_settings()
        settings["code"] = wifi.new_code()
        _save_wifi_settings(settings)
        _start_server()
        self._refresh()

    def _refresh(self):
        on = self.enabled.isChecked()
        self.new_code.setEnabled(on)
        if not on:
            self.details.setText("Wi-Fi sync is off.")
            return
        if _server_error:
            self.details.setText(_server_error)
            return
        port = _server.port if _server else _port()
        addresses = ["%s:%d" % (a, port) for a in wifi.local_addresses()]
        addresses.append("%s.local:%d" % (socket.gethostname(), port))
        self.details.setText(
            "In Rev on iPhone, go to Settings > Sync over Wi-Fi and type:\n\n"
            "Computer:  %s\n"
            "Pairing code:  %s\n\n"
            "If Windows asks whether Anki may use the network, allow it on private networks.\n"
            "\"New pairing code\" disconnects a phone you paired before."
            % ("\n                    ".join(addresses), _wifi_settings()["code"]))


def _show_wifi_dialog():
    WifiDialog().exec()


# ---------------------------------------------------------------- when things happen

def _on_timer():
    if mw.state != "review":
        run_sync("timer")


def _peer_files():
    """When each Rev app's file in the folder last changed (None if it isn't there)."""
    times = {}
    for name, _ in core.PEERS:
        try:
            times[name] = os.stat(os.path.join(_folder(), name)).st_mtime_ns
        except OSError:
            times[name] = None
    return times


def _on_watch():
    """Every few seconds: did a Rev app's file just change? Then sync now instead of at the next timer.
    (Only the files' times are looked at, which costs next to nothing.) A change during a review waits
    until the review is over."""
    global _seen_files
    if not mw.col or _running or mw.state == "review":
        return
    now = _peer_files()
    if now != _seen_files:
        _seen_files = now
        run_sync("file changed")


def _on_profile_open():
    global _timer
    QTimer.singleShot(3000, lambda: run_sync("open"))
    minutes = max(1, int(_config().get("interval_minutes", 5)))
    _timer = QTimer(mw)
    _timer.timeout.connect(_on_timer)
    _timer.start(minutes * 60 * 1000)
    global _watch_timer, _seen_files
    _seen_files = _peer_files()   # the sync a few seconds after opening covers what's there now
    _watch_timer = QTimer(mw)
    _watch_timer.timeout.connect(_on_watch)
    _watch_timer.start(3000)
    _start_server()


def _on_profile_close():
    global _timer, _watch_timer
    _stop_server()
    if _watch_timer:
        _watch_timer.stop()
        _watch_timer = None
    if _timer:
        _timer.stop()
        _timer = None
    run_sync("close")


gui_hooks.profile_did_open.append(_on_profile_open)
gui_hooks.profile_will_close.append(_on_profile_close)
gui_hooks.sync_will_start.append(lambda: run_sync("before AnkiWeb sync"))
gui_hooks.sync_did_finish.append(lambda: run_sync("after AnkiWeb sync"))

_action = QAction("Rev Sync now", mw)
_action.triggered.connect(lambda: run_sync("menu", show_result=True))
mw.form.menuTools.addAction(_action)

_wifi_action = QAction("Rev Sync Wi-Fi...", mw)
_wifi_action.triggered.connect(_show_wifi_dialog)
mw.form.menuTools.addAction(_wifi_action)
