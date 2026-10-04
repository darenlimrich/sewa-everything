using System.Security.Cryptography;
using System.Text;

namespace SewaEverything.Infrastructure.Payments;

public static class MidtransSignature
{
    public static string Compute(string orderId, string statusCode, string grossAmount, string serverKey)
    {
        var raw = string.Concat(orderId, statusCode, grossAmount, serverKey);
        var hash = SHA512.HashData(Encoding.UTF8.GetBytes(raw));

        return Convert.ToHexStringLower(hash);
    }

    public static bool Verify(
        string? received, string orderId, string statusCode, string grossAmount, string serverKey)
    {
        if (string.IsNullOrWhiteSpace(received))
        {
            return false;
        }

        var expected = Compute(orderId, statusCode, grossAmount, serverKey);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(received.Trim().ToLowerInvariant()));
    }
}
