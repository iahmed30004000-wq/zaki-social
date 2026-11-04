using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Jobs;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;

namespace ZakiSocial.Workers;

/// <summary>
/// المجدوِل: كل دقيقة بيحوّل المنشورات المجدولة المستحقة لمهام نشر بالطابور. بيشتغل بوضع النظام (كل المساحات)،
/// وبياخد المنشور بتحديث ذري (scheduled -> publishing) لحتى ما ينشر مرتين لو شغّال أكتر من عامل.
/// المنشور المتكرر (daily/weekly) بيعمل نسخة جديدة مجدولة للمرة الجاية.
/// </summary>
public sealed class SchedulerJob(AppDbContext db, ITenantContext tenant, IJobQueue queue, ILogger<SchedulerJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(CancellationToken ct)
    {
        tenant.UseSystem();
        var now = DateTimeOffset.UtcNow;
        var due = await db.Posts.Where(p => p.Status == PostStatus.Scheduled && p.ScheduledAt <= now)
            .OrderBy(p => p.ScheduledAt).Take(100).ToListAsync(ct);

        foreach (var post in due)
        {
            var claimed = await db.Posts.Where(p => p.Id == post.Id && p.Status == PostStatus.Scheduled)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PostStatus.Publishing), ct);
            if (claimed == 0) continue;

            var targets = await db.PostTargets.Where(t => t.PostId == post.Id && t.Status != "published").ToListAsync(ct);
            if (targets.Count == 0)
            {
                await db.Posts.Where(p => p.Id == post.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PostStatus.Failed), ct);
                continue;
            }
            foreach (var t in targets)
            {
                t.Status = "pending";
                t.Error = null;
            }
            await db.SaveChangesAsync(ct);
            foreach (var t in targets) queue.EnqueuePublish(post.WorkspaceId, t.Id);

            if (post.RepeatRule != "none") await CloneNextAsync(post, targets.Select(t => t.AccountId).ToList(), now, ct);
            logger.LogInformation("scheduled post {Post} queued ({Targets} targets)", post.Id, targets.Count);
        }
    }

    private async Task CloneNextAsync(Post post, IReadOnlyList<Guid> accountIds, DateTimeOffset now, CancellationToken ct)
    {
        var step = post.RepeatRule == "weekly" ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
        var next = post.ScheduledAt!.Value + step;
        while (next <= now) next += step; // لو الخادم كان واقف، ما نعبّي الماضي

        var copy = new Post
        {
            Id = Guid.NewGuid(), WorkspaceId = post.WorkspaceId, Body = post.Body, AiGenerated = post.AiGenerated,
            Status = PostStatus.Scheduled, ScheduledAt = next, RepeatRule = post.RepeatRule, CreatedBy = post.CreatedBy,
            ApprovedAt = post.ApprovedAt, ApprovedBy = post.ApprovedBy, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Posts.Add(copy);
        foreach (var accountId in accountIds)
            db.PostTargets.Add(new PostTarget { Id = Guid.NewGuid(), WorkspaceId = post.WorkspaceId, PostId = copy.Id, AccountId = accountId, Status = "pending" });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>بيسجّل المجدوِل كمهمة متكررة كل دقيقة بالإقلاع.</summary>
public sealed class RecurringJobsRegistrar(IRecurringJobManager manager, Microsoft.Extensions.Configuration.IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        manager.AddOrUpdate<SchedulerJob>("scheduler", j => j.RunAsync(CancellationToken.None), Cron.Minutely());
        manager.AddOrUpdate<CollectorJob>("collector", j => j.RunAsync(null, CancellationToken.None), configuration["Workers:CollectorCron"] ?? "*/15 * * * *");
        manager.AddOrUpdate<ResponderJob>("responder", j => j.RunAsync(CancellationToken.None), configuration["Workers:ResponderCron"] ?? "* * * * *");
        manager.AddOrUpdate<ProxyHealthJob>("proxy-health", j => j.RunAsync(CancellationToken.None), configuration["Workers:ProxyHealthCron"] ?? "*/10 * * * *");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
