using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;

namespace ZakiSocial.Integrations.Facebook;

public sealed class FacebookOptions
{
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string GraphBase { get; set; } = "https://graph.facebook.com/v21.0";
    public string DialogBase { get; set; } = "https://www.facebook.com/v21.0/dialog/oauth";
    public string Scopes { get; set; } = "pages_show_list,pages_manage_posts,pages_read_engagement,pages_manage_engagement,read_insights";
}

/// <summary>
/// فيسبوك عبر Graph API الرسمي. ربط صفحات: كود -> توكن قصير -> طويل (60 يوم) -> توكنات الصفحات (ما بتنتهي).
/// التوكنات بترسل بترويسة Authorization أو بجسم POST، مش بالرابط، لحتى ما توصل لأي سجل.
/// </summary>
public sealed class FacebookConnector(HttpClient http, IOptions<FacebookOptions> options) : IPlatformConnector
{
    private FacebookOptions Opt => options.Value;

    public string Platform => Platforms.Facebook;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.AppId) && !string.IsNullOrWhiteSpace(Opt.AppSecret);

    public string BuildAuthUrl(string state, string redirectUri) =>
        $"{Opt.DialogBase}?client_id={Uri.EscapeDataString(Opt.AppId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString(Opt.Scopes)}&response_type=code";

    public async Task<IReadOnlyList<ConnectedAccount>> ConnectAsync(string code, string redirectUri, CancellationToken ct)
    {
        var shortToken = await TokenAsync(new()
        {
            ["client_id"] = Opt.AppId,
            ["client_secret"] = Opt.AppSecret,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
        }, ct);

        var longToken = await TokenAsync(new()
        {
            ["grant_type"] = "fb_exchange_token",
            ["client_id"] = Opt.AppId,
            ["client_secret"] = Opt.AppSecret,
            ["fb_exchange_token"] = shortToken,
        }, ct);

        using var pages = await SendAsync(HttpMethod.Get, "me/accounts?fields=id,name,access_token&limit=100", longToken, null, ct);
        var accounts = new List<ConnectedAccount>();
        foreach (var page in pages.RootElement.GetProperty("data").EnumerateArray())
        {
            var token = page.GetProperty("access_token").GetString()!;
            // توكن الصفحة المشتق من توكن مستخدم طويل ما بينتهي.
            accounts.Add(new ConnectedAccount(page.GetProperty("id").GetString()!, page.GetProperty("name").GetString()!, token, null, null));
        }
        return accounts;
    }

    public async Task<PublishResult> PublishAsync(AccountCredentials account, PostContent content, CancellationToken ct)
    {
        var form = new Dictionary<string, string> { ["message"] = content.Text };
        using var doc = await SendAsync(HttpMethod.Post, $"{account.RemoteUserId}/feed", account.AccessToken, form, ct);
        return new PublishResult(doc.RootElement.GetProperty("id").GetString()!);
    }

    public async Task<IReadOnlyDictionary<string, double>> FetchMetricsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get,
            $"{remoteId}?fields=reactions.summary(true),comments.summary(true),shares", account.AccessToken, null, ct);
        var root = doc.RootElement;
        return new Dictionary<string, double>
        {
            ["reactions"] = Summary(root, "reactions"),
            ["comments"] = Summary(root, "comments"),
            ["shares"] = root.TryGetProperty("shares", out var s) && s.TryGetProperty("count", out var c) ? c.GetDouble() : 0,
        };
    }

    public async Task<IReadOnlyList<RemoteComment>> FetchCommentsAsync(AccountCredentials account, string remoteId, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"{remoteId}/comments?fields=id,from,message,created_time&limit=100", account.AccessToken, null, ct);
        var list = new List<RemoteComment>();
        foreach (var c in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var author = c.TryGetProperty("from", out var f) && f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var created = DateTimeOffset.TryParse(c.GetProperty("created_time").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : DateTimeOffset.UtcNow;
            list.Add(new RemoteComment(c.GetProperty("id").GetString()!, author, c.GetProperty("message").GetString() ?? "", created));
        }
        return list;
    }

    public async Task ReplyAsync(AccountCredentials account, string commentId, string text, CancellationToken ct)
    {
        using var _ = await SendAsync(HttpMethod.Post, $"{commentId}/comments", account.AccessToken, new() { ["message"] = text }, ct);
    }

    private async Task<string> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Post, "oauth/access_token", null, form, ct);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? bearer, Dictionary<string, string>? form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{Opt.GraphBase.TrimEnd('/')}/{path}");
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // نمرّر رسالة فيسبوك فقط (بدون الجسم الكامل ولا أي توكن).
            string message = "facebook_error";
            try { message = JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("message").GetString() ?? message; }
            catch (Exception) { /* جسم غير متوقع */ }
            throw new PlatformException(Platform, (int)response.StatusCode, message);
        }
        return JsonDocument.Parse(body);
    }

    private static double Summary(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.TryGetProperty("summary", out var s) && s.TryGetProperty("total_count", out var t)
            ? t.GetDouble() : 0;
}
