using System.Collections.Concurrent;
using System.Net;
using ZakiSocial.Core.Integrations;

namespace ZakiSocial.Integrations;

/// <summary>لو في بروكسي محدد للحساب (ProxyContext) بنمرّر الطلب من خلاله؛ وإلا بيمشي عادي.</summary>
public sealed class ProxyRoutingHandler : DelegatingHandler
{
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var proxyUrl = ProxyContext.Current;
        if (proxyUrl is null) return base.SendAsync(request, cancellationToken);
        return Clients.GetOrAdd(proxyUrl, Create).SendAsync(request, cancellationToken);
    }

    private static HttpClient Create(string proxyUrl)
    {
        var uri = new Uri(proxyUrl);
        var proxy = new WebProxy(new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}")) { BypassProxyOnLocal = false };
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        return new HttpClient(new SocketsHttpHandler { Proxy = proxy, UseProxy = true, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = Timeout.InfiniteTimeSpan, // المهلة بتجي من العميل الأصلي (CancellationToken)
        };
    }
}
