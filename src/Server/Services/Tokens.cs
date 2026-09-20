using System.Security.Cryptography;

namespace SaveLocker.Server.Services;

/// <summary>Generates per-machine API keys and hashes them for storage.</summary>
public static class Tokens
{
    /// <summary>Create a new random API key (URL-safe base64, 256 bits).</summary>
    public static string NewApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>Stable hash of an API key for storage/lookup.</summary>
    public static string Hash(string apiKey)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // PBKDF2 parameters are part of the ON-DISK format, so they travel WITH the hash:
    //   v2:{iterations}:{salt}:{hash}   — written today; the count lives in the string, so raising
    //                                     it later needs no new format and no migration
    //   v1:{salt}:{hash}                — written before v2; the count was a compile-time constant
    //                                     (100,000), which is why a new tag was needed to move it.
    // Both still verify. A v1 (or under-strength v2) hash is re-written at the next successful
    // sign-in — see NeedsRehash / SettingsService.UpgradeAdminPasswordHashAsync — because that is
    // the only moment the plaintext is in hand.
    /// <summary>OWASP's current PBKDF2-HMAC-SHA256 minimum (Password Storage Cheat Sheet).</summary>
    public const int CurrentIterations = 600_000;
    private const int LegacyIterations = 100_000;
    // A stored hash is trusted input, but a corrupted or hand-edited one must not be able to pin a
    // core for minutes on every request.
    private const int MaxAcceptedIterations = 5_000_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// PBKDF2-SHA256 hash of a user-chosen password, suitable for storage.
    /// Uses the static <c>Pbkdf2</c> API — the <c>Rfc2898DeriveBytes</c> constructors are obsolete
    /// (SYSLIB0060). Same algorithm, same inputs, same bytes: hashes written by the old constructor
    /// still verify, which <c>tests/verify-password-compat.ps1</c> proves end to end.
    /// </summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, CurrentIterations, Algorithm, HashBytes);
        return $"v2:{CurrentIterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>Returns true when <paramref name="password"/> matches a hash produced by <see cref="HashPassword"/>
    /// (or by the older v1 writer).</summary>
    public static bool VerifyPassword(string password, string storedHash)
    {
        if (!TryParse(storedHash, out var iterations, out var saltB64, out var hashB64)) return false;
        try
        {
            var salt = Convert.FromBase64String(saltB64);
            var expected = Convert.FromBase64String(hashB64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    /// <summary>True when a verified hash should be re-written at today's strength: any v1 hash, or
    /// a v2 hash written with fewer iterations than <see cref="CurrentIterations"/>.</summary>
    public static bool NeedsRehash(string storedHash) =>
        !TryParse(storedHash, out var iterations, out _, out _) || iterations < CurrentIterations;

    private static bool TryParse(string stored, out int iterations, out string salt, out string hash)
    {
        iterations = 0; salt = hash = "";
        var parts = stored.Split(':');
        switch (parts[0])
        {
            case "v1" when parts.Length == 3:
                (iterations, salt, hash) = (LegacyIterations, parts[1], parts[2]);
                return true;
            case "v2" when parts.Length == 4
                           && int.TryParse(parts[1], out iterations)
                           && iterations is > 0 and <= MaxAcceptedIterations:
                (salt, hash) = (parts[2], parts[3]);
                return true;
            default:
                return false;
        }
    }
}
