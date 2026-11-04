using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;

namespace ZakiSocial.Infrastructure.Auth;

public sealed record CreatedUser(Guid UserId, string Email, string Role, string TempPassword);

/// <summary>
/// المالك بيعمل مستخدمين مباشرة (إيميل + كلمة سر مؤقتة + دور) بدل الدعوة، ومثلها إعادة تعيين كلمة السر.
/// المستخدم اللي بيعمله المالك بيرتبط بمساحته (ManagedByWorkspaceId) وبس مساحته بتقدر تعيد تعيين كلمته،
/// لحتى ما يصير حساب شخص عضو بمساحات ثانية تحت سيطرة مالك مساحة وحدة. المستخدم بجدول عام فبنشتغل بوضع النظام،
/// بعد ما نلتقط رقم المساحة من السياق (والصلاحية «مالك» بتتفحص بالمسار قبل ما نوصل هون).
/// </summary>
public sealed class UserAdminService(AppDbContext db, ITenantContext tenant)
{
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly string[] ValidRoles = [Roles.Owner, Roles.Editor, Roles.Viewer];
    private const int MinPasswordLength = 10;
    private const string PasswordAlphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<CreatedUser> CreateAsync(string email, string? password, string role, CancellationToken ct)
    {
        var workspaceId = tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");
        email = AuthService.NormalizeEmail(email);
        if (!ValidRoles.Contains(role)) throw new AuthException(400, "role_invalid");
        if (!string.IsNullOrWhiteSpace(password) && password.Length < MinPasswordLength) throw new AuthException(400, "password_too_short");

        tenant.UseSystem();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new AuthException(409, "email_taken");

        var temp = string.IsNullOrWhiteSpace(password) ? NewPassword() : password;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow,
            ManagedByWorkspaceId = workspaceId,
            MustChangePassword = true,
        };
        user.PasswordHash = Hasher.HashPassword(user, temp);
        db.Users.Add(user);
        db.Memberships.Add(new Membership { UserId = user.Id, WorkspaceId = workspaceId, Role = role });
        await db.SaveChangesAsync(ct);
        return new CreatedUser(user.Id, email, role, temp);
    }

    /// <summary>كلمة سر مؤقتة جديدة لمستخدم أنشأته هالمساحة. بتسجّل خروجه من كل الأجهزة وبتجبره يغيّرها عند الدخول.</summary>
    public async Task<string> ResetPasswordAsync(Guid userId, string? password, CancellationToken ct)
    {
        var workspaceId = tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");
        if (!string.IsNullOrWhiteSpace(password) && password.Length < MinPasswordLength) throw new AuthException(400, "password_too_short");

        tenant.UseSystem();
        var user = await (from u in db.Users
                          join m in db.Memberships on u.Id equals m.UserId
                          where u.Id == userId && m.WorkspaceId == workspaceId
                          select u).FirstOrDefaultAsync(ct)
                   ?? throw new AuthException(404, "not_found");
        if (user.ManagedByWorkspaceId != workspaceId) throw new AuthException(403, "not_managed");

        var temp = string.IsNullOrWhiteSpace(password) ? NewPassword() : password;
        user.PasswordHash = Hasher.HashPassword(user, temp);
        user.MustChangePassword = true;
        var now = DateTimeOffset.UtcNow;
        foreach (var t in await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct))
            t.RevokedAt = now;
        await db.SaveChangesAsync(ct);
        return temp;
    }

    private static string NewPassword() =>
        new(Enumerable.Range(0, 14).Select(_ => PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)]).ToArray());
}
