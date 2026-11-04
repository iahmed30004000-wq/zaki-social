using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Ai;

namespace ZakiSocial.Integrations.Ai;

/// <summary>Claude عبر Messages API الرسمي (POST /v1/messages). المفتاح من البيئة فقط.</summary>
public sealed class ClaudeProvider(HttpClient http, IOptionsMonitor<ProviderOptions> monitor) : IAiProvider
{
    private ProviderOptions Opt => monitor.Get(AiProviders.Claude);

    public string Name => AiProviders.Claude;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.ApiKey) && !string.IsNullOrWhiteSpace(Opt.Model);
    public AiCapabilities Capabilities => new(true, true, true);

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        using var response = await SendAsync(request, stream: false, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            if (block.TryGetProperty("text", out var t)) sb.Append(t.GetString());
        var usage = doc.RootElement.TryGetProperty("usage", out var u) ? u : default;
        return new AiResult(sb.ToString(), Name, Opt.Model, Tokens(usage, "input_tokens"), Tokens(usage, "output_tokens"));
    }

    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await SendAsync(request, stream: true, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            string? piece = null;
            try
            {
                using var doc = JsonDocument.Parse(line[5..].Trim());
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "content_block_delta"
                    && doc.RootElement.GetProperty("delta").TryGetProperty("text", out var t))
                    piece = t.GetString();
            }
            catch (JsonException) { /* سطر مش بيانات */ }
            if (!string.IsNullOrEmpty(piece)) yield return piece;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(AiRequest request, bool stream, CancellationToken ct)
    {
        var messages = request.Messages.Select(m => new
        {
            role = m.Role == "assistant" ? "assistant" : "user",
            content = m.Images is { Count: > 0 }
                ? new object[] { new { type = "text", text = m.Text } }
                    .Concat(m.Images.Select(i => (object)new { type = "image", source = new { type = "base64", media_type = i.MimeType, data = i.Base64 } })).ToArray()
                : (object)m.Text,
        });

        var payload = new Dictionary<string, object?>
        {
            ["model"] = Opt.Model,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = messages,
            ["stream"] = stream,
        };
        if (!string.IsNullOrWhiteSpace(request.System)) payload["system"] = request.System;

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{(string.IsNullOrWhiteSpace(Opt.BaseUrl) ? "https://api.anthropic.com" : Opt.BaseUrl).TrimEnd('/')}/v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-api-key", Opt.ApiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");

        var response = await http.SendAsync(message, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new AiProviderException(Name, status, $"{Name}_http_{status}");
        }
        return response;
    }

    private static int Tokens(JsonElement usage, string field) =>
        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(field, out var v) && v.TryGetInt32(out var n) ? n : 0;
}
