#!/usr/bin/env bash
# فحص النشر عبر الطابور (ريديس + عامل)، مع خادم فيسبوك تجريبي.
# الاستعمال: scripts/ci-publish-check.sh http://127.0.0.1:8080 http://127.0.0.1:9090
set -eu

BASE="${1:-http://127.0.0.1:8080}"
FAKE="${2:-http://127.0.0.1:9090}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
call() { # call <METHOD> <path> <bearer> [json]  -> /tmp/body , /tmp/code
  curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code
}
expect() { [ "$(cat /tmp/code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(cat /tmp/code))"; }; }
wait_status() { # wait_status <postId> <bearer> <status> [seconds]
  for _ in $(seq 1 "${4:-40}"); do
    call GET "/api/posts/$1" "$2"
    [ "$(jq -r .status /tmp/body)" = "$3" ] && return 0
    sleep 1
  done
  cat /tmp/body; echo; fail "post $1 never reached $3"
}

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"pub-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Pub\"}"
OWNER=$(jq -r .accessToken /tmp/body)

# ربط صفحتين (نفس مسار فحص الحسابات)
call GET /api/accounts/facebook/connect-url "$OWNER"; expect 200 "connect-url"
STATE=$(jq -r .url /tmp/body | sed -E 's/.*[?&]state=([^&]*).*/\1/')
curl -s -o /dev/null "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE"
call GET /api/accounts "$OWNER"
ACC1=$(jq -r '.[] | select(.displayName=="Page One") | .id' /tmp/body)
ACC2=$(jq -r '.[] | select(.displayName=="Page Two") | .id' /tmp/body)
[ -n "$ACC1" ] && [ -n "$ACC2" ] || fail "accounts missing"

echo "-- مسودة ثم نشر على صفحتين"
call POST /api/posts "$OWNER" "{\"body\":\"مرحبا من الاختبار\",\"accountIds\":[\"$ACC1\",\"$ACC2\"]}"; expect 200 "create draft"
POST=$(jq -r .id /tmp/body)
[ "$(jq -r .status /tmp/body)" = draft ] || fail "new post must be draft"
call POST "/api/posts/$POST/publish" "$OWNER"; expect 200 "publish"
[ "$(jq -r .status /tmp/body)" = publishing ] || fail "publish must only queue (status publishing), not publish inline"
wait_status "$POST" "$OWNER" published
[ "$(jq '[.targets[] | select(.status=="published" and (.remotePostId | endswith("_9001")))] | length' /tmp/body)" = 2 ] || fail "both targets must be published with a remote id"

echo "-- الخادم التجريبي استلم النشر بتوكن الصفحة الصحيح"
curl -s "$FAKE/__requests" | jq -e '[.[] | select(.method=="POST" and (.path=="/111/feed" or .path=="/222/feed"))] | length >= 2' > /dev/null || fail "feed requests missing"
curl -s "$FAKE/__requests" | jq -e '[.[] | select(.path=="/111/feed" and .bearer=="PAGETOKEN_ONE" and .body.message=="مرحبا من الاختبار")] | length == 1' > /dev/null || fail "page one must be published with its own token"

echo "-- نشر مرة ثانية ممنوع"
call POST "/api/posts/$POST/publish" "$OWNER"; expect 409 "already published"

echo "-- فشل المنصة: إعادة محاولات ثم failed"
call POST /api/posts "$OWNER" "{\"body\":\"FAIL هذا المنشور لازم يفشل\",\"accountIds\":[\"$ACC1\"]}"; expect 200 "create failing"
BAD=$(jq -r .id /tmp/body)
call POST "/api/posts/$BAD/publish" "$OWNER"; expect 200 "publish failing"
wait_status "$BAD" "$OWNER" failed 60
[ "$(jq -r '.targets[0].error' /tmp/body)" != null ] || fail "failed target must carry an error"
TRIES=$(curl -s "$FAKE/__requests" | jq '[.[] | select(.path=="/111/feed" and (.body.message | startswith("FAIL")))] | length')
[ "$TRIES" = 3 ] || fail "expected 3 attempts (1 + 2 retries), got $TRIES"

echo "-- المشاهد ما بينشر ولا بيكتب"
call POST /api/workspace/invitations "$OWNER" "{\"email\":\"pub-viewer-$SUFFIX@example.com\",\"role\":\"viewer\"}"
CODE=$(jq -r .code /tmp/body)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"pub-viewer-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"
VIEWER=$(jq -r .accessToken /tmp/body)
call GET /api/posts "$VIEWER"; expect 200 "viewer can read posts"
call POST /api/posts "$VIEWER" "{\"body\":\"x\",\"accountIds\":[]}"; expect 403 "viewer must not create"
call POST "/api/posts/$BAD/publish" "$VIEWER"; expect 403 "viewer must not publish"

echo "-- مساحة ثانية ما بتشوف منشورات هالمساحة"
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"pub-other-$SUFFIX@example.com\",\"password\":\"$PASS\"}"
OTHER=$(jq -r .accessToken /tmp/body)
call GET /api/posts "$OTHER"; [ "$(jq length /tmp/body)" = 0 ] || fail "other workspace must not see posts"
call GET "/api/posts/$POST" "$OTHER"; expect 404 "foreign post must be invisible"

echo "publish checks ok"
