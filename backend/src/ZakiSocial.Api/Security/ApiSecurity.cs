using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Api.Security;

public static class ApiSecurity
{
    /// <summary>
    /// السيرفر الخلفي ما بيتفتح للإنترنت مباشرة (بس كادي/تراييفك من الشبكة الداخلية)، فبنثق بترويسات X-Forwarded
    /// لنعرف عنوان الزائر الحقيقي (للحدود) ونعرف إذا الاتصال https (للكوكي).
    /// </summary>
    public static void UseForwarded(this WebApplication app)
    {
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        app.UseForwardedHeaders(options);
    }

    /// <summary>ترويسات أمان على كل رد: JSON ما بيتخزّن بالكاش ولا بيتعرض بإطار.</summary>
    public static void UseSecurityHeaders(this WebApplication app) =>
        app.Use(async (http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                var h = http.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "DENY";
                h["Referrer-Policy"] = "no-referrer";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                if (http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments("/api/public"))
                {
                    h["Cache-Control"] = "no-store";
                    h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                }
                return Task.CompletedTask;
            });
            await next();
        });

    /// <summary>سجل طلبات منظّم بدون سلسلة الاستعلام (فيها أكواد OAuth) وبدون ترويسات.</summary>
    public static void UseRequestLog(this WebApplication app) =>
        app.Use(async (http, next) =>
        {
            var watch = Stopwatch.StartNew();
            await next();
            var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("http");
            logger.LogInformation("{Method} {Path} -> {Status} in {Ms}ms user={User} workspace={Workspace}",
                http.Request.Method, http.Request.Path.Value, http.Response.StatusCode, watch.ElapsedMilliseconds,
                http.User.FindFirst("sub")?.Value ?? "-", http.User.FindFirst(JwtService.WorkspaceClaim)?.Value ?? "-");
        });

    /// <summary>
    /// حدّ الطلبات على ريديس: مسارات الدخول لكل عنوان، وباقي الواجهة لكل مستخدم (أو عنوان لو بدون تسجيل).
    /// بيجي بعد المصادقة عشان نعرف المستخدم.
    /// </summary>
    public static void UseRateLimits(this WebApplication app) =>
        app.Use(async (http, next) =>
        {
            var path = http.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/") || path == "/api/health" || path.StartsWith("/api/public/")) { await next(); return; }

            var config = http.RequestServices.GetRequiredService<IConfiguration>();
            var limiter = http.RequestServices.GetRequiredService<RateLimiter>();
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var isAuth = path.StartsWith("/api/auth/");
            var (key, limit) = isAuth
                ? ($"ip-auth:{ip}", config.GetValue("RateLimit:AuthPerIpPerMinute", 30))
                : http.User.FindFirst("sub")?.Value is { } sub
                    ? ($"user:{sub}", config.GetValue("RateLimit:ApiPerUserPerMinute", 600))
                    : ($"ip-api:{ip}", config.GetValue("RateLimit:ApiPerIpPerMinute", 120));

            var (allowed, retry) = await limiter.HitAsync(key, limit);
            if (!allowed)
            {
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = retry.ToString();
                await http.Response.WriteAsJsonAsync(new { error = "rate_limited" });
                return;
            }
            await next();
        });

    /// <summary>
    /// تدقيق: كل نقطة بالنظام لازم تطلب تسجيل دخول، ما عدا قائمة بيضا صغيرة. لو في نقطة مكشوفة بالغلط
    /// التطبيق بيطفي نفسه عند الإقلاع (يوقف النشر). بيشتغل لما Security:AuditEndpoints=true.
    /// </summary>
    public static void AuditEndpointsOnStart(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Security:AuditEndpoints")) return;
        string[] open =
        [
            "/health", "/api/health", "/api/auth/register", "/api/auth/login", "/api/auth/refresh", "/api/auth/logout",
            "/api/accounts/{platform}/callback", "/api/public/media/{ws}/{id}/{exp:long}/{sig}/{name}",
        ];
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var routes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
            var exposed = routes.Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null && !open.Contains(e.RoutePattern.RawText ?? "")).Select(e => e.RoutePattern.RawText).ToList();
            if (exposed.Count > 0)
            {
                Console.Error.WriteLine("ENDPOINT AUDIT FAILED: " + string.Join(", ", exposed));
                Environment.Exit(3);
            }
            Console.WriteLine($"ENDPOINT AUDIT OK: {routes.Count} endpoints, {open.Length} public by design");
        });
    }
}
