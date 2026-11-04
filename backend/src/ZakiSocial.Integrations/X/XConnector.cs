using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;

namespace ZakiSocial.Integrations.X;

public sealed class XOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AuthBase { get; set; } = "https://twitter.com/i/oauth2/authorize";
    public string ApiBase { get; set; } = "https://api.x.com";
}

/// <summary>X (API v2) بتسجيل دخول OAuth 2.0 + PKCE. نص وحتى 4 صور؛ الفيديو (رفع مجزّأ) لسا مش مدعوم.</summary>
public sealed class XConnector(HttpClient http, IOptions<XOptions> options) : IPlatformConnector
{
    private const int MaxChars = 280;
    private XOptions Opt => options.Value;
    private string Api(string path) => $"{Opt.ApiBase.TrimEnd('/')}/{path}";

    public string Platform => Platforms.X;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.ClientId) && !string.IsNullOrWhiteSpace(Opt.ClientSecret);

    public string BuildAuthUrl(string state, string redirectUri)
    {
        var (_, challenge) = PlatformHttp.Pkce(Opt.ClientSecret, state);
        return $"{Opt.AuthBase}?response_type=code&client_id={PlatformHttp.Q(Opt.ClientId)}&redirect_uri={PlatformHttp.Q(redirectUri)}" +
               $"&scope={PlatformHttp.Q("tweet.read tweet.write users.read offline.access media.write")}&state={PlatformHttp.Q(state)}" +
               $"&code_challenge={challenge}&code_challenge_method=S256";
    }

    public async Task<IReadOnlyList<ConnectedAccount>> ConnectAsync(string code, string redirectUri, string state, CancellationToken ct)
    {
        var (verifier, _) = PlatformHttp.Pkce(Opt.ClientSecret, state);
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Api("2/oauth2/token"), new Dictionary<string, string>
        {
            ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = redirectUri, ["code_verifier"] = verifier, ["client_id"] = Opt.ClientId,
        }, basicUser: Opt.ClientId, basicPass: Opt.ClientSecret), ct);
        var access = token.Root.GetProperty("access_token").GetString()!;

        using var me = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Api("2/users/me"), access), ct);
        var data = me.Root.GetProperty("data");
        return [new ConnectedAccount(data.GetProperty("id").GetString()!, "@" + data.GetProperty("username").GetString(), access, Str(token.Root, "refresh_token"), Expiry(token.Root))];
    }

    public async Task<RefreshedToken?> RefreshAsync(AccountCredentials account, CancellationToken ct)
    {
        if (account.RefreshToken is null) return null;
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Api("2/oauth2/token"), new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = account.RefreshToken, ["client_id"] = Opt.ClientId,
        }, basicUser: Opt.ClientId, basicPass: Opt.ClientSecret), ct);
        return new RefreshedToken(token.Root.GetProperty("access_token").GetString()!, Str(token.Root, "refresh_token") ?? account.RefreshToken, Expiry(token.Root));
    }

    public async Task<PublishResult> PublishAsync(AccountCredentials account, PostContent content, CancellationToken ct)
    {
        if (content.Text.Length > MaxChars) throw new PlatformException(Platform, 400, "x_text_too_long");
        if (content.Media.Any(m => m.Kind == "video")) throw new PlatformException(Platform, 400, "x_video_not_supported");
        if (content.Media.Count > 4) throw new PlatformException(Platform, 400, "x_max_4_images");

        var mediaIds = new List<string>();
        foreach (var m in content.Media)
        {
            using var form = new MultipartFormDataContent { { new StringContent("tweet_image"), "media_category" } };
            var bytes = new ByteArrayContent(m.Data);
            bytes.Headers.ContentType = MediaTypeHeaderValue.Parse(m.ContentType);
            form.Add(bytes, "media", m.FileName);
            var request = new HttpRequestMessage(HttpMethod.Post, Api("2/media/upload")) { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            using var uploaded = await PlatformHttp.SendAsync(http, Platform, request, ct);
            mediaIds.Add(uploaded.Root.GetProperty("data").GetProperty("id").GetString()!);
        }

        object body = mediaIds.Count == 0 ? (object)new { text = content.Text } : new { text = content.Text, media = new { media_ids = mediaIds } };
        using var tweet = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Json(HttpMethod.Post, Api("2/tweets"), body, account.AccessToken), ct);
        return new PublishResult(tweet.Root.GetProperty("data").GetProperty("id").GetString()!);
    }

    public async Task<IReadOnlyDictionary<string, double>> FetchMetricsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Api($"2/tweets/{remoteId}?tweet.fields=public_metrics"), account.AccessToken), ct);
        var m = doc.Root.GetProperty("data").GetProperty("public_metrics");
        double Num(string n) => m.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        return new Dictionary<string, double> { ["reactions"] = Num("like_count"), ["comments"] = Num("reply_count"), ["shares"] = Num("retweet_count") };
    }

    public async Task<IReadOnlyList<RemoteComment>> FetchCommentsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform,
            PlatformHttp.Get(Api($"2/tweets/search/recent?query={PlatformHttp.Q("conversation_id:" + remoteId)}&tweet.fields=created_at,author_id&max_results=50"), account.AccessToken), ct);
        var list = new List<RemoteComment>();
        if (!doc.Root.TryGetProperty("data", out var data)) return list;
        foreach (var t in data.EnumerateArray())
        {
            var when = t.TryGetProperty("created_at", out var c) && DateTimeOffset.TryParse(c.GetString(), out var parsed) ? parsed : DateTimeOffset.UtcNow;
            list.Add(new RemoteComment(t.GetProperty("id").GetString()!, t.TryGetProperty("author_id", out var a) ? a.GetString() ?? "" : "", t.GetProperty("text").GetString() ?? "", when));
        }
        return list;
    }

    public async Task ReplyAsync(AccountCredentials account, string commentId, string text, CancellationToken ct)
    {
        using var _ = await PlatformHttp.SendAsync(http, Platform,
            PlatformHttp.Json(HttpMethod.Post, Api("2/tweets"), new { text, reply = new { in_reply_to_tweet_id = commentId } }, account.AccessToken), ct);
    }

    private static string? Str(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Expiry(JsonElement root) =>
        root.TryGetProperty("expires_in", out var v) && v.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;
}
