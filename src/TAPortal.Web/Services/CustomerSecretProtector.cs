using System.Security.Cryptography;
using System.Text;

namespace TAPortal.Web.Services;

// Encrypts the passwords / PINs of customer system accounts (dbo.CustomerSystemAccounts) with AES-256-GCM under one
// fixed key from configuration (CustomerSecrets:Key = base64 of 32 random bytes, generated once with
// deploy/windows/New-TACustomerSecretKey.ps1). The database only ever holds ciphertext. The key never rotates by
// itself, so keeping one copy of it outside the server is the whole backup. Saving secrets is refused while the key
// is missing or malformed.
public interface ICustomerSecretProtector
{
    bool Enabled { get; }
    // context binds the ciphertext to one customer / account / field (CustomerSecretContext.For): a value copied to
    // another row in the database no longer decrypts.
    byte[] Protect(string plaintext, string context);
    // Null when the ciphertext cannot be decrypted (different key or context, or data tampered with).
    string? Unprotect(byte[] ciphertext, string context);
}

public static class CustomerSecretContext
{
    public static string For(Guid customerId, Guid accountId, SecretField field) => $"{customerId:N}|{accountId:N}|{field}";
}

public sealed class AesGcmCustomerSecretProtector : ICustomerSecretProtector
{
    public const string ConfigKey = "CustomerSecrets:Key";
    // Layout: [version 0x01][nonce 12][tag 16][ciphertext]. Associated data: purpose string + "|" + context.
    private const byte Version = 1;
    private const int NonceSize = 12, TagSize = 16, HeaderSize = 1 + NonceSize + TagSize;
    private const string Purpose = "TAPortal.CustomerSecrets.v1";
    private readonly byte[]? key;

    public AesGcmCustomerSecretProtector(IConfiguration configuration) => key = ParseKey(configuration[ConfigKey]);

    public bool Enabled => key is not null;

    public static byte[]? ParseKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value.Trim()) is { Length: 32 } bytes ? bytes : null; }
        catch (FormatException) { return null; }
    }

    public static string GenerateKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static byte[] Aad(string context) => Encoding.UTF8.GetBytes(Purpose + "|" + context);

    public byte[] Protect(string plaintext, string context)
    {
        if (key is null) throw new InvalidOperationException($"Customer secrets are disabled: {ConfigKey} is not configured.");
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[HeaderSize + plain.Length];
        output[0] = Version;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(HeaderSize), output.AsSpan(1 + NonceSize, TagSize), Aad(context));
        return output;
    }

    public string? Unprotect(byte[] ciphertext, string context)
    {
        if (key is null || ciphertext.Length < HeaderSize || ciphertext[0] != Version) return null;
        var plain = new byte[ciphertext.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(ciphertext.AsSpan(1, NonceSize), ciphertext.AsSpan(HeaderSize), ciphertext.AsSpan(1 + NonceSize, TagSize), plain, Aad(context));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return null; }
    }
}
