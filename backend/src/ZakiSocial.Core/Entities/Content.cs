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
    /// <summary>none / daily / weekly: بعد النشر بينعمل نسخة مجدولة للمرة الجاية.</summary>
    public string RepeatRule { get; set; } = "none";
    public Guid? CreatedBy { get; set; }
    /// <summary>اعتماد بشري: محتوى الذكاء الاصطناعي ما بينشر قبل ما إنسان يعتمده.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
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
    /// <summary>اعتماد بشري: المرفوع من مستخدم معتمد تلقائياً، والمولّد بالذكاء الاصطناعي لازم يتعتمد قبل الاستعمال.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    /// <summary>processing / ready / failed</summary>
    public string Status { get; set; } = "processing";
    public string? Error { get; set; }
    public string? ProcessedKey { get; set; }
    public string? ThumbnailKey { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>ربط منشور بوسائطه بترتيب.</summary>
public class PostMedia : ITenantOwned
{
    public Guid PostId { get; set; }
    public Guid MediaId { get; set; }
    public Guid WorkspaceId { get; set; }
    public int SortOrder { get; set; }
}
