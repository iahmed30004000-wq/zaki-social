using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Infrastructure.Prompts;

namespace ZakiSocial.Infrastructure.Automation;

public sealed record Totals(int Posts, double Reactions, double Comments, double Shares, double Views);
public sealed record PlatformRow(string Platform, int Posts, double Reactions, double Comments, double Shares, double Views);
public sealed record DayRow(string Date, int Posts, double Reactions);
public sealed record TopPost(Guid PostId, string Body, string Platform, string Account, double Reactions, double Comments);
public sealed record AnalyticsSummary(int Days, Totals Totals, IReadOnlyList<PlatformRow> ByPlatform, IReadOnlyList<DayRow> ByDay, IReadOnlyList<TopPost> Top);
public sealed record Recommendation(string Text, string Provider, int Posts);

/// <summary>
/// تحليلات الأداء من لقطات الجامع (آخر لقطة لكل منشور) + توصيات الذكاء الاصطناعي عبر برومبت analysis_performance.
/// </summary>
public sealed class AnalyticsService(AppDbContext db, ITenantContext tenant, PromptService prompts)
{
    private Guid Workspace => tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public async Task<AnalyticsSummary> SummaryAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var targets = await (from t in db.PostTargets
                             join a in db.SocialAccounts on t.AccountId equals a.Id
                             join p in db.Posts on t.PostId equals p.Id
                             where t.WorkspaceId == Workspace && t.Status == "published" && t.PublishedAt >= since
                             select new { t.Id, t.PostId, t.PublishedAt, a.Platform, Account = a.DisplayName, p.Body }).ToListAsync(ct);
        var ids = targets.Select(t => t.Id).ToList();
        var snaps = await db.AnalyticsSnapshots.Where(s => s.WorkspaceId == Workspace && s.PostTargetId != null && ids.Contains(s.PostTargetId.Value))
            .OrderByDescending(s => s.CapturedAt).Select(s => new { s.PostTargetId, s.Metrics }).ToListAsync(ct);

        // آخر لقطة لكل منشور
        var latest = new Dictionary<Guid, JsonElement>();
        foreach (var s in snaps)
        {
            if (latest.ContainsKey(s.PostTargetId!.Value)) continue;
            try { latest[s.PostTargetId.Value] = JsonDocument.Parse(s.Metrics).RootElement.Clone(); }
            catch (JsonException) { /* لقطة تالفة */ }
        }
        double M(Guid id, string name) => latest.TryGetValue(id, out var m) && m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        var rows = targets.Select(t => new { t.Id, t.PostId, t.PublishedAt, t.Platform, t.Account, t.Body, R = M(t.Id, "reactions"), C = M(t.Id, "comments"), S = M(t.Id, "shares"), V = M(t.Id, "views") }).ToList();

        return new AnalyticsSummary(days,
            new Totals(rows.Count, rows.Sum(r => r.R), rows.Sum(r => r.C), rows.Sum(r => r.S), rows.Sum(r => r.V)),
            rows.GroupBy(r => r.Platform).Select(g => new PlatformRow(g.Key, g.Count(), g.Sum(r => r.R), g.Sum(r => r.C), g.Sum(r => r.S), g.Sum(r => r.V))).OrderByDescending(r => r.Posts).ToList(),
            rows.GroupBy(r => r.PublishedAt!.Value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(g => g.Key).Select(g => new DayRow(g.Key, g.Count(), g.Sum(r => r.R))).ToList(),
            rows.OrderByDescending(r => r.R + r.C).Take(5).Select(r => new TopPost(r.PostId, r.Body.Length > 120 ? r.Body[..120] : r.Body, r.Platform, r.Account, r.R, r.C)).ToList());
    }

    public async Task<Recommendation> RecommendAsync(int days, CancellationToken ct)
    {
        var s = await SummaryAsync(days, ct);
        var sb = new StringBuilder();
        sb.AppendLine($"آخر {s.Days} يوم: {s.Totals.Posts} منشور، تفاعلات {s.Totals.Reactions}، تعليقات {s.Totals.Comments}، مشاركات {s.Totals.Shares}، مشاهدات {s.Totals.Views}.");
        foreach (var p in s.ByPlatform) sb.AppendLine($"- {p.Platform}: {p.Posts} منشور، تفاعلات {p.Reactions}، تعليقات {p.Comments}");
        foreach (var t in s.Top) sb.AppendLine($"* أفضل منشور ({t.Platform}): «{t.Body}» تفاعلات {t.Reactions} تعليقات {t.Comments}");
        if (s.Totals.Posts == 0) sb.Append("لا توجد منشورات منشورة بعد بهالفترة.");

        var result = await prompts.RunByNameAsync("analysis_performance", new Dictionary<string, string> { ["metrics"] = sb.ToString().Trim() }, ct);
        return new Recommendation(result.Text, result.Provider, s.Totals.Posts);
    }
}
