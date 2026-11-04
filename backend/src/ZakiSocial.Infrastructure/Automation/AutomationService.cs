using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Infrastructure.Automation;

public sealed record RuleDto(Guid Id, string Name, Guid? AccountId, string? MatchPattern, string? PromptName, bool IsEnabled, int DelayMinSeconds, int DelayMaxSeconds);
public sealed record ReplyDto(Guid Id, string CommentId, string Status, string Text, string? Error, DateTimeOffset At);
public sealed record CollectorStatus(int Comments, int Replies, DateTimeOffset? LastCollectedAt);

/// <summary>قواعد المجيب الآلي وسجل الردود. كل قاعدة بتنشأ مطفية (IsEnabled=false) لحد ما تفعّلها بنفسك.</summary>
public sealed class AutomationService(AppDbContext db, ITenantContext tenant)
{
    private Guid Workspace => tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public async Task<IReadOnlyList<RuleDto>> RulesAsync(CancellationToken ct) =>
        (await db.ResponderRules.Where(r => r.WorkspaceId == Workspace).OrderBy(r => r.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<RuleDto> CreateRuleAsync(RuleInput i, CancellationToken ct)
    {
        Validate(i);
        var rule = new ResponderRule { Id = Guid.NewGuid(), WorkspaceId = Workspace, CreatedAt = DateTimeOffset.UtcNow };
        Apply(rule, i);
        await CheckAccountAsync(rule.AccountId, ct);
        db.ResponderRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task<RuleDto> UpdateRuleAsync(Guid id, RuleInput i, CancellationToken ct)
    {
        Validate(i);
        var rule = await db.ResponderRules.FirstOrDefaultAsync(r => r.Id == id && r.WorkspaceId == Workspace, ct) ?? throw new AuthException(404, "not_found");
        Apply(rule, i);
        await CheckAccountAsync(rule.AccountId, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct)
    {
        var rule = await db.ResponderRules.FirstOrDefaultAsync(r => r.Id == id && r.WorkspaceId == Workspace, ct) ?? throw new AuthException(404, "not_found");
        db.ResponderRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReplyDto>> RepliesAsync(CancellationToken ct)
    {
        var rows = await db.CollectedData.Where(c => c.WorkspaceId == Workspace && c.Kind == "reply").OrderByDescending(c => c.CollectedAt).Take(50).ToListAsync(ct);
        return rows.Select(r =>
        {
            using var doc = JsonDocument.Parse(r.Data);
            string S(string n) => doc.RootElement.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            return new ReplyDto(r.Id, S("commentId"), S("status"), S("text"), S("error") is { Length: > 0 } e ? e : null, r.CollectedAt);
        }).ToList();
    }

    public async Task<CollectorStatus> StatusAsync(CancellationToken ct)
    {
        var comments = await db.CollectedData.CountAsync(c => c.WorkspaceId == Workspace && c.Kind == "comment", ct);
        var replies = await db.CollectedData.CountAsync(c => c.WorkspaceId == Workspace && c.Kind == "reply", ct);
        var last = await db.CollectedData.Where(c => c.WorkspaceId == Workspace && c.Kind == "comment").MaxAsync(c => (DateTimeOffset?)c.CollectedAt, ct);
        return new CollectorStatus(comments, replies, last);
    }

    /// <summary>هل نص التعليق بيطابق القاعدة؟ فارغ = كل التعليقات. regex غير صالح = بحث نصي عادي.</summary>
    public static bool Matches(string? pattern, string text)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)); }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException) { return text.Contains(pattern, StringComparison.OrdinalIgnoreCase); }
    }

    private async Task CheckAccountAsync(Guid? accountId, CancellationToken ct)
    {
        if (accountId is not null && !await db.SocialAccounts.AnyAsync(a => a.Id == accountId && a.WorkspaceId == Workspace, ct)) throw new AuthException(400, "account_invalid");
    }

    private static void Validate(RuleInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Name)) throw new AuthException(400, "name_required");
        if (i.DelayMinSeconds < 1 || i.DelayMaxSeconds < i.DelayMinSeconds || i.DelayMaxSeconds > 86400) throw new AuthException(400, "delay_invalid");
        if (i.PromptName is { Length: > 60 }) throw new AuthException(400, "name_invalid");
    }

    private static void Apply(ResponderRule rule, RuleInput i)
    {
        rule.Name = i.Name.Trim();
        rule.AccountId = i.AccountId;
        rule.MatchPattern = string.IsNullOrWhiteSpace(i.MatchPattern) ? null : i.MatchPattern.Trim();
        rule.PromptName = string.IsNullOrWhiteSpace(i.PromptName) ? null : i.PromptName.Trim();
        rule.IsEnabled = i.IsEnabled;
        rule.DelayMinSeconds = i.DelayMinSeconds;
        rule.DelayMaxSeconds = i.DelayMaxSeconds;
    }

    private static RuleDto ToDto(ResponderRule r) => new(r.Id, r.Name, r.AccountId, r.MatchPattern, r.PromptName, r.IsEnabled, r.DelayMinSeconds, r.DelayMaxSeconds);
}

public sealed record RuleInput(string Name, Guid? AccountId, string? MatchPattern, string? PromptName, bool IsEnabled, int DelayMinSeconds, int DelayMaxSeconds);
