namespace ZakiSocial.Core.Entities;

public class AnalyticsSnapshot : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? PostTargetId { get; set; }
    /// <summary>JSON: مقاييس الأداء.</summary>
    public string Metrics { get; set; } = "{}";
    public DateTimeOffset CapturedAt { get; set; }
}

/// <summary>بيانات يجمعها العامل «جامع البيانات» (تعليقات، رسائل، إحصائيات عامة).</summary>
public class CollectedItem : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? AccountId { get; set; }
    public string Kind { get; set; } = "";
    public string Data { get; set; } = "{}";
    public DateTimeOffset CollectedAt { get; set; }
}

/// <summary>سجل التطبيق. ممنوع تسجيل توكنات أو مفاتيح أو كلمات سر هون.</summary>
public class AppLog : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Level { get; set; } = "info";
    public string Message { get; set; } = "";
    public string? Context { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>إعداد بمفتاح. القيمة العادية jsonb، والسرية مشفّرة بـ EncValue.</summary>
public class AppSetting : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public byte[]? EncValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
