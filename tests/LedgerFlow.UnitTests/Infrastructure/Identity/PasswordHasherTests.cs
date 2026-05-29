using FluentAssertions;
using ledgerflowApi.Infrastructure.Identity;
using Xunit;

namespace LedgerFlow.UnitTests.Infrastructure.Identity;

/// <summary>
/// Tests for BCrypt PasswordHasher.
/// These run slower than other unit tests (~150ms each due to BCrypt work factor),
/// but that's intentional — BCrypt is supposed to be slow.
/// Keep this collection small and focused.
/// </summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    // ── Hash ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Hash_ValidPassword_ReturnsBCryptFormattedString()
    {
        var hash = _hasher.Hash("MySecurePassword123!");
        hash.Should().StartWith("$2a$");
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        // BCrypt generates a random salt each time
        var hash1 = _hasher.Hash("SamePassword");
        var hash2 = _hasher.Hash("SamePassword");
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Hash_EmptyPassword_ThrowsArgumentException()
    {
        var act = () => _hasher.Hash("");
        act.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    // ── Verify ────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var password = "CorrectHorseBatteryStaple";
        var hash = _hasher.Hash(password);

        _hasher.Verify(password, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("RealPassword");
        _hasher.Verify("WrongPassword", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("SomePassword");
        _hasher.Verify("", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyHash_ReturnsFalse()
    {
        _hasher.Verify("SomePassword", "").Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalseNotThrow()
    {
        // Malformed hashes should fail gracefully, not throw
        _hasher.Verify("SomePassword", "not-a-bcrypt-hash").Should().BeFalse();
    }

    [Fact]
    public void Verify_CaseSensitive_ReturnsFalseForWrongCase()
    {
        var hash = _hasher.Hash("Password");
        _hasher.Verify("password", hash).Should().BeFalse();
    }
}
