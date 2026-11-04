namespace ZakiSocial.Core.Entities;

/// <summary>توكن تجديد: نخزّن بصمته (SHA-256) فقط، والقيمة الأصلية بتنبعت للمتصفح بكوكي httpOnly.</summary>
public class RefreshToken : IHasId
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>دعوة عضو لمساحة. الكود الأصلي بيظهر مرة وحدة للمالك، ونخزّن بصمته.</summary>
public class Invitation : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = Roles.Viewer;
    public string CodeHash { get; set; } = "";
    public Guid? InvitedBy { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
