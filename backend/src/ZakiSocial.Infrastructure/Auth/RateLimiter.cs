using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ZakiSocial.Infrastructure.Auth;

/// <summary>
/// عدّاد نافذة ثابتة (دقيقة) على ريديس. لو ريديس واقع بنسمح (fail-open) وبنسجّل تحذير، لأن إيقاف النظام كله
/// بسبب العدّاد أسوأ من غيابه المؤقت.
/// </summary>
public sealed class RateLimiter(RedisConnection redis, ILogger<RateLimiter> logger)
{
    /// <returns>(مسموح؟، ثواني للنافذة الجاية)</returns>
    public async Task<(bool Allowed, int RetryAfterSeconds)> HitAsync(string key, int limit)
    {
        var now = DateTimeOffset.UtcNow;
        var window = now.ToUnixTimeSeconds() / 60;
        var retry = (int)(60 - now.ToUnixTimeSeconds() % 60);
        try
        {
            var db = await redis.DatabaseAsync();
            var redisKey = $"rl:{key}:{window}";
            var count = await db.StringIncrementAsync(redisKey);
            if (count == 1) await db.KeyExpireAsync(redisKey, TimeSpan.FromSeconds(75));
            return (count <= limit, retry);
        }
        catch (Exception ex)
        {
            logger.LogWarning("rate limiter unavailable ({Error}); allowing", ex.GetType().Name);
            return (true, retry);
        }
    }

    /// <summary>مفتاح بدون بيانات شخصية: بصمة الإيميل بدل الإيميل نفسه.</summary>
    public static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()))).ToLowerInvariant()[..24];
}

/// <summary>حماية تسجيل الدخول من التخمين: محاولات لكل إيميل بالدقيقة (نجاح أو فشل).</summary>
public sealed class LoginThrottle(RateLimiter limiter, IConfiguration configuration)
{
    public async Task CheckAsync(string email)
    {
        var limit = configuration.GetValue("RateLimit:LoginPerEmailPerMinute", 8);
        var (allowed, _) = await limiter.HitAsync($"login:{RateLimiter.Fingerprint(email)}", limit);
        if (!allowed) throw new AuthException(429, "rate_limited");
    }
}
