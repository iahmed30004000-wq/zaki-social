using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ZakiSocial.Infrastructure.Auth;

public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "zaki-social";
    public string Audience { get; set; } = "zaki-social";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 14;

    /// <summary>المفتاح من متغير البيئة Jwt__Key فقط، وطوله 32 حرف على الأقل.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be set (environment variable Jwt__Key) and at least 32 characters.");
    }

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed class JwtService(IOptions<JwtOptions> options)
{
    public const string WorkspaceClaim = "workspace_id";

    public AccessToken Create(Guid userId, string email, Guid workspaceId, string role)
    {
        var opt = options.Value;
        var expires = DateTimeOffset.UtcNow.AddMinutes(opt.AccessMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(WorkspaceClaim, workspaceId.ToString()),
            // الدور هون للعرض بس؛ الصلاحية الفعلية بتنقرأ من قاعدة البيانات بكل طلب.
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var token = new JwtSecurityToken(
            opt.Issuer, opt.Audience, claims,
            notBefore: DateTime.UtcNow, expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(opt.SigningKey(), SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
