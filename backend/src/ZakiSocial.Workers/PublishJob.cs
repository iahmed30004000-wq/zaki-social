using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;
using ZakiSocial.Core.Jobs;
using ZakiSocial.Core.Media;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Media;
using ZakiSocial.Infrastructure.ProxyPool;
using ZakiSocial.Infrastructure.Security;

namespace ZakiSocial.Workers;

/// <summary>
/// عامل الناشر: ينفّذ نشر هدف واحد (حساب واحد). إعادة المحاولة بتأجيل متزايد (Workers:RetryDelaysSeconds)،
/// وبعد آخر محاولة الهدف بيصير failed والمنشور بيتحدّث. Hangfire بدون إعادة تلقائية، لأن الجدولة هون.
/// </summary>
public sealed class PublishJob(
    AppDbContext db,
    ITenantContext tenant,
    TokenCryptor crypto,
    IMediaStorage storage,
    PublicMediaUrlService publicUrls,
    ProxyService proxies,
    IConnectorRegistry connectors,
    IJobQueue queue,
    IConfiguration configuration,
    ILogger<PublishJob> logger)
{
    public const string WorkerRole = "worker";

    // سقف النشر المتزامن بالعملية (حماية من حدود المنصات): Workers:MaxConcurrentPublishes
    private static SemaphoreSlim? _gate;
    private static readonly object GateLock = new();
    private SemaphoreSlim Gate { get { lock (GateLock) { return _gate ??= new SemaphoreSlim(configuration.GetValue("Workers:MaxConcurrentPublishes", 3)); } } }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid workspaceId, Guid targetId, int attempt, CancellationToken ct)
    {
        tenant.UseWorkspace(workspaceId, WorkerRole);

        var target = await db.PostTargets.FirstOrDefaultAsync(t => t.Id == targetId && t.WorkspaceId == workspaceId, ct);
        if (target is null || target.Status == "published") return; // انحذف أو انتشر من قبل (تنفيذ مكرر آمن)

        var post = await db.Posts.FirstAsync(p => p.Id == target.PostId, ct);
        var account = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == target.AccountId, ct);

        target.Status = "publishing";
        await db.SaveChangesAsync(ct);

        try
        {
            if (account?.EncAccessToken is null) throw new PlatformException(account?.Platform ?? "unknown", 409, "account_not_connected");
            var credentials = new AccountCredentials(account.Id, account.RemoteUserId, crypto.Decrypt(account.EncAccessToken),
                account.EncRefreshToken is null ? null : crypto.Decrypt(account.EncRefreshToken));
            var connector = connectors.Get(account.Platform);
            using var proxyScope = ProxyContext.Use(await proxies.ResolveUrlAsync(account.ProxyId, ct)); // بروكسي الحساب الثابت
            // تجديد التوكن قبل انتهائه (تيك توك: يومياً، X: ساعتين، يوتيوب: ساعة...)
            if (account.TokenExpiry is { } expiry && expiry < DateTimeOffset.UtcNow.AddMinutes(5) && credentials.RefreshToken is not null)
            {
                try
                {
                    var refreshed = await connector.RefreshAsync(credentials, ct);
                    if (refreshed is not null)
                    {
                        account.EncAccessToken = crypto.Encrypt(refreshed.AccessToken);
                        if (refreshed.RefreshToken is not null) account.EncRefreshToken = crypto.Encrypt(refreshed.RefreshToken);
                        account.TokenExpiry = refreshed.ExpiresAt;
                        account.Status = "active";
                        await db.SaveChangesAsync(ct);
                        credentials = credentials with { AccessToken = refreshed.AccessToken, RefreshToken = refreshed.RefreshToken ?? credentials.RefreshToken };
                    }
                }
                catch (PlatformException)
                {
                    account.Status = "expired"; // لازم إعادة ربط الحساب
                    await db.SaveChangesAsync(ct);
                    throw;
                }
            }

            // وسائط المنشور بالترتيب (النسخة الموحّدة الجاهزة فقط؛ غير الجاهز = إعادة محاولة)
            var mediaFiles = new List<MediaFile>();
            var assets = await (from pm in db.PostMedia join m in db.MediaAssets on pm.MediaId equals m.Id
                                where pm.PostId == post.Id orderby pm.SortOrder select m).ToListAsync(ct);
            foreach (var m in assets)
            {
                if (m.Status != "ready" || m.ProcessedKey is null) throw new PlatformException(account.Platform, 409, "media_not_ready");
                await using var stream = await storage.OpenReadAsync(m.ProcessedKey, ct);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                var video = m.Kind == "video";
                mediaFiles.Add(new MediaFile(m.Kind, video ? "video/mp4" : "image/jpeg", video ? "video.mp4" : "image.jpg", buffer.ToArray(), PublicUrl: publicUrls.CreateAbsolute(workspaceId, m.Id, m.Kind)));
            }

            PublishResult result;
            await Gate.WaitAsync(ct);
            try { result = await connector.PublishAsync(credentials, new PostContent(post.Body, mediaFiles), ct); }
            finally { Gate.Release(); }

            target.Status = "published";
            target.RemotePostId = result.RemoteId;
            target.PublishedAt = DateTimeOffset.UtcNow;
            target.Error = null;
            logger.LogInformation("published target {Target} on {Platform}", targetId, account.Platform);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var delays = Delays();
            // نسجّل الرسالة المختصرة بس (بدون توكنات).
            target.Error = Truncate(ex is PlatformException pe ? $"{pe.Status}: {pe.Message}" : Sanitize($"{ex.GetType().Name}: {ex.Message}"));
            if (ex is PlatformException { Status: 401 } && account is not null) account.Status = "expired";
            if (attempt <= delays.Length && !IsPermanent(ex))
            {
                target.Status = "pending";
                await db.SaveChangesAsync(ct);
                queue.EnqueuePublish(workspaceId, targetId, attempt + 1, delays[attempt - 1]);
                logger.LogWarning("publish attempt {Attempt} failed for {Target}; retrying", attempt, targetId);
                return;
            }
            target.Status = "failed";
            logger.LogError("publish failed for {Target} after {Attempt} attempts", targetId, attempt);
        }

        await db.SaveChangesAsync(ct);
        await UpdatePostStatusAsync(post.Id, ct);
    }

    /// <summary>حالة المنشور من حالة أهدافه: published لو الكل انتشر، failed لو في فاشل وما في شي لسا شغّال.</summary>
    private async Task UpdatePostStatusAsync(Guid postId, CancellationToken ct)
    {
        var statuses = await db.PostTargets.Where(t => t.PostId == postId).Select(t => t.Status).ToListAsync(ct);
        var post = await db.Posts.FirstAsync(p => p.Id == postId, ct);
        if (statuses.All(s => s == "published")) post.Status = PostStatus.Published;
        else if (!statuses.Any(s => s is "pending" or "publishing") && statuses.Any(s => s == "failed")) post.Status = PostStatus.Failed;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>رفض نهائي من المنصة (طلب خاطئ، ممنوع، غير موجود...): إعادة المحاولة ما بتفيد.</summary>
    private static bool IsPermanent(Exception ex) => ex is PlatformException { Status: 400 or 401 or 403 or 404 or 413 or 415 or 422 or 501 };

    private TimeSpan[] Delays() =>
        (configuration["Workers:RetryDelaysSeconds"] ?? "30,120,600,1800,3600")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => TimeSpan.FromSeconds(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

    /// <summary>أي خطأ غير متوقع بيتسجّل بالهدف (ما بيضل عالق)، بعد إخفاء بيانات الدخول من أي عنوان.</summary>
    private static string Sanitize(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"://[^/@\s]*@", "://***@");

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
