using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ZakiSocial.Infrastructure.Auth;

namespace ZakiSocial.Infrastructure.Media;

/// <summary>
/// روابط عامة موقّعة ومؤقتة لوسائط المنشورات (إنستغرام وغيره بيسحبوا الملف من رابط عام).
/// الرابط بيحتوي المساحة والوسيط ووقت الانتهاء وتوقيع HMAC؛ ما بيكشف أي ملف تاني ولا بيعيش أكتر من ساعتين.
/// </summary>
public sealed class PublicMediaUrlService(IOptions<JwtOptions> jwt, IConfiguration configuration)
{
    private byte[] Key => SHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key + "|public-media"));

    public string CreateAbsolute(Guid workspaceId, Guid mediaId, string kind, TimeSpan? ttl = null)
    {
        var exp = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromHours(2)).ToUnixTimeSeconds();
        var name = kind == "video" ? "video.mp4" : "image.jpg";
        var baseUrl = (configuration["App:PublicUrl"] ?? "https://localhost").TrimEnd('/');
        return $"{baseUrl}/api/public/media/{workspaceId:N}/{mediaId:N}/{exp}/{Sign(workspaceId, mediaId, exp)}/{name}";
    }

    public bool Validate(Guid workspaceId, Guid mediaId, long exp, string signature)
    {
        if (exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        var expected = Encoding.UTF8.GetBytes(Sign(workspaceId, mediaId, exp));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(signature));
    }

    private string Sign(Guid workspaceId, Guid mediaId, long exp) =>
        Convert.ToBase64String(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes($"{workspaceId:N}.{mediaId:N}.{exp}")))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
