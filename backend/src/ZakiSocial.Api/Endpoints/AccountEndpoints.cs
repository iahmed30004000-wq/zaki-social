using System.Security.Claims;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/accounts");

        g.MapGet("", async (AccountService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)))
            .RequireAuthorization(AuthPolicies.Member);

        g.MapGet("/platforms", (AccountService svc) => Results.Ok(svc.Platforms().Select(p => new { platform = p.Platform, configured = p.Configured })))
            .RequireAuthorization(AuthPolicies.Member);

        g.MapDelete("/{id:guid}", async (Guid id, AccountService svc, CancellationToken ct) =>
        {
            await svc.DisconnectAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.Editor);

        // الخطوة 1 من الربط: رابط صفحة الموافقة عند المنصة.
        g.MapGet("/{platform}/connect-url", (string platform, AccountService svc, OAuthStateService states, ITenantContext tenant, ClaimsPrincipal user, IConfiguration cfg) =>
        {
            var state = states.Create(new OAuthState(AuthEndpoints.UserId(user), tenant.WorkspaceId!.Value, platform));
            return Results.Ok(new { url = svc.ConnectUrl(platform, state, RedirectUri(cfg, platform)) });
        }).RequireAuthorization(AuthPolicies.Editor);

        // الخطوة 2: المنصة بترجّع المتصفح هون بدون توكن دخول؛ الهوية من "state" الموقّع.
        g.MapGet("/{platform}/callback", async (string platform, string? code, string? state, string? error,
            OAuthStateService states, AuthService auth, AccountService svc, ITenantContext tenant, IConfiguration cfg, CancellationToken ct) =>
        {
            const string back = "/app/accounts";
            var parsed = states.Validate(state);
            if (parsed is null || parsed.Platform != platform) return Results.Redirect($"{back}?error=state_invalid");
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return Results.Redirect($"{back}?error=denied");

            // نتأكد إنه لسا عضو بصلاحية تحرير، ثم نشتغل بوضع مساحته (عزل الصفوف).
            var role = await auth.MemberRoleAsync(parsed.UserId, parsed.WorkspaceId, ct);
            if (role is not (Roles.Owner or Roles.Editor)) return Results.Redirect($"{back}?error=forbidden");
            tenant.UseWorkspace(parsed.WorkspaceId, role);

            try
            {
                var count = await svc.ConnectAsync(platform, code, RedirectUri(cfg, platform), state!, ct);
                return Results.Redirect($"{back}?connected={count}");
            }
            catch (PlatformException)
            {
                return Results.Redirect($"{back}?error=platform_error");
            }
        });
    }

    private static string RedirectUri(IConfiguration cfg, string platform) =>
        $"{(cfg["App:PublicUrl"] ?? "https://localhost").TrimEnd('/')}/api/accounts/{platform}/callback";
}
