using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZakiSocial.Core.Ai;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Infrastructure.Ai;

public sealed record UsageRow(string Provider, int Requests, long InputTokens, long OutputTokens);

/// <summary>
/// الموجّه الموحّد: الباقي بالنظام ما بيحكي مع مزوّد مباشرة. بيختار المزوّد حسب المهمة والخصوصية والتوفّر،
/// وبيحوّل تلقائياً للبديل لو فشل، وبيسجّل الاستهلاك لكل مساحة وبيطبّق حد الشهر.
/// </summary>
public sealed class AiRouter(
    IEnumerable<IAiProvider> providers,
    AppDbContext db,
    ITenantContext tenant,
    IConfiguration configuration,
    ILogger<AiRouter> logger) : IAiRouter
{
    private static readonly string[] Creative = [AiProviders.Claude, AiProviders.OpenAi, AiProviders.Local];
    private static readonly string[] Bulk = [AiProviders.Local, AiProviders.OpenAi, AiProviders.Claude];
    private static readonly string[] Vision = [AiProviders.OpenAi, AiProviders.Claude];

    /// <summary>سياسة التوجيه: المهمة -> ترتيب المزوّدين المفضّل.</summary>
    private static readonly Dictionary<string, string[]> Policy = new()
    {
        [AiTasks.Caption] = Creative,
        [AiTasks.Ideas] = Creative,
        [AiTasks.Rewrite] = Creative,
        [AiTasks.Analysis] = Creative,
        [AiTasks.Classify] = Bulk,
        [AiTasks.Hashtags] = Bulk,
        [AiTasks.Summarize] = Bulk,
        [AiTasks.Reply] = Bulk,
        [AiTasks.Vision] = Vision,
    };

    public IReadOnlyList<(string Name, bool Configured, AiCapabilities Capabilities)> Providers() =>
        providers.Select(p => (p.Name, p.IsConfigured, p.Capabilities)).ToList();

    public async Task<AiResult> RunAsync(AiRequest request, CancellationToken ct)
    {
        await EnforceQuotaAsync(ct);
        var candidates = Candidates(request);
        for (var i = 0; i < candidates.Count; i++)
        {
            try
            {
                var result = await candidates[i].CompleteAsync(request, ct) with { FellBack = i > 0 };
                await RecordAsync(request.Task, result, ct);
                return result;
            }
            catch (Exception ex) when (IsProviderFailure(ex, ct))
            {
                logger.LogWarning("ai provider {Provider} failed ({Error}); trying next", candidates[i].Name, ex.GetType().Name);
            }
        }
        throw new AuthException(502, "ai_unavailable");
    }

    /// <summary>توليد صورة: أول مزوّد صور مفعّل (OpenAI ثم الداخلي)، مع تحويل تلقائي، وتسجيل الاستهلاك.</summary>
    public async Task<ImageResult> GenerateImageAsync(string prompt, string size, CancellationToken ct)
    {
        await EnforceQuotaAsync(ct);
        var order = new[] { AiProviders.OpenAi, AiProviders.Local };
        var candidates = order.Select(n => providers.OfType<IImageProvider>().FirstOrDefault(p => p.Name == n)).Where(p => p is { ImageConfigured: true }).Cast<IImageProvider>().ToList();
        if (candidates.Count == 0) throw new AuthException(503, "image_not_configured");
        for (var i = 0; i < candidates.Count; i++)
        {
            try
            {
                var image = await candidates[i].GenerateImageAsync(prompt, size, ct);
                await RecordAsync(AiTasks.Image, new AiResult("", image.Provider, image.Model, prompt.Length / 4, 0, i > 0), ct);
                return image;
            }
            catch (Exception ex) when (IsProviderFailure(ex, ct))
            {
                logger.LogWarning("image provider {Provider} failed ({Error}); trying next", candidates[i].Name, ex.GetType().Name);
            }
        }
        throw new AuthException(502, "ai_unavailable");
    }

    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        await EnforceQuotaAsync(ct);
        foreach (var provider in Candidates(request))
        {
            var enumerator = provider.StreamAsync(request, ct).GetAsyncEnumerator(ct);
            bool hasFirst;
            try
            {
                hasFirst = await enumerator.MoveNextAsync(); // الفشل قبل أول جزء = نجرّب المزوّد التالي
            }
            catch (Exception ex) when (IsProviderFailure(ex, ct))
            {
                await enumerator.DisposeAsync();
                logger.LogWarning("ai provider {Provider} failed to stream ({Error}); trying next", provider.Name, ex.GetType().Name);
                continue;
            }

            var chars = 0;
            try
            {
                if (hasFirst)
                {
                    chars += enumerator.Current.Length;
                    yield return enumerator.Current;
                    while (await enumerator.MoveNextAsync())
                    {
                        chars += enumerator.Current.Length;
                        yield return enumerator.Current;
                    }
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            // البث ما بيرجّع عدّاد توكنات: تقدير تقريبي (حرف لكل ربع توكن).
            await RecordAsync(request.Task, new AiResult("", provider.Name, "stream", 0, chars / 4), ct);
            yield break;
        }
        throw new AuthException(502, "ai_unavailable");
    }

    public async Task<IReadOnlyList<UsageRow>> UsageThisMonthAsync(CancellationToken ct)
    {
        var from = MonthStart();
        return await db.AiUsages
            .Where(u => u.CreatedAt >= from)
            .GroupBy(u => u.Provider)
            .Select(g => new UsageRow(g.Key, g.Count(), g.Sum(u => (long)u.InputTokens), g.Sum(u => (long)u.OutputTokens)))
            .ToListAsync(ct);
    }

    private List<IAiProvider> Candidates(AiRequest request)
    {
        var needsVision = request.Task == AiTasks.Vision || request.Messages.Any(m => m.Images is { Count: > 0 });
        var order = request.Sensitive
            ? [AiProviders.Local] // بيانات حسّاسة ما بتغادر السيرفر
            : Policy.GetValueOrDefault(request.Task, Creative).ToList();

        if (!request.Sensitive && !string.IsNullOrWhiteSpace(request.PreferProvider))
        {
            order.Remove(request.PreferProvider);
            order.Insert(0, request.PreferProvider);
        }

        var list = order
            .Select(name => providers.FirstOrDefault(p => p.Name == name))
            .Where(p => p is { IsConfigured: true } && (!needsVision || p.Capabilities.Vision))
            .Cast<IAiProvider>()
            .ToList();
        if (list.Count == 0) throw new AuthException(503, "ai_not_configured");
        return list;
    }

    private async Task EnforceQuotaAsync(CancellationToken ct)
    {
        var limit = configuration.GetValue<long>("Ai:MonthlyTokenLimit");
        if (limit <= 0 || tenant.WorkspaceId is null) return;
        var from = MonthStart();
        var used = await db.AiUsages.Where(u => u.CreatedAt >= from).SumAsync(u => (long)u.InputTokens + u.OutputTokens, ct);
        if (used >= limit) throw new AuthException(429, "ai_quota_exceeded");
    }

    private async Task RecordAsync(string task, AiResult result, CancellationToken ct)
    {
        if (tenant.WorkspaceId is not { } workspace) return;
        db.AiUsages.Add(new AiUsage
        {
            Id = Guid.NewGuid(), WorkspaceId = workspace, Provider = result.Provider, Model = result.Model, Task = task,
            InputTokens = result.InputTokens, OutputTokens = result.OutputTokens, FellBack = result.FellBack, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private static DateTimeOffset MonthStart() => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>فشل المزوّد (شبكة، رفض، مهلة، قاطع الدارة) بيستحق البديل؛ إلغاء الطالب لا.</summary>
    private static bool IsProviderFailure(Exception ex, CancellationToken ct) =>
        !(ex is OperationCanceledException && ct.IsCancellationRequested) && ex is not AuthException;
}
