using System.Security.Cryptography;
using System.Text;

namespace SaveLocker.Shared;

/// <summary>
/// Join codes: the short, typable credential that replaced the enrollment file (tasks/enrollment-options,
/// option B). Lives in Shared because both sides of the wire normalize the same way — the server when it
/// mints and looks a code up, the agent when a person types one into Settings or the CLI.
/// <para>
/// Crockford base32 — no I, L, O or U — so the characters that survive a human relay are exactly the
/// ones the alphabet contains; <see cref="NormalizeJoinCode"/> maps the confusables the other way for
/// people who type them anyway. Eight characters is 40 bits: too little to be safe on its own, which
/// is why the server throttles every lookup (JoinThrottle) rather than relying on the code's length.
/// </para>
/// </summary>
public static class JoinCodes
{
    public const int Length = 8;
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A fresh code (8 Crockford characters, ~40 bits of entropy).</summary>
    public static string New() =>
        string.Concat(RandomNumberGenerator.GetItems<char>(Crockford, Length));

    /// <summary>
    /// Canonical form of a user-supplied code: uppercase, separators dropped, and the confusables
    /// people actually type mapped in (I/L → 1, O → 0). Returns null when anything outside the
    /// alphabet remains — including the excluded U — so a typo is refused before it is sent anywhere.
    /// </summary>
    public static string? NormalizeJoinCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var sb = new StringBuilder(Length);
        foreach (var ch in raw.Trim().ToUpperInvariant())
        {
            var c = ch switch { 'I' or 'L' => '1', 'O' => '0', _ => ch };
            if (char.IsWhiteSpace(c) || c is '-' or '_' or '.') continue; // separators people add
            if (!Crockford.Contains(c)) return null;
            sb.Append(c);
        }
        return sb.Length == Length ? sb.ToString() : null;
    }

    /// <summary>The code as it is shown to a person: <c>K7QM-4H2X</c>, one dash after four.</summary>
    public static string FormatJoinCode(string code) =>
        code.Length == Length ? code[..4] + "-" + code[4..] : code;
}
