using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SewaEverything.Infrastructure.Auth;

public static class Totp
{
    public const int Digits = 6;

    public const int StepSeconds = 30;

    public const int SecretByteLength = 20;

    private const int Modulus = 1_000_000;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretByteLength);

    public static long StepAt(DateTimeOffset moment) =>
        moment.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> mac = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, mac);

        var offset = mac[^1] & 0x0F;

        var binary = ((mac[offset] & 0x7F) << 24)
                   | (mac[offset + 1] << 16)
                   | (mac[offset + 2] << 8)
                   | mac[offset + 3];

        return (binary % Modulus).ToString(
            CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    public static bool TryMatch(
        ReadOnlySpan<byte> secret, string? candidate, long current, int window, out long matched)
    {
        matched = 0;

        if (!LooksLikeCode(candidate))
        {
            return false;
        }

        var diberikan = Encoding.ASCII.GetBytes(candidate!.Trim());

        for (var geser = -window; geser <= window; geser++)
        {
            var step = current + geser;

            if (CryptographicOperations.FixedTimeEquals(
                    diberikan, Encoding.ASCII.GetBytes(Compute(secret, step))))
            {
                matched = step;
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeCode(string? candidate)
    {
        if (candidate is null)
        {
            return false;
        }

        var dipangkas = candidate.Trim();

        return dipangkas.Length == Digits && dipangkas.All(char.IsAsciiDigit);
    }

    public static string BuildUri(string issuer, string account, string secretBase32)
    {
        var penerbit = Uri.EscapeDataString(issuer);
        var akun = Uri.EscapeDataString(account);

        return $"otpauth://totp/{penerbit}:{akun}" +
               $"?secret={secretBase32}" +
               $"&issuer={penerbit}" +
               $"&algorithm=SHA1" +
               $"&digits={Digits}" +
               $"&period={StepSeconds}";
    }
}
