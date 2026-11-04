#!/usr/bin/env bash
# فحص الدخول والصلاحيات وعزل المساحات على الخادم الشغّال (يستعمله مسار جيت هاب).
# الاستعمال: scripts/ci-auth-check.sh http://127.0.0.1:8080
set -eu

BASE="${1:-http://127.0.0.1:8080}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }

# post <path> <json> [bearer] -> يكتب الجسم بـ /tmp/body وكود الحالة بـ /tmp/code وترويسات بـ /tmp/hdr
post() {
  local auth=()
  [ -n "${3:-}" ] && auth=(-H "Authorization: Bearer $3")
  curl -s -o /tmp/body -D /tmp/hdr -w "%{http_code}" -X POST "$BASE$1" -H 'Content-Type: application/json' "${auth[@]}" -d "$2" > /tmp/code
}
get() { curl -s -o /tmp/body -w "%{http_code}" "$BASE$1" -H "Authorization: Bearer $2" > /tmp/code; }
send() { # send <METHOD> <path> <bearer> [json]
  curl -s -o /tmp/body -w "%{http_code}" -X "$1" "$BASE$2" -H "Authorization: Bearer $3" -H 'Content-Type: application/json' ${4:+-d "$4"} > /tmp/code
}
code() { cat /tmp/code; }
field() { jq -r "$1" /tmp/body; }
expect() { [ "$(code)" = "$1" ] || { cat /tmp/body; echo; fail "$2 (expected $1, got $(code))"; }; }

SUFFIX=$(date +%s)
OWNER_A="owner-a-$SUFFIX@example.com"
OWNER_B="owner-b-$SUFFIX@example.com"
VIEWER="viewer-$SUFFIX@example.com"

echo "-- يرفض بدون توكن"
get /api/me ""; expect 401 "me without token"

echo "-- تسجيل مالك المساحة A"
post /api/auth/register "{\"email\":\"$OWNER_A\",\"password\":\"$PASS\",\"workspaceName\":\"A\"}"; expect 200 "register A"
TOKEN_A=$(field .accessToken); WS_A=$(field .workspace.id); UID_A=$(field .user.id)
grep -qi '^set-cookie: zs_refresh=.*httponly' /tmp/hdr || fail "refresh cookie must be httpOnly"
REFRESH_A=$(grep -i '^set-cookie: zs_refresh=' /tmp/hdr | sed -E 's/^[^=]*=([^;]*);.*/\1/' | tr -d '\r')

echo "-- كلمة سر قصيرة وإيميل مكرر"
post /api/auth/register "{\"email\":\"short-$SUFFIX@example.com\",\"password\":\"123\"}"; expect 400 "short password"
post /api/auth/register "{\"email\":\"$OWNER_A\",\"password\":\"$PASS\"}"; expect 409 "duplicate email"

echo "-- تسجيل مالك المساحة B (مساحة ثانية)"
post /api/auth/register "{\"email\":\"$OWNER_B\",\"password\":\"$PASS\",\"workspaceName\":\"B\"}"; expect 200 "register B"
TOKEN_B=$(field .accessToken); WS_B=$(field .workspace.id)
[ "$WS_A" != "$WS_B" ] || fail "workspaces must differ"

echo "-- دخول وعرض الحساب"
post /api/auth/login "{\"email\":\"$OWNER_A\",\"password\":\"wrong-password-here\"}"; expect 401 "wrong password"
post /api/auth/login "{\"email\":\"$OWNER_A\",\"password\":\"$PASS\"}"; expect 200 "login A"
TOKEN_A=$(field .accessToken)
get /api/me "$TOKEN_A"; expect 200 "me"
[ "$(field .role)" = owner ] || fail "A must be owner"
[ "$(field .workspaceId)" = "$WS_A" ] || fail "me must show workspace A"

echo "-- تجديد التوكن بكوكي التجديد (مرة وحدة)"
curl -s -o /tmp/body -w "%{http_code}" -X POST "$BASE/api/auth/refresh" -H "Cookie: zs_refresh=$REFRESH_A" > /tmp/code; expect 200 "refresh"
curl -s -o /tmp/body -w "%{http_code}" -X POST "$BASE/api/auth/refresh" -H "Cookie: zs_refresh=$REFRESH_A" > /tmp/code; expect 401 "refresh token must be single use"

echo "-- عزل المساحات: المالك B ما بيشوف أعضاء A"
get /api/workspace/members "$TOKEN_B"; expect 200 "members B"
[ "$(jq 'length' /tmp/body)" = 1 ] || fail "B must see only its own member"
[ "$(field '.[0].email')" = "$OWNER_B" ] || fail "B members must be B's owner only"

echo "-- دعوة عضو مشاهد"
post /api/workspace/invitations "{\"email\":\"$VIEWER\",\"role\":\"viewer\"}" "$TOKEN_A"; expect 200 "invite"
CODE=$(field .code)
post /api/auth/register "{\"email\":\"someone-else-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"; expect 400 "invite for another email"
post /api/auth/register "{\"email\":\"$VIEWER\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"; expect 200 "register with invite"
TOKEN_V=$(field .accessToken); [ "$(field .workspace.id)" = "$WS_A" ] || fail "viewer must land in workspace A"
post /api/auth/register "{\"email\":\"again-$SUFFIX@example.com\",\"password\":\"$PASS\",\"inviteCode\":\"$CODE\"}"; expect 400 "invite is single use"
UID_V=$(jq -r '.user.id' <<< "$(curl -s -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' -d "{\"email\":\"$VIEWER\",\"password\":\"$PASS\"}")")

echo "-- الصلاحيات: المشاهد يقرأ بس"
get /api/workspace/members "$TOKEN_V"; expect 200 "viewer can list members"
[ "$(jq 'length' /tmp/body)" = 2 ] || fail "workspace A must have 2 members"
post /api/workspace/invitations "{\"email\":\"x-$SUFFIX@example.com\",\"role\":\"viewer\"}" "$TOKEN_V"; expect 403 "viewer must not invite"
send PUT "/api/workspace/members/$UID_A/role" "$TOKEN_V" '{"role":"viewer"}'; expect 403 "viewer must not change roles"
send DELETE "/api/workspace/members/$UID_A" "$TOKEN_V"; expect 403 "viewer must not remove members"

echo "-- المالك الوحيد ما بينشال ولا بينخفض"
send PUT "/api/workspace/members/$UID_A/role" "$TOKEN_A" '{"role":"viewer"}'; expect 409 "last owner cannot be demoted"
send DELETE "/api/workspace/members/$UID_A" "$TOKEN_A"; expect 409 "last owner cannot be removed"

echo "-- المالك بيرقّي المشاهد لمحرّر"
send PUT "/api/workspace/members/$UID_V/role" "$TOKEN_A" '{"role":"editor"}'; expect 204 "owner changes role"

echo "-- تبديل المساحة لمساحة مش عضو فيها مرفوض"
post /api/auth/switch-workspace "{\"workspaceId\":\"$WS_B\"}" "$TOKEN_A"; expect 403 "switch to foreign workspace"

echo "-- إزالة عضو بتنفذ فوراً (التوكن القديم ما عاد يفتح المساحة)"
send DELETE "/api/workspace/members/$UID_V" "$TOKEN_A"; expect 204 "owner removes member"
get /api/workspace/members "$TOKEN_V"; expect 403 "removed member must lose access immediately"

echo "auth checks ok"
