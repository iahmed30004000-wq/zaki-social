#!/usr/bin/env bash
# فحص الجدولة: المجدوِل (كل دقيقة)، إعادة الجدولة، إلغاء الجدولة، التكرار، الاعتماد، وأفضل وقت نشر.
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
in_seconds() { date -u -d "+$1 seconds" +%Y-%m-%dT%H:%M:%SZ; }
wait_status() { # <post> <bearer> <status> <seconds>
  for _ in $(seq 1 "$4"); do call GET "/api/posts/$1" "$2"; [ "$(field .status)" = "$3" ] && return 0; sleep 1; done
  cat /tmp/body; echo; fail "post $1 never reached $3"
}

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"sch-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Sch\"}"
OWNER=$(jq -r .accessToken /tmp/body)
call GET /api/accounts/facebook/connect-url "$OWNER"
STATE=$(field .url | sed -E 's/.*[?&]state=([^&]*).*/\1/')
curl -s -o /dev/null "$BASE/api/accounts/facebook/callback?code=good-code&state=$STATE"
call GET /api/accounts "$OWNER"; ACC=$(jq -r '.[0].id' /tmp/body)

mkpost() { call POST /api/posts "$OWNER" "{\"body\":\"$1\",\"accountIds\":[\"$ACC\"]}"; expect 200 "create"; field .id; }

echo "-- جدولة بعد ثواني: المجدوِل بينشرها لحاله"
P1=$(mkpost "منشور مجدول")
call PUT "/api/posts/$P1/schedule" "$OWNER" "{\"when\":\"$(in_seconds 5)\"}"; expect 200 "schedule"
[ "$(field .status)" = scheduled ] || fail "status must be scheduled"
wait_status "$P1" "$OWNER" published 150
call GET "/api/posts/$P1" "$OWNER"; [ "$(field .scheduledAt)" != null ] || fail "scheduledAt stays, so the calendar keeps showing when it was published"

echo "-- وقت بالماضي مرفوض"
P2=$(mkpost "منشور ثاني")
call PUT "/api/posts/$P2/schedule" "$OWNER" '{"when":"2020-01-01T00:00:00Z"}'; expect 400 "past"

echo "-- إعادة جدولة (سحب وإفلات) وإلغاء الجدولة"
FAR=$(date -u -d "+3 days" +%Y-%m-%dT%H:%M:%SZ); FAR2=$(date -u -d "+4 days" +%Y-%m-%dT%H:%M:%SZ)
call PUT "/api/posts/$P2/schedule" "$OWNER" "{\"when\":\"$FAR\"}"; expect 200 "schedule far"
call PUT "/api/posts/$P2/schedule" "$OWNER" "{\"when\":\"$FAR2\"}"; expect 200 "reschedule"
[ "$(field .scheduledAt | cut -c1-10)" = "$(echo $FAR2 | cut -c1-10)" ] || fail "reschedule must move the time"
call GET "/api/posts?from=$(date -u -d '+3 days -1 hour' +%Y-%m-%dT%H:%M:%SZ)&to=$(date -u -d '+5 days' +%Y-%m-%dT%H:%M:%SZ)" "$OWNER"; expect 200 "calendar range"
[ "$(jq '[.[] | select(.id=="'$P2'")] | length' /tmp/body)" = 1 ] || fail "calendar range must include the scheduled post"
call GET "/api/posts?from=$(date -u -d '+10 days' +%Y-%m-%dT%H:%M:%SZ)&to=$(date -u -d '+11 days' +%Y-%m-%dT%H:%M:%SZ)" "$OWNER"
[ "$(jq length /tmp/body)" = 0 ] || fail "calendar range must exclude other days"
call DELETE "/api/posts/$P2/schedule" "$OWNER"; expect 200 "unschedule"
[ "$(field .status)" = draft ] || fail "unschedule returns to draft"

echo "-- منشور بدون حسابات ما بينجدول، والمولّد ما بينجدول قبل الاعتماد"
call POST /api/posts "$OWNER" '{"body":"بدون حسابات","accountIds":[]}'; NOACC=$(field .id)
call PUT "/api/posts/$NOACC/schedule" "$OWNER" "{\"when\":\"$FAR\"}"; expect 400 "no targets"
call POST /api/posts "$OWNER" "{\"body\":\"مولّد\",\"accountIds\":[\"$ACC\"],\"aiGenerated\":true}"; AIP=$(field .id)
call PUT "/api/posts/$AIP/schedule" "$OWNER" "{\"when\":\"$FAR\"}"; expect 409 "ai post must be approved first"
call POST "/api/posts/$AIP/approve" "$OWNER"; expect 200 "approve"
call PUT "/api/posts/$AIP/schedule" "$OWNER" "{\"when\":\"$FAR\"}"; expect 200 "schedule approved ai post"

echo "-- التكرار اليومي: بعد النشر بينعمل منشور جديد مجدول لباكر"
P3=$(mkpost "منشور متكرر")
call PUT "/api/posts/$P3/schedule" "$OWNER" "{\"when\":\"$(in_seconds 5)\",\"repeat\":\"daily\"}"; expect 200 "schedule daily"
wait_status "$P3" "$OWNER" published 150
call GET "/api/posts?status=scheduled" "$OWNER"
[ "$(jq '[.[] | select(.body=="منشور متكرر" and .repeatRule=="daily")] | length' /tmp/body)" = 1 ] || fail "next occurrence must be scheduled"
NEXT=$(jq -r '[.[] | select(.body=="منشور متكرر")][0].scheduledAt' /tmp/body)
[ "$(date -u -d "$NEXT" +%s)" -gt "$(date -u -d '+20 hours' +%s)" ] || fail "next occurrence must be about a day later ($NEXT)"

echo "-- المشاهد ما بيجدول"
call POST /api/workspace/invitations "$OWNER" "{\"email\":\"sch-viewer-$SUFFIX@example.com\",\"role\":\"viewer\"}"; CODE=$(field .code)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"sch-viewer-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"
VIEWER=$(jq -r .accessToken /tmp/body)
call PUT "/api/posts/$AIP/schedule" "$VIEWER" "{\"when\":\"$FAR2\"}"; expect 403 "viewer must not schedule"
call GET "/api/schedule/best-time?timezone=Asia/Amman" "$VIEWER"; expect 403 "viewer must not run ai"

echo "-- أفضل وقت نشر بالذكاء الاصطناعي (من بيانات النشر السابقة)"
call GET "/api/schedule/best-time?timezone=Asia/Amman" "$OWNER"; expect 200 "best time"
[ "$(field .dataPoints)" -ge 2 ] || fail "best time must use published posts ($(field .dataPoints))"
[ -n "$(field .text)" ] || fail "best time text empty"
call GET "/api/schedule/best-time?timezone=Not/AZone" "$OWNER"; expect 400 "invalid timezone"

echo "schedule checks ok"
