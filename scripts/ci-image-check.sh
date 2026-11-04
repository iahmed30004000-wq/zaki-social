#!/usr/bin/env bash
# فحص توليد الصور بالذكاء الاصطناعي: التوليد، الدخول للمكتبة بدون اعتماد، منع الإرفاق قبل الاعتماد، برومبت المركز، الصلاحيات.
set -eu

BASE="${1:-http://127.0.0.1:8080}"
FAKE="${2:-http://127.0.0.1:9090}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
call() { curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code; }
expect() { [ "$(cat /tmp/code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(cat /tmp/code))"; }; }
field() { jq -r "$1" /tmp/body; }
wait_media() { for _ in $(seq 1 60); do call GET "/api/media/$2" "$1"; [ "$(field .status)" = "$3" ] && return 0; sleep 1; done; cat /tmp/body; echo; fail "media $2 never reached $3"; }
wait_post() { for _ in $(seq 1 60); do call GET "/api/posts/$1" "$2"; [ "$(field .status)" = "$3" ] && return 0; sleep 1; done; cat /tmp/body; echo; fail "post $1 never reached $3"; }

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"img-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Img\"}"
OWNER=$(jq -r .accessToken /tmp/body)
call GET /api/accounts/facebook/connect-url "$OWNER"
STATE=$(field .url | sed -E 's/.*[?&]state=([^&]*).*/\1/')
curl -s -o /dev/null "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE"
call GET /api/accounts "$OWNER"; ACC=$(jq -r '.[] | select(.displayName=="Page One") | .id' /tmp/body)

echo "-- توليد صورة من وصف مباشر: بتنزل بالمكتبة معالجة، مولّدة بالذكاء الاصطناعي، وغير معتمدة"
call POST /api/ai/image "$OWNER" '{"prompt":"فنجان قهوة على طاولة خشبية"}'; expect 200 "generate"
IMG=$(field .id)
[ "$(field .aiGenerated)" = true ] && [ "$(field .approvedAt)" = null ] || fail "generated image must be flagged and unapproved"
wait_media "$OWNER" "$IMG" ready 60
[ "$(field .width)" = 96 ] || fail "generated png must be processed (width 96, got $(field .width))"

echo "-- الطلب وصل لمزوّد الصور بالنموذج والوصف الصحيحين"
curl -s "$FAKE/__requests" | jq -e '[.[] | select(.path=="/openai/images/generations" and .body.model=="img-ci" and (.body.prompt | contains("فنجان قهوة")) and .body.size=="1024x1024")] | length == 1' > /dev/null || fail "image request missing"

echo "-- ما بينربط بمنشور قبل الاعتماد البشري"
call POST /api/posts "$OWNER" "{\"body\":\"منشور بصورة مولّدة\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$IMG\"]}"; expect 409 "unapproved ai media"
[ "$(field .error)" = media_approval_required ] || fail "error must be media_approval_required"

echo "-- بعد الاعتماد بينربط وبينشر"
call POST "/api/media/$IMG/approve" "$OWNER"; expect 200 "approve"
[ "$(field .approvedAt)" != null ] || fail "approvedAt set"
call POST /api/posts "$OWNER" "{\"body\":\"منشور بصورة مولّدة\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$IMG\"]}"; expect 200 "attach approved"
P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; expect 200 "publish"; wait_post "$P" "$OWNER" published
curl -s "$FAKE/__requests" | jq -e '[.[] | select(.path=="/111/photos" and .body.message=="منشور بصورة مولّدة")] | length == 1' > /dev/null || fail "published photo upload missing"

echo "-- برومبت من مركز البرومبتات: image_product_ad بالمتغيرات"
call POST /api/ai/image "$OWNER" '{"promptName":"image_product_ad","variables":{"product":"عطر رجالي","style":"فاخر"}}'; expect 200 "hub image prompt"
curl -s "$FAKE/__requests" | jq -e '[.[] | select(.path=="/openai/images/generations" and (.body.prompt | contains("عطر رجالي")) and (.body.prompt | contains("فاخر")))] | length == 1' > /dev/null || fail "hub prompt must be rendered into the image prompt"
call POST /api/ai/image "$OWNER" '{"promptName":"image_product_ad","variables":{"product":"عطر"}}'; expect 400 "missing variable"
call POST /api/ai/image "$OWNER" '{}'; expect 400 "prompt required"

echo "-- المشاهد ما بيولّد، ومساحة ثانية ما بتشوف الصور"
call POST /api/workspace/invitations "$OWNER" "{\"email\":\"img-viewer-$SUFFIX@example.com\",\"role\":\"viewer\"}"; CODE=$(field .code)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"img-viewer-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"
VIEWER=$(jq -r .accessToken /tmp/body)
call POST /api/ai/image "$VIEWER" '{"prompt":"x"}'; expect 403 "viewer must not generate"
call POST "/api/media/$IMG/approve" "$VIEWER"; expect 403 "viewer must not approve"
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"img-other-$SUFFIX@example.com\",\"password\":\"$PASS\"}"
OTHER=$(jq -r .accessToken /tmp/body)
call GET /api/media "$OTHER"; [ "$(jq length /tmp/body)" = 0 ] || fail "other workspace must not see generated images"

echo "-- استهلاك الصور مسجّل"
call GET /api/ai/usage "$OWNER"; expect 200 "usage"
[ "$(jq '[.[] | select(.provider=="openai")] | length' /tmp/body)" -ge 1 ] || fail "image usage must be recorded"

echo "image checks ok"
