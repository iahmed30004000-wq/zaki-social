namespace ZakiSocial.Core.Entities;

public static class PostStatus
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Publishing = "publishing";
    public const string Published = "published";
    public const string Failed = "failed";
}

public class Post : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Body { get; set; } = "";
    public bool AiGenerated { get; set; }
    public string Status { get; set; } = PostStatus.Draft;
    public DateTimeOffset? ScheduledAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>نفس المنشور على عدة حسابات: صف لكل حساب.</summary>
public class PostTarget : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid PostId { get; set; }
    public Guid AccountId { get; set; }
    public string Status { get; set; } = "pending";
    public string? RemotePostId { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public class MediaAsset : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string StorageKey { get; set; } = "";
    public string Kind { get; set; } = "image";
    public bool AiGenerated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
