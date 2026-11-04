namespace ZakiSocial.Core.Entities;

/// <summary>قالب برومبت محفوظ بقاعدة البيانات (مركز البرومبتات). ممنوع برومبت مكتوب بالكود.</summary>
public class Prompt : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string Body { get; set; } = "";
    /// <summary>JSON: تعريف المتغيرات {{...}}.</summary>
    public string? Variables { get; set; }
    /// <summary>claude / openai / local</summary>
    public string? PreferredAi { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PromptVersion : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid PromptId { get; set; }
    public int Version { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>قاعدة للمجيب الآلي: متى نرد وكيف (الخطوة 13).</summary>
public class ResponderRule : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? AccountId { get; set; }
    public string Name { get; set; } = "";
    public string? MatchPattern { get; set; }
    public string? PromptName { get; set; }
    public bool IsEnabled { get; set; }
    public int DelayMinSeconds { get; set; } = 30;
    public int DelayMaxSeconds { get; set; } = 300;
    public DateTimeOffset CreatedAt { get; set; }
}
