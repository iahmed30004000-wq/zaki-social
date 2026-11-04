#!/usr/bin/env bash
# فحص إنشاء المستخدمين من المالك وتغيير كلمة السر (يستعمله مسار جيت هاب).
# الاستعمال: scripts/ci-members-check.sh http://127.0.0.1:8080
set -eu

BASE="${1:-http://127.0.0.1:8080}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }

# call <METHOD> <path> <json> [bearer] -> الجسم بـ /tmp/body، الحالة بـ /tmp/code، الترويسات بـ /tmp/hdr
call() {
  local auth=()
  [ -n "${4:-}" ] && auth=(-H "Authorization: Bearer $4")
  curl -s -o /tmp/body -D /tmp/hdr -w "%{http_code}" -X "$1" "$BASE$2" -H 'Content-Type: application/json' "${auth[@]}" ${3:+-d "$3"} > /tmp/code
}
code() { cat /tmp/code; }
field() { jq -r "$1" /tmp/body; }
expect() { [ "$(code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(code))"; }; }

SUFFIX=$(date +%s)
OWNER="boss-$SUFFIX@example.com"
EMP="emp-$SUFFIX@example.com"
EMP2="emp2-$SUFFIX@example.com"
OTHER="other-$SUFFIX@example.com"

echo "-- مالك المساحة A ومالك مساحة ثانية"
call POST /api/auth/register "{\"email\":\"$OWNER\",\"password\":\"$PASS\",\"workspaceName\":\"A\"}"; expect 200 "register owner"
TOKEN_O=$(field .accessToken)
call POST /api/auth/register "{\"email\":\"$OTHER\",\"password\":\"$PASS\",\"workspaceName\":\"B\"}"; expect 200 "register other owner"
TOKEN_X=$(field .accessToken)

echo "-- المالك بينشئ مستخدم (كلمة السر بتتولّد)"
call POST /api/workspace/members "{\"email\":\"$EMP\",\"role\":\"editor\"}" "$TOKEN_O"; expect 200 "create user"
TEMP=$(field .tempPassword); EMP_ID=$(field .userId)
[ ${#TEMP} -ge 10 ] || fail "generated password too short"
call POST /api/workspace/members "{\"email\":\"$EMP\",\"role\":\"editor\"}" "$TOKEN_O"; expect 409 "duplicate email"
call POST /api/workspace/members "{\"email\":\"x-$SUFFIX@example.com\",\"role\":\"god\"}" "$TOKEN_O"; expect 400 "bad role"
call POST /api/workspace/members "{\"email\":\"y-$SUFFIX@example.com\",\"role\":\"viewer\",\"password\":\"short\"}" "$TOKEN_O"; expect 400 "short typed password"

echo "-- مستخدم بكلمة سر كتبها المالك"
call POST /api/workspace/members "{\"email\":\"$EMP2\",\"role\":\"viewer\",\"password\":\"typed-by-the-boss-1\"}" "$TOKEN_O"; expect 200 "create with typed password"
[ "$(field .tempPassword)" = "typed-by-the-boss-1" ] || fail "typed password should be returned as is"

echo "-- المستخدم بيدخل بالمؤقتة ولازم يغيّرها"
call POST /api/auth/login "{\"email\":\"$EMP\",\"password\":\"$TEMP\"}"; expect 200 "login with temp password"
[ "$(field .mustChangePassword)" = "true" ] || fail "mustChangePassword should be true"
[ "$(field .workspace.role)" = "editor" ] || fail "role should be editor"
TOKEN_E=$(field .accessToken)
OLD_REFRESH=$(grep -i '^set-cookie: zs_refresh=' /tmp/hdr | sed -E 's/^[^=]*=([^;]*);.*/\1/' | tr -d '\r')

echo "-- تغيير كلمة السر: حالية غلط، قصيرة، نفس القديمة"
call POST /api/auth/change-password "{\"current\":\"wrong-password-xx\",\"next\":\"a-brand-new-password\"}" "$TOKEN_E"; expect 401 "wrong current"
call POST /api/auth/change-password "{\"current\":\"$TEMP\",\"next\":\"short\"}" "$TOKEN_E"; expect 400 "short new"
call POST /api/auth/change-password "{\"current\":\"$TEMP\",\"next\":\"$TEMP\"}" "$TOKEN_E"; expect 400 "same password"
call POST /api/auth/change-password "{\"current\":\"$TEMP\",\"next\":\"my-own-new-password\"}" "$TOKEN_E"; expect 200 "change password"
[ "$(field .mustChangePassword)" = "false" ] || fail "mustChangePassword should be false after change"
TOKEN_E=$(field .accessToken)

echo "-- القديمة ما عادت تشتغل والجلسات القديمة اتسجّل خروجها"
call POST /api/auth/login "{\"email\":\"$EMP\",\"password\":\"$TEMP\"}"; expect 401 "old password rejected"
curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/auth/refresh" -H "Cookie: zs_refresh=$OLD_REFRESH" > /tmp/code; expect 401 "old refresh revoked"
call POST /api/auth/login "{\"email\":\"$EMP\",\"password\":\"my-own-new-password\"}"; expect 200 "login with new password"
[ "$(field .mustChangePassword)" = "false" ] || fail "mustChangePassword should stay false"

echo "-- المحرر ما بيقدر ينشئ مستخدمين ولا يعيد تعيين كلمات"
call POST /api/workspace/members "{\"email\":\"z-$SUFFIX@example.com\",\"role\":\"viewer\"}" "$TOKEN_E"; expect 403 "editor cannot create users"
call POST "/api/workspace/members/$EMP_ID/reset-password" "{}" "$TOKEN_E"; expect 403 "editor cannot reset"

echo "-- المالك بيعيد تعيين كلمة المستخدم"
call GET /api/workspace/members "" "$TOKEN_O"; expect 200 "list members"
[ "$(jq -r --arg e "$EMP" '.[]|select(.email==$e)|.managed' /tmp/body)" = "true" ] || fail "created user should be managed"
[ "$(jq -r --arg e "$OWNER" '.[]|select(.email==$e)|.managed' /tmp/body)" = "false" ] || fail "self-registered owner should not be managed"
call POST "/api/workspace/members/$EMP_ID/reset-password" "{}" "$TOKEN_O"; expect 200 "owner resets password"
TEMP2=$(field .tempPassword)
call POST /api/auth/login "{\"email\":\"$EMP\",\"password\":\"my-own-new-password\"}"; expect 401 "password changed by reset"
call POST /api/auth/login "{\"email\":\"$EMP\",\"password\":\"$TEMP2\"}"; expect 200 "login with reset password"
[ "$(field .mustChangePassword)" = "true" ] || fail "must change again after reset"

echo "-- مالك مساحة ثانية ما بيقدر يلمس هالمستخدم"
call POST "/api/workspace/members/$EMP_ID/reset-password" "{}" "$TOKEN_X"; expect 404 "other workspace cannot reset"

echo "-- المالك ما بيقدر يعيد تعيين كلمة سر حساب مش من إنشائه (حتى حسابه هو)"
OWNER_ID=$(jq -r --arg e "$OWNER" '.[]|select(.email==$e)|.userId' <(curl -s "$BASE/api/workspace/members" -H "Authorization: Bearer $TOKEN_O"))
call POST "/api/workspace/members/$OWNER_ID/reset-password" "{}" "$TOKEN_O"; expect 403 "cannot reset unmanaged account"

echo "OK: members admin check passed"
