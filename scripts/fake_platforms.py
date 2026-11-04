#!/usr/bin/env python3
"""خادم تجريبي بيقلّد واجهات المنصات والذكاء الاصطناعي، لفحوصات المسار فقط (ما بيتصل بأي خدمة حقيقية).

الاستعمال: python3 scripts/fake_platforms.py 9090
بيسجّل كل طلب بـ /tmp/fake_platforms.log (المسار + الطريقة + نوع الترويسة، بدون قيم التوكنات)
وبيعرض /__requests لقراءة الطلبات من سكربتات الفحص.
"""
import json
import sys
import threading
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

REQUESTS = []
LOCK = threading.Lock()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):  # صامت
        pass

    def _body(self):
        n = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(n).decode() if n else ""
        ctype = self.headers.get("Content-Type", "")
        if "json" in ctype and raw:
            return json.loads(raw)
        return {k: v[0] for k, v in urllib.parse.parse_qs(raw).items()}

    def _send(self, obj, status=200):
        data = json.dumps(obj).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _bearer(self):
        h = self.headers.get("Authorization", "")
        return h[7:] if h.startswith("Bearer ") else ""

    def _record(self, body):
        with LOCK:
            REQUESTS.append({"method": self.command, "path": self.path.split("?")[0], "bearer": self._bearer(), "body": body})

    def do_GET(self):
        path = self.path.split("?")[0]
        if path == "/__requests":
            return self._send(REQUESTS)
        self._record({})
        self._route("GET", path, {})

    def do_POST(self):
        body = self._body()
        path = self.path.split("?")[0]
        self._record(body)
        self._route("POST", path, body)

    def _route(self, method, path, body):
        token = self._bearer()
        # ---- فيسبوك Graph ----
        if method == "POST" and path == "/oauth/access_token":
            if body.get("grant_type") == "fb_exchange_token":
                if not str(body.get("fb_exchange_token", "")).startswith("SHORT_"):
                    return self._send({"error": {"message": "bad short token"}}, 400)
                return self._send({"access_token": "LONG_USER_TOKEN", "expires_in": 5184000})
            if body.get("code") != "good-code":
                return self._send({"error": {"message": "bad code"}}, 400)
            return self._send({"access_token": "SHORT_USER_TOKEN"})
        if method == "GET" and path == "/me/accounts":
            if token != "LONG_USER_TOKEN":
                return self._send({"error": {"message": "bad token"}}, 401)
            return self._send({"data": [
                {"id": "111", "name": "Page One", "access_token": "PAGETOKEN_ONE"},
                {"id": "222", "name": "Page Two", "access_token": "PAGETOKEN_TWO"},
            ]})
        if method == "POST" and path.endswith("/feed"):
            if not token.startswith("PAGETOKEN_"):
                return self._send({"error": {"message": "bad page token"}}, 401)
            if "FAIL" in body.get("message", ""):
                return self._send({"error": {"message": "simulated failure"}}, 500)
            return self._send({"id": path.split("/")[1] + "_9001"})
        if method == "GET" and path.endswith("/comments"):
            return self._send({"data": [{"id": "c1", "from": {"name": "Ali"}, "message": "كم السعر؟", "created_time": "2026-11-04T10:00:00+0000"}]})
        if method == "POST" and path.endswith("/comments"):
            return self._send({"id": "reply1"})
        if method == "GET" and path.count("/") == 1:
            return self._send({"reactions": {"summary": {"total_count": 7}}, "comments": {"summary": {"total_count": 2}}, "shares": {"count": 1}})
        self._send({"error": {"message": "unknown route " + path}}, 404)


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 9090
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
