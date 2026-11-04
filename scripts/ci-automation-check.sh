#!/usr/bin/env bash
# فحص الأتمتة: بروكسي ثابت، جامع بيانات، مجيب آلي بتأخير، نسخ منشور بفواصل، تحليلات وتوصيات.
set -eu

BASE="${1:-http://127.0.0.1:8080}"
FAKE="${2:-http://127.0.0.1:9090}"
PROXY="${3:-http://127.0.0.1:9091}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
call() { curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code; }
expect() { [ "$(cat /tmp/code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(cat /tmp/code))"; }; }
field() { jq -r "$1" /tmp/body; }
requests() { curl -s "$FAKE/__requests"; }
proxied() { curl -s "$PROXY/__requests"; }
wait_post() { for _ in $(seq 1 60); do call GET "/api/posts/$1" "$2"; [ "$(field .status)" = "$3" ] && return 0; sleep 1; done; cat /tmp/body; echo; fail "post $1 never reached $3"; }

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"aut-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Aut\"}"
OWNER=$(jq -r .accessToken /tmp/body)
call GET /api/accounts/facebook/connect-url "$OWNER"
STATE=$(field .url | sed -E 's/.*[?&]state=([^&]*).*/\1/')
curl -s -o /dev/null "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE"
call GET /api/accounts "$OWNER"
ACC1=$(jq -r '.[] | select(.displayName=="Page One") | .id' /tmp/body)
ACC2=$(jq -r '.[] | select(.displayName=="Page Two") | .id' /tmp/body)

mkpost() { call POST /api/posts "$OWNER" "{\"body\":\"$1\",\"accountIds\":[\"$2\"]}"; field .id; }

echo "-- بروكسيات: تحقق، فحص صحة، عناوين"
call POST /api/proxies "$OWNER" '{"name":"bad","host":"127.0.0.1","port":70000}'; expect 400 "invalid port"
call POST /api/proxies "$OWNER" '{"name":"سليم","host":"127.0.0.1","port":9091,"username":"user","password":"p@ss word"}'; expect 200 "create healthy proxy"
PX_OK=$(field .id); [ "$(field .status)" = healthy ] && [ "$(field .hasCredentials)" = true ] || fail "proxy must be healthy with credentials flag"
jq -e 'has("password") | not' /tmp/body > /dev/null || fail "proxy secrets must not be returned"
[ "$(PGHOST=127.0.0.1 PGUSER=smoke PGPASSWORD=smoke-test-password-not-used-anywhere PGDATABASE=smoke psql -q -tA -c "select count(*) from proxies where position('p@ss' in encode(enc_credentials,'escape')) > 0")" = 0 ] || fail "proxy credentials must be stored encrypted"
call POST /api/proxies "$OWNER" '{"name":"ميت","host":"127.0.0.1","port":9}'; expect 200 "create dead proxy"
PX_BAD=$(field .id); [ "$(field .status)" = unhealthy ] || fail "dead proxy must be unhealthy"

echo "-- حساب مربوط ببروكسي سليم: النشر بيمر منه (وبيوصل للوجهة)"
call PUT "/api/accounts/$ACC1/proxy" "$OWNER" "{\"proxyId\":\"$PX_OK\"}"; expect 204 "assign proxy"
P=$(mkpost "عبر البروكسي" "$ACC1")
call POST "/api/posts/$P/publish" "$OWNER"; expect 200 "publish"; wait_post "$P" "$OWNER" published
proxied | jq -e '[.[] | select(.path=="/111/feed" and .proxied==true and .body.message=="عبر البروكسي")] | length == 1' > /dev/null || fail "the request must go through the proxy"
requests | jq -e '[.[] | select(.path=="/111/feed" and .body.message=="عبر البروكسي")] | length == 0' > /dev/null || fail "the request must not bypass the proxy"

echo "-- بروكسي غير سليم: النشر ما بيطلع مباشر (بيفشل بدل ما يكشف عنوان السيرفر)"
call PUT "/api/accounts/$ACC1/proxy" "$OWNER" "{\"proxyId\":\"$PX_BAD\"}"; expect 204 "assign dead proxy"
P=$(mkpost "ما لازم يطلع مباشر" "$ACC1")
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" failed
[ "$(field '.targets[0].error')" = "503: proxy_unhealthy" ] || fail "unhealthy proxy error: $(field '.targets[0].error')"
requests | jq -e '[.[] | select(.body.message=="ما لازم يطلع مباشر")] | length == 0' > /dev/null || fail "no direct request allowed with a dead proxy"
call PUT "/api/accounts/$ACC1/proxy" "$OWNER" '{"proxyId":null}'; expect 204 "unassign"

echo "-- ربط تلقائي: كل حساب بدون بروكسي بياخد أقل بروكسي سليم حمولة"
call POST /api/proxies/auto-assign "$OWNER"; expect 200 "auto assign"
[ "$(field .assigned)" = 2 ] || fail "two accounts must be assigned"
call GET /api/proxies "$OWNER"; [ "$(jq '[.[] | select(.id=="'$PX_OK'")][0].accounts' /tmp/body)" = 2 ] || fail "both accounts on the only healthy proxy"
call PUT "/api/accounts/$ACC1/proxy" "$OWNER" '{"proxyId":null}'; call PUT "/api/accounts/$ACC2/proxy" "$OWNER" '{"proxyId":null}'
call DELETE "/api/proxies/$PX_BAD" "$OWNER"; expect 204 "delete proxy"

echo "-- الجامع: منشور منشور على فيسبوك، وبعدها جمع المقاييس والتعليقات"
P=$(mkpost "منشور للجمع" "$ACC1")
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" published
call POST /api/automation/collect-now "$OWNER"; expect 202 "collect now"
for _ in $(seq 1 40); do call GET /api/automation/status "$OWNER"; [ "$(field .comments)" -ge 1 ] && break; sleep 1; done
[ "$(field .comments)" -ge 1 ] || fail "collector must store comments"
call POST /api/automation/collect-now "$OWNER"; sleep 6
call GET /api/automation/status "$OWNER"; [ "$(field .comments)" = 1 ] || fail "collecting again must not duplicate comments (got $(field .comments))"

echo "-- التحليلات: آخر لقطة لكل منشور"
call GET "/api/analytics/summary?days=7" "$OWNER"; expect 200 "summary"
[ "$(field .totals.posts)" = 2 ] || fail "published posts counted (got $(field .totals.posts))"
[ "$(field .totals.reactions)" = 14 ] || fail "reactions = 7 per published post from the latest snapshot (got $(field .totals.reactions))"
[ "$(field '.byPlatform[0].platform')" = facebook ] || fail "by platform"
[ "$(field '.byDay | length')" -ge 1 ] || fail "by day"
call POST /api/analytics/recommendations "$OWNER" '{"days":7}'; expect 200 "recommendations"
[ -n "$(field .text)" ] && [ "$(field .posts)" -ge 2 ] || fail "recommendation text"

echo "-- المجيب الآلي: قاعدة مطفية ما بترد، وقاعدة مفعّلة بترد مرة وحدة بعد تأخير"
call POST /api/automation/rules "$OWNER" '{"name":"مطفية","matchPattern":"","isEnabled":false,"delayMinSeconds":1,"delayMaxSeconds":2}'; expect 200 "rule off"
[ "$(field .isEnabled)" = false ] || fail "rule must stay off until enabled"
call POST /api/automation/rules "$OWNER" '{"name":"أسعار","matchPattern":"سعر","isEnabled":true,"delayMinSeconds":1,"delayMaxSeconds":2}'; expect 200 "rule on"
call POST /api/automation/rules "$OWNER" '{"name":"سيئة","matchPattern":"x","isEnabled":false,"delayMinSeconds":10,"delayMaxSeconds":5}'; expect 400 "delay range"
for _ in $(seq 1 100); do call GET /api/automation/replies "$OWNER"; [ "$(jq '[.[] | select(.status=="sent")] | length' /tmp/body)" -ge 1 ] && break; sleep 2; done
[ "$(jq '[.[] | select(.status=="sent")] | length' /tmp/body)" = 1 ] || { cat /tmp/body; fail "exactly one reply must be sent"; }
[ "$(jq -r '.[0].text' /tmp/body)" = "local says hi" ] || fail "reply text must come from the ai router (reply task goes to the local model)"
requests | jq -e '[.[] | select(.method=="POST" and .path=="/c1/comments" and .bearer=="PAGETOKEN_ONE" and .body.message=="local says hi")] | length == 1' > /dev/null || fail "the reply must reach the platform with the page token"
sleep 65
call GET /api/automation/replies "$OWNER"; [ "$(jq length /tmp/body)" = 1 ] || fail "a comment must never be answered twice"

echo "-- نسخ منشور لعدة حسابات بفواصل عشوائية"
P=$(mkpost "للنسخ" "$ACC1")
call POST "/api/posts/$P/replicate" "$OWNER" "{\"accountIds\":[\"$ACC1\",\"$ACC2\"],\"minGapSeconds\":5,\"maxGapSeconds\":9}"; expect 200 "replicate"
[ "$(jq length /tmp/body)" = 2 ] && [ "$(jq '[.[] | select(.status=="scheduled")] | length' /tmp/body)" = 2 ] || fail "two scheduled copies expected"
T1=$(jq -r '.[0].scheduledAt' /tmp/body); T2=$(jq -r '.[1].scheduledAt' /tmp/body)
GAP=$(( $(date -u -d "$T2" +%s) - $(date -u -d "$T1" +%s) ))
[ "$GAP" -ge 5 ] && [ "$GAP" -le 9 ] || fail "gap between copies must be 5..9 seconds (got $GAP)"
call POST "/api/posts/$P/replicate" "$OWNER" "{\"accountIds\":[\"$ACC1\"],\"minGapSeconds\":1,\"maxGapSeconds\":2}"; expect 400 "gap too small"
call POST /api/posts "$OWNER" "{\"body\":\"مولّد\",\"accountIds\":[\"$ACC1\"],\"aiGenerated\":true}"; AIP=$(field .id)
call POST "/api/posts/$AIP/replicate" "$OWNER" "{\"accountIds\":[\"$ACC2\"],\"minGapSeconds\":5,\"maxGapSeconds\":9}"; expect 409 "ai post needs approval"

echo "-- المشاهد ما بيدير الأتمتة، ومساحة ثانية ما بتشوف شي"
call POST /api/workspace/invitations "$OWNER" "{\"email\":\"aut-viewer-$SUFFIX@example.com\",\"role\":\"viewer\"}"; CODE=$(field .code)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"aut-viewer-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"
VIEWER=$(jq -r .accessToken /tmp/body)
call GET /api/automation/rules "$VIEWER"; expect 200 "viewer reads rules"
call POST /api/automation/rules "$VIEWER" '{"name":"x","isEnabled":true,"delayMinSeconds":1,"delayMaxSeconds":2}'; expect 403 "viewer must not create rules"
call POST /api/proxies "$VIEWER" '{"name":"x","host":"127.0.0.1","port":9091}'; expect 403 "viewer must not add proxies"
call POST /api/analytics/recommendations "$VIEWER" '{"days":7}'; expect 403 "viewer must not run ai"
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"aut-other-$SUFFIX@example.com\",\"password\":\"$PASS\"}"
OTHER=$(jq -r .accessToken /tmp/body)
call GET /api/automation/rules "$OTHER"; [ "$(jq length /tmp/body)" = 0 ] || fail "other workspace must not see rules"
call GET /api/proxies "$OTHER"; [ "$(jq length /tmp/body)" = 0 ] || fail "other workspace must not see proxies"
call GET /api/automation/replies "$OTHER"; [ "$(jq length /tmp/body)" = 0 ] || fail "other workspace must not see replies"
call GET "/api/analytics/summary" "$OTHER"; [ "$(field .totals.posts)" = 0 ] || fail "other workspace analytics must be empty"

echo "automation checks ok"
