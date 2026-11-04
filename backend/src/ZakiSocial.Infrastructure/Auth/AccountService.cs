using Microsoft.EntityFrameworkCore;
using ZakiSocial.Core.Entities;
using ZakiSocial.Core.Integrations;
using ZakiSocial.Core.Tenancy;
using ZakiSocial.Data;
using ZakiSocial.Infrastructure.Security;

namespace ZakiSocial.Infrastructure.Auth;

public sealed record AccountDto(Guid Id, string Platform, string DisplayName, string RemoteUserId, string Status, DateTimeOffset? TokenExpiry);

/// <summary>الحسابات المربوطة. التوكنات بتتشفّر قبل التخزين وما بترجع للواجهة أبداً.</summary>
public sealed class AccountService(
    AppDbContext db,
    ITenantContext tenant,
    TokenCryptor crypto,
    IConnectorRegistry connectors)
{
    private Guid Workspace => tenant.WorkspaceId ?? throw new AuthException(403, "no_workspace");

    public async Task<IReadOnlyList<AccountDto>> ListAsync(CancellationToken ct) =>
        await db.SocialAccounts
            .Where(a => a.WorkspaceId == Workspace)
            .OrderBy(a => a.Platform).ThenBy(a => a.DisplayName)
            .Select(a => new AccountDto(a.Id, a.Platform, a.DisplayName, a.RemoteUserId, a.Status, a.TokenExpiry))
            .ToListAsync(ct);

    public string ConnectUrl(string platform, string state, string redirectUri)
    {
        var connector = connectors.Get(platform);
        if (!connector.IsConfigured) throw new AuthException(503, $"{platform}_not_configured");
        return connector.BuildAuthUrl(state, redirectUri);
    }

    /// <summary>بيربط كل حسابات المنصة اللي رجعت (مثلاً كل صفحات فيسبوك) ويحدّث الموجود.</summary>
    public async Task<int> ConnectAsync(string platform, string code, string redirectUri, CancellationToken ct)
    {
        var connector = connectors.Get(platform);
        if (!connector.IsConfigured) throw new AuthException(503, $"{platform}_not_configured");
        var remote = await connector.ConnectAsync(code, redirectUri, ct);

        foreach (var a in remote)
        {
            var existing = await db.SocialAccounts.FirstOrDefaultAsync(
                x => x.WorkspaceId == Workspace && x.Platform == platform && x.RemoteUserId == a.RemoteUserId, ct);
            if (existing is null)
            {
                existing = new SocialAccount { Id = Guid.NewGuid(), WorkspaceId = Workspace, Platform = platform, RemoteUserId = a.RemoteUserId, CreatedAt = DateTimeOffset.UtcNow };
                db.SocialAccounts.Add(existing);
            }
            existing.DisplayName = a.DisplayName;
            existing.EncAccessToken = crypto.Encrypt(a.AccessToken);
            existing.EncRefreshToken = a.RefreshToken is null ? null : crypto.Encrypt(a.RefreshToken);
            existing.TokenExpiry = a.ExpiresAt;
            existing.Status = "active";
        }
        await db.SaveChangesAsync(ct);
        return remote.Count;
    }

    public async Task DisconnectAsync(Guid id, CancellationToken ct)
    {
        var account = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == id && a.WorkspaceId == Workspace, ct)
                      ?? throw new AuthException(404, "not_found");
        db.SocialAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>يفك تشفير بيانات حساب لحظة الاستعمال بس (نشر، جمع بيانات).</summary>
    public async Task<AccountCredentials> CredentialsAsync(Guid id, CancellationToken ct)
    {
        var a = await db.SocialAccounts.FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == Workspace, ct)
                ?? throw new AuthException(404, "not_found");
        if (a.EncAccessToken is null) throw new AuthException(409, "account_not_connected");
        return new AccountCredentials(a.Id, a.RemoteUserId, crypto.Decrypt(a.EncAccessToken),
            a.EncRefreshToken is null ? null : crypto.Decrypt(a.EncRefreshToken));
    }
}
