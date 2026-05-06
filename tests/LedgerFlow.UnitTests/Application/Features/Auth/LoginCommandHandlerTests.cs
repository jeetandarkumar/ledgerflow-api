using FluentAssertions;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Features.Auth.Commands.Login;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LedgerFlow.UnitTests.Application.Features.Auth;

/// <summary>
/// Unit tests for LoginCommandHandler.
/// Mocks all external dependencies so tests run fast and in isolation.
/// The handler has complex branching logic (lockout, tenant status, timing
/// protection), so each path gets its own test.
/// </summary>
public class LoginCommandHandlerTests
{
    // ── Shared mocks and SUT ──────────────────────────────────────────────────

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILogger<LoginCommandHandler>> _logger = new();

    private LoginCommandHandler CreateHandler()
        => new(_userRepo.Object, _tenantRepo.Object, _auditRepo.Object,
               _hasher.Object, _tokenService.Object, _unitOfWork.Object, _logger.Object);

    // ── Test data factories ───────────────────────────────────────────────────

    private static Tenant CreateActiveTenant()
    {
        // Use reflection to build Tenant — its constructor is private (EF Core pattern)
        var tenant = (Tenant)Activator.CreateInstance(typeof(Tenant), nonPublic: true)!;
        typeof(Tenant).GetProperty("Id")!.SetValue(tenant, Guid.NewGuid());
        typeof(Tenant).GetProperty("Name")!.SetValue(tenant, "Acme Corp");
        typeof(Tenant).GetProperty("Status")!.SetValue(tenant, TenantStatus.Active);
        return tenant;
    }

    private static User CreateActiveUser(Guid tenantId, string email = "alice@acme.com")
    {
        return User.Create(
            tenantId: tenantId,
            firstName: "Alice",
            lastName: "Smith",
            email: email,
            passwordHash: "$2a$11$validhashXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
            role: UserRole.Member);
    }

    private static LoginCommand MakeCommand(Guid tenantId, string email = "alice@acme.com")
        => new(tenantId, email, "SecurePass123!");

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsSuccessWithTokens()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        var user = CreateActiveUser(tenant.Id);
        var command = MakeCommand(tenant.Id);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userRepo.Setup(r => r.GetByEmailAsync(tenant.Id, command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(command.Password, user.PasswordHash)).Returns(true);
        _tokenService.Setup(t => t.GenerateAccessToken(user)).Returns("access-token");
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("refresh-token");
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<Task>, CancellationToken>((fn, _) => fn().GetAwaiter().GetResult());

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.AccessToken.Should().Be("access-token");
        result.Data!.RefreshToken.Should().Be("refresh-token");
        result.Data!.User.Email.Should().Be(user.Email);
    }

    // ── Tenant validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownTenantId_ReturnsFailureWithGenericMessage()
    {
        // Arrange
        var command = MakeCommand(Guid.NewGuid());
        _tenantRepo.Setup(r => r.GetByIdAsync(command.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — must be generic to prevent tenant enumeration
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("incorrect"));
        // Password verify must NOT be called (no timing risk here — tenant not found)
        _hasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Cancelled)]
    public async Task Handle_SuspendedOrCancelledTenant_ReturnsFailure(TenantStatus status)
    {
        // Arrange
        var tenant = CreateActiveTenant();
        typeof(Tenant).GetProperty("Status")!.SetValue(tenant, status);
        var command = MakeCommand(tenant.Id);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not available") || e.Contains("contact support"));
    }

    // ── User validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_UserNotFound_PerformsDummyHashAndReturnsGenericError()
    {
        // Arrange — timing protection: even when user not found, Verify must be called
        var tenant = CreateActiveTenant();
        var command = MakeCommand(tenant.Id, "notexist@acme.com");

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userRepo.Setup(r => r.GetByEmailAsync(tenant.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("incorrect"));
        // Timing protection: Verify must still be called even though user doesn't exist
        _hasher.Verify(h => h.Verify(command.Password, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DeactivatedUser_ReturnsGenericFailure()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        var user = CreateActiveUser(tenant.Id);
        user.Deactivate(); // deactivate the user
        var command = MakeCommand(tenant.Id);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userRepo.Setup(r => r.GetByEmailAsync(tenant.Id, command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — generic message so attacker can't enumerate deactivated accounts
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("incorrect"));
    }

    // ── Password verification ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WrongPassword_IncrementsFailedAttemptsAndReturnsFailure()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        var user = CreateActiveUser(tenant.Id);
        var command = MakeCommand(tenant.Id);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userRepo.Setup(r => r.GetByEmailAsync(tenant.Id, command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(command.Password, user.PasswordHash)).Returns(false);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<Task>, CancellationToken>((fn, _) => fn().GetAwaiter().GetResult());

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("incorrect"));
        // User's failed attempt counter should have been incremented and saved
        _userRepo.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LockedOutUser_ReturnsLockoutMessage()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        var user = CreateActiveUser(tenant.Id);
        // Lock the user by recording 5 failed attempts
        for (var i = 0; i < 5; i++) user.RecordFailedLogin();

        var command = MakeCommand(tenant.Id);

        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userRepo.Setup(r => r.GetByEmailAsync(tenant.Id, command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — lockout message reveals it's a lockout (user already knows their account)
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("locked"));
        // Password should NOT be verified when account is locked
        _hasher.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
