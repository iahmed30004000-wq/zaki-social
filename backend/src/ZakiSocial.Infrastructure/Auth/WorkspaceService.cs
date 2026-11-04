using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;

namespace ZakiSocial.Infrastructure.Auth;

public sealed record MemberDto(Guid UserId, string Email, string Role, bool Managed);
public sealed record InvitationDto(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);
public sealed record CreatedInvitation(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt, string Code);

/// <summary>
/// أعضاء ودعوات المساحة الحالية. بيشتغل بوضع المساحة (دور zaki_app)، فعزل الصفوف مطبّق من قاعدة البيانات
/// حتى لو نسينا فلتر بالكود.
/// </summary>
public sealed class WorkspaceService(AppDbContext db, ITenantContext tenant)
{
    private static readonly string[] ValidRoles = [Roles.Owner, Roles.Editor, Roles.Viewer];

    private Guid CurrentWorkspace =>
        tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public async Task<IReadOnlyList<MemberDto>> MembersAsync(CancellationToken ct) =>
        await (from m in db.Memberships
               join u in db.Users on m.UserId equals u.Id
               where m.WorkspaceId == CurrentWorkspace
               orderby u.Email
               select new MemberDto(u.Id, u.Email, m.Role, u.ManagedByWorkspaceId == CurrentWorkspace)).ToListAsync(ct);

    public async Task<IReadOnlyList<InvitationDto>> PendingInvitationsAsync(CancellationToken ct) =>
        await db.Invitations
            .Where(i => i.WorkspaceId == CurrentWorkspace && i.AcceptedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new InvitationDto(i.Id, i.Email, i.Role, i.ExpiresAt))
            .ToListAsync(ct);

    public async Task<CreatedInvitation> InviteAsync(string email, string role, Guid invitedBy, CancellationToken ct)
    {
        email = AuthService.NormalizeEmail(email);
        RequireValidRole(role);

        var alreadyMember = await (from m in db.Memberships
                                   join u in db.Users on m.UserId equals u.Id
                                   where m.WorkspaceId == CurrentWorkspace && u.Email == email
                                   select m.UserId).AnyAsync(ct);
        if (alreadyMember) throw new AuthException(409, "already_member");

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var invite = new Invitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = CurrentWorkspace,
            Email = email,
            Role = role,
            CodeHash = AuthService.Hash(code),
            InvitedBy = invitedBy,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Invitations.Add(invite);
        await db.SaveChangesAsync(ct);
        return new CreatedInvitation(invite.Id, email, role, invite.ExpiresAt, code);
    }

    public async Task RevokeInvitationAsync(Guid id, CancellationToken ct)
    {
        var invite = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id && i.WorkspaceId == CurrentWorkspace, ct)
                     ?? throw new AuthException(404, "not_found");
        db.Invitations.Remove(invite);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangeRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        RequireValidRole(role);
        var member = await FindMemberAsync(userId, ct);
        if (member.Role == Roles.Owner && role != Roles.Owner) await RequireAnotherOwnerAsync(userId, ct);
        member.Role = role;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(Guid userId, CancellationToken ct)
    {
        var member = await FindMemberAsync(userId, ct);
        if (member.Role == Roles.Owner) await RequireAnotherOwnerAsync(userId, ct);
        db.Memberships.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Membership> FindMemberAsync(Guid userId, CancellationToken ct) =>
        await db.Memberships.FirstOrDefaultAsync(m => m.UserId == userId && m.WorkspaceId == CurrentWorkspace, ct)
        ?? throw new AuthException(404, "not_found");

    /// <summary>ممنوع تبقى المساحة بدون مالك.</summary>
    private async Task RequireAnotherOwnerAsync(Guid exceptUserId, CancellationToken ct)
    {
        var others = await db.Memberships.CountAsync(
            m => m.WorkspaceId == CurrentWorkspace && m.Role == Roles.Owner && m.UserId != exceptUserId, ct);
        if (others == 0) throw new AuthException(409, "last_owner");
    }

    private static void RequireValidRole(string role)
    {
        if (!ValidRoles.Contains(role)) throw new AuthException(400, "role_invalid");
    }
}
