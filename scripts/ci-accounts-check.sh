#!/usr/bin/env bash
# فحص ربط فيسبوك وتشفير التوكنات (مع خادم تجريبي بدل فيسبوك الحقيقي).
# الاستعمال: scripts/ci-accounts-check.sh http://127.0.0.1:8080 http://127.0.0.1:9090
set -eu

# مهلة لكل نداء: ما بنقبل ينعلّق الفحص بلا نهاية
curl() { command curl --connect-timeout 10 --max-time 60 "$@"; }

BASE="${1:-http://127.0.0.1:8080}"
FAKE="${2:-http://127.0.0.1:9090}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
expect() { [ "$(cat /tmp/code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(cat /tmp/code))"; }; }
call() { # call <METHOD> <path> <bearer> [json]
  curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code
}
psqlq() { PGHOST=127.0.0.1 PGUSER=smoke PGPASSWORD=smoke-test-password-not-used-anywhere PGDATABASE=smoke psql -q -v ON_ERROR_STOP=1 -tA "$@"; }

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"acc-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Acc\"}"
OWNER=$(jq -r .accessToken /tmp/body); WS=$(jq -r .workspace.id /tmp/body)

echo "-- رابط الربط"
call GET /api/accounts/facebook/connect-url "$OWNER"; expect 200 "connect-url"
URL=$(jq -r .url /tmp/body)
case "$URL" in *client_id=ci-app-id*state=*scope=*) ;; *) fail "bad connect url: $URL";; esac
STATE=$(printf '%s' "$URL" | sed -E 's/.*[?&]state=([^&]*).*/\1/')

echo "-- رجعة فيسبوك: state مزوّر أو كود غلط ما بيربط"
curl -s -o /dev/null -w "%{http_code} %{redirect_url}" "$BASE/api/accounts/facebook/callback?code=good-code&state=forged.value" > /tmp/out
grep -q "error=state_invalid" /tmp/out || fail "forged state must be refused: $(cat /tmp/out)"
curl -s -o /dev/null -w "%{http_code} %{redirect_url}" "$BASE/api/accounts/facebook/callback?code=bad-code&state=$STATE" > /tmp/out
grep -q "error=platform_error" /tmp/out || fail "bad code must be a platform error: $(cat /tmp/out)"

echo "-- رجعة فيسبوك الصحيحة بتربط صفحتين"
curl -s -o /dev/null -w "%{http_code} %{redirect_url}" "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE" > /tmp/out
grep -q "connected=2" /tmp/out || fail "expected connected=2: $(cat /tmp/out)"
call GET /api/accounts "$OWNER"; expect 200 "list accounts"
[ "$(jq length /tmp/body)" = 2 ] || fail "expected 2 accounts"
jq -e '.[0] | has("encAccessToken") or has("accessToken") | not' /tmp/body > /dev/null || fail "tokens must never be returned"

echo "-- التوكنات مشفّرة بقاعدة البيانات (مش نص)"
[ "$(psqlq -c "select count(*) from social_accounts where workspace_id='$WS' and enc_access_token is not null and length(enc_access_token) > 28")" = 2 ] || fail "tokens must be stored encrypted"
[ "$(psqlq -c "select count(*) from social_accounts where workspace_id='$WS' and position('PAGETOKEN' in encode(enc_access_token,'escape')) > 0")" = 0 ] || fail "plain token found in database"

echo "-- إعادة الربط ما بتكرر الحسابات"
curl -s -o /dev/null "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE"
call GET /api/accounts "$OWNER"; [ "$(jq length /tmp/body)" = 2 ] || fail "reconnect must not duplicate accounts"

echo "-- التوكنات ما بتنكتب بأي طلب لفيسبوك بالرابط"
curl -s "$FAKE/__requests" | jq -e 'all(.[]; (.path | contains("PAGETOKEN") | not))' > /dev/null || fail "token leaked into a URL"

echo "-- المشاهد ما بيربط ولا بيفصل"
curl -s -o /tmp/body -X POST "$BASE/api/workspace/invitations" -H "Authorization: Bearer $OWNER" -H 'Content-Type: application/json' -d "{\"email\":\"acc-viewer-$SUFFIX@example.com\",\"role\":\"viewer\"}"
CODE=$(jq -r .code /tmp/body)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"acc-viewer-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"
VIEWER=$(jq -r .accessToken /tmp/body)
call GET /api/accounts/facebook/connect-url "$VIEWER"; expect 403 "viewer must not connect"
call GET /api/accounts "$VIEWER"; expect 200 "viewer can list"
ID=$(curl -s "$BASE/api/accounts" -H "Authorization: Bearer $VIEWER" | jq -r '.[0].id')
call DELETE "/api/accounts/$ID" "$VIEWER"; expect 403 "viewer must not disconnect"

echo "-- المالك بيفصل حساب"
call DELETE "/api/accounts/$ID" "$OWNER"; expect 204 "disconnect"
call GET /api/accounts "$OWNER"; [ "$(jq length /tmp/body)" = 1 ] || fail "one account must remain"

echo "accounts checks ok"
