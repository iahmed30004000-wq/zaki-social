using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ZakiSocial.Core.Health;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;

namespace ZakiSocial.Infrastructure;

public sealed class HealthService(AppDbContext db, RedisConnection redis, ITenantContext tenant, ILogger<HealthService> logger) : IHealthService
{
    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        tenant.UseSystem(); // الفحص ما إله علاقة بمساحة، وقاعدة جديدة ما فيها دور zaki_app بعد
        var database = await ProbeAsync("database", async () => await db.Database.CanConnectAsync(ct));
        var cache = await ProbeAsync("redis", async () =>
        {
            await redis.PingAsync();
            return true;
        });

        var status = database == "ok" && cache == "ok" ? "ok" : "degraded";
        return new HealthReport(status, database, cache, DateTimeOffset.UtcNow);
    }

    private async Task<string> ProbeAsync(string name, Func<Task<bool>> probe)
    {
        try
        {
            return await probe() ? "ok" : "fail";
        }
        catch (Exception ex)
        {
            // نسجّل نوع الخطأ بس، بدون نص الاتصال لأنه ممكن يحتوي كلمة سر.
            logger.LogWarning("health probe {Probe} failed: {Error}", name, ex.GetType().Name);
            return "fail";
        }
    }
}
