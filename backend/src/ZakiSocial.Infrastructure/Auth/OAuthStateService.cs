using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ZakiSocial.Infrastructure.Auth;

public sealed record OAuthState(Guid UserId, Guid WorkspaceId, string Platform);

/// <summary>
/// "state" لربط الحساب: موقّع بـ HMAC وصالح 10 دقايق. الرجعة من المنصة ما فيها توكن دخول،
/// فبهالتوقيع نعرف مين المستخدم وأي مساحة بدون ما يقدر حدا يزوّرها.
/// </summary>
public sealed class OAuthStateService(IOptions<JwtOptions> jwt)
{
    private record Payload(Guid U, Guid W, string P, long E);

    private byte[] Key => SHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key + "|oauth-state"));

    public string Create(OAuthState state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(state.UserId, state.WorkspaceId, state.Platform, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()));
        var sig = HMACSHA256.HashData(Key, payload);
        return B64(payload) + "." + B64(sig);
    }

    public OAuthState? Validate(string? state)
    {
        try
        {
            var parts = (state ?? "").Split('.');
            if (parts.Length != 2) return null;
            var payload = FromB64(parts[0]);
            var expected = HMACSHA256.HashData(Key, payload);
            if (!CryptographicOperations.FixedTimeEquals(expected, FromB64(parts[1]))) return null;
            var p = JsonSerializer.Deserialize<Payload>(payload)!;
            if (p.E < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return new OAuthState(p.U, p.W, p.P);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string B64(byte[] data) => Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
