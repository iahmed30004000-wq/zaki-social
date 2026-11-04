using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;

namespace ZakiSocial.Infrastructure.Auth;

public static class AuthPolicies
{
    /// <summary>أي عضو بالمساحة الحالية.</summary>
    public const string Member = "Member";
    /// <summary>محرّر أو مالك.</summary>
    public const string Editor = "Editor";
    /// <summary>مالك فقط.</summary>
    public const string Owner = "Owner";
}

public static class AuthSetup
{
    public static IServiceCollection AddZakiAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new JwtOptions();
        configuration.GetSection("Jwt").Bind(options);
        options.Validate();
        _ = new ZakiSocial.Infrastructure.Security.TokenCryptor(configuration); // يفشل بالإقلاع لو مفتاح التشفير ناقص
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false; // نبقي أسماء الحقول كما هي: sub, email, workspace_id
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = options.SigningKey(),
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = ClaimTypes.Role,
                };
            });

        services.AddAuthorization(auth =>
        {
            auth.AddPolicy(AuthPolicies.Member, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasRole(c, Roles.Owner, Roles.Editor, Roles.Viewer)));
            auth.AddPolicy(AuthPolicies.Editor, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasRole(c, Roles.Owner, Roles.Editor)));
            auth.AddPolicy(AuthPolicies.Owner, p => p.RequireAuthenticatedUser().RequireAssertion(c => HasRole(c, Roles.Owner)));
        });

        services.AddSingleton<ZakiSocial.Infrastructure.Security.TokenCryptor>();
        services.AddSingleton<OAuthStateService>();
        services.AddScoped<AccountService>();
        services.AddScoped<JwtService>();
        services.AddScoped<AuthService>();
        services.AddScoped<WorkspaceService>();
        return services;
    }

    /// <summary>الدور بيجي من قاعدة البيانات (middleware التالي)، مش من التوكن، فإزالة عضو بتنفذ فوراً.</summary>
    private static bool HasRole(AuthorizationHandlerContext context, params string[] allowed)
    {
        var tenant = (context.Resource as HttpContext)?.RequestServices.GetService<ITenantContext>();
        return tenant?.Role is { } role && allowed.Contains(role);
    }

    /// <summary>
    /// بعد المصادقة: بيقرأ المساحة من التوكن، وبيتحقق من عضوية المستخدم فيها من قاعدة البيانات،
    /// وبيثبّت المساحة والدور للطلب. بعدها كل استعلام بيمر بعزل الصفوف.
    /// </summary>
    public static IApplicationBuilder UseZakiTenant(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            if (http.User.Identity?.IsAuthenticated == true)
            {
                var sub = http.User.FindFirstValue("sub");
                var ws = http.User.FindFirstValue(JwtService.WorkspaceClaim);
                if (Guid.TryParse(sub, out var userId) && Guid.TryParse(ws, out var workspaceId))
                {
                    var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
                    // نثبّت المساحة مؤقتاً بدور أدنى لنقدر نقرأ العضوية (سياسة العزل بتخلي الاستعلام يشوف مساحته بس).
                    tenant.UseWorkspace(workspaceId, Roles.Viewer);
                    var db = http.RequestServices.GetRequiredService<AppDbContext>();
                    var role = await db.Memberships
                        .Where(m => m.UserId == userId && m.WorkspaceId == workspaceId)
                        .Select(m => m.Role)
                        .FirstOrDefaultAsync(http.RequestAborted);
                    if (role is null)
                    {
                        tenant.UseWorkspace(workspaceId, "none");
                        http.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await http.Response.WriteAsJsonAsync(new { error = "not_a_member" });
                        return;
                    }
                    tenant.UseWorkspace(workspaceId, role);
                }
            }
            await next();
        });
}
