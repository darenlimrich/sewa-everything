using System.Text;

namespace SewaEverything.Infrastructure.Auth;

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var hasil = new StringBuilder((data.Length * 8 + 4) / 5);

        var buffer = 0;
        var bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;

            while (bits >= 5)
            {
                bits -= 5;
                hasil.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }

        if (bits > 0)
        {
            hasil.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return hasil.ToString();
    }

    public static bool TryDecode(string? text, out byte[] data)
    {
        data = [];

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var bytes = new List<byte>(text.Length * 5 / 8 + 1);

        var buffer = 0;
        var bits = 0;

        foreach (var raw in text)
        {
            if (raw is ' ' or '-' or '=')
            {
                continue;
            }

            var index = Alphabet.IndexOf(char.ToUpperInvariant(raw));

            if (index < 0)
            {
                return false;
            }

            buffer = (buffer << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        if (bytes.Count == 0)
        {
            return false;
        }

        data = [.. bytes];
        return true;
    }
}
