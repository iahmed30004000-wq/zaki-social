namespace ZakiSocial.Core.Integrations;

/// <summary>
/// البروكسي الثابت (sticky) للحساب الجاري التعامل معه. العامل بيفتحه حوالين نداءات المنصة، ومعالج HTTP
/// بيمرّر الطلبات عبره. بدونه الطلبات بتطلع مباشرة.
/// </summary>
public static class ProxyContext
{
    private static readonly AsyncLocal<string?> Current_ = new();

    public static string? Current => Current_.Value;

    /// <param name="proxyUrl">http://user:pass@host:port أو null.</param>
    public static IDisposable Use(string? proxyUrl)
    {
        var previous = Current_.Value;
        Current_.Value = proxyUrl;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current_.Value = previous;
    }
}
