using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZakiSocial.Core.Health;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Tenancy;

namespace ZakiSocial.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddZakiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // الاتصال يجي من متغيرات البيئة فقط (ConnectionStrings__Postgres). ممنوع أي سر بالكود.
        var connection = configuration.GetConnectionString("Postgres") ?? "Host=localhost;Timeout=3";

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            DbOptions.Configure(options, connection);
            options.AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>());
        });

        services.AddSingleton<RedisConnection>();
        services.AddScoped<IHealthService, HealthService>();
        services.AddScoped<ZakiSocial.Infrastructure.Posts.PostService>();
        services.AddScoped<ZakiSocial.Infrastructure.Ai.AiRouter>();
        services.AddScoped<ZakiSocial.Core.Ai.IAiRouter>(sp => sp.GetRequiredService<ZakiSocial.Infrastructure.Ai.AiRouter>());
        return services;
    }

    /// <summary>
    /// يطبّق الهجرات عند الإقلاع لما Database:AutoMigrate = true. بيعيد المحاولة لأن القاعدة ممكن
    /// تكون لسا بتقلع. الفشل النهائي بيوقف التطبيق (أفضل من تشغيله بقاعدة ناقصة).
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Database:AutoMigrate")) return;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().UseSystem();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                Console.Error.WriteLine($"migration attempt {attempt} failed: {ex.GetType().Name}");
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
