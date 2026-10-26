"""Rev Sync over Wi-Fi: a tiny web server inside Anki that Rev on iPhone talks to directly.

iCloud can take many minutes to pass files between the iPhone and the computer. On the same Wi-Fi,
Rev on iPhone can instead hand its file (the same content as rev.json) straight to this add-on and get
Anki's file (the same content as anki.json) back in one go. The sync rules don't change at all: the
add-on applies the file exactly as if iCloud had delivered it. iCloud stays as the fallback, for when
the phone is away from home or Anki is closed.

Safety:
  - Off until you switch it on (Tools > Rev Sync Wi-Fi...).
  - Every request must carry the pairing code shown in Anki. Wrong codes are counted, and after
    a few in a row the server stops listening to anyone for a minute, so the code can't be guessed.
  - It only answers these few requests, and image names can't point outside Anki's media folder.

What Rev on iPhone can ask (all under /rev-sync/v1, with the header "X-Rev-Code: <code>"):
  GET  /hello          is this the right computer, and is the code right?
  POST /sync           body: Rev's file. Answer: {"anki": Anki's file, "report": what changed}
  PUT  /media/<name>   body: an image a Rev card uses
  GET  /media/<name>   an image an Anki card uses

This module doesn't import Anki, so it can be tested on its own (tests/test_wifi.py).
"""

import hmac
import json
import os
import re
import secrets
import socket
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlsplit

API = "/rev-sync/v1"
DEFAULT_PORT = 8765
MAX_BODY = 64 * 1024 * 1024       # 64 MB: more than any Rev file or picture needs
MAX_FAILURES = 5                  # wrong codes in a row before the server pauses
PAUSE_SECONDS = 60
CODE_LETTERS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"   # no 0/O or 1/I, so it's easy to type


class Busy(Exception):
    """Anki can't sync right now (for example you're in the middle of a review).
    Rev on iPhone then uses iCloud instead, as before."""


def new_code():
    """A fresh pairing code like "K7QM-3XPA" (8 letters/digits: about a trillion possibilities)."""
    letters = "".join(secrets.choice(CODE_LETTERS) for _ in range(8))
    return letters[:4] + "-" + letters[4:]


def normalize_code(code):
    """Codes are compared without dashes, spaces or lower/upper case, so typing is forgiving."""
    return re.sub(r"[^A-Z0-9]", "", (code or "").upper())


def safe_media_name(name):
    """The image's file name if it's a plain file name, else None (no folders, no "..", no hidden files)."""
    if not name or len(name) > 200 or name.startswith("."):
        return None
    if "/" in name or "\\" in name or ":" in name or "\0" in name or name != os.path.basename(name):
        return None
    return name


def local_addresses():
    """This computer's addresses on the home network (the ones to type into Rev on iPhone)."""
    found = []
    try:
        # Asks the system which address it would use to reach the internet. Nothing is sent.
        probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            probe.connect(("192.0.2.1", 9))
            found.append(probe.getsockname()[0])
        finally:
            probe.close()
    except OSError:
        pass
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            found.append(info[4][0])
    except OSError:
        pass
    usable = []
    for address in found:
        if address.startswith("127.") or address.startswith("169.254.") or address in usable:
            continue
        usable.append(address)
    return usable


class AddonHandlers:
    """What the add-on does for each request. Anki itself is reached only through the functions given,
    so the tests can use the same code with a throw-away collection.

    on_main(fn)     runs fn on Anki's main thread (where the collection may be used) and returns its result
    sync(wifi_rev)  one full sync using that file from Rev on iPhone; returns (report, Anki's file);
                    raises Busy when Anki can't sync right now
    media_dir       Anki's media folder
    wifi_rev_path   where the newest file received over Wi-Fi is kept (so a late iCloud copy can't undo it)
    """

    def __init__(self, on_main, sync, media_dir, wifi_rev_path):
        self.on_main = on_main
        self.run_sync = sync
        self.media_dir = media_dir
        self.wifi_rev_path = wifi_rev_path

    def stored_rev(self):
        """The newest file Rev on iPhone sent over Wi-Fi, or None."""
        try:
            with open(self.wifi_rev_path, encoding="utf-8") as f:
                return json.load(f)
        except (OSError, ValueError):
            return None

    def sync(self, rev):
        stored = self.stored_rev()
        if stored and (stored.get("writtenAt") or 0) > (rev.get("writtenAt") or 0):
            rev = stored   # an older file than one already received: keep using the newer one
        else:
            os.makedirs(os.path.dirname(self.wifi_rev_path), exist_ok=True)
            tmp = self.wifi_rev_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(rev, f, ensure_ascii=False)
            os.replace(tmp, self.wifi_rev_path)
        report, snapshot = self.on_main(lambda: self.run_sync(rev))
        return {"anki": snapshot, "report": report}

    def put_media(self, name, data):
        # Like images arriving through iCloud: a picture already in Anki is never replaced.
        path = os.path.join(self.media_dir, name)
        if not os.path.exists(path):
            tmp = path + ".revsync-tmp"
            with open(tmp, "wb") as f:
                f.write(data)
            os.replace(tmp, path)

    def get_media(self, name):
        try:
            with open(os.path.join(self.media_dir, name), "rb") as f:
                return f.read()
        except OSError:
            return None


class WifiServer:
    """Listens on the home network and passes requests on to `handlers`, which has:
        sync(rev_snapshot) -> {"anki": ..., "report": ...}   (may raise Busy)
        put_media(name, data)
        get_media(name) -> bytes or None
    """

    def __init__(self, handlers, code, port=DEFAULT_PORT, host="0.0.0.0"):
        self.handlers = handlers
        self.code = normalize_code(code)
        self.host = host
        self.port = port
        self._httpd = None
        self._lock = threading.Lock()
        self._failures = 0
        self._paused_until = 0.0

    # ---- starting and stopping

    def start(self):
        """Starts listening. Raises OSError if the port is already used by another program."""
        self._httpd = ThreadingHTTPServer((self.host, self.port), _handler_for(self))
        self._httpd.daemon_threads = True
        self.port = self._httpd.server_address[1]
        threading.Thread(target=self._httpd.serve_forever, name="rev-sync-wifi", daemon=True).start()

    def stop(self):
        if self._httpd:
            self._httpd.shutdown()
            self._httpd.server_close()
            self._httpd = None

    @property
    def running(self):
        return self._httpd is not None

    # ---- the pairing code

    def check_code(self, given):
        """"ok", "wrong" or "paused" (after too many wrong codes in a row)."""
        with self._lock:
            if time.time() < self._paused_until:
                return "paused"
            if self.code and hmac.compare_digest(normalize_code(given).encode(), self.code.encode()):
                self._failures = 0
                return "ok"
            self._failures += 1
            if self._failures >= MAX_FAILURES:
                self._failures = 0
                self._paused_until = time.time() + PAUSE_SECONDS
            return "wrong"


def _handler_for(server):
    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"
        server_version = "RevSync/1"

        def log_message(self, *args):
            pass  # keep Anki's console quiet

        # ---- answers

        def _send(self, status, body=b"", content_type="application/json"):
            if isinstance(body, (dict, list)):
                body = json.dumps(body, ensure_ascii=False).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            if body:
                self.wfile.write(body)

        def _error(self, status, message):
            self._send(status, {"error": message})

        # ---- checks every request goes through

        def _route(self):
            """The part of the address after /rev-sync/v1, or None (after answering) if the request is refused."""
            path = urlsplit(self.path).path
            if not path.startswith(API + "/"):
                self._error(404, "not found")
                return None
            verdict = server.check_code(self.headers.get("X-Rev-Code"))
            if verdict == "paused":
                self._error(429, "too many wrong codes; try again in a minute")
                return None
            if verdict != "ok":
                self._error(401, "wrong pairing code")
                return None
            return path[len(API):]

        def _body(self):
            try:
                length = int(self.headers.get("Content-Length") or 0)
            except ValueError:
                length = -1
            if length < 0 or length > MAX_BODY:
                self._error(413, "too large")
                self.close_connection = True
                return None
            return self.rfile.read(length) if length else b""

        def _media_name(self, route):
            name = safe_media_name(unquote(route[len("/media/"):]))
            if not name:
                self._error(400, "bad file name")
            return name

        # ---- the requests

        def do_GET(self):
            route = self._route()
            if route is None:
                return
            if route == "/hello":
                self._send(200, {"app": "rev-sync", "api": 1, "computer": socket.gethostname()})
            elif route.startswith("/media/"):
                name = self._media_name(route)
                if not name:
                    return
                data = server.handlers.get_media(name)
                if data is None:
                    self._error(404, "no such image")
                else:
                    self._send(200, data, "application/octet-stream")
            else:
                self._error(404, "not found")

        def do_POST(self):
            route = self._route()
            if route is None:
                return
            if route != "/sync":
                self._error(404, "not found")
                return
            body = self._body()
            if body is None:
                return
            try:
                rev = json.loads(body.decode("utf-8"))
                if not isinstance(rev, dict) or not isinstance(rev.get("cards"), list):
                    raise ValueError("not a Rev file")
            except ValueError:
                self._error(400, "not a Rev file")
                return
            try:
                self._send(200, server.handlers.sync(rev))
            except Busy as busy:
                self._error(503, str(busy) or "Anki is busy")
            except Exception as error:   # shown on the iPhone; Rev then falls back to iCloud
                self._error(500, "sync failed in Anki: %s" % error)

        def do_PUT(self):
            route = self._route()
            if route is None:
                return
            if not route.startswith("/media/"):
                self._error(404, "not found")
                return
            name = self._media_name(route)
            if not name:
                return
            body = self._body()
            if body is None:
                return
            server.handlers.put_media(name, body)
            self._send(204)

    return Handler
