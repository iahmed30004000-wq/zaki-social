using ZakiSocial.Core.Health;
using ZakiSocial.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddZakiInfrastructure(builder.Configuration);

var app = builder.Build();

// يرجّع 200 دايماً ومعه حالة كل مكوّن. "degraded" يعني قاعدة البيانات أو ريديس مش واصلين.
static async Task<IResult> Health(IHealthService health, CancellationToken ct) =>
    Results.Ok(await health.CheckAsync(ct));

app.MapGet("/health", Health);
app.MapGet("/api/health", Health);

app.Run();
