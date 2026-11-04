using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Ai;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Infrastructure.Prompts;

public sealed record PromptDto(Guid Id, string Name, string? Category, string Task, string Body, IReadOnlyList<string> Variables, string? PreferredAi, int Version, bool IsActive);
public sealed record PromptVersionDto(int Version, string Body, DateTimeOffset CreatedAt);
public sealed record PromptDetailDto(PromptDto Prompt, IReadOnlyList<PromptVersionDto> Versions);
public sealed record PromptRunResult(string Text, string Provider, string Model, bool FellBack, string Rendered);

/// <summary>
/// مركز البرومبتات: كل البرومبتات بقاعدة البيانات، بمتغيرات {{اسم}} وإصدارات.
/// التطبيق بيطلب البرومبت بالاسم وقت التشغيل، فتعديله من الواجهة بيغيّر ناتج الذكاء الاصطناعي فوراً بدون أي برمجة.
/// </summary>
public sealed partial class PromptService(AppDbContext db, ITenantContext tenant, IAiRouter ai)
{
    private static readonly string[] Tasks =
        [AiTasks.Caption, AiTasks.Ideas, AiTasks.Rewrite, AiTasks.Analysis, AiTasks.Classify, AiTasks.Hashtags, AiTasks.Summarize, AiTasks.Reply];
    private static readonly string[] Ais = [AiProviders.Claude, AiProviders.OpenAi, AiProviders.Local];

    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex VariablePattern();

    private Guid Workspace => tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public static IReadOnlyList<string> ExtractVariables(string body) =>
        VariablePattern().Matches(body).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>بيعبّي المتغيرات. أي متغير ناقص = خطأ (ما بنبعت برومبت ناقص للذكاء الاصطناعي).</summary>
    public static string Render(string body, IReadOnlyDictionary<string, string>? values)
    {
        var missing = ExtractVariables(body).Where(v => values is null || !values.TryGetValue(v, out var x) || string.IsNullOrWhiteSpace(x)).ToList();
        if (missing.Count > 0) throw new AuthException(400, "variable_missing");
        return VariablePattern().Replace(body, m => values![m.Groups[1].Value]);
    }

    public async Task<IReadOnlyList<PromptDto>> ListAsync(CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        var rows = await db.Prompts.Where(p => p.WorkspaceId == Workspace).OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<PromptDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await FindAsync(id, ct);
        var versions = await db.PromptVersions.Where(v => v.PromptId == id).OrderByDescending(v => v.Version)
            .Select(v => new PromptVersionDto(v.Version, v.Body, v.CreatedAt)).ToListAsync(ct);
        return new PromptDetailDto(ToDto(p), versions);
    }

    public async Task<PromptDto> CreateAsync(string name, string? category, string task, string body, string? preferredAi, Guid userId, CancellationToken ct)
    {
        name = (name ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(name, "^[a-z][a-z0-9_]{1,59}$")) throw new AuthException(400, "name_invalid");
        Validate(body, task, preferredAi);
        if (await db.Prompts.AnyAsync(p => p.WorkspaceId == Workspace && p.Name == name, ct)) throw new AuthException(409, "name_taken");

        var prompt = NewPrompt(name, category, task, body, preferredAi, userId);
        db.Prompts.Add(prompt);
        db.PromptVersions.Add(NewVersion(prompt));
        await db.SaveChangesAsync(ct);
        return ToDto(prompt);
    }

    /// <summary>كل تعديل بالنص بيصير إصدار جديد؛ تعديل الإعدادات (المهمة، المزوّد) ما بيزيد الإصدار.</summary>
    public async Task<PromptDto> UpdateAsync(Guid id, string? category, string task, string body, string? preferredAi, CancellationToken ct)
    {
        Validate(body, task, preferredAi);
        var p = await FindAsync(id, ct);
        p.Category = category;
        p.Task = task;
        p.PreferredAi = string.IsNullOrWhiteSpace(preferredAi) ? null : preferredAi;
        if (p.Body != body)
        {
            p.Body = body;
            p.Version += 1;
            p.Variables = JsonSerializer.Serialize(ExtractVariables(body));
            db.PromptVersions.Add(NewVersion(p));
        }
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<PromptDto> RollbackAsync(Guid id, int version, CancellationToken ct)
    {
        var p = await FindAsync(id, ct);
        var old = await db.PromptVersions.FirstOrDefaultAsync(v => v.PromptId == id && v.Version == version, ct)
                  ?? throw new AuthException(404, "version_not_found");
        if (p.Body != old.Body)
        {
            p.Body = old.Body;
            p.Version += 1; // الرجوع بيسجّل إصدار جديد بنص قديم (السجل ما بينمسح)
            p.Variables = JsonSerializer.Serialize(ExtractVariables(p.Body));
            db.PromptVersions.Add(NewVersion(p));
        }
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<PromptDto> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var p = await FindAsync(id, ct);
        p.IsActive = active;
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    /// <summary>تجربة برومبت بقيم عينة (بيمر عبر الموجّه ويسجّل الاستهلاك).</summary>
    public async Task<PromptRunResult> TestAsync(Guid id, IReadOnlyDictionary<string, string>? values, CancellationToken ct) =>
        await RunAsync(await FindAsync(id, ct), values, ct);

    /// <summary>الاستعمال الفعلي: برومبت بالاسم من قاعدة البيانات (مؤلّف المنشورات، المجيب...).</summary>
    public async Task<PromptRunResult> RunByNameAsync(string name, IReadOnlyDictionary<string, string>? values, CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        var p = await db.Prompts.FirstOrDefaultAsync(x => x.WorkspaceId == Workspace && x.Name == name, ct)
                ?? throw new AuthException(404, "prompt_not_found");
        if (!p.IsActive) throw new AuthException(409, "prompt_inactive");
        return await RunAsync(p, values, ct);
    }

    private async Task<PromptRunResult> RunAsync(Prompt p, IReadOnlyDictionary<string, string>? values, CancellationToken ct)
    {
        var rendered = Render(p.Body, values);
        var result = await ai.RunAsync(new AiRequest(p.Task, [new AiMessage("user", rendered)], PreferProvider: p.PreferredAi, MaxTokens: 1500), ct);
        return new PromptRunResult(result.Text.Trim(), result.Provider, result.Model, result.FellBack, rendered);
    }

    /// <summary>مساحة جديدة بدون أي برومبت = نزرع المكتبة الجاهزة مرة وحدة.</summary>
    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        if (await db.Prompts.AnyAsync(p => p.WorkspaceId == Workspace, ct)) return;
        foreach (var s in PromptSeeds.All)
        {
            var prompt = NewPrompt(s.Name, s.Category, s.Task, s.Body, s.PreferredAi, null);
            db.Prompts.Add(prompt);
            db.PromptVersions.Add(NewVersion(prompt));
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // طلبين بنفس اللحظة زرعوا سوا: الفريد (مساحة، اسم) بيمنع التكرار، والثاني بيلقى الموجود.
            db.ChangeTracker.Clear();
        }
    }

    private Prompt NewPrompt(string name, string? category, string task, string body, string? preferredAi, Guid? userId) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = Workspace, Name = name, Category = category, Task = task, Body = body,
        Variables = JsonSerializer.Serialize(ExtractVariables(body)),
        PreferredAi = string.IsNullOrWhiteSpace(preferredAi) ? null : preferredAi,
        Version = 1, IsActive = true, CreatedBy = userId, CreatedAt = DateTimeOffset.UtcNow,
    };

    private PromptVersion NewVersion(Prompt p) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = Workspace, PromptId = p.Id, Version = p.Version, Body = p.Body, CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<Prompt> FindAsync(Guid id, CancellationToken ct) =>
        await db.Prompts.FirstOrDefaultAsync(p => p.Id == id && p.WorkspaceId == Workspace, ct) ?? throw new AuthException(404, "not_found");

    private static PromptDto ToDto(Prompt p) =>
        new(p.Id, p.Name, p.Category, p.Task, p.Body, ExtractVariables(p.Body), p.PreferredAi, p.Version, p.IsActive);

    private static void Validate(string body, string task, string? preferredAi)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new AuthException(400, "body_required");
        if (body.Length > 20000) throw new AuthException(400, "body_too_long");
        if (!Tasks.Contains(task)) throw new AuthException(400, "task_invalid");
        if (!string.IsNullOrWhiteSpace(preferredAi) && !Ais.Contains(preferredAi)) throw new AuthException(400, "ai_invalid");
    }
}
