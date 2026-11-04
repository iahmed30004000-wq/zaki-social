using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;
using ZakiSocial.Integrations.Facebook;

namespace ZakiSocial.Integrations.Instagram;

/// <summary>
/// إنستغرام (حساب احترافي مربوط بصفحة فيسبوك) عبر Instagram Graph API: نفس تطبيق ميتا ونفس تسجيل الدخول.
/// النشر بيحتاج رابط عام للوسائط (المنصة بتسحبها): بنعطيها رابط موقّع مؤقت من السيرفر.
/// </summary>
public sealed class InstagramConnector(HttpClient http, IOptions<FacebookOptions> options) : IPlatformConnector
{
    private const string Scopes = "instagram_basic,instagram_content_publish,instagram_manage_comments,instagram_manage_insights,pages_show_list,pages_read_engagement,business_management";
    private FacebookOptions Opt => options.Value;
    private string Graph(string path) => $"{Opt.GraphBase.TrimEnd('/')}/{path}";

    public string Platform => Platforms.Instagram;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.AppId) && !string.IsNullOrWhiteSpace(Opt.AppSecret);

    public string BuildAuthUrl(string state, string redirectUri) =>
        $"{Opt.DialogBase}?client_id={PlatformHttp.Q(Opt.AppId)}&redirect_uri={PlatformHttp.Q(redirectUri)}&state={PlatformHttp.Q(state)}&scope={PlatformHttp.Q(Scopes)}&response_type=code";

    public async Task<IReadOnlyList<ConnectedAccount>> ConnectAsync(string code, string redirectUri, string state, CancellationToken ct)
    {
        var shortToken = await TokenAsync(new() { ["client_id"] = Opt.AppId, ["client_secret"] = Opt.AppSecret, ["redirect_uri"] = redirectUri, ["code"] = code }, ct);
        var longToken = await TokenAsync(new() { ["grant_type"] = "fb_exchange_token", ["client_id"] = Opt.AppId, ["client_secret"] = Opt.AppSecret, ["fb_exchange_token"] = shortToken }, ct);

        using var pages = await PlatformHttp.SendAsync(http, Platform,
            PlatformHttp.Get(Graph("me/accounts?fields=id,name,access_token,instagram_business_account{id,username}&limit=100"), longToken), ct);
        var accounts = new List<ConnectedAccount>();
        foreach (var page in pages.Root.GetProperty("data").EnumerateArray())
        {
            if (!page.TryGetProperty("instagram_business_account", out var ig)) continue; // صفحة بدون حساب إنستغرام مربوط
            var name = ig.TryGetProperty("username", out var u) ? "@" + u.GetString() : page.GetProperty("name").GetString()!;
            accounts.Add(new ConnectedAccount(ig.GetProperty("id").GetString()!, name, page.GetProperty("access_token").GetString()!, null, null));
        }
        return accounts;
    }

    public async Task<PublishResult> PublishAsync(AccountCredentials account, PostContent content, CancellationToken ct)
    {
        var media = content.Media;
        if (media.Count == 0) throw new PlatformException(Platform, 400, "instagram_requires_media");
        if (media.Any(m => string.IsNullOrEmpty(m.PublicUrl))) throw new PlatformException(Platform, 400, "instagram_requires_public_url");
        var ig = account.RemoteUserId;
        string creation;

        if (media.Count == 1)
        {
            var m = media[0];
            var form = m.Kind == "video"
                ? new Dictionary<string, string> { ["media_type"] = "REELS", ["video_url"] = m.PublicUrl!, ["caption"] = content.Text }
                : new Dictionary<string, string> { ["image_url"] = m.PublicUrl!, ["caption"] = content.Text };
            creation = await CreateContainerAsync(ig, form, account.AccessToken, ct);
            if (m.Kind == "video") await WaitFinishedAsync(creation, account.AccessToken, ct);
        }
        else
        {
            if (media.Any(m => m.Kind == "video")) throw new PlatformException(Platform, 400, "instagram_carousel_images_only");
            var children = new List<string>();
            foreach (var m in media)
                children.Add(await CreateContainerAsync(ig, new() { ["image_url"] = m.PublicUrl!, ["is_carousel_item"] = "true" }, account.AccessToken, ct));
            creation = await CreateContainerAsync(ig, new() { ["media_type"] = "CAROUSEL", ["children"] = string.Join(",", children), ["caption"] = content.Text }, account.AccessToken, ct);
        }

        using var published = await PlatformHttp.SendAsync(http, Platform,
            PlatformHttp.Form(HttpMethod.Post, Graph($"{ig}/media_publish"), new Dictionary<string, string> { ["creation_id"] = creation }, account.AccessToken), ct);
        return new PublishResult(published.Root.GetProperty("id").GetString()!);
    }

    public async Task<IReadOnlyDictionary<string, double>> FetchMetricsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Graph($"{remoteId}?fields=like_count,comments_count"), account.AccessToken), ct);
        double Num(string n) => doc.Root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        return new Dictionary<string, double> { ["reactions"] = Num("like_count"), ["comments"] = Num("comments_count") };
    }

    public async Task<IReadOnlyList<RemoteComment>> FetchCommentsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Graph($"{remoteId}/comments?fields=id,username,text,timestamp&limit=100"), account.AccessToken), ct);
        var list = new List<RemoteComment>();
        foreach (var c in doc.Root.GetProperty("data").EnumerateArray())
        {
            string Str(string a, string b) => c.TryGetProperty(a, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString()! : c.TryGetProperty(b, out var y) && y.ValueKind == JsonValueKind.String ? y.GetString()! : "";
            var author = c.TryGetProperty("username", out var u) ? u.GetString() ?? "" : c.TryGetProperty("from", out var f) && f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var when = DateTimeOffset.TryParse(Str("timestamp", "created_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.UtcNow;
            list.Add(new RemoteComment(c.GetProperty("id").GetString()!, author, Str("text", "message"), when));
        }
        return list;
    }

    public async Task ReplyAsync(AccountCredentials account, string commentId, string text, CancellationToken ct)
    {
        using var _ = await PlatformHttp.SendAsync(http, Platform,
            PlatformHttp.Form(HttpMethod.Post, Graph($"{commentId}/replies"), new Dictionary<string, string> { ["message"] = text }, account.AccessToken), ct);
    }

    private async Task<string> CreateContainerAsync(string ig, Dictionary<string, string> form, string token, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Graph($"{ig}/media"), form, token), ct);
        return doc.Root.GetProperty("id").GetString()!;
    }

    /// <summary>الريلز بتنتظر المعالجة عند إنستغرام قبل النشر (حتى ~4 دقايق).</summary>
    private async Task WaitFinishedAsync(string creation, string token, CancellationToken ct)
    {
        for (var i = 0; i < 48; i++)
        {
            using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Get(Graph($"{creation}?fields=status_code"), token), ct);
            var status = doc.Root.TryGetProperty("status_code", out var s) ? s.GetString() : null;
            if (status == "FINISHED") return;
            if (status == "ERROR" || status == "EXPIRED") throw new PlatformException(Platform, 422, "instagram_media_processing_failed");
            await Task.Delay(TimeSpan.FromSeconds(i == 0 ? 0.2 : 5), ct);
        }
        throw new PlatformException(Platform, 504, "instagram_media_processing_timeout");
    }

    private async Task<string> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var doc = await PlatformHttp.SendAsync(http, Platform, PlatformHttp.Form(HttpMethod.Post, Graph("oauth/access_token"), form), ct);
        return doc.Root.GetProperty("access_token").GetString()!;
    }
}
