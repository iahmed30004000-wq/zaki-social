#!/usr/bin/env bash
# فحص موجّه الذكاء الاصطناعي (مزوّدين تجريبيين): التوجيه، التحويل التلقائي، الخصوصية، البث، حد الاستهلاك.
set -eu

BASE="${1:-http://127.0.0.1:8080}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
call() { curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code; }
expect() { [ "$(cat /tmp/code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(cat /tmp/code))"; }; }
field() { jq -r "$1" /tmp/body; }

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"ai-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"AI\"}"
OWNER=$(jq -r .accessToken /tmp/body)

echo "-- المزوّدين المفعّلين"
call GET /api/ai/providers "$OWNER"; expect 200 "providers"
[ "$(jq '[.[] | select(.configured)] | length' /tmp/body)" = 3 ] || fail "three providers must be configured"

echo "-- ping مع إجبار كل مزوّد"
for p in claude openai local; do
  call GET "/api/ai/ping?provider=$p" "$OWNER"; expect 200 "ping $p"
  [ "$(field .provider)" = "$p" ] || fail "ping must use $p, got $(field .provider)"
done

echo "-- سياسة التوجيه: إبداعي -> Claude ، تصنيف بكميات -> الداخلي"
call POST /api/ai/complete "$OWNER" '{"task":"caption","prompt":"اكتب كابشن"}'; expect 200 "caption"
[ "$(field .provider)" = claude ] || fail "caption must go to claude"
call POST /api/ai/complete "$OWNER" '{"task":"classify","prompt":"صنّف هذا"}'; expect 200 "classify"
[ "$(field .provider)" = local ] || fail "classify must go to local"

echo "-- تحويل تلقائي لما Claude يفشل"
call POST /api/ai/complete "$OWNER" '{"task":"caption","prompt":"CLAUDE_DOWN اكتب كابشن"}'; expect 200 "fallback"
[ "$(field .provider)" = openai ] || fail "must fall back to openai, got $(field .provider)"
[ "$(field .fellBack)" = true ] || fail "fellBack must be true"

echo "-- البيانات الحسّاسة للنموذج الداخلي فقط"
call POST /api/ai/complete "$OWNER" '{"task":"caption","prompt":"بيانات حسّاسة","sensitive":true}'; expect 200 "sensitive"
[ "$(field .provider)" = local ] || fail "sensitive must stay on local"

echo "-- البث"
STREAM=$(curl -s -X POST "$BASE/api/ai/complete" -H "Authorization: Bearer $OWNER" -H 'Content-Type: application/json' -d '{"task":"classify","prompt":"stream","stream":true}')
echo "$STREAM" | grep -q "local streams ok" || echo "$STREAM" | tr -d '\n' | grep -q "local .*streams .*ok" || { echo "$STREAM"; fail "stream must deliver the pieces"; }
echo "$STREAM" | grep -q "\[DONE\]" || fail "stream must end with [DONE]"

echo "-- الاستهلاك مسجّل لكل مساحة"
call GET /api/ai/usage "$OWNER"; expect 200 "usage"
[ "$(jq '[.[] | .requests] | add' /tmp/body)" -ge 5 ] || fail "usage must be recorded"

echo "-- حد الشهر"
call POST /api/ai/complete "$OWNER" '{"task":"classify","prompt":"BIGUSAGE"}'; expect 200 "big usage call"
call POST /api/ai/complete "$OWNER" '{"task":"classify","prompt":"after the limit"}'; expect 429 "quota must stop further calls"

echo "-- المشاهد ما بيستعمل الذكاء الاصطناعي (تكلفة) وغيره ما بيشوف الاستهلاك"
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"ai-other-$SUFFIX@example.com\",\"password\":\"$PASS\"}"
OTHER=$(jq -r .accessToken /tmp/body)
call GET /api/ai/usage "$OTHER"; expect 200 "other usage"
[ "$(jq length /tmp/body)" = 0 ] || fail "other workspace must not see usage"
call POST /api/ai/complete "$OTHER" '{"task":"classify","prompt":"hello"}'; expect 200 "other workspace has its own quota"

echo "ai checks ok"
