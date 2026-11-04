namespace ZakiSocial.Core.Entities;

/// <summary>سجل استهلاك الذكاء الاصطناعي لكل مساحة (لمراقبة التكلفة وتطبيق الحدود).</summary>
public class AiUsage : IHasId, ITenantOwned
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string Task { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public bool FellBack { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
