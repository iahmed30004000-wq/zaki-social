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
                mediaFiles.Add(new MediaFile(m.Kind, video ? "video/mp4" : "image/jpeg", video ? "video.mp4" : "image.jpg", buffer.ToArray()));
            }

            PublishResult result;
            await Gate.WaitAsync(ct);
            try { result = await connectors.Get(account.Platform).PublishAsync(credentials, new PostContent(post.Body, mediaFiles), ct); }
            finally { Gate.Release(); }

            target.Status = "published";
            target.RemotePostId = result.RemoteId;
            target.PublishedAt = DateTimeOffset.UtcNow;
            target.Error = null;
            logger.LogInformation("published target {Target} on {Platform}", targetId, account.Platform);
        }
        catch (Exception ex) when (ex is PlatformException or HttpRequestException or TaskCanceledException)
        {
            var delays = Delays();
            // نسجّل الرسالة المختصرة بس (بدون توكنات).
            target.Error = Truncate(ex is PlatformException pe ? $"{pe.Status}: {pe.Message}" : ex.GetType().Name);
            if (attempt <= delays.Length)
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

    private TimeSpan[] Delays() =>
        (configuration["Workers:RetryDelaysSeconds"] ?? "30,120,600,1800,3600")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => TimeSpan.FromSeconds(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
