using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace SewaEverything.Contracts;

public static class PasswordPolicy
{
    public const int MinLength = 12;

    public const int MaxLength = 128;

    public static readonly string Aturan =
        $"Minimal {MinLength} karakter. Hindari kata sandi yang umum dipakai, deretan berurutan, " +
        "angka saja, atau yang memuat nama maupun alamat email Anda.";

    private static readonly HashSet<string> Umum = new(StringComparer.Ordinal)
    {
        "passwordpassword", "password1234", "password12345", "katasandi123",
        "qwertyuiopasdf", "qwertyuiopasdfgh", "qwerty123456", "qwertyuiop12",
        "administrator", "adminadmin12", "admin1234567", "letmeinletmein",
        "iloveyou1234", "welcome12345", "abcd12345678", "sayacintakamu",
        "123456789012", "1234567890123", "12345678901234", "111111111111",
        "monkeymonkey", "dragondragon", "trustno1trustno1", "sunshine1234"
    };

    private const string Baris1 = "1234567890";
    private const string Baris2 = "qwertyuiop";
    private const string Baris3 = "asdfghjkl";
    private const string Baris4 = "zxcvbnm";
    private const string Abjad  = "abcdefghijklmnopqrstuvwxyz";

    public static string? Periksa(string? password, string? email = null, string? name = null)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Kata sandi wajib diisi.";
        }

        if (password.Length < MinLength)
        {
            return $"Kata sandi minimal {MinLength} karakter.";
        }

        if (password.Length > MaxLength)
        {
            return $"Kata sandi maksimal {MaxLength} karakter.";
        }

        var kecil = password.ToLowerInvariant();

        if (Umum.Contains(kecil))
        {
            return "Kata sandi ini termasuk yang paling sering dipakai orang. Pilih yang lain.";
        }

        if (password.All(char.IsDigit))
        {
            return "Kata sandi tidak boleh hanya berupa angka.";
        }

        if (kecil.Distinct().Count() < 5)
        {
            return "Kata sandi terlalu sedikit variasi karakternya.";
        }

        if (Berurutan(kecil))
        {
            return "Kata sandi tidak boleh berupa deretan huruf atau angka berurutan.";
        }

        if (MemuatIdentitas(kecil, email, name))
        {
            return "Kata sandi tidak boleh memuat nama atau alamat email Anda.";
        }

        return null;
    }

    private static bool Berurutan(string kecil)
    {
        foreach (var baris in new[] { Baris1, Baris2, Baris3, Baris4, Abjad })
        {
            if (baris.Contains(kecil, StringComparison.Ordinal))
            {
                return true;
            }

            var terbalik = new string(baris.Reverse().ToArray());

            if (terbalik.Contains(kecil, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MemuatIdentitas(string kecil, string? email, string? name)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var lokal = email.Split('@')[0].Trim().ToLowerInvariant();

            if (lokal.Length >= 4 && kecil.Contains(lokal, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var kata in name.ToLowerInvariant().Split(
                     [' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (kata.Length >= 4 && kecil.Contains(kata, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public string? EmailProperty { get; init; }

    public string? NameProperty { get; init; }

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var galat = PasswordPolicy.Periksa(
            value as string,
            Baca(context.ObjectInstance, EmailProperty),
            Baca(context.ObjectInstance, NameProperty));

        return galat is null
            ? ValidationResult.Success
            : new ValidationResult(galat, context.MemberName is null ? null : [context.MemberName]);
    }

    private static string? Baca(object? instance, string? property) =>
        instance is null || property is null
            ? null
            : instance.GetType()
                .GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(instance) as string;
}
