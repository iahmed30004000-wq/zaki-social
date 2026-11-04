using System.Security.Claims;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Api.Endpoints;

public sealed record InviteRequest(string Email, string Role);
public sealed record RoleRequest(string Role);

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", (ClaimsPrincipal user, ITenantContext tenant) => Results.Ok(new
        {
            userId = AuthEndpoints.UserId(user),
            email = user.FindFirstValue("email"),
            workspaceId = tenant.WorkspaceId,
            role = tenant.Role,
        })).RequireAuthorization(AuthPolicies.Member);

        var ws = app.MapGroup("/api/workspace");

        ws.MapGet("/members", async (WorkspaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.MembersAsync(ct))).RequireAuthorization(AuthPolicies.Member);

        ws.MapGet("/invitations", async (WorkspaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.PendingInvitationsAsync(ct))).RequireAuthorization(AuthPolicies.Owner);

        ws.MapPost("/invitations", async (InviteRequest r, WorkspaceService svc, ClaimsPrincipal user, CancellationToken ct) =>
            Results.Ok(await svc.InviteAsync(r.Email, r.Role, AuthEndpoints.UserId(user), ct))).RequireAuthorization(AuthPolicies.Owner);

        ws.MapDelete("/invitations/{id:guid}", async (Guid id, WorkspaceService svc, CancellationToken ct) =>
        {
            await svc.RevokeInvitationAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Owner);

        ws.MapPut("/members/{userId:guid}/role", async (Guid userId, RoleRequest r, WorkspaceService svc, CancellationToken ct) =>
        {
            await svc.ChangeRoleAsync(userId, r.Role, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Owner);

        ws.MapDelete("/members/{userId:guid}", async (Guid userId, WorkspaceService svc, CancellationToken ct) =>
        {
            await svc.RemoveMemberAsync(userId, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Owner);
    }
}
