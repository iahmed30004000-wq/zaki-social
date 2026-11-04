using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZakiSocial.Core.Integrations;

namespace ZakiSocial.Integrations;

public sealed record PlatformResponse(JsonDocument Json, IReadOnlyDictionary<string, string> Headers) : IDisposable
{
    public JsonElement Root => Json.RootElement;
    public void Dispose() => Json.Dispose();
}

/// <summary>أدوات مشتركة لموصّلات المنصات: إرسال، تحويل الأخطاء لرسالة آمنة (بدون توكنات)، وPKCE.</summary>
public static class PlatformHttp
{
    public static async Task<PlatformResponse> SendAsync(HttpClient http, string platform, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        JsonDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException) { /* مش JSON */ }
        }
        if (!response.IsSuccessStatusCode) throw new PlatformException(platform, (int)response.StatusCode, Message(platform, doc));

        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
        return new PlatformResponse(doc ?? JsonDocument.Parse("{}"), headers);
    }

    public static HttpRequestMessage Form(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>> form, string? bearer = null, string? basicUser = null, string? basicPass = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = new FormUrlEncodedContent(form) };
        Authorize(request, bearer, basicUser, basicPass);
        return request;
    }

    public static HttpRequestMessage Json(HttpMethod method, string url, object? body, string? bearer = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        Authorize(request, bearer, null, null);
        return request;
    }

    public static HttpRequestMessage Get(string url, string? bearer) => Json(HttpMethod.Get, url, null, bearer);

    private static void Authorize(HttpRequestMessage request, string? bearer, string? user, string? pass)
    {
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        else if (user is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));
    }

    /// <summary>رسالة خطأ المنصة بدون الجسم الكامل (ممكن يحتوي توكنات).</summary>
    private static string Message(string platform, JsonDocument? doc)
    {
        if (doc is null) return $"{platform}_error";
        var root = doc.RootElement;
        string[][] paths = [["error", "message"], ["error_description"], ["message"], ["error", "code"], ["detail"], ["title"], ["error"]];
        foreach (var path in paths)
        {
            var node = root;
            var ok = root.ValueKind == JsonValueKind.Object;
            foreach (var key in path)
            {
                if (!ok || node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(key, out node)) { ok = false; break; }
            }
            if (ok && node.ValueKind == JsonValueKind.String && node.GetString() is { Length: > 0 } text) return text.Length <= 200 ? text : text[..200];
        }
        return $"{platform}_error";
    }

    public static string Base64Url(byte[] data) => Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>PKCE: verifier مشتق من الـstate بسر السيرفر (ما بيتخزّن ولا بيظهر)، وchallenge = SHA-256 له.</summary>
    public static (string Verifier, string Challenge) Pkce(string secret, string state)
    {
        var verifier = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("pkce|" + state)));
        return (verifier, Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier))));
    }

    public static string Q(string value) => Uri.EscapeDataString(value);
}
