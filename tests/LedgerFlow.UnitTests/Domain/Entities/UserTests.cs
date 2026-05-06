using FluentAssertions;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Exceptions;
using Xunit;

namespace LedgerFlow.UnitTests.Domain.Entities;

/// <summary>
/// Unit tests for the User aggregate.
/// Covers creation, activation/deactivation, password change,
/// role assignment, and account lockout logic.
/// </summary>
public class UserTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static User CreateUser(string email = "alice@acme.com", UserRole role = UserRole.Member)
        => User.Create(TenantId, "Alice", "Smith", email,
            "$2a$11$dummyhashXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", role);

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidInputs_ReturnsActiveUser()
    {
        var user = CreateUser();

        user.IsActive.Should().BeTrue();
        user.Email.Should().Be("alice@acme.com");
        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        user.FullName.Should().Be("Alice Smith");
        user.TenantId.Should().Be(TenantId);
        user.FailedLoginAttempts.Should().Be(0);
        user.IsLockedOut().Should().BeFalse();
    }

    [Fact]
    public void Create_NormalisesEmailToLowerCase()
    {
        var user = User.Create(TenantId, "Bob", "Jones", "BOB@ACME.COM",
            "$2a$11$dummyhashXXX");
        user.Email.Should().Be("bob@acme.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithEmptyFirstName_ThrowsDomainException(string firstName)
    {
        var act = () => User.Create(TenantId, firstName, "Smith", "a@b.com", "hash");
        act.Should().Throw<DomainException>().WithMessage("*First name*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithEmptyLastName_ThrowsDomainException(string lastName)
    {
        var act = () => User.Create(TenantId, "Alice", lastName, "a@b.com", "hash");
        act.Should().Throw<DomainException>().WithMessage("*Last name*");
    }

    [Fact]
    public void Create_WithInvalidEmail_ThrowsDomainException()
    {
        var act = () => User.Create(TenantId, "Alice", "Smith", "not-an-email", "hash");
        act.Should().Throw<DomainException>().WithMessage("*valid email*");
    }

    [Fact]
    public void Create_WithEmptyPasswordHash_ThrowsDomainException()
    {
        var act = () => User.Create(TenantId, "Alice", "Smith", "a@b.com", "");
        act.Should().Throw<DomainException>().WithMessage("*password hash*");
    }

    // ── Activate / Deactivate ─────────────────────────────────────────────────

    [Fact]
    public void Deactivate_ActiveUser_SetsIsActiveToFalse()
    {
        var user = CreateUser();
        user.Deactivate();
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_DeactivatedUser_SetsIsActiveToTrue()
    {
        var user = CreateUser();
        user.Deactivate();
        user.Reactivate();
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_AlreadyDeactivatedUser_ThrowsDomainException()
    {
        var user = CreateUser();
        user.Deactivate();
        var act = () => user.Deactivate();
        act.Should().Throw<DomainException>().WithMessage("*already deactivated*");
    }

    // ── ChangePassword ────────────────────────────────────────────────────────

    [Fact]
    public void ChangePassword_WithNewHash_UpdatesPasswordHash()
    {
        var user = CreateUser();
        const string newHash = "$2a$11$newhashXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX";

        user.UpdatePasswordHash(newHash);

        user.PasswordHash.Should().Be(newHash);
    }

    [Fact]
    public void ChangePassword_WithSameHash_ThrowsDomainException()
    {
        var user = CreateUser();
        var act = () => user.UpdatePasswordHash(user.PasswordHash);
        act.Should().Throw<DomainException>().WithMessage("*same as*");
    }

    [Fact]
    public void ChangePassword_WithEmptyHash_ThrowsDomainException()
    {
        var user = CreateUser();
        var act = () => user.UpdatePasswordHash("");
        act.Should().Throw<DomainException>();
    }

    // ── Role assignment ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Viewer)]
    public void AssignRole_ValidRole_UpdatesRole(UserRole newRole)
    {
        var user = CreateUser(role: UserRole.Member);
        user.ChangeRole(newRole);
        user.Role.Should().Be(newRole);
    }

    // ── Login / lockout ───────────────────────────────────────────────────────

    [Fact]
    public void RecordFailedLogin_IncrementsCounter()
    {
        var user = CreateUser();
        user.RecordFailedLogin();
        user.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public void RecordFailedLogin_FiveConsecutive_LocksAccount()
    {
        var user = CreateUser();
        for (var i = 0; i < 5; i++) user.RecordFailedLogin();

        user.IsLockedOut().Should().BeTrue();
        user.LockedUntil.Should().NotBeNull();
        user.LockedUntil.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void RecordSuccessfulLogin_ResetsFailedAttempts()
    {
        var user = CreateUser();
        user.RecordFailedLogin();
        user.RecordFailedLogin();

        user.RecordSuccessfulLogin();

        user.FailedLoginAttempts.Should().Be(0);
        user.IsLockedOut().Should().BeFalse();
        user.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public void IsLockedOut_WhenLockoutEndIsPast_ReturnsFalse()
    {
        // Simulate a lockout that has expired — lockout end in the past
        var user = CreateUser();
        for (var i = 0; i < 5; i++) user.RecordFailedLogin();

        // Force the lockout end to the past via reflection
        typeof(User).GetProperty("LockoutEnd")!
            .SetValue(user, DateTime.UtcNow.AddMinutes(-1));

        user.IsLockedOut().Should().BeFalse();
    }

    [Fact]
    public void UnlockAccount_LockedUser_ClearsLockout()
    {
        var user = CreateUser();
        for (var i = 0; i < 5; i++) user.RecordFailedLogin();

        user.RecordSuccessfulLogin();

        user.IsLockedOut().Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
    }

    // ── FullName ──────────────────────────────────────────────────────────────

    [Fact]
    public void FullName_CombinesFirstAndLastName()
    {
        var user = User.Create(TenantId, "John", "Doe", "j@d.com", "hash");
        user.FullName.Should().Be("John Doe");
    }
}
