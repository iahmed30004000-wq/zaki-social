using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Auth;
using ZakiSocial.Infrastructure.Security;

namespace ZakiSocial.Infrastructure.ProxyPool;

public sealed record ProxyDto(Guid Id, string Name, string Host, int Port, bool HasCredentials, string Status, DateTimeOffset? LastCheckedAt, int Accounts);

/// <summary>
/// مجمّع البروكسيات: كل حساب مربوط ببروكسي ثابت (sticky) والعامل بيمرّر نداءات المنصة منه. فحص صحة دوري.
/// بيانات دخول البروكسي مشفّرة. العناوين الداخلية/المحلية ممنوعة إلا لو الإعداد Proxies:AllowPrivateHosts.
/// </summary>
public sealed class ProxyService(AppDbContext db, ITenantContext tenant, TokenCryptor crypto, IConfiguration configuration)
{
    private Guid Workspace => tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public async Task<IReadOnlyList<ProxyDto>> ListAsync(CancellationToken ct)
    {
        var proxies = await db.Proxies.Where(p => p.WorkspaceId == Workspace).OrderBy(p => p.Name).ToListAsync(ct);
        var counts = await db.SocialAccounts.Where(a => a.WorkspaceId == Workspace && a.ProxyId != null)
            .GroupBy(a => a.ProxyId!.Value).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        return proxies.Select(p => new ProxyDto(p.Id, p.Name, p.Host, p.Port, p.EncCredentials is not null, p.Status, p.LastCheckedAt, counts.FirstOrDefault(c => c.Key == p.Id)?.N ?? 0)).ToList();
    }

    public async Task<ProxyDto> CreateAsync(string name, string host, int port, string? username, string? password, CancellationToken ct)
    {
        host = (host ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || host.Length == 0 || port is < 1 or > 65535) throw new AuthException(400, "proxy_invalid");
        await EnsureHostAllowedAsync(host, ct);
        var proxy = new Proxy
        {
            Id = Guid.NewGuid(), WorkspaceId = Workspace, Name = name.Trim(), Host = host, Port = port,
            EncCredentials = string.IsNullOrEmpty(username) ? null : crypto.Encrypt(JsonSerializer.Serialize(new { u = username, p = password ?? "" })),
            Status = "unknown", CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Proxies.Add(proxy);
        await db.SaveChangesAsync(ct);
        await CheckAsync(proxy, ct);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(p => p.Id == proxy.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var proxy = await db.Proxies.FirstOrDefaultAsync(p => p.Id == id && p.WorkspaceId == Workspace, ct) ?? throw new AuthException(404, "not_found");
        db.Proxies.Remove(proxy); // الحسابات المربوطة بيصير ProxyId عندها null (SetNull)
        await db.SaveChangesAsync(ct);
    }

    public async Task AssignAsync(Guid accountId, Guid? proxyId, CancellationToken ct)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.WorkspaceId == Workspace, ct) ?? throw new AuthException(404, "not_found");
        if (proxyId is not null && !await db.Proxies.AnyAsync(p => p.Id == proxyId && p.WorkspaceId == Workspace, ct)) throw new AuthException(404, "proxy_not_found");
        account.ProxyId = proxyId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>يربط كل حساب بدون بروكسي بأقل بروكسي سليم حمولة (الربط ثابت بعدها).</summary>
    public async Task<int> AutoAssignAsync(CancellationToken ct)
    {
        var healthy = await db.Proxies.Where(p => p.WorkspaceId == Workspace && p.Status == "healthy").ToListAsync(ct);
        if (healthy.Count == 0) throw new AuthException(409, "no_healthy_proxy");
        var load = await db.SocialAccounts.Where(a => a.WorkspaceId == Workspace && a.ProxyId != null)
            .GroupBy(a => a.ProxyId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var unassigned = await db.SocialAccounts.Where(a => a.WorkspaceId == Workspace && a.ProxyId == null).ToListAsync(ct);
        foreach (var account in unassigned)
        {
            var best = healthy.OrderBy(p => load.GetValueOrDefault(p.Id)).First();
            account.ProxyId = best.Id;
            load[best.Id] = load.GetValueOrDefault(best.Id) + 1;
        }
        await db.SaveChangesAsync(ct);
        return unassigned.Count;
    }

    /// <summary>عنوان البروكسي الكامل لحساب (بوضع العامل). null لو الحساب بدون بروكسي أو البروكسي مش سليم.</summary>
    public async Task<string?> ResolveUrlAsync(Guid? proxyId, CancellationToken ct)
    {
        if (proxyId is null) return null;
        var proxy = await db.Proxies.FirstOrDefaultAsync(p => p.Id == proxyId, ct);
        if (proxy is null) return null;
        if (proxy.Status == "unhealthy") throw new Core.Integrations.PlatformException("proxy", 503, "proxy_unhealthy"); // بنعيد المحاولة بدل ما نكشف عنوان السيرفر
        var userInfo = "";
        if (proxy.EncCredentials is not null)
        {
            using var doc = JsonDocument.Parse(crypto.Decrypt(proxy.EncCredentials));
            userInfo = $"{Uri.EscapeDataString(doc.RootElement.GetProperty("u").GetString()!)}:{Uri.EscapeDataString(doc.RootElement.GetProperty("p").GetString() ?? "")}@";
        }
        return $"http://{userInfo}{proxy.Host}:{proxy.Port}";
    }

    /// <summary>فحص صحة كل البروكسيات المعروفة (للعامل، بوضع النظام).</summary>
    public async Task CheckAllAsync(CancellationToken ct)
    {
        foreach (var proxy in await db.Proxies.ToListAsync(ct)) await CheckAsync(proxy, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task CheckAsyncCore(Proxy proxy, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(proxy.Host, proxy.Port, timeout.Token);
    }

    private static async Task CheckAsync(Proxy proxy, CancellationToken ct)
    {
        try
        {
            await CheckAsyncCore(proxy, ct);
            proxy.Status = "healthy";
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            proxy.Status = "unhealthy";
        }
        proxy.LastCheckedAt = DateTimeOffset.UtcNow;
    }

    private async Task EnsureHostAllowedAsync(string host, CancellationToken ct)
    {
        if (configuration.GetValue<bool>("Proxies:AllowPrivateHosts")) return;
        IPAddress[] addresses;
        try { addresses = IPAddress.TryParse(host, out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, ct); }
        catch (SocketException) { throw new AuthException(400, "proxy_host_unresolvable"); }
        if (addresses.Any(IsInternal)) throw new AuthException(400, "proxy_host_not_allowed");
    }

    private static bool IsInternal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
        var b = ip.GetAddressBytes();
        return ip.AddressFamily == AddressFamily.InterNetwork && (b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 0);
    }
}
