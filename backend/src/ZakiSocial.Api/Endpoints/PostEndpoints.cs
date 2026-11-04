using System.Security.Claims;
using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Infrastructure.Posts;

namespace ZakiSocial.Api.Endpoints;

public sealed record ScheduleRequest(DateTimeOffset When, string? Repeat);
public sealed record PostRequest(string Body, List<Guid>? AccountIds, bool AiGenerated = false);

public static class PostEndpoints
{
    public static void MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/posts");

        g.MapGet("", async (string? status, DateTimeOffset? from, DateTimeOffset? to, PostService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(status, from, to, ct)))
            .RequireAuthorization(AuthPolicies.Member);

        g.MapGet("/{id:guid}", async (Guid id, PostService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(id, ct)))
            .RequireAuthorization(AuthPolicies.Member);

        g.MapPost("", async (PostRequest r, PostService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(await svc.CreateAsync(r.Body, r.AccountIds ?? [], AuthEndpoints.UserId(user), r.AiGenerated, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapPut("/{id:guid}", async (Guid id, PostRequest r, PostService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateDraftAsync(id, r.Body, r.AccountIds ?? [], ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapDelete("/{id:guid}", async (Guid id, PostService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Editor);

        g.MapPost("/{id:guid}/approve", async (Guid id, PostService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(await svc.ApproveAsync(id, AuthEndpoints.UserId(user), ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapPut("/{id:guid}/schedule", async (Guid id, ScheduleRequest r, PostService svc, CancellationToken ct) =>
            Results.Ok(await svc.ScheduleAsync(id, r.When, r.Repeat, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapDelete("/{id:guid}/schedule", async (Guid id, PostService svc, CancellationToken ct) =>
            Results.Ok(await svc.UnscheduleAsync(id, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        // ما بينشر مباشرة: بيحط مهام بالطابور والعامل بينفّذ.
        g.MapPost("/{id:guid}/publish", async (Guid id, PostService svc, CancellationToken ct) =>
            Results.Ok(await svc.PublishAsync(id, ct)))
            .RequireAuthorization(AuthPolicies.Editor);
    }
}
