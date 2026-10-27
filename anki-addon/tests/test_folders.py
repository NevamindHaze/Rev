"""The add-on's "sync_folder" setting on Windows and on Linux (where there's no iCloud Drive for Windows).

Usage: python test_folders.py
Only uses a temporary folder as the home folder; nothing else is touched.
"""

import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "rev_sync"))
import core  # noqa: E402

failures = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        failures.append(message)


def main():
    home = tempfile.mkdtemp(prefix="revsync-home-")
    usual = os.path.join(home, "Rev Sync")
    try:
        print("1. On Linux, the original (Windows) setting means ~/Rev Sync, which is made")
        folder = core.sync_folder("%USERPROFILE%\\iCloudDrive\\Rev Sync", windows=False, home=home)
        check(folder == usual, "the folder is %s" % folder)
        check(os.path.isdir(usual), "...and it exists now")
        check(core.sync_folder("", windows=False, home=home) == usual, "no setting: the same")

        print("2. On Linux, ~ and $HOME are understood")
        check(core.sync_folder("~/Documents/Rev Sync", windows=False, home=home) == home + "/Documents/Rev Sync", "~/Documents/Rev Sync")
        check(core.sync_folder("$HOME/Rev Sync", windows=False, home=home) == home + "/Rev Sync", "$HOME/Rev Sync")
        check(core.sync_folder("${HOME}/Rev Sync", windows=False, home=home) == home + "/Rev Sync", "${HOME}/Rev Sync")
        check(core.sync_folder("/mnt/data/Rev Sync", windows=False, home=home) == "/mnt/data/Rev Sync", "a full path stays")

        print("3. On Windows nothing changes")
        os.environ["REVSYNC_TEST_HOME"] = "C:\\Users\\test"
        check(core.sync_folder("%REVSYNC_TEST_HOME%\\iCloudDrive\\Rev Sync", windows=True) == "C:\\Users\\test\\iCloudDrive\\Rev Sync",
              "variables are filled in as before")
    finally:
        shutil.rmtree(home, ignore_errors=True)
    print("\n%s" % ("ALL PASSED" if not failures else "%d FAILED" % len(failures)))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
