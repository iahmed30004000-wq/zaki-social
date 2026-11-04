namespace ZakiSocial.Core.Tenancy;

/// <summary>
/// المساحة الحالية للطلب. الافتراضي "غير محدد": الاتصال بقاعدة البيانات بيشتغل بدور zaki_app
/// بدون مساحة، فبيشوف صفر صفوف (أمان افتراضي). النظام (تسجيل الدخول، الهجرات، الصحة) بيطلب UseSystem() صراحة.
/// </summary>
public interface ITenantContext
{
    Guid? WorkspaceId { get; }
    string? Role { get; }
    bool IsSystem { get; }

    void UseWorkspace(Guid workspaceId, string role);
    void UseSystem();
}
