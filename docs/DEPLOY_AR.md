# دليل النشر والتشغيل

النظام مبني ليشتغل على سيرفر واحد: واجهة أمامية + خادم خلفي (مع العمّال) + قاعدة بيانات + ريديس + تخزين ملفات + كادي.

## الطريقة الحالية (كوليفاي) — شغّالة على https://trend.zakiai.io
1. أي دمج على `main` بيبني الصورتين على جيت هاب وبيجرّبهم، وبينشرهم بـ `ghcr.io/iahmed30004000-wq/zaki-social-backend` و `...-frontend`.
2. كوليفاي بيشغّل `docker-compose.prod.yml`. بعد كل دمج: Reload Compose ثم Redeploy (أو يتم تلقائي لما تضيف سرّين `COOLIFY_WEBHOOK_URL` و `COOLIFY_TOKEN` بإعدادات المستودع، راجع الأسفل).
3. كلمات السر (قاعدة البيانات، التخزين، توقيع الجلسات، التشفير) بيولّدها كوليفاي لحاله (`SERVICE_PASSWORD_*`). ما بتكتبها ولا بتنحفظ بأي ملف.
4. المفاتيح الخارجية (ميتا، تيك توك، إكس، يوتيوب، لينكدإن، الذكاء الاصطناعي) بتحطها بكوليفاي ← Environment Variables (انظر `docs/PLATFORMS_AR.md`).

### تفعيل النشر التلقائي بعد كل دمج
بإعدادات مستودع `zaki-social` على جيت هاب ← Secrets and variables ← Actions أضف:
- `COOLIFY_WEBHOOK_URL` = `https://coolify.zakiai.io/api/v1/deploy?uuid=vlfga7btkm0znp1d8msjouyf&force=false`
- `COOLIFY_TOKEN` = توكن API من كوليفاي (Keys & Tokens ← API tokens) — تكتبه إنت بنفسك.

## تركيب من الصفر على سيرفر جديد (بدون كوليفاي)
```bash
# 1) سيرفر أوبونتو 24.04 بـ 4 جيجا رام على الأقل، ودومين موجّه لعنوانه (سجل A)
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-v2 git ufw
sudo ufw allow OpenSSH && sudo ufw allow 80 && sudo ufw allow 443 && sudo ufw --force enable

# 2) الكود
git clone https://github.com/iahmed30004000-wq/zaki-social.git && cd zaki-social

# 3) الأسرار (انسخها لملف .env واملأها بقيم عشوائية طويلة؛ ما بتروح لجيت)
cat > .env <<'EOF'
POSTGRES_PASSWORD=غيّرني
MINIO_ROOT_USER=zaki_minio
MINIO_ROOT_PASSWORD=غيّرني
JWT_KEY=غيّرني-32-حرف-على-الأقل
ENCRYPTION_KEY=غيّرني-32-حرف-على-الأقل
APP_PUBLIC_URL=https://دومينك
EOF
chmod 600 .env

# 4) التشغيل المحلي/السيرفر البسيط (كادي بيجيب شهادة https لحاله إذا استبدلت localhost بدومينك بملف docker/caddy/Caddyfile)
docker compose up -d --build
```
> ملف `docker-compose.prod.yml` مخصّص لكوليفاي (بيعتمد على متغيراته `SERVICE_*`). للتركيب اليدوي استعمل `docker-compose.yml`.

## النسخ الاحتياطي والاستعادة
خدمة `backup` بتاخد كل 24 ساعة: نسخة قاعدة البيانات (`db-التاريخ.dump`، آخر 14) ونسخة ملفات الوسائط (`media-التاريخ.tar.gz`، آخر 7)، على وحدة التخزين `backups`.

**لازم تنسخها لمكان تاني** (نسخة على نفس السيرفر ما بتحميك لو السيرفر انحذف): مثلاً `rclone` لدرايف، أو تحميل يدوي أسبوعي.
```bash
# قائمة النسخ
docker run --rm -v zaki-social_backups:/b alpine ls -lh /b
# تحميل آخر نسخة قاعدة لجهازك
docker run --rm -v zaki-social_backups:/b -v "$PWD":/out alpine sh -c 'cp "$(ls -t /b/db-*.dump | head -1)" /out/'
```
**الاستعادة** (على قاعدة فاضية، والتطبيق متوقف):
```bash
docker compose stop backend frontend
docker compose exec -T postgres dropdb -U zaki --if-exists zaki_social && docker compose exec -T postgres createdb -U zaki zaki_social
docker compose exec -T postgres pg_restore -U zaki -d zaki_social --no-owner < db-التاريخ.dump
docker compose start backend frontend
```
هالاستعادة (pg_dump ← pg_restore) بتنجرّب فعلياً بكل دمج على قاعدة حقيقية (خطوة «الأمان» بالمسار).
لاستعادة الملفات: فك `media-التاريخ.tar.gz` داخل وحدة `miniodata`.

## المراقبة والتنبيهات
- كل الخدمات بتكتب سجلات JSON منظّمة (طلب: الطريقة، المسار بدون استعلام، الحالة، المدة، المستخدم، المساحة). ما في توكنات ولا كلمات سر ولا أكواد OAuth بالسجلات (بتنفحص بكل دمج).
- نقطة الصحة: `https://trend.zakiai.io/api/health` ← `{"status":"ok","database":"ok","redis":"ok"}`. الحالة `degraded` = القاعدة أو ريديس واقعين.
- **تنبيه بسيط ومجاني:** افتح حساب UptimeRobot/Uptime Kuma وخلّيه يفحص `/api/health` كل دقيقة ويبعتلك تلغرام/إيميل لو الرد مش 200 أو الجسم ما فيه `"status":"ok"`.
- سجلات وبحث ولوحات: مجلد `deploy/monitoring` فيه Loki + Promtail + Grafana (اختياري، بيشتغل جنب النظام على نفس السيرفر):
  ```bash
  docker compose -f deploy/monitoring/docker-compose.yml up -d   # غرافانا على المنفذ 3001 (محلي فقط)
  ```
  وبغرافانا: Explore ← Loki ← `{service="backend"} |= "\"LogLevel\":\"Error\""` لأخطاء الخادم، و `|= "rate_limited"` لمحاولات التخمين.

## تحديث النظام وإرجاع نسخة قديمة
كل دمج بينشر وسمين للصورة: `:main` (الأحدث) و `:sha-xxxxxxx` (ثابت). للرجوع: بكوليفاي غيّر وسم الصورة بالملف لوسم قديم وسوّي Redeploy (الهجرات بتتطبق للأمام فقط؛ لو هجرة جديدة غيّرت الجداول، استعد نسخة القاعدة القديمة معها).
