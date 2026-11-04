using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Infrastructure.Prompts;

namespace ZakiSocial.Infrastructure.Posts;

public sealed record BestTimeResult(string Text, string Provider, int DataPoints);

/// <summary>اقتراح أفضل وقت نشر: الموجّه بيقرأ أداء المنشورات السابقة وبيقترح أوقات (البرومبت best_time من مركز البرومبتات).</summary>
public sealed class ScheduleService(AppDbContext db, ITenantContext tenant, PromptService prompts)
{
    public async Task<BestTimeResult> BestTimeAsync(string? timezone, CancellationToken ct)
    {
        var workspace = tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");
        var zone = ResolveZone(timezone);
        var since = DateTimeOffset.UtcNow.AddDays(-60);

        var rows = await (from t in db.PostTargets
                          join s in db.AnalyticsSnapshots on t.Id equals s.PostTargetId into snaps
                          where t.WorkspaceId == workspace && t.Status == "published" && t.PublishedAt >= since
                          select new { t.PublishedAt, Metrics = snaps.OrderByDescending(x => x.CapturedAt).Select(x => x.Metrics).FirstOrDefault() })
            .Take(200).ToListAsync(ct);

        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            var local = TimeZoneInfo.ConvertTime(r.PublishedAt!.Value, zone);
            sb.AppendLine($"{local.ToString("yyyy-MM-dd ddd HH:mm", CultureInfo.InvariantCulture)} — {r.Metrics ?? "no metrics yet"}");
        }
        if (rows.Count == 0) sb.Append("لا توجد بيانات نشر سابقة بعد؛ اقترح أوقات عامة جيدة للنشر.");

        var result = await prompts.RunByNameAsync("best_time",
            new Dictionary<string, string> { ["metrics"] = sb.ToString().Trim(), ["timezone"] = zone.Id }, ct);
        return new BestTimeResult(result.Text, result.Provider, rows.Count);
    }

    private static TimeZoneInfo ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new AuthException(400, "timezone_invalid"); }
    }
}
