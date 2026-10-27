"""A phone that hangs up halfway mustn't make Anki show an error window.

Anki shows anything printed to the console as "Anki encountered a problem", and Python's web server prints
a traceback when a connection is reset. This checks the add-on's server stays quiet then, and keeps working.

Usage: python test_hangup.py   (doesn't need Anki)
"""

import contextlib
import io
import os
import socket
import struct
import sys
import time
import urllib.request

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "rev_sync"))
import wifi  # noqa: E402


class Handlers:
    def sync(self, rev):
        return {"anki": None, "report": {}}

    def put_media(self, name, data):
        pass

    def get_media(self, name):
        return None


def hang_up(port, send=b""):
    s = socket.create_connection(("127.0.0.1", port))
    if send:
        s.sendall(send)
    # SO_LINGER 0: close with a reset, the way a phone dropping off the Wi-Fi looks to the server.
    s.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER, struct.pack("ii", 1, 0))
    s.close()


server = wifi.WifiServer(Handlers(), wifi.new_code(), port=0, host="127.0.0.1")
server.start()
err = io.StringIO()
with contextlib.redirect_stderr(err):
    hang_up(server.port)                               # hangs up before asking anything
    hang_up(server.port, b"GET /rev-sync/v1/ping HT")  # hangs up mid-request
    time.sleep(0.5)

failures = []
if err.getvalue():
    failures.append("server printed to the console:\n" + err.getvalue())
else:
    print("  ok   no console output when a phone hangs up")

# Still answers afterwards.
try:
    urllib.request.urlopen("http://127.0.0.1:%d/" % server.port, timeout=5)
    print("  ok   server still answers")
except urllib.error.HTTPError:
    print("  ok   server still answers")
except Exception as e:  # noqa: BLE001
    failures.append("server stopped answering: %r" % e)
server.stop()

for f in failures:
    print("  FAIL " + f)
sys.exit(1 if failures else 0)
