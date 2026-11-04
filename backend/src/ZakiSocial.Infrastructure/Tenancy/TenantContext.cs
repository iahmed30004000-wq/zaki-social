using ZakiSocial.Core.Tenancy;

namespace ZakiSocial.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? WorkspaceId { get; private set; }
    public string? Role { get; private set; }
    public bool IsSystem { get; private set; }

    public void UseWorkspace(Guid workspaceId, string role)
    {
        WorkspaceId = workspaceId;
        Role = role;
        IsSystem = false;
    }

    public void UseSystem()
    {
        WorkspaceId = null;
        Role = null;
        IsSystem = true;
    }
}
