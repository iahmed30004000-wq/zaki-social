using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;
using ZakiSocial.Core.Jobs;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Automation;
using ZakiSocial.Infrastructure.ProxyPool;
using ZakiSocial.Infrastructure.Prompts;
using ZakiSocial.Infrastructure.Security;

namespace ZakiSocial.Workers;

/// <summary>جلسة حساب للعامل: فك التشفير، تجديد التوكن لو قرب ينتهي، والبروكسي الثابت للحساب.</summary>
public sealed class AccountSession(AppDbContext db, TokenCryptor crypto, IConnectorRegistry connectors, ProxyService proxies)
{
    public sealed class Handle(IPlatformConnector connector, AccountCredentials credentials, IDisposable proxyScope) : IDisposable
    {
        public IPlatformConnector Connector { get; } = connector;
        public AccountCredentials Credentials { get; } = credentials;
        public void Dispose() => proxyScope.Dispose();
    }

    public async Task<Handle> OpenAsync(SocialAccount account, CancellationToken ct)
    {
        if (account.EncAccessToken is null) throw new PlatformException(account.Platform, 409, "account_not_connected");
        var connector = connectors.Get(account.Platform);
        var credentials = new AccountCredentials(account.Id, account.RemoteUserId, crypto.Decrypt(account.EncAccessToken),
            account.EncRefreshToken is null ? null : crypto.Decrypt(account.EncRefreshToken));

        var scope = ProxyContext.Use(await proxies.ResolveUrlAsync(account.ProxyId, ct));
        try
        {
            if (account.TokenExpiry is { } expiry && expiry < DateTimeOffset.UtcNow.AddMinutes(5) && credentials.RefreshToken is not null)
            {
                var refreshed = await connector.RefreshAsync(credentials, ct);
                if (refreshed is not null)
                {
                    account.EncAccessToken = crypto.Encrypt(refreshed.AccessToken);
                    if (refreshed.RefreshToken is not null) account.EncRefreshToken = crypto.Encrypt(refreshed.RefreshToken);
                    account.TokenExpiry = refreshed.ExpiresAt;
                    await db.SaveChangesAsync(ct);
                    credentials = credentials with { AccessToken = refreshed.AccessToken, RefreshToken = refreshed.RefreshToken ?? credentials.RefreshToken };
                }
            }
            return new Handle(connector, credentials, scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}

/// <summary>
/// جامع البيانات: لكل حساب مفعّل بيسحب مقاييس المنشورات الأخيرة (لقطات تحليلات) وتعليقاتها الجديدة (للمجيب).
/// بيشتغل بوضع النظام على كل المساحات؛ فشل حساب ما بيوقف الباقي.
/// </summary>
public sealed class CollectorJob(AppDbContext db, ITenantContext tenant, AccountSession sessions, ILogger<CollectorJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid? workspaceId, CancellationToken ct)
    {
        tenant.UseSystem();
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var accounts = await db.SocialAccounts
            .Where(a => a.Status == "active" && a.EncAccessToken != null && (workspaceId == null || a.WorkspaceId == workspaceId))
            .ToListAsync(ct);

        foreach (var account in accounts)
        {
            try
            {
                var targets = await db.PostTargets
                    .Where(t => t.AccountId == account.Id && t.Status == "published" && t.RemotePostId != null && t.PublishedAt >= since)
                    .OrderByDescending(t => t.PublishedAt).Take(30).ToListAsync(ct);
                if (targets.Count == 0) continue;

                using var session = await sessions.OpenAsync(account, ct);
                foreach (var target in targets)
                {
                    var metrics = await session.Connector.FetchMetricsAsync(session.Credentials, target.RemotePostId!, ct);
                    if (metrics.Count > 0)
                        db.AnalyticsSnapshots.Add(new AnalyticsSnapshot
                        {
                            Id = Guid.NewGuid(), WorkspaceId = account.WorkspaceId, AccountId = account.Id, PostTargetId = target.Id,
                            Metrics = JsonSerializer.Serialize(metrics), CapturedAt = DateTimeOffset.UtcNow,
                        });

                    var comments = await session.Connector.FetchCommentsAsync(session.Credentials, target.RemotePostId!, ct);
                    var ownName = account.DisplayName.TrimStart('@');
                    var externalIds = comments.Select(c => $"{account.Platform}:{c.Id}").ToList();
                    var known = await db.CollectedData.Where(c => c.WorkspaceId == account.WorkspaceId && c.Kind == "comment" && externalIds.Contains(c.ExternalId!))
                        .Select(c => c.ExternalId!).ToListAsync(ct);
                    foreach (var c in comments)
                    {
                        var externalId = $"{account.Platform}:{c.Id}";
                        if (known.Contains(externalId) || c.AuthorName.TrimStart('@') == ownName) continue; // مكرر أو من صفحتنا نفسها
                        db.CollectedData.Add(new CollectedItem
                        {
                            Id = Guid.NewGuid(), WorkspaceId = account.WorkspaceId, AccountId = account.Id, Kind = "comment", ExternalId = externalId, CollectedAt = DateTimeOffset.UtcNow,
                            Data = JsonSerializer.Serialize(new { commentId = c.Id, author = c.AuthorName, text = c.Text, createdAt = c.CreatedAt, postTargetId = target.Id, remotePostId = target.RemotePostId }),
                        });
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) when (ex is PlatformException or HttpRequestException or DbUpdateException or TaskCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning("collector skipped account {Account} ({Platform}): {Error}", account.Id, account.Platform, ex.GetType().Name);
            }
        }
    }
}

/// <summary>
/// المجيب الآلي: للتعليقات الجديدة اللي بتطابق قاعدة مفعّلة، بيولّد الرد بالذكاء الاصطناعي (برومبت من المركز)
/// وبيجدوله بعد تأخير عشوائي طبيعي. كل تعليق بينرد عليه مرة وحدة (علامة reply فريدة).
/// </summary>
public sealed class ResponderJob(AppDbContext db, ITenantContext tenant, PromptService prompts, IJobQueue queue, ILogger<ResponderJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken ct)
    {
        tenant.UseSystem();
        var rules = await db.ResponderRules.Where(r => r.IsEnabled).ToListAsync(ct);

        foreach (var rule in rules)
        {
            tenant.UseWorkspace(rule.WorkspaceId, PublishJob.WorkerRole); // من هون العزل مطبّق على مساحة القاعدة
            var comments = await db.CollectedData
                .Where(c => c.WorkspaceId == rule.WorkspaceId && c.Kind == "comment" && (rule.AccountId == null || c.AccountId == rule.AccountId))
                .OrderByDescending(c => c.CollectedAt).Take(50).ToListAsync(ct);
            var done = (await db.CollectedData.Where(c => c.WorkspaceId == rule.WorkspaceId && c.Kind == "reply").Select(c => c.ExternalId!).ToListAsync(ct)).ToHashSet();

            foreach (var comment in comments)
            {
                if (done.Contains(comment.ExternalId!)) continue;
                using var doc = JsonDocument.Parse(comment.Data);
                var text = doc.RootElement.GetProperty("text").GetString() ?? "";
                if (!AutomationService.Matches(rule.MatchPattern, text)) continue;
                var remoteCommentId = doc.RootElement.GetProperty("commentId").GetString()!;

                // نحجز الرد أول (فريد بالمساحة+نوع+معرّف) قبل التوليد: ما بيصير ردين لنفس التعليق
                var marker = new CollectedItem
                {
                    Id = Guid.NewGuid(), WorkspaceId = rule.WorkspaceId, AccountId = comment.AccountId, Kind = "reply", ExternalId = comment.ExternalId, CollectedAt = DateTimeOffset.UtcNow,
                    Data = JsonSerializer.Serialize(new { commentId = remoteCommentId, status = "generating", text = "" }),
                };
                db.CollectedData.Add(marker);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); continue; }

                try
                {
                    var generated = await prompts.RunByNameAsync(rule.PromptName ?? "reply_comment",
                        new Dictionary<string, string> { ["comment"] = text, ["context"] = rule.Name }, ct);
                    marker.Data = JsonSerializer.Serialize(new { commentId = remoteCommentId, status = "queued", text = generated.Text });
                    await db.SaveChangesAsync(ct);
                    var delay = TimeSpan.FromSeconds(Random.Shared.Next(rule.DelayMinSeconds, rule.DelayMaxSeconds + 1));
                    queue.EnqueueReply(rule.WorkspaceId, marker.Id, delay);
                }
                catch (Exception ex) when (ex is Infrastructure.Auth.AuthException or HttpRequestException)
                {
                    // فشل التوليد (مثلاً الذكاء الاصطناعي مش متاح): بنشيل الحجز ليرجع يتجرّب بالدورة الجاية
                    logger.LogWarning("reply generation failed for a comment: {Error}", ex.GetType().Name);
                    db.CollectedData.Remove(marker);
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        tenant.UseSystem();
    }
}

/// <summary>إرسال رد مجدول على تعليق (بعد التأخير العشوائي).</summary>
public sealed class ReplyJob(AppDbContext db, ITenantContext tenant, AccountSession sessions, ILogger<ReplyJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid workspaceId, Guid markerId, CancellationToken ct)
    {
        tenant.UseWorkspace(workspaceId, PublishJob.WorkerRole);
        var marker = await db.CollectedData.FirstOrDefaultAsync(c => c.Id == markerId && c.WorkspaceId == workspaceId && c.Kind == "reply", ct);
        if (marker is null) return;
        using var doc = JsonDocument.Parse(marker.Data);
        if (doc.RootElement.GetProperty("status").GetString() != "queued") return; // تنفيذ مكرر آمن
        var commentId = doc.RootElement.GetProperty("commentId").GetString()!;
        var text = doc.RootElement.GetProperty("text").GetString()!;

        var account = marker.AccountId is null ? null : await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == marker.AccountId, ct);
        try
        {
            if (account is null) throw new PlatformException("unknown", 404, "account_gone");
            using var session = await sessions.OpenAsync(account, ct);
            await session.Connector.ReplyAsync(session.Credentials, commentId, text, ct);
            marker.Data = JsonSerializer.Serialize(new { commentId, status = "sent", text });
        }
        catch (Exception ex) when (ex is PlatformException or HttpRequestException)
        {
            marker.Data = JsonSerializer.Serialize(new { commentId, status = "failed", text, error = ex is PlatformException pe ? $"{pe.Status}: {pe.Message}" : ex.GetType().Name });
            logger.LogWarning("reply failed ({Error})", ex.GetType().Name);
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ProxyHealthJob(ITenantContext tenant, ProxyService proxies)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken ct)
    {
        tenant.UseSystem();
        await proxies.CheckAllAsync(ct);
    }
}
