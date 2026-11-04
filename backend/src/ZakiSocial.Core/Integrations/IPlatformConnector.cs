namespace ZakiSocial.Core.Integrations;

/// <summary>حساب رجع من المنصة بعد الربط (التوكنات بنص عادي هون فقط، وتتشفّر قبل أي تخزين).</summary>
public sealed record ConnectedAccount(
    string RemoteUserId,
    string DisplayName,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt);

/// <summary>بيانات الدخول لحساب مخزّن (بعد فك التشفير بلحظة الاستعمال).</summary>
public sealed record AccountCredentials(Guid AccountId, string RemoteUserId, string AccessToken, string? RefreshToken);

/// <summary>ملف وسائط جاهز للرفع للمنصة (النسخة الموحّدة بعد معالجة FFmpeg).</summary>
public sealed record MediaFile(string Kind, string ContentType, string FileName, byte[] Data);

public sealed record PostContent(string Text, IReadOnlyList<MediaFile> Media);

public sealed record PublishResult(string RemoteId);

public sealed record RemoteComment(string Id, string AuthorName, string Text, DateTimeOffset CreatedAt);

/// <summary>خطأ من المنصة (مرفوض، حد الاستعمال، توكن منتهي...). الرسالة بدون أي توكن.</summary>
public sealed class PlatformException(string platform, int status, string message) : Exception(message)
{
    public string Platform { get; } = platform;
    public int Status { get; } = status;
}

/// <summary>
/// واجهة موحدة لكل منصة. إضافة منصة جديدة = صف جديد بالتسجيل، والباقي (المؤلّف، الحسابات، الجدولة) بيشتغل بشكل عام.
/// الـ API الرسمية فقط.
/// </summary>
public interface IPlatformConnector
{
    string Platform { get; }
    bool IsConfigured { get; }

    string BuildAuthUrl(string state, string redirectUri);
    Task<IReadOnlyList<ConnectedAccount>> ConnectAsync(string code, string redirectUri, CancellationToken ct);
    Task<PublishResult> PublishAsync(AccountCredentials account, PostContent content, CancellationToken ct);
    Task<IReadOnlyDictionary<string, double>> FetchMetricsAsync(AccountCredentials account, string remoteId, CancellationToken ct);
    Task<IReadOnlyList<RemoteComment>> FetchCommentsAsync(AccountCredentials account, string remoteId, CancellationToken ct);
    Task ReplyAsync(AccountCredentials account, string commentId, string text, CancellationToken ct);
}

public interface IConnectorRegistry
{
    IPlatformConnector Get(string platform);
}
