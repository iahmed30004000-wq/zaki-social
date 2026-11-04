using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;

namespace ZakiSocial.Integrations.LinkedIn;

public sealed class LinkedInOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AuthBase { get; set; } = "https://www.linkedin.com/oauth/v2/authorization";
    public string TokenUrl { get; set; } = "https://www.linkedin.com/oauth/v2/accessToken";
    public string ApiBase { get; set; } = "https://api.linkedin.com";
    public string ApiVersion { get; set; } = "202405";
}

/// <summary>
/// لينكدإن (Posts API): نشر نصي باسم العضو (w_member_social). النشر باسم صفحة شركة بدو موافقة منفصلة من لينكدإن
/// (Community Management / Marketing Developer Platform). الصور والفيديو لسا مش مدعومة هون.
/// </summary>
public sealed class LinkedInConnector(HttpClient http, IOptions<LinkedInOptions> options) : IPlatformConnector
{
    private const string Scopes = "openid profile w_member_social";
    private LinkedInOptions Opt => options.Value;
    private string Api(string path) => $"{Opt.ApiBase.TrimEnd('/')}/{path}";

    public string Platform => Platforms.LinkedIn;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.ClientId) && !string.IsNullOrWhiteSpace(Opt.ClientSecret);

    public string BuildAuthUrl(string state, string redirectUri) =>
        $"{Opt.AuthBase}?response_type=code&client_id={PlatformHttp.Q(Opt.ClientId)}&redirect_uri={PlatformHttp.Q(redirectUri)}&state={PlatformHttp.Q(state)}&scope={PlatformHttp.Q(Scopes)}";

    public async Task<IReadOnlyList<ConnectedAccount>> ConnectAsync(string code, string redirectUri, string state, CancellationToken ct)
    {
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Opt.TokenUrl, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = Opt.ClientId, ["client_secret"] = Opt.ClientSecret, ["redirect_uri"] = redirectUri,
        }), ct);
        var access = token.Root.GetProperty("access_token").GetString()!;
        var expiry = token.Root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : (DateTimeOffset?)null;
        var refresh = token.Root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;

        using var me = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Api("v2/userinfo"), access), ct);
        var name = me.Root.TryGetProperty("name", out var n) ? n.GetString() ?? "LinkedIn" : "LinkedIn";
        return [new ConnectedAccount(me.Root.GetProperty("sub").GetString()!, name, access, refresh, expiry)];
    }

    public async Task<RefreshedToken?> RefreshAsync(AccountCredentials account, CancellationToken ct)
    {
        if (account.RefreshToken is null) return null; // لينكدإن بيعطي refresh token للبرامج المعتمدة فقط
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Opt.TokenUrl, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = account.RefreshToken, ["client_id"] = Opt.ClientId, ["client_secret"] = Opt.ClientSecret,
        }), ct);
        var expiry = token.Root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : (DateTimeOffset?)null;
        return new RefreshedToken(token.Root.GetProperty("access_token").GetString()!, token.Root.TryGetProperty("refresh_token", out var r) ? r.GetString() : account.RefreshToken, expiry);
    }

    public async Task<PublishResult> PublishAsync(AccountCredentials account, PostContent content, CancellationToken ct)
    {
        if (content.Media.Count > 0) throw new PlatformException(Platform, 400, "linkedin_media_not_supported");
        var request = Versioned(PlatformHttp.Json(HttpMethod.Post, Api("rest/posts"), new
        {
            author = $"urn:li:person:{account.RemoteUserId}",
            commentary = content.Text,
            visibility = "PUBLIC",
            distribution = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<string>(), thirdPartyDistributionChannels = Array.Empty<string>() },
            lifecycleState = "PUBLISHED",
            isReshareDisabledByAuthor = false,
        }, account.AccessToken));
        using var response = await PlatformHttp.SendAsync(http, Platform, request, ct);
        var id = response.Headers.GetValueOrDefault("x-restli-id") ?? throw new PlatformException(Platform, 502, "linkedin_no_post_id");
        return new PublishResult(id);
    }

    public Task<IReadOnlyDictionary<string, double>> FetchMetricsAsync(AccountCredentials account, string remoteId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>()); // الإحصاءات بتحتاج صلاحيات منتج إضافي

    public async Task<IReadOnlyList<RemoteComment>> FetchCommentsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, Versioned(PlatformHttp.Get(Api($"rest/socialActions/{PlatformHttp.Q(remoteId)}/comments"), account.AccessToken)), ct);
        var list = new List<RemoteComment>();
        if (!doc.Root.TryGetProperty("elements", out var elements)) return list;
        foreach (var c in elements.EnumerateArray())
        {
            var text = c.TryGetProperty("message", out var m) && m.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            list.Add(new RemoteComment(c.TryGetProperty("$URN", out var u) ? u.GetString() ?? "" : c.GetProperty("id").ToString(), c.TryGetProperty("actor", out var a) ? a.GetString() ?? "" : "", text, DateTimeOffset.UtcNow));
        }
        return list;
    }

    public async Task ReplyAsync(AccountCredentials account, string commentId, string text, CancellationToken ct)
    {
        // commentId بصيغة "<urn المنشور>|<urn التعليق>" عند الجامع؛ هون بنرد على المنشور مباشرة.
        var target = commentId.Split('|')[0];
        using var _ = await PlatformHttp.SendAsync(http, Platform, Versioned(PlatformHttp.Json(HttpMethod.Post, Api($"rest/socialActions/{PlatformHttp.Q(target)}/comments"),
            new { actor = $"urn:li:person:{account.RemoteUserId}", message = new { text } }, account.AccessToken)), ct);
    }

    private HttpRequestMessage Versioned(HttpRequestMessage request)
    {
        request.Headers.Add("LinkedIn-Version", Opt.ApiVersion);
        request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        return request;
    }
}
