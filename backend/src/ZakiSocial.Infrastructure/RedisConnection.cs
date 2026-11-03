using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace ZakiSocial.Infrastructure;

/// <summary>اتصال ريديس واحد مشترك، بيتفتح أول ما نحتاجه.</summary>
public sealed class RedisConnection(IConfiguration configuration)
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _mux = new(() =>
    {
        var options = ConfigurationOptions.Parse(configuration["Redis:ConnectionString"] ?? "localhost:6379");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 2000;
        options.SyncTimeout = 2000;
        options.AsyncTimeout = 2000;
        return ConnectionMultiplexer.ConnectAsync(options);
    });

    public async Task PingAsync()
    {
        var mux = await _mux.Value;
        await mux.GetDatabase().PingAsync();
    }
}
