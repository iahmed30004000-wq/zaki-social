#!/usr/bin/env bash
# فحص الأمان على الخادم الشغّال (آخر فحص بالمسار، بعد كل حركة الفحوصات الثانية):
# ترويسات، حدّ محاولات الدخول، تدقيق نقاط النهاية، نظافة السجلات من الأسرار، ونسخ احتياطي مع استعادة فعلية.
set -eu

BASE="${1:-http://127.0.0.1:8080}"
WEB="${2:-http://127.0.0.1:3000}"
PASS="correct-horse-battery-staple"
fail() { echo "::error::$1"; exit 1; }
psqlq() { PGHOST=127.0.0.1 PGUSER=smoke PGPASSWORD=smoke-test-password-not-used-anywhere psql -q -v ON_ERROR_STOP=1 -tA "$@"; }

echo "-- ترويسات الأمان على الواجهة الخلفية والأمامية"
H=$(curl -sI "$BASE/api/health")
for want in "x-content-type-options: nosniff" "x-frame-options: deny" "cache-control: no-store" "referrer-policy: no-referrer" "content-security-policy: default-src 'none'"; do
  echo "$H" | tr -d '\r' | grep -qi "^$want" || fail "backend header missing: $want"
done
W=$(curl -sI "$WEB/")
for want in "x-content-type-options: nosniff" "x-frame-options: deny" "content-security-policy: default-src 'self'"; do
  echo "$W" | tr -d '\r' | grep -qi "^$want" || fail "frontend header missing: $want"
done
echo "$W" | tr -d '\r' | grep -i "^content-security-policy" | grep -q "frame-ancestors 'none'" || fail "frontend csp must forbid framing"

echo "-- كل نقطة بتحتاج تسجيل دخول (ما عدا قائمة بيضا صغيرة) بدون توكن = 401"
for path in /api/me /api/accounts /api/accounts/platforms /api/posts /api/media /api/prompts /api/workspace/members /api/automation/rules /api/automation/status /api/proxies /api/analytics/summary /api/ai/usage /api/ai/providers "/api/schedule/best-time"; do
  code=$(curl -s -o /dev/null -w "%{http_code}" "$BASE$path")
  [ "$code" = 401 ] || fail "$path must require login (got $code)"
done
for method in POST PUT DELETE; do
  code=$(curl -s -o /dev/null -w "%{http_code}" -X "$method" "$BASE/api/posts/00000000-0000-0000-0000-000000000000" -H 'Content-Type: application/json' -d '{}')
  [ "$code" = 401 ] || fail "$method on a post must require login (got $code)"
done
docker logs backend 2>&1 | grep -q "ENDPOINT AUDIT OK" || fail "the endpoint audit did not pass at startup"

echo "-- حدّ محاولات الدخول لكل إيميل (تخمين)"
SUFFIX=$(date +%s)
EMAIL="rl-$SUFFIX@example.com"
curl -s -o /dev/null -X POST "$BASE/api/auth/register" -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}"
SEEN429=0
for i in $(seq 1 20); do
  code=$(curl -s -o /tmp/body -D /tmp/hdr -w "%{http_code}" -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"wrong-password-$i\"}")
  if [ "$code" = 429 ]; then
    SEEN429=1
    grep -qi '^retry-after:' /tmp/hdr || fail "429 must carry Retry-After"
    [ "$(jq -r .error /tmp/body)" = rate_limited ] || fail "429 body"
    break
  fi
  [ "$code" = 401 ] || fail "wrong password must be 401 until limited (got $code)"
done
[ "$SEEN429" = 1 ] || fail "login attempts must be rate limited per email"
code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}")
[ "$code" = 429 ] || fail "even the right password is blocked during the lockout window (got $code)"
code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' -d "{\"email\":\"other-$SUFFIX@example.com\",\"password\":\"x\"}")
[ "$code" = 401 ] || fail "another email must not be limited (got $code)"

echo "-- السجلات: JSON منظّم، وما فيها أي سر أو توكن أو كلمة سر أو كود OAuth"
docker logs backend > /tmp/backend.log 2>&1
grep -q '"Category":"http"' /tmp/backend.log || fail "structured http request logs expected"
[ "$(grep -c '"Category":"http"' /tmp/backend.log)" -gt 50 ] || fail "request log must cover the traffic"
SECRETS='PAGETOKEN_|LONG_USER_TOKEN|SHORT_USER_TOKEN|TT_ACCESS|TT_REFRESH|X_ACCESS|X_REFRESH|YT_ACCESS|YT_REFRESH|LI_ACCESS|ci-only-signing-key|ci-only-encryption-key|ci-app-secret|ci-claude-key|ci-openai-key|correct-horse|smoke-test-password|ci-minio-password|ci-tt-secret|ci-x-secret|ci-yt-secret|ci-li-secret|p@ss|good-code'
if grep -E "$SECRETS" /tmp/backend.log | head -3; then fail "a secret appeared in the backend logs"; fi
grep -E '"Path":|http (GET|POST)' /tmp/backend.log | grep -q '?code=' && fail "query strings (oauth codes) must not be logged"
docker logs frontend > /tmp/frontend.log 2>&1
if grep -E "$SECRETS" /tmp/frontend.log | head -3; then fail "a secret appeared in the frontend logs"; fi

echo "-- نسخ احتياطي واستعادة فعلية (نفس أوامر خدمة backup)"
TMP=$(mktemp -d)
docker run --rm --network host -e PGPASSWORD=smoke-test-password-not-used-anywhere postgres:16-alpine pg_dump -h 127.0.0.1 -U smoke -Fc smoke > "$TMP/db.dump"
[ -s "$TMP/db.dump" ] || fail "backup file is empty"
docker run --rm --network host -e PGPASSWORD=smoke-test-password-not-used-anywhere postgres:16-alpine sh -c "createdb -h 127.0.0.1 -U smoke restore_check" || fail "scratch database"
docker run --rm --network host -v "$TMP:/b:ro" -e PGPASSWORD=smoke-test-password-not-used-anywhere postgres:16-alpine pg_restore -h 127.0.0.1 -U smoke -d restore_check --no-owner /b/db.dump 2> "$TMP/restore.err" || true
SRC=$(psqlq -d smoke -c "select count(*) from users")
DST=$(psqlq -d restore_check -c "select count(*) from users")
[ "$SRC" -gt 5 ] && [ "$SRC" = "$DST" ] || { head -5 "$TMP/restore.err"; fail "restore must reproduce the data (users: $SRC vs $DST)"; }
[ "$(psqlq -d restore_check -c "select count(*) from social_accounts where enc_access_token is not null")" = "$(psqlq -d smoke -c "select count(*) from social_accounts where enc_access_token is not null")" ] || fail "restored tokens differ"
psqlq -d smoke -c "drop database restore_check"
echo "backup restored: $SRC users"

echo "security checks ok"
