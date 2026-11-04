using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ZakiSocial.Infrastructure.Security;

/// <summary>
/// تشفير AES-256-GCM لتوكنات المنصات والأسرار قبل التخزين. الناتج: nonce(12) + tag(16) + النص المشفّر.
/// المفتاح من متغير البيئة Encryption__Key (32 حرف على الأقل) ومش من الكود، والمفتاح الفعلي = SHA-256 له.
/// </summary>
public sealed class TokenCryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public TokenCryptor(IConfiguration configuration)
    {
        var secret = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("Encryption:Key must be set (environment variable Encryption__Key) and at least 32 characters.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public byte[] Encrypt(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);

        var result = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        cipher.CopyTo(result, NonceSize + TagSize);
        return result;
    }

    public string Decrypt(byte[] blob)
    {
        if (blob.Length < NonceSize + TagSize) throw new CryptographicException("blob too short");
        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
