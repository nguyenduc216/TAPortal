using System.Security.Cryptography;
using System.Text;

namespace TAPortal.Shared;
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public static (bool IsValid, string Error) Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return (false, "Mật khẩu không được để trống.");
        if (password.Length < MinimumLength)
            return (false, $"Mật khẩu phải có ít nhất {MinimumLength} ký tự.");
        if (!password.Any(char.IsLetter))
            return (false, "Mật khẩu phải có ít nhất một chữ cái.");
        if (!password.Any(char.IsDigit))
            return (false, "Mật khẩu phải có ít nhất một chữ số.");
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            return (false, "Mật khẩu phải có ít nhất một ký tự đặc biệt.");
        return (true, string.Empty);
    }

    // No look-alike characters (0/O, 1/l/I) so the password can be read out or typed from a message.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", Lower = "abcdefghijkmnpqrstuvwxyz", Digits = "23456789", Special = "@#$%&*!?";

    /// <summary>A random 12-character password that satisfies <see cref="Validate"/>: handed to a user once, to be changed.</summary>
    public static string GenerateTemporary()
    {
        const string all = Upper + Lower + Digits + Special;
        var chars = new List<char> { Pick(Upper), Pick(Lower), Pick(Digits), Pick(Special) };
        while (chars.Count < 12) chars.Add(Pick(all));
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray());

        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
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
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error, nameof(password));
        return Derive(password);
    }

    /// <summary>
    /// Hash of an initial password staff hand out that may not pass PasswordPolicy (e.g. 123456 from the weak list).
    /// Only for accounts saved with MustChangePassword=1, so the password is replaced at the first sign-in.
    /// </summary>
    public string HashTemporary(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Mật khẩu không được để trống.", nameof(password));
        return Derive(password);
    }

    private static string Derive(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"PBKDF2$SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return false;
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != "PBKDF2" || parts[1] != "SHA256")
            return false;
        if (!int.TryParse(parts[2], out var iterations) || iterations < 10000 || iterations > 2000000)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            if (salt.Length < 16 || expected.Length != 32)
                return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
