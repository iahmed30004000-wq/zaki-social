using ZakiSocial.Api.Endpoints;
using ZakiSocial.Core.Health;
using ZakiSocial.Infrastructure;
using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Integrations;
using ZakiSocial.Workers;

var builder = WebApplication.CreateBuilder(args);
// رفع الوسائط: حد 320 ميغا بالطلب (الفيديو 300 ميغا كحد أقصى للملف)
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 320L * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 320L * 1024 * 1024);
builder.Services.AddZakiInfrastructure(builder.Configuration);
builder.Services.AddZakiAuth(builder.Configuration);
builder.Services.AddZakiIntegrations(builder.Configuration);
builder.Services.AddZakiWorkers(builder.Configuration);

var app = builder.Build();

await app.Services.MigrateDatabaseAsync(app.Configuration);

// أخطاء متوقعة (كلمة سر خطأ، ممنوع، غير موجود) بترجع كود وحالة بدل 500.
app.Use(async (http, next) =>
{
    try
    {
        await next();
    }
    catch (AuthException ex) when (!http.Response.HasStarted)
    {
        http.Response.StatusCode = ex.Status;
        await http.Response.WriteAsJsonAsync(new { error = ex.Code });
    }
});

app.UseAuthentication();
app.UseZakiTenant();
app.UseAuthorization();

// يرجّع 200 دايماً ومعه حالة كل مكوّن. "degraded" يعني قاعدة البيانات أو ريديس مش واصلين.
static async Task<IResult> Health(IHealthService health, CancellationToken ct) =>
    Results.Ok(await health.CheckAsync(ct));

app.MapGet("/health", Health);
app.MapGet("/api/health", Health);
app.MapAuthEndpoints();
app.MapWorkspaceEndpoints();
app.MapAccountEndpoints();
app.MapPostEndpoints();
app.MapAiEndpoints();
app.MapPromptEndpoints();
app.MapMediaEndpoints();

app.Run();
