using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Jobs;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Infrastructure.Posts;

public sealed record TargetDto(Guid Id, Guid AccountId, string AccountName, string Platform, string Status, string? RemotePostId, string? Error, DateTimeOffset? PublishedAt);
public sealed record PostDto(Guid Id, string Body, string Status, bool AiGenerated, DateTimeOffset? ApprovedAt, DateTimeOffset? ScheduledAt, string RepeatRule, DateTimeOffset CreatedAt, IReadOnlyList<TargetDto> Targets);

/// <summary>
/// المنشورات. حالات المنشور: draft -> publishing -> published | failed.
/// publish هنا بس بيغيّر الحالة وبيحط مهام بالطابور؛ التنفيذ الفعلي عند العامل (PublishJob).
/// </summary>
public sealed class PostService(AppDbContext db, ITenantContext tenant, IJobQueue queue)
{
    private Guid Workspace => tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public async Task<IReadOnlyList<PostDto>> ListAsync(string? status, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = db.Posts.Where(p => p.WorkspaceId == Workspace);
        if (!string.IsNullOrEmpty(status)) query = query.Where(p => p.Status == status);
        // التقويم: المنشورات ضمن فترة (وقت الجدولة، وإلا وقت الإنشاء)
        if (from is not null) query = query.Where(p => (p.ScheduledAt ?? p.CreatedAt) >= from);
        if (to is not null) query = query.Where(p => (p.ScheduledAt ?? p.CreatedAt) < to);
        var posts = await query.OrderByDescending(p => p.ScheduledAt ?? p.CreatedAt).Take(from is null && to is null ? 50 : 500).ToListAsync(ct);
        return await ToDtosAsync(posts, ct);
    }

    public async Task<PostDto> GetAsync(Guid id, CancellationToken ct)
    {
        var post = await FindAsync(id, ct);
        return (await ToDtosAsync([post], ct))[0];
    }

    public async Task<PostDto> CreateAsync(string body, IReadOnlyList<Guid> accountIds, Guid createdBy, bool aiGenerated, CancellationToken ct)
    {
        ValidateBody(body);
        var post = new Post { Id = Guid.NewGuid(), WorkspaceId = Workspace, Body = body.Trim(), AiGenerated = aiGenerated, Status = PostStatus.Draft, CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow };
        db.Posts.Add(post);
        await AddTargetsAsync(post, accountIds, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(post.Id, ct);
    }

    public async Task<PostDto> UpdateDraftAsync(Guid id, string body, IReadOnlyList<Guid> accountIds, CancellationToken ct)
    {
        ValidateBody(body);
        var post = await FindAsync(id, ct);
        if (post.Status != PostStatus.Draft) throw new AuthException(409, "post_not_editable");
        post.Body = body.Trim();
        post.ApprovedAt = null;
        post.ApprovedBy = null;
        db.PostTargets.RemoveRange(db.PostTargets.Where(t => t.PostId == id));
        await AddTargetsAsync(post, accountIds, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var post = await FindAsync(id, ct);
        if (post.Status is PostStatus.Publishing or PostStatus.Published) throw new AuthException(409, "post_not_deletable");
        db.Posts.Remove(post);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>اعتماد بشري لمنشور مولّد بالذكاء الاصطناعي. بيلغي أي تعديل لاحق على النص.</summary>
    public async Task<PostDto> ApproveAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var post = await FindAsync(id, ct);
        if (post.Status != PostStatus.Draft) throw new AuthException(409, "post_not_editable");
        post.ApprovedAt = DateTimeOffset.UtcNow;
        post.ApprovedBy = userId;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>جدولة أو إعادة جدولة (سحب وإفلات بالتقويم). الوقت UTC؛ الواجهة بتحوّل من توقيت المستخدم.</summary>
    public async Task<PostDto> ScheduleAsync(Guid id, DateTimeOffset when, string? repeat, CancellationToken ct)
    {
        var post = await FindAsync(id, ct);
        if (post.Status is not (PostStatus.Draft or PostStatus.Scheduled)) throw new AuthException(409, "post_not_schedulable");
        if (post.AiGenerated && post.ApprovedAt is null) throw new AuthException(409, "approval_required");
        if (when < DateTimeOffset.UtcNow.AddSeconds(-5)) throw new AuthException(400, "schedule_in_past");
        repeat = string.IsNullOrWhiteSpace(repeat) ? post.RepeatRule : repeat;
        if (repeat is not ("none" or "daily" or "weekly")) throw new AuthException(400, "repeat_invalid");
        if (!await db.PostTargets.AnyAsync(t => t.PostId == id, ct)) throw new AuthException(400, "no_targets");
        post.Status = PostStatus.Scheduled;
        post.ScheduledAt = when.ToUniversalTime();
        post.RepeatRule = repeat;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PostDto> UnscheduleAsync(Guid id, CancellationToken ct)
    {
        var post = await FindAsync(id, ct);
        if (post.Status != PostStatus.Scheduled) throw new AuthException(409, "post_not_scheduled");
        post.Status = PostStatus.Draft;
        post.ScheduledAt = null;
        post.RepeatRule = "none";
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>ينقل المنشور لحالة النشر ويحط مهمة لكل حساب مش منشور بعد. ما بينشر بنفسه.</summary>
    public async Task<PostDto> PublishAsync(Guid id, CancellationToken ct)
    {
        var post = await FindAsync(id, ct);
        if (post.Status is not (PostStatus.Draft or PostStatus.Failed or PostStatus.Scheduled)) throw new AuthException(409, "post_not_publishable");
        if (post.AiGenerated && post.ApprovedAt is null) throw new AuthException(409, "approval_required"); // محتوى الذكاء الاصطناعي لازم اعتماد بشري

        var targets = await db.PostTargets.Where(t => t.PostId == id && t.Status != "published").ToListAsync(ct);
        if (targets.Count == 0) throw new AuthException(400, "no_targets");

        post.Status = PostStatus.Publishing;
        post.ScheduledAt = null;
        foreach (var t in targets)
        {
            t.Status = "pending";
            t.Error = null;
        }
        await db.SaveChangesAsync(ct);

        foreach (var t in targets) queue.EnqueuePublish(Workspace, t.Id);
        return await GetAsync(id, ct);
    }

    private async Task AddTargetsAsync(Post post, IReadOnlyList<Guid> accountIds, CancellationToken ct)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var valid = await db.SocialAccounts.Where(a => a.WorkspaceId == Workspace && ids.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
        if (valid.Count != ids.Count) throw new AuthException(400, "account_invalid");
        foreach (var accountId in ids)
            db.PostTargets.Add(new PostTarget { Id = Guid.NewGuid(), WorkspaceId = Workspace, PostId = post.Id, AccountId = accountId, Status = "pending" });
    }

    private async Task<Post> FindAsync(Guid id, CancellationToken ct) =>
        await db.Posts.FirstOrDefaultAsync(p => p.Id == id && p.WorkspaceId == Workspace, ct) ?? throw new AuthException(404, "not_found");

    private async Task<IReadOnlyList<PostDto>> ToDtosAsync(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var ids = posts.Select(p => p.Id).ToList();
        var targets = await (from t in db.PostTargets
                             join a in db.SocialAccounts on t.AccountId equals a.Id
                             where ids.Contains(t.PostId)
                             select new { t.PostId, Dto = new TargetDto(t.Id, a.Id, a.DisplayName, a.Platform, t.Status, t.RemotePostId, t.Error, t.PublishedAt) })
            .ToListAsync(ct);
        return posts.Select(p => new PostDto(p.Id, p.Body, p.Status, p.AiGenerated, p.ApprovedAt, p.ScheduledAt, p.RepeatRule, p.CreatedAt,
            targets.Where(t => t.PostId == p.Id).Select(t => t.Dto).OrderBy(t => t.AccountName).ToList())).ToList();
    }

    private static void ValidateBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new AuthException(400, "body_required");
        if (body.Length > 20000) throw new AuthException(400, "body_too_long");
    }
}
