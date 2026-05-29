using ledgerflowApi.Application.Common.Interfaces;
using BCrypt.Net;

namespace ledgerflowApi.Infrastructure.Identity;

/// <summary>
/// BCrypt implementation of IPasswordHasher.
///
/// Why BCrypt over SHA-256/PBKDF2:
/// - BCrypt is designed to be slow (adjustable work factor) — exactly what you want
///   for password hashing to resist offline brute-force attacks.
/// - The work factor (cost) is embedded in the hash string itself, so stored hashes
///   can be transparently migrated to a higher work factor over time without invalidating
///   existing user passwords (you re-hash on their next successful login).
/// - BCrypt auto-generates a random salt per hash — no separate salt storage needed.
///
/// Work factor 11:
/// - At work factor 11, BCrypt takes ~150-300ms on typical server hardware.
/// - OWASP recommends a minimum of 10 (2023 guidance). 11 gives headroom.
/// - Factor 12 would double that to ~600ms — acceptable for a login endpoint
///   but burdensome on registration load tests. 11 is a pragmatic choice.
///
/// BCrypt 72-byte truncation:
/// The BCrypt spec truncates passwords at 72 bytes. We enforce MaximumLength(72)
/// in the validator rather than silently truncating, so users with a 100-character
/// password know their full password is being used rather than the first 72 chars.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    /// <inheritdoc/>
    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        // BCrypt.HashPassword generates a cryptographically random 128-bit salt
        // internally. Each call produces a completely different hash string
        // even for the same input — this is the desired behaviour.
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);
    }

    /// <inheritdoc/>
    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;

        try
        {
            // BCrypt.Verify extracts the salt and work factor from the stored hash string
            // and recomputes the hash in constant time, then does a constant-time comparison.
            // This prevents timing-oracle attacks that could distinguish "hash computed but wrong"
            // from "hash not even computed" when an attacker tests different passwords.
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            // BCrypt.Verify throws on a malformed hash (wrong format/version).
            // Treat that as a verification failure rather than surfacing an exception.
            return false;
        }
    }
}
