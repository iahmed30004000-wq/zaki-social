namespace ZakiSocial.Core.Entities;

/// <summary>كل كيان له معرّف فريد.</summary>
public interface IHasId
{
    Guid Id { get; set; }
}

/// <summary>كل كيان تابع لمساحة عمل. ممنوع أي سجل بدون workspace_id (عزل المساحات).</summary>
public interface ITenantOwned
{
    Guid WorkspaceId { get; set; }
}

public static class Roles
{
    public const string Owner = "owner";
    public const string Editor = "editor";
    public const string Viewer = "viewer";
}

/// <summary>المساحة (المستأجر): كل بيانات النظام مرتبطة بمساحة.</summary>
public class Workspace : IHasId
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Plan { get; set; } = "internal";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>المستخدم عام لكل المساحات؛ الربط بالمساحة عبر Membership.</summary>
public class User : IHasId
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>المساحة اللي أنشأت هالمستخدم مباشرة (null = سجّل بنفسه أو بدعوة). بس هي بتقدر تعيد تعيين كلمة سره.</summary>
    public Guid? ManagedByWorkspaceId { get; set; }

    /// <summary>كلمة سر مؤقتة حطها المالك: لازم المستخدم يغيّرها أول ما يدخل.</summary>
    public bool MustChangePassword { get; set; }
}

/// <summary>ربط مستخدم بمساحة ودور (owner / editor / viewer).</summary>
public class Membership : ITenantOwned
{
    public Guid UserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Role { get; set; } = Roles.Viewer;
}
