using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;

namespace ZakiSocial.Integrations.TikTok;

public sealed class TikTokOptions
{
    public string ClientKey { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AuthBase { get; set; } = "https://www.tiktok.com/v2/auth/authorize/";
    public string ApiBase { get; set; } = "https://open.tiktokapis.com";
    /// <summary>التطبيقات غير المدقَّقة عند تيك توك بتنشر خاص فقط (SELF_ONLY) لحد ما يعتمدوا التطبيق.</summary>
    public string PrivacyLevel { get; set; } = "SELF_ONLY";
}

/// <summary>تيك توك عبر Content Posting API الرسمي (رفع فيديو مباشر). التوكن بينتهي يومياً: التجديد بالموصّل.</summary>
public sealed class TikTokConnector(HttpClient http, IOptions<TikTokOptions> options) : IPlatformConnector
{
    private TikTokOptions Opt => options.Value;
    private string Api(string path) => $"{Opt.ApiBase.TrimEnd('/')}/{path}";

    public string Platform => Platforms.TikTok;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.ClientKey) && !string.IsNullOrWhiteSpace(Opt.ClientSecret);

    public string BuildAuthUrl(string state, string redirectUri) =>
        $"{Opt.AuthBase}?client_key={PlatformHttp.Q(Opt.ClientKey)}&scope={PlatformHttp.Q("user.info.basic,video.publish,video.upload")}" +
        $"&response_type=code&redirect_uri={PlatformHttp.Q(redirectUri)}&state={PlatformHttp.Q(state)}";

    public async Task<IReadOnlyList<ConnectedAccount>> ConnectAsync(string code, string redirectUri, string state, CancellationToken ct)
    {
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Api("v2/oauth/token/"), new Dictionary<string, string>
        {
            ["client_key"] = Opt.ClientKey, ["client_secret"] = Opt.ClientSecret, ["code"] = code,
            ["grant_type"] = "authorization_code", ["redirect_uri"] = redirectUri,
        }), ct);
        var access = token.Root.GetProperty("access_token").GetString()!;
        var openId = token.Root.GetProperty("open_id").GetString()!;

        var name = openId;
        using var info = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Api("v2/user/info/?fields=open_id,display_name"), access), ct);
        if (info.Root.TryGetProperty("data", out var d) && d.TryGetProperty("user", out var u) && u.TryGetProperty("display_name", out var n)) name = n.GetString() ?? openId;

        return [new ConnectedAccount(openId, name, access, Str(token.Root, "refresh_token"), Expiry(token.Root))];
    }

    public async Task<RefreshedToken?> RefreshAsync(AccountCredentials account, CancellationToken ct)
    {
        if (account.RefreshToken is null) return null;
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Api("v2/oauth/token/"), new Dictionary<string, string>
        {
            ["client_key"] = Opt.ClientKey, ["client_secret"] = Opt.ClientSecret, ["grant_type"] = "refresh_token", ["refresh_token"] = account.RefreshToken,
        }), ct);
        return new RefreshedToken(token.Root.GetProperty("access_token").GetString()!, Str(token.Root, "refresh_token") ?? account.RefreshToken, Expiry(token.Root));
    }

    public async Task<PublishResult> PublishAsync(AccountCredentials account, PostContent content, CancellationToken ct)
    {
        var video = content.Media.FirstOrDefault(m => m.Kind == "video") ?? throw new PlatformException(Platform, 400, "tiktok_requires_video");
        var size = video.Data.LongLength;

        using var init = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Json(HttpMethod.Post, Api("v2/post/publish/video/init/"), new
        {
            post_info = new { title = content.Text.Length <= 2200 ? content.Text : content.Text[..2200], privacy_level = Opt.PrivacyLevel, disable_comment = false },
            source_info = new { source = "FILE_UPLOAD", video_size = size, chunk_size = size, total_chunk_count = 1 },
        }, account.AccessToken), ct);
        CheckError(init.Root);
        var data = init.Root.GetProperty("data");
        var publishId = data.GetProperty("publish_id").GetString()!;
        var uploadUrl = data.GetProperty("upload_url").GetString()!;

        using (var upload = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(video.Data) })
        {
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            upload.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, size - 1, size);
            using var _ = await PlatformHttp.SendAsync(http, Platform, upload, ct);
        }

        for (var i = 0; i < 40; i++)
        {
            using var status = await PlatformHttp.SendAsync(http, Platform,
                PlatformHttp.Json(HttpMethod.Post, Api("v2/post/publish/status/fetch/"), new { publish_id = publishId }, account.AccessToken), ct);
            CheckError(status.Root);
            var s = status.Root.GetProperty("data").GetProperty("status").GetString();
            if (s == "PUBLISH_COMPLETE")
            {
                var posted = status.Root.GetProperty("data").TryGetProperty("publicaly_available_post_id", out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0
                    ? ids[0].ToString() : publishId;
                return new PublishResult(posted);
            }
            if (s == "FAILED") throw new PlatformException(Platform, 422, "tiktok_publish_failed");
            await Task.Delay(TimeSpan.FromSeconds(i == 0 ? 0.2 : 3), ct);
        }
        throw new PlatformException(Platform, 504, "tiktok_publish_timeout");
    }

    // واجهة النشر الرسمية ما بتعطي تعليقات ولا مقاييس عامة: لازم موافقات إضافية.
    public Task<IReadOnlyDictionary<string, double>> FetchMetricsAsync(AccountCredentials account, string remoteId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());

    public Task<IReadOnlyList<RemoteComment>> FetchCommentsAsync(AccountCredentials account, string remoteId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RemoteComment>>([]);

    public Task ReplyAsync(AccountCredentials account, string commentId, string text, CancellationToken ct) =>
        throw new PlatformException(Platform, 501, "tiktok_reply_not_supported");

    /// <summary>تيك توك بيرجّع 200 مع error.code غير "ok" عند الفشل.</summary>
    private void CheckError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) && c.GetString() is { } code && code != "ok")
            throw new PlatformException(Platform, 400, $"tiktok_{code}");
    }

    private static string? Str(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Expiry(JsonElement root) =>
        root.TryGetProperty("expires_in", out var v) && v.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;
}
