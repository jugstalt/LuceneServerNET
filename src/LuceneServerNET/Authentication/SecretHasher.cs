using System;
using System.Security.Cryptography;
using System.Text;

namespace LuceneServerNET.Authentication;

public static class SecretHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int DefaultIterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string secret, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string secret, string secretHash)
    {
        if (secret is null || String.IsNullOrWhiteSpace(secretHash))
        {
            return false;
        }

        var parts = secretHash.Split('$');
        if (parts.Length != 4 ||
            !Prefix.Equals(parts[0], StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], out int iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Constant time comparison of plain text secrets. SHA256 digests are compared,
    /// so the length of the configured secret does not leak either.
    /// </summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    }
}
