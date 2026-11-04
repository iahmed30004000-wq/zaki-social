namespace ZakiSocial.Core.Ai;

public static class AiProviders
{
    public const string Claude = "claude";
    public const string OpenAi = "openai";
    public const string Local = "local";
}

/// <summary>أنواع المهام اللي بتحدد المزوّد بسياسة التوجيه.</summary>
public static class AiTasks
{
    // إبداعية: كابشن، أفكار حملات، إعادة صياغة، تحليل وتوصيات
    public const string Caption = "caption";
    public const string Ideas = "ideas";
    public const string Rewrite = "rewrite";
    public const string Analysis = "analysis";
    // حجم كبير وبسيطة: تصنيف، وسوم، تلخيص، ردود قصيرة
    public const string Classify = "classify";
    public const string Hashtags = "hashtags";
    public const string Summarize = "summarize";
    public const string Reply = "reply";
    // رؤية
    public const string Vision = "vision";
    // توليد صور
    public const string Image = "image";
}

public sealed record AiImage(string MimeType, string Base64);
public sealed record AiMessage(string Role, string Text, IReadOnlyList<AiImage>? Images = null);

/// <param name="Sensitive">بيانات حسّاسة: بتروح للنموذج الداخلي فقط ومش بتخرج من السيرفر.</param>
public sealed record AiRequest(
    string Task,
    IReadOnlyList<AiMessage> Messages,
    string? System = null,
    int MaxTokens = 1024,
    bool Sensitive = false,
    string? PreferProvider = null);

public sealed record AiResult(string Text, string Provider, string Model, int InputTokens, int OutputTokens, bool FellBack = false);

public sealed record AiCapabilities(bool Text, bool Vision, bool Json);

public sealed record ImageResult(byte[] Bytes, string MimeType, string Provider, string Model);

/// <summary>مزوّد بيولّد صور (واجهة OpenAI images). التوليد يمر بالموجّه فقط.</summary>
public interface IImageProvider
{
    string Name { get; }
    bool ImageConfigured { get; }
    Task<ImageResult> GenerateImageAsync(string prompt, string size, CancellationToken ct);
}

/// <summary>أي مزوّد ذكاء اصطناعي. التطبيق ما بيحكي مع مزوّد مباشرة؛ بس عبر IAiRouter.</summary>
public interface IAiProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    AiCapabilities Capabilities { get; }

    Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct);
    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct);
}

public sealed class AiProviderException(string provider, int status, string message) : Exception(message)
{
    public string Provider { get; } = provider;
    public int Status { get; } = status;
}

public interface IAiRouter
{
    Task<AiResult> RunAsync(AiRequest request, CancellationToken ct);
    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct);
    Task<ImageResult> GenerateImageAsync(string prompt, string size, CancellationToken ct);
    IReadOnlyList<(string Name, bool Configured, AiCapabilities Capabilities)> Providers();
}
