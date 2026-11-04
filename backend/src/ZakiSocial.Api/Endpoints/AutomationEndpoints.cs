using ZakiSocial.Core.Jobs;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Infrastructure.Automation;
using ZakiSocial.Infrastructure.Posts;
using ZakiSocial.Infrastructure.ProxyPool;

namespace ZakiSocial.Api.Endpoints;

public sealed record ProxyRequest(string Name, string Host, int Port, string? Username, string? Password);
public sealed record AssignProxyRequest(Guid? ProxyId);
public sealed record ReplicateRequest(List<Guid>? AccountIds, int MinGapSeconds, int MaxGapSeconds);
public sealed record RecommendRequest(int Days = 30);

public static class AutomationEndpoints
{
    public static void MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var a = app.MapGroup("/api/automation");

        a.MapGet("/rules", async (AutomationService svc, CancellationToken ct) => Results.Ok(await svc.RulesAsync(ct))).RequireAuthorization(AuthPolicies.Member);
        a.MapPost("/rules", async (RuleInput r, AutomationService svc, CancellationToken ct) => Results.Ok(await svc.CreateRuleAsync(r, ct))).RequireAuthorization(AuthPolicies.Editor);
        a.MapPut("/rules/{id:guid}", async (Guid id, RuleInput r, AutomationService svc, CancellationToken ct) => Results.Ok(await svc.UpdateRuleAsync(id, r, ct))).RequireAuthorization(AuthPolicies.Editor);
        a.MapDelete("/rules/{id:guid}", async (Guid id, AutomationService svc, CancellationToken ct) =>
        {
            await svc.DeleteRuleAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Editor);

        a.MapGet("/replies", async (AutomationService svc, CancellationToken ct) => Results.Ok(await svc.RepliesAsync(ct))).RequireAuthorization(AuthPolicies.Member);
        a.MapGet("/status", async (AutomationService svc, CancellationToken ct) => Results.Ok(await svc.StatusAsync(ct))).RequireAuthorization(AuthPolicies.Member);

        // جمع فوري (المقاييس والتعليقات) لمساحتي، بالطابور
        a.MapPost("/collect-now", (IJobQueue queue, ITenantContext tenant) =>
        {
            queue.EnqueueCollect(tenant.WorkspaceId);
            return Results.Accepted();
        }).RequireAuthorization(AuthPolicies.Editor);

        var p = app.MapGroup("/api/proxies");
        p.MapGet("", async (ProxyService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct))).RequireAuthorization(AuthPolicies.Editor);
        p.MapPost("", async (ProxyRequest r, ProxyService svc, CancellationToken ct) => Results.Ok(await svc.CreateAsync(r.Name, r.Host, r.Port, r.Username, r.Password, ct))).RequireAuthorization(AuthPolicies.Owner);
        p.MapDelete("/{id:guid}", async (Guid id, ProxyService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Owner);
        p.MapPost("/auto-assign", async (ProxyService svc, CancellationToken ct) => Results.Ok(new { assigned = await svc.AutoAssignAsync(ct) })).RequireAuthorization(AuthPolicies.Owner);
        app.MapPut("/api/accounts/{id:guid}/proxy", async (Guid id, AssignProxyRequest r, ProxyService svc, CancellationToken ct) =>
        {
            await svc.AssignAsync(id, r.ProxyId, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Owner);

        // نسخ منشور لعدة حسابات بفواصل عشوائية بينها (مجدولة)
        app.MapPost("/api/posts/{id:guid}/replicate", async (Guid id, ReplicateRequest r, PostService svc, CancellationToken ct) =>
            Results.Ok(await svc.ReplicateAsync(id, r.AccountIds ?? [], r.MinGapSeconds, r.MaxGapSeconds, ct))).RequireAuthorization(AuthPolicies.Editor);

        var an = app.MapGroup("/api/analytics");
        an.MapGet("/summary", async (int? days, AnalyticsService svc, CancellationToken ct) => Results.Ok(await svc.SummaryAsync(days ?? 30, ct))).RequireAuthorization(AuthPolicies.Member);
        an.MapPost("/recommendations", async (RecommendRequest r, AnalyticsService svc, CancellationToken ct) => Results.Ok(await svc.RecommendAsync(r.Days, ct))).RequireAuthorization(AuthPolicies.Editor);
    }
}
