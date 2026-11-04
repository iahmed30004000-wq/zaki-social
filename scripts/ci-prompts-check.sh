#!/usr/bin/env bash
# فحص مركز البرومبتات: زرع المكتبة، التعبئة، الإصدارات، الرجوع، التغيير الفوري للناتج، الاعتماد البشري.
set -eu

# مهلة لكل نداء: ما بنقبل ينعلّق الفحص بلا نهاية
curl() { command curl --connect-timeout 10 --max-time 60 "$@"; }

BASE="${1:-http://127.0.0.1:8080}"
FAKE="${2:-http://127.0.0.1:9090}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
call() { curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code; }
expect() { [ "$(cat /tmp/code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(cat /tmp/code))"; }; }
field() { jq -r "$1" /tmp/body; }
# آخر نص انبعت لمزوّد الذكاء الاصطناعي (الخادم التجريبي بيسجّل الطلبات)
last_ai_prompt() { curl -s "$FAKE/__requests" | jq -r '[.[] | select(.method=="POST" and (.path | endswith("/chat/completions") or endswith("/v1/messages")))] | last | .body.messages[0].content'; }

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"pr-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Pr\"}"
OWNER=$(jq -r .accessToken /tmp/body)

echo "-- المكتبة الجاهزة بتنزرع للمساحة الجديدة"
call GET /api/prompts "$OWNER"; expect 200 "list prompts"
[ "$(jq length /tmp/body)" -ge 9 ] || fail "starter library must be seeded"
jq -e '.[] | select(.name=="caption_facebook") | (.variables | index("topic")) != null' /tmp/body > /dev/null || fail "variables must be extracted"
call GET /api/prompts "$OWNER"; [ "$(jq length /tmp/body)" -ge 9 ] && [ "$(jq '[.[].name] | unique | length' /tmp/body)" = "$(jq length /tmp/body)" ] || fail "seed must not duplicate"

echo "-- برومبت جديد وتجربته: القيم بتتعبّى والناتج من الذكاء الاصطناعي"
call POST /api/prompts "$OWNER" '{"name":"my_caption","category":"content","task":"caption","body":"اكتب كابشن عن {{topic}} بنبرة {{tone}}","preferredAi":"claude"}'; expect 200 "create"
PID=$(field .id); [ "$(field .version)" = 1 ] || fail "new prompt is version 1"
call POST "/api/prompts/$PID/test" "$OWNER" '{"variables":{"topic":"القهوة","tone":"مرحة"}}'; expect 200 "test"
[ "$(field .provider)" = claude ] || fail "preferred provider must be claude"
[ "$(last_ai_prompt)" = "اكتب كابشن عن القهوة بنبرة مرحة" ] || fail "rendered prompt not sent: $(last_ai_prompt)"

echo "-- متغير ناقص = خطأ ومش بيروح للذكاء الاصطناعي"
call POST "/api/prompts/$PID/test" "$OWNER" '{"variables":{"topic":"القهوة"}}'; expect 400 "missing variable"

echo "-- تعديل البرومبت بيغيّر ما بينبعت فوراً بدون أي برمجة (إصدار 2)"
call PUT "/api/prompts/$PID" "$OWNER" '{"category":"content","task":"caption","body":"اكتب كابشن قصير جداً عن {{topic}} بنبرة {{tone}}","preferredAi":"claude"}'; expect 200 "update"
[ "$(field .version)" = 2 ] || fail "edit must create version 2"
call POST "/api/prompts/by-name/my_caption/generate" "$OWNER" '{"variables":{"topic":"الشاي","tone":"رسمية"}}'; expect 200 "generate by name"
[ "$(last_ai_prompt)" = "اكتب كابشن قصير جداً عن الشاي بنبرة رسمية" ] || fail "edited prompt must be used immediately: $(last_ai_prompt)"

echo "-- الإصدارات والرجوع"
call GET "/api/prompts/$PID" "$OWNER"; expect 200 "detail"
[ "$(jq '.versions | length' /tmp/body)" = 2 ] || fail "two versions expected"
call POST "/api/prompts/$PID/rollback" "$OWNER" '{"version":1}'; expect 200 "rollback"
[ "$(field .version)" = 3 ] && [ "$(field .body)" = "اكتب كابشن عن {{topic}} بنبرة {{tone}}" ] || fail "rollback restores old text as a new version"

echo "-- إيقاف برومبت"
call POST "/api/prompts/$PID/active" "$OWNER" '{"isActive":false}'; expect 200 "deactivate"
call POST "/api/prompts/by-name/my_caption/generate" "$OWNER" '{"variables":{"topic":"x","tone":"y"}}'; expect 409 "inactive prompt must not run"

echo "-- أسماء مكررة وغير صالحة"
call POST /api/prompts "$OWNER" '{"name":"my_caption","task":"caption","body":"x"}'; expect 409 "duplicate name"
call POST /api/prompts "$OWNER" '{"name":"Bad Name!","task":"caption","body":"x"}'; expect 400 "invalid name"
call POST /api/prompts "$OWNER" '{"name":"bad_task","task":"nonsense","body":"x"}'; expect 400 "invalid task"

echo "-- المشاهد بيقرأ بس، ومساحة ثانية بمكتبتها الخاصة"
call POST /api/workspace/invitations "$OWNER" "{\"email\":\"pr-viewer-$SUFFIX@example.com\",\"role\":\"viewer\"}"
CODE=$(field .code)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"pr-viewer-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"
VIEWER=$(jq -r .accessToken /tmp/body)
call GET /api/prompts "$VIEWER"; expect 200 "viewer reads"
call PUT "/api/prompts/$PID" "$VIEWER" '{"task":"caption","body":"hacked {{x}}"}'; expect 403 "viewer must not edit"
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"pr-other-$SUFFIX@example.com\",\"password\":\"$PASS\"}"
OTHER=$(jq -r .accessToken /tmp/body)
call GET /api/prompts "$OTHER"; expect 200 "other list"
jq -e 'all(.[]; .name != "my_caption")' /tmp/body > /dev/null || fail "other workspace must not see my_caption"
call GET "/api/prompts/$PID" "$OTHER"; expect 404 "foreign prompt invisible"

echo "-- الاعتماد البشري: منشور مولّد بالذكاء الاصطناعي ما بينشر قبل الاعتماد"
call GET /api/accounts/facebook/connect-url "$OWNER"; expect 200 "connect-url"
STATE=$(jq -r .url /tmp/body | sed -E 's/.*[?&]state=([^&]*).*/\1/')
curl -s -o /dev/null "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE"
call GET /api/accounts "$OWNER"; ACC=$(jq -r '.[0].id' /tmp/body)
call POST /api/posts "$OWNER" "{\"body\":\"نص من الذكاء الاصطناعي\",\"accountIds\":[\"$ACC\"],\"aiGenerated\":true}"; expect 200 "create ai post"
POST=$(field .id); [ "$(field .aiGenerated)" = true ] || fail "aiGenerated flag"
call POST "/api/posts/$POST/publish" "$OWNER"; expect 409 "unapproved ai post must not publish"
[ "$(field .error)" = approval_required ] || fail "error must be approval_required"
call POST "/api/posts/$POST/approve" "$VIEWER"; expect 403 "viewer must not approve"
call PUT "/api/posts/$POST" "$OWNER" "{\"body\":\"نص معدّل\",\"accountIds\":[\"$ACC\"]}"; expect 200 "edit"
call POST "/api/posts/$POST/approve" "$OWNER"; expect 200 "approve"
[ "$(field .approvedAt)" != null ] || fail "approvedAt must be set"
call PUT "/api/posts/$POST" "$OWNER" "{\"body\":\"نص معدّل بعد الاعتماد\",\"accountIds\":[\"$ACC\"]}"; expect 200 "edit after approval"
[ "$(field .approvedAt)" = null ] || fail "editing must clear the approval"
call POST "/api/posts/$POST/publish" "$OWNER"; expect 409 "edited post needs approval again"
call POST "/api/posts/$POST/approve" "$OWNER"; expect 200 "approve again"
call POST "/api/posts/$POST/publish" "$OWNER"; expect 200 "publish after approval"

echo "prompts checks ok"
