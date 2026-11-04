using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Ai;

namespace ZakiSocial.Integrations.Ai;

public sealed class ProviderOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
}

/// <summary>أي خادم بواجهة OpenAI (chat/completions): أوبن إيه آي نفسه أو نموذج داخلي (Ollama, vLLM...).</summary>
public abstract class OpenAiCompatibleProvider(HttpClient http, string name, IOptions<ProviderOptions> options, bool needsKey, bool vision) : IAiProvider
{
    protected ProviderOptions Opt => options.Value;

    public string Name => name;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Opt.BaseUrl) && !string.IsNullOrWhiteSpace(Opt.Model) && (!needsKey || !string.IsNullOrWhiteSpace(Opt.ApiKey));
    public AiCapabilities Capabilities => new(true, vision, true);

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        using var response = await SendAsync(request, stream: false, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var usage = doc.RootElement.TryGetProperty("usage", out var u) ? u : default;
        return new AiResult(text, Name, Opt.Model, Tokens(usage, "prompt_tokens"), Tokens(usage, "completion_tokens"));
    }

    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await SendAsync(request, stream: true, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            string? piece = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0 && choices[0].TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var c))
                    piece = c.GetString();
            }
            catch (JsonException) { /* سطر مش بيانات */ }
            if (!string.IsNullOrEmpty(piece)) yield return piece;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(AiRequest request, bool stream, CancellationToken ct)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.System)) messages.Add(new { role = "system", content = request.System });
        foreach (var m in request.Messages)
        {
            object content = m.Images is { Count: > 0 } && vision
                ? new object[] { new { type = "text", text = m.Text } }
                    .Concat(m.Images.Select(i => (object)new { type = "image_url", image_url = new { url = $"data:{i.MimeType};base64,{i.Base64}" } })).ToArray()
                : m.Text;
            messages.Add(new { role = m.Role, content });
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Opt.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { model = Opt.Model, messages, max_tokens = request.MaxTokens, stream }), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(Opt.ApiKey)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Opt.ApiKey);

        var response = await http.SendAsync(message, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new AiProviderException(Name, status, $"{Name}_http_{status}"); // بدون جسم الرد
        }
        return response;
    }

    private static int Tokens(JsonElement usage, string field) =>
        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(field, out var v) && v.TryGetInt32(out var n) ? n : 0;
}

public sealed class OpenAiProvider(HttpClient http, IOptionsMonitor<ProviderOptions> monitor)
    : OpenAiCompatibleProvider(http, AiProviders.OpenAi, new NamedOptions(monitor, AiProviders.OpenAi), needsKey: true, vision: true);

public sealed class LocalLlmProvider(HttpClient http, IOptionsMonitor<ProviderOptions> monitor)
    : OpenAiCompatibleProvider(http, AiProviders.Local, new NamedOptions(monitor, AiProviders.Local), needsKey: false, vision: false);

/// <summary>خيارات بإسم المزوّد (Ai:OpenAi, Ai:Claude, Ai:Local).</summary>
public sealed class NamedOptions(IOptionsMonitor<ProviderOptions> monitor, string name) : IOptions<ProviderOptions>
{
    public ProviderOptions Value => monitor.Get(name);
}
