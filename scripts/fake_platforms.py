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


def make_png(width=96, height=64):
    """PNG صالح بلون واحد (بدون مكتبات)."""
    import struct
    import zlib
    raw = b"".join(b"\x00" + b"\x20\x80\xc0" * width for _ in range(height))
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):  # صامت
        pass

    def _body(self):
        n = int(self.headers.get("Content-Length") or 0)
        data = self.rfile.read(n) if n else b""
        ctype = self.headers.get("Content-Type", "")
        if "multipart" in ctype:
            import re
            fields = dict(re.findall(r'name="?([^";\r\n]+)"?[^\r\n]*\r\n(?:[A-Za-z-]+:[^\r\n]*\r\n)*\r\n([^\r]*)\r\n', data.decode("utf-8", "replace")))
            return {"_multipart": True, "size": n, **fields}
        if ctype.startswith("video/") or ctype.startswith("image/"):
            return {"_binary": True, "size": n}
        raw = data.decode()
        if "json" in ctype and raw:
            return json.loads(raw)
        return {k: v[0] for k, v in urllib.parse.parse_qs(raw).items()}

    def _send(self, obj, status=200, headers=None):
        data = json.dumps(obj).encode()
        self.send_response(status)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _bearer(self):
        h = self.headers.get("Authorization", "")
        return h[7:] if h.startswith("Bearer ") else ""

    def _record(self, body):
        with LOCK:
            REQUESTS.append({"method": self.command, "path": self.path.split("?")[0], "query": self.path.partition("?")[2], "bearer": self._bearer(),
                             "basic": self.headers.get("Authorization", "").startswith("Basic "), "version": self.headers.get("LinkedIn-Version"), "body": body})

    def do_GET(self):
        path = self.path.split("?")[0]
        if path == "/__requests":
            return self._send(REQUESTS)
        self._record({})
        self._route("GET", path, {})

    def do_PUT(self):
        body = self._body()
        path = self.path.split("?")[0]
        self._record(body)
        self._route("PUT", path, body)

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
                {"id": "111", "name": "Page One", "access_token": "PAGETOKEN_ONE", "instagram_business_account": {"id": "ig111", "username": "page_one_ig"}},
                {"id": "222", "name": "Page Two", "access_token": "PAGETOKEN_TWO"},
            ]})
        if method == "POST" and path.endswith("/feed"):
            if not token.startswith("PAGETOKEN_"):
                return self._send({"error": {"message": "bad page token"}}, 401)
            if "BADREQ" in body.get("message", ""):
                return self._send({"error": {"message": "simulated bad request"}}, 400)
            if "FAIL" in body.get("message", ""):
                return self._send({"error": {"message": "simulated failure"}}, 500)
            return self._send({"id": path.split("/")[1] + "_9001"})
        if method == "GET" and path.endswith("/comments"):
            return self._send({"data": [{"id": "c1", "from": {"name": "Ali"}, "message": "كم السعر؟", "created_time": "2026-11-04T10:00:00+0000"}]})
        if method == "POST" and path.endswith("/comments"):
            return self._send({"id": "reply1"})
        # ---- إنستغرام ----
        if method == "GET" and path.startswith("/container_"):
            return self._send({"status_code": "FINISHED", "id": path[1:]})
        if method == "POST" and path.startswith("/ig111/") and path.endswith("/media"):
            if not token.startswith("PAGETOKEN_"):
                return self._send({"error": {"message": "bad page token"}}, 401)
            return self._send({"id": "container_%d" % len(REQUESTS)})
        if method == "POST" and path == "/ig111/media_publish":
            return self._send({"id": "igpost_1"})
        if method == "POST" and path.endswith("/replies"):
            return self._send({"id": "igreply"})
        # ---- تيك توك ----
        if path == "/tiktok/v2/oauth/token/" and method == "POST":
            if body.get("grant_type") == "refresh_token":
                if body.get("refresh_token") != "TT_REFRESH_1":
                    return self._send({"error": "invalid_grant", "error_description": "bad refresh"}, 400)
                return self._send({"access_token": "TT_ACCESS_2", "refresh_token": "TT_REFRESH_2", "expires_in": 86400, "open_id": "tt_open_1"})
            if body.get("code") != "good-code":
                return self._send({"error": "invalid_grant", "error_description": "bad code"}, 400)
            return self._send({"access_token": "TT_ACCESS_1", "refresh_token": "TT_REFRESH_1", "expires_in": 1, "open_id": "tt_open_1"})
        if path == "/tiktok/v2/user/info/" and method == "GET":
            return self._send({"data": {"user": {"open_id": "tt_open_1", "display_name": "TT Shop"}}, "error": {"code": "ok"}})
        if path == "/tiktok/v2/post/publish/video/init/" and method == "POST":
            if token != "TT_ACCESS_2":
                return self._send({"error": {"code": "access_token_invalid", "message": "token expired"}}, 401)
            return self._send({"data": {"publish_id": "pub1", "upload_url": "http://127.0.0.1:9090/tiktok/upload/pub1"}, "error": {"code": "ok"}})
        if path == "/tiktok/upload/pub1" and method == "PUT":
            return self._send({})
        if path == "/tiktok/v2/post/publish/status/fetch/" and method == "POST":
            return self._send({"data": {"status": "PUBLISH_COMPLETE", "publicaly_available_post_id": ["7001"]}, "error": {"code": "ok"}})
        # ---- X ----
        if path == "/x/2/oauth2/token" and method == "POST":
            if body.get("grant_type") == "refresh_token":
                return self._send({"access_token": "X_ACCESS_2", "refresh_token": "X_REFRESH_2", "expires_in": 7200})
            if body.get("code") != "good-code" or not body.get("code_verifier"):
                return self._send({"error": "invalid_request", "error_description": "bad code or verifier"}, 400)
            return self._send({"access_token": "X_ACCESS_1", "refresh_token": "X_REFRESH_1", "expires_in": 7200})
        if path == "/x/2/users/me" and method == "GET":
            return self._send({"data": {"id": "x_user_1", "username": "zaki_shop"}})
        if path == "/x/2/media/upload" and method == "POST":
            return self._send({"data": {"id": "x_media_1"}})
        if path == "/x/2/tweets" and method == "POST":
            if not token.startswith("X_ACCESS_"):
                return self._send({"title": "Unauthorized"}, 401)
            return self._send({"data": {"id": "x_tweet_1", "text": body.get("text", "")}})
        # ---- يوتيوب ----
        if path == "/google/token" and method == "POST":
            if body.get("code") != "good-code":
                return self._send({"error": "invalid_grant", "error_description": "bad code"}, 400)
            return self._send({"access_token": "YT_ACCESS_1", "refresh_token": "YT_REFRESH_1", "expires_in": 3600})
        if path == "/yt/youtube/v3/channels" and method == "GET":
            return self._send({"items": [{"id": "UCfake1", "snippet": {"title": "Zaki Channel"}}]})
        if path == "/yt/upload/youtube/v3/videos" and method == "POST":
            if not token.startswith("YT_ACCESS_"):
                return self._send({"error": {"message": "bad token"}}, 401)
            return self._send({}, 200, {"Location": "http://127.0.0.1:9090/yt/upload/session1"})
        if path == "/yt/upload/session1" and method == "PUT":
            return self._send({"id": "yt_vid_1"})
        # ---- لينكدإن ----
        if path == "/li/oauth/v2/accessToken" and method == "POST":
            if body.get("code") != "good-code":
                return self._send({"error": "invalid_request", "error_description": "bad code"}, 400)
            return self._send({"access_token": "LI_ACCESS_1", "expires_in": 5184000})
        if path == "/li/v2/userinfo" and method == "GET":
            return self._send({"sub": "li_user_1", "name": "Zaki LI"})
        if path == "/li/rest/posts" and method == "POST":
            return self._send({}, 201, {"x-restli-id": "urn:li:share:9001"})
        if method == "GET" and path.count("/") == 1:
            return self._send({"reactions": {"summary": {"total_count": 7}}, "comments": {"summary": {"total_count": 2}}, "shares": {"count": 1}})
        if method == "POST" and path.endswith("/photos"):
            if not token.startswith("PAGETOKEN_"):
                return self._send({"error": {"message": "bad page token"}}, 401)
            page = path.split("/")[1]
            if body.get("published") == "false":
                return self._send({"id": "unpub_%d" % len(REQUESTS)})
            return self._send({"id": "photo_%d" % len(REQUESTS), "post_id": page + "_photo"})
        if method == "POST" and path.endswith("/videos"):
            if not token.startswith("PAGETOKEN_"):
                return self._send({"error": {"message": "bad page token"}}, 401)
            return self._send({"id": "video_%d" % len(REQUESTS)})
        if method == "POST" and path == "/openai/images/generations":
            if not token:
                return self._send({"error": "no key"}, 401)
            import base64
            return self._send({"data": [{"b64_json": base64.b64encode(make_png()).decode()}]})
        # ---- الذكاء الاصطناعي ----
        if method == "POST" and path in ("/claude/v1/messages", "/openai/chat/completions", "/local/chat/completions"):
            who = path.split("/")[1]
            prompt = json.dumps(body.get("messages", []), ensure_ascii=False)
            if who == "claude" and not self.headers.get("x-api-key"):
                return self._send({"error": "no key"}, 401)
            if who == "openai" and not token:
                return self._send({"error": "no key"}, 401)
            if who == "claude" and "CLAUDE_DOWN" in prompt:
                return self._send({"error": "overloaded"}, 500)
            usage_in, usage_out = (1500, 600) if "BIGUSAGE" in prompt else (5, 7)
            text = who + " says hi"
            if who != "claude" and body.get("stream"):
                data = "".join("data: " + json.dumps({"choices": [{"delta": {"content": part}}]}) + "\n\n" for part in (who + " ", "streams ", "ok"))
                data += "data: [DONE]\n\n"
                raw = data.encode()
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Content-Length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)
                return
            if who == "claude":
                return self._send({"content": [{"type": "text", "text": text}], "usage": {"input_tokens": usage_in, "output_tokens": usage_out}})
            return self._send({"choices": [{"message": {"content": text}}], "usage": {"prompt_tokens": usage_in, "completion_tokens": usage_out}})
        self._send({"error": {"message": "unknown route " + path}}, 404)


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 9090
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
