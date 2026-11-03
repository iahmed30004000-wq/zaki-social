using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZakiSocial.Core.Health;
using ZakiSocial.Data;

namespace ZakiSocial.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddZakiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // الاتصال يجي من متغيرات البيئة فقط (ConnectionStrings__Postgres). ممنوع أي سر بالكود.
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres") ?? "Host=localhost;Timeout=3"));
        services.AddSingleton<RedisConnection>();
        services.AddScoped<IHealthService, HealthService>();
        return services;
    }
}
