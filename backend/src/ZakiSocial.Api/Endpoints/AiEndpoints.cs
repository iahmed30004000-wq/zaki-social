using System.Text;
using ZakiSocial.Core.Ai;
using ZakiSocial.Infrastructure.Ai;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Api.Endpoints;

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

        g.MapGet("/usage", async (AiRouter router, CancellationToken ct) => Results.Ok(await router.UsageThisMonthAsync(ct)))
            .RequireAuthorization(AuthPolicies.Owner);
    }
}
