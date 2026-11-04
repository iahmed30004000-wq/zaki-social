#!/usr/bin/env bash
# فحص الوسائط: رفع (MinIO)، معالجة FFmpeg بالعامل، حدود، صلاحيات، عزل، ونشر صورة/فيديو/عدة صور عبر الطابور.
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
upload() { # upload <bearer> <file> <type> [name]
  curl -s -o /tmp/body -w "%{http_code}" -X POST "$BASE/api/media" -H "Authorization: Bearer $1" -F "file=@$2;type=$3;filename=${4:-$(basename "$2")}" > /tmp/code
}
wait_media() { # wait_media <bearer> <id> <status> <seconds>
  for _ in $(seq 1 "$4"); do call GET "/api/media/$2" "$1"; [ "$(field .status)" = "$3" ] && return 0; sleep 1; done
  cat /tmp/body; echo; fail "media $2 never reached $3"
}
wait_post() {
  for _ in $(seq 1 60); do call GET "/api/posts/$1" "$2"; [ "$(field .status)" = "$3" ] && return 0; sleep 1; done
  cat /tmp/body; echo; fail "post $1 never reached $3"
}
requests() { curl -s "$FAKE/__requests"; }

command -v ffmpeg > /dev/null || fail "ffmpeg غير مثبّت (خطوة تثبيت FFmpeg بالمسار)"
WORK=$(mktemp -d)
ffmpeg -loglevel error -f lavfi -i testsrc=size=800x600:rate=1 -frames:v 1 "$WORK/photo.png"
ffmpeg -loglevel error -f lavfi -i testsrc=size=640x360:rate=10 -f lavfi -i sine=frequency=440 -t 2 -pix_fmt yuv420p -shortest "$WORK/clip.mp4"
head -c 4096 /dev/urandom > "$WORK/broken.png"
echo "not media" > "$WORK/note.txt"

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"med-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Med\"}"
OWNER=$(jq -r .accessToken /tmp/body)
call GET /api/accounts/facebook/connect-url "$OWNER"
STATE=$(field .url | sed -E 's/.*[?&]state=([^&]*).*/\1/')
curl -s -o /dev/null "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE"
call GET /api/accounts "$OWNER"; ACC=$(jq -r '.[] | select(.displayName=="Page One") | .id' /tmp/body)

echo "-- رفع صورة: بترجع processing وبعدها العامل بيجهزها"
upload "$OWNER" "$WORK/photo.png" image/png; expect 200 "upload image"
IMG=$(field .id); [ "$(field .status)" = processing ] || fail "upload must return processing"
wait_media "$OWNER" "$IMG" ready 60
[ "$(field .width)" = 800 ] && [ "$(field .height)" = 600 ] || fail "image dimensions"
[ "$(field .hasThumbnail)" = true ] || fail "thumbnail expected"
curl -s -o "$WORK/out.jpg" -w "%{content_type}" "$BASE/api/media/$IMG/file" -H "Authorization: Bearer $OWNER" | grep -q "image/jpeg" || fail "processed image must be jpeg"
head -c 3 "$WORK/out.jpg" | xxd -p | grep -qi "ffd8ff" || fail "output must be a real jpeg"
curl -s -o "$WORK/thumb.jpg" "$BASE/api/media/$IMG/thumb" -H "Authorization: Bearer $OWNER"; head -c 3 "$WORK/thumb.jpg" | xxd -p | grep -qi "ffd8ff" || fail "thumbnail must be a jpeg"

echo "-- رفع فيديو: H.264 ومدة"
upload "$OWNER" "$WORK/clip.mp4" video/mp4; expect 200 "upload video"
VID=$(field .id); wait_media "$OWNER" "$VID" ready 90
[ "$(field .kind)" = video ] && [ "$(field .durationSeconds | cut -d. -f1)" -ge 1 ] || fail "video duration"
curl -s -o "$WORK/out.mp4" "$BASE/api/media/$VID/file" -H "Authorization: Bearer $OWNER"
[ "$(ffprobe -v error -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 "$WORK/out.mp4")" = h264 ] || fail "video must be h264"

echo "-- أنواع غير مدعومة، ملف تالف"
upload "$OWNER" "$WORK/note.txt" text/plain; expect 415 "unsupported type"
upload "$OWNER" "$WORK/broken.png" image/png; expect 200 "corrupt upload accepted for processing"
BAD=$(field .id); wait_media "$OWNER" "$BAD" failed 60
[ "$(field .error)" != null ] || fail "failed media must carry an error"

echo "-- نشر صورة وحدة: بيروح لـ /photos بتوكن الصفحة"
call POST /api/posts "$OWNER" "{\"body\":\"منشور بصورة\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$IMG\"]}"; expect 200 "post with image"
P1=$(field .id); [ "$(field '.mediaIds | length')" = 1 ] || fail "media attached"
call POST "/api/posts/$P1/publish" "$OWNER"; expect 200 "publish"; wait_post "$P1" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/111/photos" and .bearer=="PAGETOKEN_ONE" and .body._multipart==true and .body.message=="منشور بصورة" and .body.size > 1000)] | length == 1' > /dev/null || fail "photo upload request missing"
call GET "/api/posts/$P1" "$OWNER"; [ "$(field '.targets[0].remotePostId')" = "111_photo" ] || fail "remote id must come from the photo post"

echo "-- نشر فيديو"
call POST /api/posts "$OWNER" "{\"body\":\"منشور بفيديو\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$VID\"]}"; P2=$(field .id)
call POST "/api/posts/$P2/publish" "$OWNER"; expect 200 "publish video"; wait_post "$P2" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/111/videos" and .body._multipart==true and .body.description=="منشور بفيديو")] | length == 1' > /dev/null || fail "video upload request missing"

echo "-- نشر صورتين: كل وحدة غير منشورة ثم منشور واحد بيجمعها"
call POST /api/posts "$OWNER" "{\"body\":\"صورتين\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$IMG\",\"$IMG\"]}"; expect 200 "dedupe same media"
upload "$OWNER" "$WORK/photo.png" image/png; IMG2=$(field .id); wait_media "$OWNER" "$IMG2" ready 60
call POST /api/posts "$OWNER" "{\"body\":\"صورتين فعلاً\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$IMG\",\"$IMG2\"]}"; P3=$(field .id)
call POST "/api/posts/$P3/publish" "$OWNER"; expect 200 "publish album"; wait_post "$P3" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/111/photos" and .body.published=="false")] | length >= 2' > /dev/null || fail "two unpublished photo uploads expected"
requests | jq -e '[.[] | select(.path=="/111/feed" and (.body["attached_media[0]"] != null) and (.body["attached_media[1]"] != null))] | length == 1' > /dev/null || fail "feed post with attached media missing"

echo "-- وسائط غير جاهزة/تالفة ما بتنربط بنشر"
call POST /api/posts "$OWNER" "{\"body\":\"فيها تالف\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$BAD\"]}"; P4=$(field .id)
call POST "/api/posts/$P4/publish" "$OWNER"; expect 409 "failed media must block publishing"

echo "-- حذف: ممنوع لو مستعمل بمنشور مجدول، وبعدها مسموح"
call POST /api/posts "$OWNER" "{\"body\":\"مجدول بصورة\",\"accountIds\":[\"$ACC\"],\"mediaIds\":[\"$IMG2\"]}"; P5=$(field .id)
call PUT "/api/posts/$P5/schedule" "$OWNER" "{\"when\":\"$(date -u -d '+2 days' +%Y-%m-%dT%H:%M:%SZ)\"}"; expect 200 "schedule"
call DELETE "/api/media/$IMG2" "$OWNER"; expect 409 "media in use by a scheduled post"
call DELETE "/api/posts/$P5/schedule" "$OWNER"; expect 200 "unschedule"
call DELETE "/api/posts/$P5" "$OWNER"; expect 204 "delete draft"
call DELETE "/api/media/$IMG2" "$OWNER"; expect 204 "delete media"
call GET "/api/media/$IMG2" "$OWNER"; expect 404 "deleted media is gone"

echo "-- المشاهد بيشوف بس، ومساحة ثانية ما بتشوف"
call POST /api/workspace/invitations "$OWNER" "{\"email\":\"med-viewer-$SUFFIX@example.com\",\"role\":\"viewer\"}"; CODE=$(field .code)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"med-viewer-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"
VIEWER=$(jq -r .accessToken /tmp/body)
call GET /api/media "$VIEWER"; expect 200 "viewer lists"
upload "$VIEWER" "$WORK/photo.png" image/png; expect 403 "viewer must not upload"
call DELETE "/api/media/$IMG" "$VIEWER"; expect 403 "viewer must not delete"
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"med-other-$SUFFIX@example.com\",\"password\":\"$PASS\"}"
OTHER=$(jq -r .accessToken /tmp/body)
call GET /api/media "$OTHER"; [ "$(jq length /tmp/body)" = 0 ] || fail "other workspace must not see media"
curl -s -o /dev/null -w "%{http_code}" "$BASE/api/media/$IMG/file" -H "Authorization: Bearer $OTHER" | grep -q 404 || fail "foreign media file must be 404"
curl -s -o /dev/null -w "%{http_code}" "$BASE/api/media/$IMG/file" | grep -q 401 || fail "media file needs login"

echo "media checks ok"
