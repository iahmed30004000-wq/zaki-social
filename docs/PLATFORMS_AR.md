# ربط المنصات: شو لازم تعمل أنت (المالك)

أنا (الوكيل) بنيت كل الموصّلات ومجرّبة على خادم تجريبي. عشان تشتغل على حساباتك الحقيقية لازم تفتح تطبيق مطوّر عند كل منصة، وتحط المفاتيح بكوليفاي (تكتبها إنت، ما بنكتبها بأي ملف).

**عنوان الرجعة (Redirect URI) لكل منصة:** `https://trend.zakiai.io/api/accounts/<المنصة>/callback`
مثلاً: `https://trend.zakiai.io/api/accounts/facebook/callback` (المنصات: facebook, instagram, tiktok, x, youtube, linkedin).

**المتغيرات بكوليفاي** (التطبيق ← Environment Variables): بتكتب القيمة بدون مسافات، وبعدها Redeploy.

| المنصة | المتغيرات | من وين |
|---|---|---|
| فيسبوك + إنستغرام | `FACEBOOK_APP_ID` ، `FACEBOOK_APP_SECRET` | developers.facebook.com ← تطبيق نوع Business |
| تيك توك | `TIKTOK_CLIENT_KEY` ، `TIKTOK_CLIENT_SECRET` | developers.tiktok.com ← Manage apps |
| إكس | `X_CLIENT_ID` ، `X_CLIENT_SECRET` | developer.x.com ← Project ← تطبيق OAuth 2.0 (Confidential client) |
| يوتيوب | `YOUTUBE_CLIENT_ID` ، `YOUTUBE_CLIENT_SECRET` | console.cloud.google.com ← OAuth client (Web) + تفعيل YouTube Data API v3 |
| لينكدإن | `LINKEDIN_CLIENT_ID` ، `LINKEDIN_CLIENT_SECRET` | linkedin.com/developers ← Create app |
| الذكاء الاصطناعي | `ANTHROPIC_API_KEY` ، `OPENAI_API_KEY` ، (اختياري) `OPENAI_IMAGE_MODEL` (الافتراضي gpt-image-1) | حسابات المزوّدين |

## فيسبوك وإنستغرام (تطبيق ميتا واحد)
- الصلاحيات (Scopes): فيسبوك `pages_show_list, pages_manage_posts, pages_read_engagement, pages_manage_engagement, read_insights`. إنستغرام `instagram_basic, instagram_content_publish, instagram_manage_comments, instagram_manage_insights, business_management`.
- حساب إنستغرام لازم يكون **احترافي (Business/Creator)** ومربوط بصفحة فيسبوك.
- بالوضع التطويري بتقدر تنشر على حساباتك أنت فقط. للنشر على حسابات غيرك لازم **App Review** (قد ياخد أسابيع): ابدأ فيه بدري.
- أضف الرجعتين (facebook و instagram) بـ Valid OAuth Redirect URIs.

## تيك توك
- المنتجات: Login Kit + Content Posting API. الصلاحيات: `user.info.basic, video.publish, video.upload`.
- التطبيق الجديد بينشر **خاص فقط (SELF_ONLY)** لحد ما تيك توك يدقّق التطبيق. بعد الموافقة غيّر `TIKTOK_PRIVACY_LEVEL` لـ `PUBLIC_TO_EVERYONE`.
- فيديو فقط (ما في نشر نصي أو صور بهالتكامل حالياً).

## إكس
- OAuth 2.0 مع PKCE والصلاحيات `tweet.read tweet.write users.read offline.access media.write`.
- النشر مدفوع حسب خطة الـ API عندهم. نص حتى 280 حرف وحتى 4 صور؛ الفيديو لسا مش مدعوم.

## يوتيوب
- فعّل YouTube Data API v3. الصلاحيات `youtube.upload, youtube.readonly, youtube.force-ssl`.
- مشاريع غوغل غير المدقَّقة بتنقفل على فيديوهات **خاصة** (والنشر العام بيتحدد بـ `YOUTUBE_PRIVACY`). لتنشر عام لازم تعمل **API Audit** وتعبي نموذج التدقيق من يوتيوب. الحصة اليومية الافتراضية بتكفي ~6 فيديوهات.
- العنوان = أول سطر من المنشور، والوصف = النص كامل.

## لينكدإن
- المنتج: Share on LinkedIn + Sign In with LinkedIn using OpenID Connect. الصلاحيات `openid profile w_member_social`.
- النشر باسم **شخص**. النشر باسم **صفحة شركة** بدو موافقة لينكدإن (Community Management API). نص فقط حالياً.

## ملاحظات أمان
- المفاتيح بكوليفاي فقط. ما بتنكتب بأي ملف ولا بالدردشة.
- لو تغيّر مفتاح التشفير (`Encryption__Key`) التوكنات المخزّنة ما بتنفك: لازم إعادة ربط الحسابات.
- التوكنات بتنخزّن مشفّرة (AES-256-GCM) وما بترجع للواجهة أبداً.
