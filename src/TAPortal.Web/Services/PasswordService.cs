using System.Security.Cryptography;
using System.Text;

namespace TAPortal.Web.Services;

public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static (bool IsValid, string Error) Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return (false, "Mật khẩu không được để trống.");
        if (password.Length < MinimumLength) return (false, $"Mật khẩu phải có ít nhất {MinimumLength} ký tự.");
        if (!password.Any(char.IsLetter)) return (false, "Mật khẩu phải có ít nhất một chữ cái.");
        if (!password.Any(char.IsDigit)) return (false, "Mật khẩu phải có ít nhất một chữ số.");
        if (!password.Any(c => !char.IsLetterOrDigit(c))) return (false, "Mật khẩu phải có ít nhất một ký tự đặc biệt.");
        return (true, string.Empty);
    }
}

public sealed class PasswordService
{
    private const int Iterations = 150_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        var validation = PasswordPolicy.Validate(password);
        if (!validation.IsValid) throw new ArgumentException(validation.Error, nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"PBKDF2$SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != "PBKDF2" || parts[1] != "SHA256") return false;
        if (!int.TryParse(parts[2], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
