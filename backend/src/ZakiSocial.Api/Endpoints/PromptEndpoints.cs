using System.Security.Claims;
using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Infrastructure.Prompts;

namespace ZakiSocial.Api.Endpoints;

public sealed record PromptCreateRequest(string Name, string? Category, string Task, string Body, string? PreferredAi);
public sealed record PromptUpdateRequest(string? Category, string Task, string Body, string? PreferredAi);
public sealed record RollbackRequest(int Version);
public sealed record ActiveRequest(bool IsActive);
public sealed record VariablesRequest(Dictionary<string, string>? Variables);

public static class PromptEndpoints
{
    public static void MapPromptEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/prompts");

        g.MapGet("", async (PromptService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)))
            .RequireAuthorization(AuthPolicies.Member);

        g.MapGet("/{id:guid}", async (Guid id, PromptService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(id, ct)))
            .RequireAuthorization(AuthPolicies.Member);

        g.MapPost("", async (PromptCreateRequest r, PromptService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(await svc.CreateAsync(r.Name, r.Category, r.Task, r.Body, r.PreferredAi, AuthEndpoints.UserId(user), ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapPut("/{id:guid}", async (Guid id, PromptUpdateRequest r, PromptService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(id, r.Category, r.Task, r.Body, r.PreferredAi, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapPost("/{id:guid}/rollback", async (Guid id, RollbackRequest r, PromptService svc, CancellationToken ct) =>
            Results.Ok(await svc.RollbackAsync(id, r.Version, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        g.MapPost("/{id:guid}/active", async (Guid id, ActiveRequest r, PromptService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetActiveAsync(id, r.IsActive, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        // تجربة برومبت بقيم عينة
        g.MapPost("/{id:guid}/test", async (Guid id, VariablesRequest r, PromptService svc, CancellationToken ct) =>
            Results.Ok(await svc.TestAsync(id, r.Variables, ct)))
            .RequireAuthorization(AuthPolicies.Editor);

        // الاستعمال الفعلي بالاسم (المؤلّف: «توليد بالذكاء الاصطناعي»)
        g.MapPost("/by-name/{name}/generate", async (string name, VariablesRequest r, PromptService svc, CancellationToken ct) =>
            Results.Ok(await svc.RunByNameAsync(name, r.Variables, ct)))
            .RequireAuthorization(AuthPolicies.Editor);
    }
}
