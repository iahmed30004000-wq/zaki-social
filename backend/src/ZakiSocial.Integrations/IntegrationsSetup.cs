using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZakiSocial.Core.Ai;
using ZakiSocial.Core.Integrations;
using ZakiSocial.Integrations.Ai;
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
    /// <summary>تسجيل الموصّلات ومزوّدي الذكاء الاصطناعي. إضافة منصة = سطر AddHttpClient جديد هون.</summary>
    public static IServiceCollection AddZakiIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FacebookOptions>(configuration.GetSection("Facebook"));
        services.AddHttpClient<FacebookConnector>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddTransient<IPlatformConnector>(sp => sp.GetRequiredService<FacebookConnector>());
        services.AddTransient<IConnectorRegistry, ConnectorRegistry>();

        // الذكاء الاصطناعي: مفاتيح ونماذج من البيئة (Ai__Claude__ApiKey ...). إعادة محاولة وحد زمني بـ Polly.
        services.Configure<ProviderOptions>(AiProviders.Claude, configuration.GetSection("Ai:Claude"));
        services.Configure<ProviderOptions>(AiProviders.OpenAi, configuration.GetSection("Ai:OpenAi"));
        services.Configure<ProviderOptions>(AiProviders.Local, configuration.GetSection("Ai:Local"));

        AddAiClient<ClaudeProvider>(services);
        AddAiClient<OpenAiProvider>(services);
        AddAiClient<LocalLlmProvider>(services);
        return services;
    }

    private static void AddAiClient<T>(IServiceCollection services) where T : class, IAiProvider
    {
        services.AddHttpClient<T>(c => c.Timeout = TimeSpan.FromMinutes(5))
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 1;
                o.Retry.Delay = TimeSpan.FromMilliseconds(300);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(90);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
            });
        services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<T>());
    }
}
