namespace CleanArch.Application.Common.Interfaces;

/// <summary>
/// Abstracts password hashing so the application layer has no direct
/// dependency on BCrypt or any specific hashing library.
///
/// The infrastructure layer (PasswordHasher) owns the BCrypt work factor,
/// salt generation, and algorithm selection. If we ever upgrade to Argon2
/// we swap the implementation without touching a single handler.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Produces a salted hash of <paramref name="password"/>.
    /// Each call returns a different hash (new random salt) — this is intentional.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Returns true when <paramref name="password"/> matches <paramref name="hash"/>.
    /// Timing-safe: takes the same wall-clock time whether the password matches or not,
    /// preventing timing-oracle attacks that could leak hash bits.
    /// </summary>
    bool Verify(string password, string hash);
}
