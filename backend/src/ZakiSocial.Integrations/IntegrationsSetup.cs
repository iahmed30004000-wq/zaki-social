using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZakiSocial.Core.Integrations;
using ZakiSocial.Integrations.Facebook;

namespace ZakiSocial.Integrations;

public sealed class ConnectorRegistry(IEnumerable<IPlatformConnector> connectors) : IConnectorRegistry
{
    private readonly Dictionary<string, IPlatformConnector> _byPlatform = connectors.ToDictionary(c => c.Platform);

    public IPlatformConnector Get(string platform) =>
        _byPlatform.TryGetValue(platform, out var c)
            ? c
            : throw new PlatformException(platform, 404, "platform_not_supported");
}

public static class IntegrationsSetup
{
    /// <summary>تسجيل الموصّلات. إضافة منصة = سطر AddHttpClient جديد هون.</summary>
    public static IServiceCollection AddZakiIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FacebookOptions>(configuration.GetSection("Facebook"));
        services.AddHttpClient<FacebookConnector>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddTransient<IPlatformConnector>(sp => sp.GetRequiredService<FacebookConnector>());
        services.AddTransient<IConnectorRegistry, ConnectorRegistry>();
        return services;
    }
}
