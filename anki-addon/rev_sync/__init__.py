"""Rev Sync: keeps Anki and the Rev apps (Rev on iPhone, and Rev for Windows if you use it) in sync through a shared iCloud Drive folder.

When it runs:
  - a few seconds after your profile opens
  - every few minutes while Anki is open (not while you're reviewing)
  - just before and just after Anki syncs with AnkiWeb
  - when you close your profile
  - Tools > Rev Sync now
Before its very first sync it makes a backup of your collection.
"""

import json
import os
import traceback

from aqt import gui_hooks, mw
from aqt.qt import QAction, QTimer
from aqt.utils import showWarning, tooltip

from . import core

ADDON = __name__
STATE_PATH = os.path.join(os.path.dirname(__file__), "user_files", "state.json")
_timer = None
_running = False


def _config():
    return mw.addonManager.getConfig(ADDON) or {}


def _folder():
    folder = _config().get("sync_folder") or ""
    return os.path.expandvars(os.path.expanduser(folder))


def _load_state():
    try:
        with open(STATE_PATH, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def _save_state(state):
    os.makedirs(os.path.dirname(STATE_PATH), exist_ok=True)
    core.write_json_atomic(STATE_PATH, state)


def run_sync(reason="", show_result=False):
    global _running
    if _running or not mw.col:
        return
    folder = _folder()
    if not folder or not os.path.isdir(os.path.dirname(folder.rstrip("\\/"))):
        if show_result:
            showWarning("Rev Sync: the sync folder isn't set or doesn't exist.\n\nSet it in Tools > Add-ons > Rev Sync > Config.")
        return
    _running = True
    try:
        state = _load_state()
        if not state.get("backedUp"):
            mw.col.create_backup(backup_folder=mw.pm.backupFolder(), force=True, wait_for_completion=True)
            state["backedUp"] = True
            _save_state(state)
        report = core.sync(mw.col, folder, state)
        _save_state(state)
        if any(report.values()):
            mw.reset()
        if show_result or any(report.values()):
            parts = ["%d %s" % (n, label) for label, n in report.items() if n]
            tooltip("Rev Sync: " + (", ".join(parts) if parts else "everything up to date"))
    except Exception:
        traceback.print_exc()
        if show_result:
            showWarning("Rev Sync failed:\n\n" + traceback.format_exc())
    finally:
        _running = False


def _on_timer():
    if mw.state != "review":
        run_sync("timer")


def _on_profile_open():
    global _timer
    QTimer.singleShot(3000, lambda: run_sync("open"))
    minutes = max(1, int(_config().get("interval_minutes", 5)))
    _timer = QTimer(mw)
    _timer.timeout.connect(_on_timer)
    _timer.start(minutes * 60 * 1000)


def _on_profile_close():
    global _timer
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
