#!/usr/bin/env bash
# فحص بقية المنصات (إنستغرام، تيك توك، إكس، يوتيوب، لينكدإن) مع خادم تجريبي: الربط، النشر بنفس المؤلّف، تجديد التوكن، أخطاء دائمة.
set -eu

BASE="${1:-http://127.0.0.1:8080}"
FAKE="${2:-http://127.0.0.1:9090}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
call() { curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code; }
expect() { [ "$(cat /tmp/code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(cat /tmp/code))"; }; }
field() { jq -r "$1" /tmp/body; }
requests() { curl -s "$FAKE/__requests"; }
wait_post() { for _ in $(seq 1 60); do call GET "/api/posts/$1" "$2"; [ "$(field .status)" = "$3" ] && return 0; sleep 1; done; cat /tmp/body; echo; fail "post $1 never reached $3"; }
upload() { curl -s -o /tmp/body -X POST "$BASE/api/media" -H "Authorization: Bearer $1" -F "file=@$2;type=$3" ; }
wait_media() { for _ in $(seq 1 90); do call GET "/api/media/$2" "$1"; [ "$(field .status)" = ready ] && return 0; sleep 1; done; fail "media $2 not ready"; }

command -v ffmpeg > /dev/null || { sudo apt-get update -qq && sudo apt-get install -y -qq ffmpeg > /dev/null; }
WORK=$(mktemp -d)
ffmpeg -loglevel error -f lavfi -i testsrc=size=800x600:rate=1 -frames:v 1 "$WORK/photo.png"
ffmpeg -loglevel error -f lavfi -i testsrc=size=640x360:rate=10 -f lavfi -i sine=frequency=440 -t 2 -pix_fmt yuv420p -shortest "$WORK/clip.mp4"

SUFFIX=$(date +%s)
curl -s -o /tmp/body -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"plat-owner-$SUFFIX@example.com\",\"password\":\"$PASS\",\"workspaceName\":\"Plat\"}"
OWNER=$(jq -r .accessToken /tmp/body)

echo "-- كل المنصات مفعّلة"
call GET /api/accounts/platforms "$OWNER"; expect 200 "platforms"
[ "$(jq '[.[] | select(.configured)] | length' /tmp/body)" = 6 ] || fail "six platforms must be configured: $(cat /tmp/body)"

connect() { # connect <platform> -> بيعمل رابط الربط ثم الرجعة (كود صالح)
  call GET "/api/accounts/$1/connect-url" "$OWNER"; expect 200 "connect-url $1"
  URL=$(field .url); STATE=$(printf '%s' "$URL" | sed -E 's/.*[?&]state=([^&]*).*/\1/')
  curl -s -o /dev/null -w "%{redirect_url}" "$BASE/api/accounts/$1/callback?code=good-code&state=$STATE" > /tmp/redirect
  grep -q "connected=" /tmp/redirect || fail "$1 connect must succeed: $(cat /tmp/redirect)"
}

connect facebook
echo "-- إنستغرام: الربط بيجيب حساب إنستغرام المربوط بصفحة فيسبوك"
connect instagram
case "$URL" in *instagram_content_publish*) ;; *) fail "instagram scopes missing: $URL";; esac
call GET /api/accounts "$OWNER"
IG=$(jq -r '.[] | select(.platform=="instagram") | .id' /tmp/body)
[ "$(jq -r '.[] | select(.platform=="instagram") | .displayName' /tmp/body)" = "@page_one_ig" ] || fail "instagram account name"
[ "$(jq '[.[] | select(.platform=="instagram")] | length' /tmp/body)" = 1 ] || fail "only the linked instagram account is connected"

echo "-- تيك توك"
connect tiktok
case "$URL" in *client_key=ci-tt-key*scope=*video.publish*) ;; *) fail "tiktok url: $URL";; esac
echo "-- إكس: PKCE (challenge بالرابط = SHA-256 للـ verifier اللي وصل للمنصة)"
connect x
CHALLENGE=$(printf '%s' "$URL" | sed -E 's/.*[?&]code_challenge=([^&]*).*/\1/')
case "$URL" in *code_challenge_method=S256*) ;; *) fail "x must use S256: $URL";; esac
VERIFIER=$(requests | jq -r '[.[] | select(.path=="/x/2/oauth2/token" and .body.code_verifier != null)] | last | .body.code_verifier')
[ -n "$VERIFIER" ] && [ "$VERIFIER" != null ] || fail "verifier must reach the token endpoint"
COMPUTED=$(printf '%s' "$VERIFIER" | openssl dgst -sha256 -binary | openssl base64 -A | tr '+/' '-_' | tr -d '=')
[ "$COMPUTED" = "$CHALLENGE" ] || fail "pkce challenge mismatch ($COMPUTED vs $CHALLENGE)"
requests | jq -e '[.[] | select(.path=="/x/2/oauth2/token" and .basic==true)] | length >= 1' > /dev/null || fail "x token request must use basic client auth"
echo "-- يوتيوب ولينكدإن"
connect youtube
case "$URL" in *access_type=offline*youtube.upload*|*youtube.upload*access_type=offline*) ;; *) fail "youtube url: $URL";; esac
connect linkedin
case "$URL" in *w_member_social*) ;; *) fail "linkedin scope: $URL";; esac

call GET /api/accounts "$OWNER"
id_of() { jq -r ".[] | select(.platform==\"$1\") | .id" /tmp/body | head -1; }
TT=$(id_of tiktok); X=$(id_of x); YT=$(id_of youtube); LI=$(id_of linkedin); FB=$(jq -r '.[] | select(.displayName=="Page One") | .id' /tmp/body)
for v in "$IG" "$TT" "$X" "$YT" "$LI" "$FB"; do [ -n "$v" ] && [ "$v" != null ] || fail "an account is missing"; done

echo "-- تيك توك: التوكن بينتهي بثانية، فالنشر بيجدّده تلقائياً قبل الإرسال"
upload "$OWNER" "$WORK/photo.png" image/png; IMG=$(field .id); wait_media "$OWNER" "$IMG"
upload "$OWNER" "$WORK/clip.mp4" video/mp4; VID=$(field .id); wait_media "$OWNER" "$VID"
sleep 2
call POST /api/posts "$OWNER" "{\"body\":\"فيديو لتيك توك\",\"accountIds\":[\"$TT\"],\"mediaIds\":[\"$VID\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; expect 200 "publish tiktok"; wait_post "$P" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/tiktok/v2/oauth/token/" and .body.grant_type=="refresh_token")] | length == 1' > /dev/null || fail "token must be refreshed before publishing"
requests | jq -e '[.[] | select(.path=="/tiktok/v2/post/publish/video/init/" and .bearer=="TT_ACCESS_2" and .body.source_info.source=="FILE_UPLOAD" and .body.post_info.privacy_level=="SELF_ONLY")] | length == 1' > /dev/null || fail "tiktok init must use the refreshed token"
requests | jq -e '[.[] | select(.path=="/tiktok/upload/pub1" and .method=="PUT" and .body.size > 1000)] | length == 1' > /dev/null || fail "tiktok upload missing"
call GET "/api/posts/$P" "$OWNER"; [ "$(field '.targets[0].remotePostId')" = 7001 ] || fail "tiktok remote id"
call GET /api/accounts "$OWNER"
[ "$(jq -r '.[] | select(.platform=="tiktok") | .tokenExpiry' /tmp/body | cut -c1-10)" != null ] || fail "token expiry stored"
[ "$(date -u -d "$(jq -r '.[] | select(.platform=="tiktok") | .tokenExpiry' /tmp/body)" +%s)" -gt "$(date -u -d '+1 hour' +%s)" ] || fail "expiry must be renewed after refresh"

echo "-- إنستغرام: صورة (بتسحبها المنصة من رابط عام موقّع)"
call POST /api/posts "$OWNER" "{\"body\":\"صورة لإنستغرام\",\"accountIds\":[\"$IG\"],\"mediaIds\":[\"$IMG\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; expect 200 "publish ig"; wait_post "$P" "$OWNER" published
IMGURL=$(requests | jq -r '[.[] | select(.path=="/ig111/media" and .body.image_url != null and .body.caption=="صورة لإنستغرام")] | last | .body.image_url')
case "$IMGURL" in http://127.0.0.1:8080/api/public/media/*/image.jpg) ;; *) fail "instagram must get a public url: $IMGURL";; esac
curl -s -o "$WORK/pub.jpg" -w "%{http_code} %{content_type}" "$IMGURL" | grep -q "200 image/jpeg" || fail "public media url must serve the image without login"
curl -s -o /dev/null -w "%{http_code}" "$(printf '%s' "$IMGURL" | sed -E 's#/[^/]+/image.jpg$#/AAAA/image.jpg#')" | grep -q 404 || fail "forged signature must be refused"
requests | jq -e '[.[] | select(.path=="/ig111/media_publish" and .body.creation_id != null)] | length >= 1' > /dev/null || fail "media_publish missing"
call GET "/api/posts/$P" "$OWNER"; [ "$(field '.targets[0].remotePostId')" = igpost_1 ] || fail "instagram remote id"

echo "-- إنستغرام: ريلز (فيديو) وبعدها شرائح (كاروسيل)"
call POST /api/posts "$OWNER" "{\"body\":\"ريلز\",\"accountIds\":[\"$IG\"],\"mediaIds\":[\"$VID\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/ig111/media" and .body.media_type=="REELS" and (.body.video_url | test("video.mp4$")))] | length == 1' > /dev/null || fail "reels container missing"
upload "$OWNER" "$WORK/photo.png" image/png; IMG2=$(field .id); wait_media "$OWNER" "$IMG2"
call POST /api/posts "$OWNER" "{\"body\":\"كاروسيل\",\"accountIds\":[\"$IG\"],\"mediaIds\":[\"$IMG\",\"$IMG2\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/ig111/media" and .body.is_carousel_item=="true")] | length >= 2' > /dev/null || fail "carousel children missing"
requests | jq -e '[.[] | select(.path=="/ig111/media" and .body.media_type=="CAROUSEL" and (.body.children | contains(",")))] | length == 1' > /dev/null || fail "carousel parent missing"

echo "-- إنستغرام بدون وسائط: رفض نهائي (ما بينعاد)"
call POST /api/posts "$OWNER" "{\"body\":\"نص بس\",\"accountIds\":[\"$IG\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" failed
[ "$(field '.targets[0].error')" = "400: instagram_requires_media" ] || fail "instagram text-only error: $(field '.targets[0].error')"

echo "-- إكس: نص، ونص طويل مرفوض، وفيديو مرفوض"
call POST /api/posts "$OWNER" "{\"body\":\"تغريدة من المنصة\",\"accountIds\":[\"$X\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/x/2/tweets" and .bearer=="X_ACCESS_1" and .body.text=="تغريدة من المنصة")] | length == 1' > /dev/null || fail "tweet request missing"
call POST /api/posts "$OWNER" "{\"body\":\"$(printf 'ا%.0s' $(seq 1 300))\",\"accountIds\":[\"$X\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" failed
[ "$(field '.targets[0].error')" = "400: x_text_too_long" ] || fail "x long text error"
call POST /api/posts "$OWNER" "{\"body\":\"فيديو لإكس\",\"accountIds\":[\"$X\"],\"mediaIds\":[\"$VID\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" failed
[ "$(field '.targets[0].error')" = "400: x_video_not_supported" ] || fail "x video error"
call POST /api/posts "$OWNER" "{\"body\":\"تغريدة بصورة\",\"accountIds\":[\"$X\"],\"mediaIds\":[\"$IMG\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/x/2/media/upload" and .body._multipart==true)] | length >= 1' > /dev/null || fail "x media upload missing"

echo "-- يوتيوب: رفع قابل للاستئناف"
call POST /api/posts "$OWNER" "{\"body\":\"عنوان الفيديو\\nوصف طويل للفيديو\",\"accountIds\":[\"$YT\"],\"mediaIds\":[\"$VID\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; expect 200 "publish yt"; wait_post "$P" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/yt/upload/youtube/v3/videos" and .bearer=="YT_ACCESS_1" and .body.snippet.title=="عنوان الفيديو" and .body.status.privacyStatus=="public")] | length == 1' > /dev/null || fail "youtube init missing"
requests | jq -e '[.[] | select(.path=="/yt/upload/session1" and .method=="PUT" and .body.size > 1000)] | length == 1' > /dev/null || fail "youtube upload missing"
call GET "/api/posts/$P" "$OWNER"; [ "$(field '.targets[0].remotePostId')" = yt_vid_1 ] || fail "youtube remote id"

echo "-- لينكدإن: نص"
call POST /api/posts "$OWNER" "{\"body\":\"منشور لينكدإن\",\"accountIds\":[\"$LI\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" published
requests | jq -e '[.[] | select(.path=="/li/rest/posts" and .bearer=="LI_ACCESS_1" and .version=="202405" and .body.author=="urn:li:person:li_user_1" and .body.commentary=="منشور لينكدإن")] | length == 1' > /dev/null || fail "linkedin post missing"
call GET "/api/posts/$P" "$OWNER"; [ "$(field '.targets[0].remotePostId')" = "urn:li:share:9001" ] || fail "linkedin remote id"

echo "-- نفس الفيديو لأربع منصات بضغطة وحدة (المؤلّف عام لكل المنصات)"
call POST /api/posts "$OWNER" "{\"body\":\"فيديو للكل\",\"accountIds\":[\"$FB\",\"$IG\",\"$TT\",\"$YT\"],\"mediaIds\":[\"$VID\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; expect 200 "publish all"; wait_post "$P" "$OWNER" published
[ "$(jq '[.targets[] | select(.status=="published")] | length' /tmp/body)" = 4 ] || fail "all four targets must be published"

echo "-- رفض نهائي مرة وحدة بدون إعادة محاولات (طلب خاطئ 400)، عكس 500 اللي بينعاد"
call POST /api/posts "$OWNER" "{\"body\":\"BADREQ منشور مرفوض\",\"accountIds\":[\"$FB\"]}"; P=$(field .id)
call POST "/api/posts/$P/publish" "$OWNER"; wait_post "$P" "$OWNER" failed
sleep 4
[ "$(requests | jq '[.[] | select(.path=="/111/feed" and (.body.message | startswith("BADREQ")))] | length')" = 1 ] || fail "a 400 must not be retried"

echo "platforms checks ok"
