namespace ZakiSocial.Core.Entities;

public static class Platforms
{
    public const string Facebook = "facebook";
    public const string Instagram = "instagram";
    public const string TikTok = "tiktok";
    public const string X = "x";
    public const string YouTube = "youtube";
    public const string LinkedIn = "linkedin";
}

/// <summary>بروكسي مرتبط بحساب. بيانات الدخول مشفّرة (bytea) ومش نص.</summary>
public class Proxy : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public byte[]? EncCredentials { get; set; }
    public string Status { get; set; } = "unknown";
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>حساب سوشال ميديا مربوط. التوكنات مشفّرة AES-256 قبل التخزين (الخطوة 04).</summary>
public class SocialAccount : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Platform { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RemoteUserId { get; set; } = "";
    public byte[]? EncAccessToken { get; set; }
    public byte[]? EncRefreshToken { get; set; }
    public DateTimeOffset? TokenExpiry { get; set; }
    public Guid? ProxyId { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}
