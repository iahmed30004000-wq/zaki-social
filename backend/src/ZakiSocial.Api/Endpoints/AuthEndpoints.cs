using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Api.Endpoints;

public sealed record RegisterRequest(string Email, string Password, string? WorkspaceName, string? InviteCode);
public sealed record LoginRequest(string Email, string Password, Guid? WorkspaceId);
public sealed record SwitchRequest(Guid WorkspaceId);
public sealed record CreateWorkspaceRequest(string Name);
public sealed record ChangePasswordRequest(string Current, string Next);

public static class AuthEndpoints
{
    private const string RefreshCookie = "zs_refresh";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/register", async (RegisterRequest r, AuthService svc, HttpContext http, CancellationToken ct) =>
            Respond(http, await svc.RegisterAsync(r.Email, r.Password, r.WorkspaceName, r.InviteCode, ct)));

        auth.MapPost("/login", async (LoginRequest r, AuthService svc, HttpContext http, CancellationToken ct) =>
            Respond(http, await svc.LoginAsync(r.Email, r.Password, r.WorkspaceId, ct)));

        auth.MapPost("/refresh", async (AuthService svc, HttpContext http, CancellationToken ct) =>
        {
            var token = http.Request.Cookies[RefreshCookie];
            if (string.IsNullOrEmpty(token)) return Results.Json(new { error = "invalid_refresh" }, statusCode: 401);
            return Respond(http, await svc.RefreshAsync(token, ct));
        });

        auth.MapPost("/logout", async (AuthService svc, HttpContext http, CancellationToken ct) =>
        {
            await svc.LogoutAsync(http.Request.Cookies[RefreshCookie], ct);
            http.Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UtcNow));
            return Results.NoContent();
        });

        auth.MapPost("/change-password", async (ChangePasswordRequest r, AuthService svc, HttpContext http, ClaimsPrincipal user, ITenantContext tenant, CancellationToken ct) =>
        {
            var userId = UserId(user);
            var workspaceId = tenant.WorkspaceId; // قبل ما الخدمة تحوّل لوضع النظام
            return Respond(http, await svc.ChangePasswordAsync(userId, workspaceId, r.Current, r.Next, ct));
        }).RequireAuthorization(AuthPolicies.Member);

        auth.MapPost("/switch-workspace", async (SwitchRequest r, AuthService svc, HttpContext http, ClaimsPrincipal user, CancellationToken ct) =>
            Respond(http, await svc.SwitchWorkspaceAsync(UserId(user), r.WorkspaceId, http.Request.Cookies[RefreshCookie], ct)))
            .RequireAuthorization();

        app.MapPost("/api/workspaces", async (CreateWorkspaceRequest r, AuthService svc, HttpContext http, ClaimsPrincipal user, CancellationToken ct) =>
            Respond(http, await svc.CreateWorkspaceAsync(UserId(user), r.Name, http.Request.Cookies[RefreshCookie], ct)))
            .RequireAuthorization();
    }

    public static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);

    /// <summary>توكن الوصول بجسم الرد (بذاكرة المتصفح فقط)، وتوكن التجديد بكوكي httpOnly.</summary>
    private static IResult Respond(HttpContext http, AuthResult result)
    {
        http.Response.Cookies.Append(RefreshCookie, result.RefreshToken, CookieOptions(result.RefreshExpiresAt));
        return Results.Ok(new
        {
            accessToken = result.Access.Token,
            accessExpiresAt = result.Access.ExpiresAt,
            user = new { id = result.UserId, email = result.Email },
            workspace = result.CurrentWorkspace,
            workspaces = result.Workspaces,
            mustChangePassword = result.MustChangePassword,
        });
    }

    private static CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
    };
}
