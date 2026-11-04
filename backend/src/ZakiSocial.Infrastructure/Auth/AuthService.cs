using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;

namespace ZakiSocial.Infrastructure.Auth;

public sealed record WorkspaceInfo(Guid Id, string Name, string Role);

public sealed record AuthResult(
    AccessToken Access,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    Guid UserId,
    string Email,
    WorkspaceInfo CurrentWorkspace,
    IReadOnlyList<WorkspaceInfo> Workspaces);

/// <summary>خطأ متوقع (بيرجع للمستخدم برسالة وكود حالة)، مش عطل.</summary>
public sealed class AuthException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>
/// تسجيل، دخول، تجديد، تبديل مساحة. كلها بوضع النظام لأن المستخدم لسا ما اختار مساحة،
/// وكل استعلام عن المساحات بيمر عبر عضويات المستخدم نفسه فقط.
/// </summary>
public sealed class AuthService(
    AppDbContext db,
    ITenantContext tenant,
    JwtService jwt,
    IOptions<JwtOptions> jwtOptions)
{
    private static readonly PasswordHasher<User> Hasher = new();
    private const int MinPasswordLength = 10;

    public async Task<AuthResult> RegisterAsync(string email, string password, string? workspaceName, string? inviteCode, CancellationToken ct)
    {
        tenant.UseSystem();
        email = NormalizeEmail(email);
        if (password.Length < MinPasswordLength) throw new AuthException(400, "password_too_short");
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new AuthException(409, "email_taken");

        var user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTimeOffset.UtcNow };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.Users.Add(user);

        Guid workspaceId;
        if (!string.IsNullOrWhiteSpace(inviteCode))
        {
            var hash = Hash(inviteCode.Trim());
            var invite = await db.Invitations.FirstOrDefaultAsync(i => i.CodeHash == hash, ct)
                         ?? throw new AuthException(400, "invite_invalid");
            if (invite.AcceptedAt is not null || invite.ExpiresAt < DateTimeOffset.UtcNow)
                throw new AuthException(400, "invite_invalid");
            if (!string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase))
                throw new AuthException(400, "invite_email_mismatch");

            invite.AcceptedAt = DateTimeOffset.UtcNow;
            workspaceId = invite.WorkspaceId;
            db.Memberships.Add(new Membership { UserId = user.Id, WorkspaceId = workspaceId, Role = invite.Role });
        }
        else
        {
            var workspace = new Workspace
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(workspaceName) ? email.Split('@')[0] : workspaceName.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Workspaces.Add(workspace);
            workspaceId = workspace.Id;
            db.Memberships.Add(new Membership { UserId = user.Id, WorkspaceId = workspace.Id, Role = Roles.Owner });
        }

        await db.SaveChangesAsync(ct);
        return await IssueAsync(user, workspaceId, ct);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, Guid? workspaceId, CancellationToken ct)
    {
        tenant.UseSystem();
        email = NormalizeEmail(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        // نفس الرسالة لو الإيميل أو كلمة السر غلط، لحتى ما نكشف مين مسجّل.
        if (user is null) throw new AuthException(401, "invalid_credentials");
        var verdict = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed) throw new AuthException(401, "invalid_credentials");
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }
        return await IssueAsync(user, workspaceId, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        tenant.UseSystem();
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                     ?? throw new AuthException(401, "invalid_refresh");
        if (stored.RevokedAt is not null || stored.ExpiresAt < DateTimeOffset.UtcNow)
            throw new AuthException(401, "invalid_refresh");

        stored.RevokedAt = DateTimeOffset.UtcNow; // تدوير: كل توكن يُستعمل مرة وحدة
        var user = await db.Users.FirstAsync(u => u.Id == stored.UserId, ct);
        return await IssueAsync(user, stored.WorkspaceId, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        tenant.UseSystem();
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>دور المستخدم بمساحة معينة من قاعدة البيانات (null لو مش عضو). للمسارات اللي ما فيها توكن دخول.</summary>
    public async Task<string?> MemberRoleAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        tenant.UseSystem();
        return await db.Memberships
            .Where(m => m.UserId == userId && m.WorkspaceId == workspaceId)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AuthResult> SwitchWorkspaceAsync(Guid userId, Guid workspaceId, string? currentRefresh, CancellationToken ct)
    {
        tenant.UseSystem();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        await LogoutAsync(currentRefresh, ct);
        return await IssueAsync(user, workspaceId, ct, mustBeMember: true);
    }

    /// <summary>مساحة جديدة لمستخدم موجود، هو مالكها.</summary>
    public async Task<AuthResult> CreateWorkspaceAsync(Guid userId, string name, string? currentRefresh, CancellationToken ct)
    {
        tenant.UseSystem();
        if (string.IsNullOrWhiteSpace(name)) throw new AuthException(400, "name_required");
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var workspace = new Workspace { Id = Guid.NewGuid(), Name = name.Trim(), CreatedAt = DateTimeOffset.UtcNow };
        db.Workspaces.Add(workspace);
        db.Memberships.Add(new Membership { UserId = userId, WorkspaceId = workspace.Id, Role = Roles.Owner });
        await db.SaveChangesAsync(ct);
        await LogoutAsync(currentRefresh, ct);
        return await IssueAsync(user, workspace.Id, ct);
    }

    private async Task<AuthResult> IssueAsync(User user, Guid? preferredWorkspace, CancellationToken ct, bool mustBeMember = false)
    {
        var memberships = await (
            from m in db.Memberships
            join w in db.Workspaces on m.WorkspaceId equals w.Id
            where m.UserId == user.Id
            orderby w.CreatedAt
            select new WorkspaceInfo(w.Id, w.Name, m.Role)).ToListAsync(ct);

        if (memberships.Count == 0) throw new AuthException(403, "no_workspace");
        var current = preferredWorkspace is null
            ? memberships[0]
            : memberships.FirstOrDefault(w => w.Id == preferredWorkspace)
              ?? (mustBeMember ? throw new AuthException(403, "not_a_member") : memberships[0]);

        var access = jwt.Create(user.Id, user.Email, current.Id, current.Role);

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var expires = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshDays);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            WorkspaceId = current.Id,
            TokenHash = Hash(raw),
            ExpiresAt = expires,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return new AuthResult(access, raw, expires, user.Id, user.Email, current, memberships);
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static string NormalizeEmail(string email)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        try { _ = new MailAddress(email); }
        catch (FormatException) { throw new AuthException(400, "email_invalid"); }
        if (!email.Contains('@')) throw new AuthException(400, "email_invalid");
        return email;
    }
}
