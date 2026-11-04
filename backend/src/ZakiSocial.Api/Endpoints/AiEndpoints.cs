using System.Text;
using ZakiSocial.Core.Ai;
using ZakiSocial.Infrastructure.Ai;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Api.Endpoints;

public sealed record ImageRequest(string? Prompt, string? PromptName, Dictionary<string, string>? Variables, string? Size);
public sealed record AiCompleteRequest(string Task, string Prompt, string? System, string? PreferProvider, bool Sensitive, bool Stream, int? MaxTokens);

public static class AiEndpoints
{
    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ai");

        g.MapGet("/providers", (IAiRouter router) =>
            Results.Ok(router.Providers().Select(p => new { name = p.Name, configured = p.Configured, vision = p.Capabilities.Vision })))
            .RequireAuthorization(AuthPolicies.Member);

        // فحص سريع: ?provider=claude|openai|local بيجبر مزوّد معيّن (للتأكد من التحويل التلقائي).
        g.MapGet("/ping", async (string? provider, IAiRouter router, CancellationToken ct) =>
        {
            var r = await router.RunAsync(new AiRequest(AiTasks.Classify, [new AiMessage("user", "ping")], MaxTokens: 16, PreferProvider: provider), ct);
            return Results.Ok(new { provider = r.Provider, model = r.Model, text = r.Text, fellBack = r.FellBack });
        }).RequireAuthorization(AuthPolicies.Editor);

        g.MapPost("/complete", async (AiCompleteRequest r, IAiRouter router, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(r.Prompt)) return Results.BadRequest(new { error = "prompt_required" });
            var request = new AiRequest(string.IsNullOrWhiteSpace(r.Task) ? AiTasks.Caption : r.Task, [new AiMessage("user", r.Prompt)],
                r.System, r.MaxTokens is > 0 and <= 4096 ? r.MaxTokens.Value : 1024, r.Sensitive, r.PreferProvider);

            if (!r.Stream)
            {
                var res = await router.RunAsync(request, ct);
                return Results.Ok(new { provider = res.Provider, model = res.Model, text = res.Text, fellBack = res.FellBack });
            }

            http.Response.ContentType = "text/event-stream";
            await foreach (var piece in router.StreamAsync(request, ct))
                await http.Response.WriteAsync($"data: {piece.Replace("\n", "\\n")}\n\n", Encoding.UTF8, ct);
            await http.Response.WriteAsync("data: [DONE]\n\n", ct);
            return Results.Empty;
        }).RequireAuthorization(AuthPolicies.Editor);

        // توليد صورة: من وصف مباشر أو من برومبت بمركز البرومبتات. بتنزل بالمكتبة غير معتمدة لحد ما إنسان يعتمدها.
        g.MapPost("/image", async (ImageRequest r, IAiRouter router, ZakiSocial.Infrastructure.Prompts.PromptService prompts,
            ZakiSocial.Infrastructure.Media.MediaService media, CancellationToken ct) =>
        {
            var prompt = !string.IsNullOrWhiteSpace(r.PromptName)
                ? await prompts.RenderByNameAsync(r.PromptName, r.Variables, ct)
                : r.Prompt;
            if (string.IsNullOrWhiteSpace(prompt)) return Results.BadRequest(new { error = "prompt_required" });
            var size = r.Size is "1024x1536" or "1536x1024" ? r.Size : "1024x1024";
            var image = await router.GenerateImageAsync(prompt, size, ct);
            return Results.Ok(await media.AddGeneratedAsync(image.Bytes, image.MimeType, prompt, ct));
        }).RequireAuthorization(AuthPolicies.Editor);

        g.MapGet("/usage", async (AiRouter router, CancellationToken ct) => Results.Ok(await router.UsageThisMonthAsync(ct)))
            .RequireAuthorization(AuthPolicies.Owner);
    }
}
