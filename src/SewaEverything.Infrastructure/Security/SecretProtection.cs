using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SewaEverything.Infrastructure.Security;

public sealed class SecretProtectionOptions
{
    public const string SectionName = "Security";

    public string? SecretKey { get; set; }
}

public interface ISecretProtector
{
    bool IsConfigured { get; }

    bool IsProtected(string? stored);

    string Protect(string plaintext);

    string? Reveal(string? stored);
}

public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string Prefix = "enc.v1.";

    private const int NonceBytes = 12;
    private const int TagBytes   = 16;

    private readonly byte[]? _key;

    public AesGcmSecretProtector(IOptions<SecretProtectionOptions> options)
    {
        var configured = options.Value.SecretKey;

        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        if (!TryReadKey(configured, out var key))
        {
            throw new InvalidOperationException(
                "Security:SecretKey harus 32 byte dalam base64. Hasilkan sekali dengan " +
                "[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 })) " +
                "lalu simpan sebagai environment variable Security__SecretKey.");
        }

        _key = key;
    }

    public bool IsConfigured => _key is not null;

    public bool IsProtected(string? stored) =>
        stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        if (_key is null)
        {
            throw new InvalidOperationException(
                "Security:SecretKey belum diisi, jadi rahasia ini tidak dapat dienkripsi.");
        }

        var nonce  = RandomNumberGenerator.GetBytes(NonceBytes);
        var plain  = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag    = new byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[NonceBytes + cipher.Length + TagBytes];
        nonce.CopyTo(blob, 0);
        cipher.CopyTo(blob, NonceBytes);
        tag.CopyTo(blob, NonceBytes + cipher.Length);

        return Prefix + Convert.ToBase64String(blob);
    }

    public string? Reveal(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || !IsProtected(stored))
        {
            return stored;
        }

        if (_key is null)
        {
            throw new InvalidOperationException(
                "Ada rahasia terenkripsi di database tapi Security:SecretKey tidak diisi. " +
                "Kembalikan kuncinya, atau tempel ulang kredensial gateway lewat /owner/gateway.");
        }

        var blob = Convert.FromBase64String(stored[Prefix.Length..]);

        if (blob.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException("Bentuk rahasia terenkripsi tidak utuh.");
        }

        var nonce  = blob.AsSpan(0, NonceBytes);
        var cipher = blob.AsSpan(NonceBytes, blob.Length - NonceBytes - TagBytes);
        var tag    = blob.AsSpan(blob.Length - TagBytes, TagBytes);
        var plain  = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    private static bool TryReadKey(string configured, out byte[] key)
    {
        key = [];

        try
        {
            var bytes = Convert.FromBase64String(configured.Trim());

            if (bytes.Length != 32)
            {
                return false;
            }

            key = bytes;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
