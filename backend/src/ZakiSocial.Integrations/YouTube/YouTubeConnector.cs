using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;

namespace ZakiSocial.Integrations.YouTube;

public sealed class YouTubeOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AuthBase { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string TokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string ApiBase { get; set; } = "https://www.googleapis.com";
    /// <summary>public / unlisted / private. المشاريع غير المدقَّقة عند يوتيوب بتنقفل على private.</summary>
    public string Privacy { get; set; } = "public";
}

/// <summary>يوتيوب عبر Data API v3: رفع فيديو بالطريقة القابلة للاستئناف (resumable). التوكن بيتجدد بـ refresh token.</summary>
public sealed class YouTubeConnector(HttpClient http, IOptions<YouTubeOptions> options) : IPlatformConnector
{
    private const string Scopes = "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly https://www.googleapis.com/auth/youtube.force-ssl";
    private YouTubeOptions Opt => options.Value;
    private string Api(string path) => $"{Opt.ApiBase.TrimEnd('/')}/{path}";

    public string Platform => Platforms.YouTube;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.ClientId) && !string.IsNullOrWhiteSpace(Opt.ClientSecret);

    public string BuildAuthUrl(string state, string redirectUri) =>
        $"{Opt.AuthBase}?client_id={PlatformHttp.Q(Opt.ClientId)}&redirect_uri={PlatformHttp.Q(redirectUri)}&response_type=code" +
        $"&scope={PlatformHttp.Q(Scopes)}&access_type=offline&prompt=consent&state={PlatformHttp.Q(state)}";

    public async Task<IReadOnlyList<ConnectedAccount>> ConnectAsync(string code, string redirectUri, string state, CancellationToken ct)
    {
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Opt.TokenUrl, new Dictionary<string, string>
        {
            ["code"] = code, ["client_id"] = Opt.ClientId, ["client_secret"] = Opt.ClientSecret, ["redirect_uri"] = redirectUri, ["grant_type"] = "authorization_code",
        }), ct);
        var access = token.Root.GetProperty("access_token").GetString()!;

        using var channels = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Api("youtube/v3/channels?part=snippet&mine=true"), access), ct);
        var accounts = new List<ConnectedAccount>();
        foreach (var c in channels.Root.GetProperty("items").EnumerateArray())
            accounts.Add(new ConnectedAccount(c.GetProperty("id").GetString()!, c.GetProperty("snippet").GetProperty("title").GetString()!, access, Str(token.Root, "refresh_token"), Expiry(token.Root)));
        return accounts;
    }

    public async Task<RefreshedToken?> RefreshAsync(AccountCredentials account, CancellationToken ct)
    {
        if (account.RefreshToken is null) return null;
        using var token = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Opt.TokenUrl, new Dictionary<string, string>
        {
            ["client_id"] = Opt.ClientId, ["client_secret"] = Opt.ClientSecret, ["refresh_token"] = account.RefreshToken, ["grant_type"] = "refresh_token",
        }), ct);
        return new RefreshedToken(token.Root.GetProperty("access_token").GetString()!, Str(token.Root, "refresh_token") ?? account.RefreshToken, Expiry(token.Root));
    }

    public async Task<PublishResult> PublishAsync(AccountCredentials account, PostContent content, CancellationToken ct)
    {
        var video = content.Media.FirstOrDefault(m => m.Kind == "video") ?? throw new PlatformException(Platform, 400, "youtube_requires_video");
        var lines = content.Text.Split('\n', 2);
        var title = lines[0].Trim();
        if (title.Length == 0) title = "Video";
        if (title.Length > 100) title = title[..100];

        // 1) فتح جلسة الرفع
        var init = new HttpRequestMessage(HttpMethod.Post, Api("upload/youtube/v3/videos?uploadType=resumable&part=snippet,status"))
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                snippet = new { title, description = content.Text, categoryId = "22" },
                status = new { privacyStatus = Opt.Privacy, selfDeclaredMadeForKids = false },
            }), Encoding.UTF8, "application/json"),
        };
        init.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        init.Headers.Add("X-Upload-Content-Type", "video/mp4");
        init.Headers.Add("X-Upload-Content-Length", video.Data.LongLength.ToString());
        using var session = await PlatformHttp.SendAsync(http, Platform, init, ct);
        var uploadUrl = session.Headers.GetValueOrDefault("location") ?? throw new PlatformException(Platform, 502, "youtube_no_upload_url");

        // 2) رفع الملف
        using var upload = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(video.Data) };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        using var done = await PlatformHttp.SendAsync(http, Platform, upload, ct);
        return new PublishResult(done.Root.GetProperty("id").GetString()!);
    }

    public async Task<IReadOnlyDictionary<string, double>> FetchMetricsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Api($"youtube/v3/videos?part=statistics&id={remoteId}"), account.AccessToken), ct);
        var stats = doc.Root.GetProperty("items")[0].GetProperty("statistics");
        double Num(string n) => stats.TryGetProperty(n, out var v) && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        return new Dictionary<string, double> { ["views"] = Num("viewCount"), ["reactions"] = Num("likeCount"), ["comments"] = Num("commentCount") };
    }

    public async Task<IReadOnlyList<RemoteComment>> FetchCommentsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Api($"youtube/v3/commentThreads?part=snippet&videoId={remoteId}&maxResults=100"), account.AccessToken), ct);
        var list = new List<RemoteComment>();
        if (!doc.Root.TryGetProperty("items", out var items)) return list;
        foreach (var item in items.EnumerateArray())
        {
            var top = item.GetProperty("snippet").GetProperty("topLevelComment");
            var s = top.GetProperty("snippet");
            var when = s.TryGetProperty("publishedAt", out var p) && DateTimeOffset.TryParse(p.GetString(), out var t) ? t : DateTimeOffset.UtcNow;
            list.Add(new RemoteComment(top.GetProperty("id").GetString()!, s.TryGetProperty("authorDisplayName", out var a) ? a.GetString() ?? "" : "", s.TryGetProperty("textOriginal", out var x) ? x.GetString() ?? "" : "", when));
        }
        return list;
    }

    public async Task ReplyAsync(AccountCredentials account, string commentId, string text, CancellationToken ct)
    {
        using var _ = await PlatformHttp.SendAsync(http, Platform,
            PlatformHttp.Json(HttpMethod.Post, Api("youtube/v3/comments?part=snippet"), new { snippet = new { parentId = commentId, textOriginal = text } }, account.AccessToken), ct);
    }

    private static string? Str(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Expiry(JsonElement root) =>
        root.TryGetProperty("expires_in", out var v) && v.TryGetInt32(out var seconds) ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;
}
