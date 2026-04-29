using CleanArch.Application.Common.Exceptions;
using CleanArch.Application.Common.Interfaces;
using CleanArch.Application.Common.Models;
using CleanArch.Application.Features.Auth.DTOs;
using CleanArch.Domain.Entities;
using CleanArch.Domain.Enums;
using CleanArch.Domain.Exceptions;
using CleanArch.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanArch.Application.Features.Auth.Commands.Login;

// ── Command ──────────────────────────────────────────────────────────────────

/// <summary>
/// Authenticates a user within a specific tenant and returns a JWT.
///
/// TenantId is explicit in the command (resolved from the login request's email + slug
/// or from a subdomain header). A user with the same email in two different tenants
/// must log in separately — their credentials are completely independent.
/// </summary>
public sealed record LoginCommand(
    Guid TenantId,
    string Email,
    string Password
) : IRequest<Result<AuthResponse>>;

// ── Validator ────────────────────────────────────────────────────────────────

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("Tenant context is required.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            // Deliberately no MaxLength here — a very long password from a password manager
            // should be accepted. BCrypt caps at 72 bytes internally; the hasher handles that.
            .MinimumLength(1).WithMessage("Password is required.");
    }
}

// ── Handler ──────────────────────────────────────────────────────────────────

/// <summary>
/// Full login flow:
///   1. Look up the user by email within the tenant.
///   2. Enforce lockout — reject immediately if the account is locked.
///   3. Verify the submitted password against the stored BCrypt hash.
///   4. On failure: increment failed-attempt counter, potentially lock the account, save, return generic error.
///   5. On success: reset failure counter, update LastLoginAt, issue tokens, write audit entry, save.
///
/// Security notes:
/// - The error message on bad credentials is intentionally generic ("invalid credentials")
///   whether the email doesn't exist OR the password is wrong. Never tell the attacker
///   which half is incorrect — that leaks account enumeration information.
/// - Audit entries are written for both success AND failure so security teams can
///   detect credential-stuffing attacks from the audit log.
/// - Both the DB write (user state update) and the audit log write are in one transaction
///   so a crash between them can't leave the state inconsistent.
/// </summary>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LoginCommandHandler> _logger;

    // Returned for BOTH "user not found" and "wrong password" — never distinguish them.
    private const string InvalidCredentialsMessage =
        "The email address or password you entered is incorrect.";

    public LoginCommandHandler(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        IAuditLogRepository auditLogRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        ILogger<LoginCommandHandler> logger)
    {
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _auditLogRepository = auditLogRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Load tenant ───────────────────────────────────────────────
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
        {
            // Don't leak whether the tenant exists.
            _logger.LogWarning(
                "Login attempted for unknown tenant {TenantId}", request.TenantId);
            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        // Suspended/cancelled tenants cannot log in at all.
        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
        {
            _logger.LogWarning(
                "Login blocked — tenant {TenantId} is {Status}", tenant.Id, tenant.Status);
            return Result<AuthResponse>.Failure(
                "This account is not available. Please contact support.");
        }

        // ── Step 2: Look up the user (email is tenant-scoped) ─────────────────
        var user = await _userRepository.GetByEmailAsync(
            request.TenantId,
            request.Email.ToLowerInvariant(),
            cancellationToken);

        if (user is null)
        {
            // User doesn't exist — still do a dummy BCrypt comparison to maintain
            // constant-time behaviour and prevent timing-oracle enumeration.
            _passwordHasher.Verify(request.Password, DummyHash);
            _logger.LogWarning(
                "Login attempt for non-existent email {Email} on tenant {TenantId}",
                request.Email, request.TenantId);
            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        // ── Step 3: Check account state ───────────────────────────────────────
        if (!user.IsActive)
        {
            _logger.LogWarning(
                "Login attempt for deactivated user {UserId}", user.Id);
            // Same generic message — don't reveal the account is deactivated.
            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        if (user.IsLockedOut())
        {
            _logger.LogWarning(
                "Login attempt for locked-out user {UserId} (locked until {LockedUntil})",
                user.Id, user.LockedUntil);
            return Result<AuthResponse>.Failure(
                "This account has been temporarily locked due to too many failed login attempts. " +
                "Please try again in 30 minutes, or contact your administrator.");
        }

        // ── Step 4: Verify password ───────────────────────────────────────────
        var passwordValid = _passwordHasher.Verify(request.Password, user.PasswordHash);

        if (!passwordValid)
        {
            // Record failure on the domain object and persist atomically with audit log.
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                user.RecordFailedLogin(); // May set LockedUntil if MaxAttempts reached
                await _userRepository.UpdateAsync(user, cancellationToken);

                var failureAudit = AuditLog.ForLogin(
                    tenantId: tenant.Id,
                    userId: user.Id,
                    userDisplayName: user.FullName,
                    succeeded: false);
                await _auditLogRepository.AddAsync(failureAudit, cancellationToken);
            }, cancellationToken);

            _logger.LogWarning(
                "Failed login for user {UserId} — attempt {Attempts}",
                user.Id, user.FailedLoginAttempts);

            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        // ── Step 5: Issue tokens and persist success state ────────────────────
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            user.RecordSuccessfulLogin();
            await _userRepository.UpdateAsync(user, cancellationToken);

            var successAudit = AuditLog.ForLogin(
                tenantId: tenant.Id,
                userId: user.Id,
                userDisplayName: user.FullName,
                succeeded: true);
            await _auditLogRepository.AddAsync(successAudit, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "User {UserId} ({Email}) logged in successfully on tenant {TenantId}",
            user.Id, user.Email, tenant.Id);

        return Result<AuthResponse>.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            User = new UserAuthInfo
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString(),
                TenantId = tenant.Id,
                TenantName = tenant.Name
            }
        });
    }

    // A valid BCrypt hash used when the user is not found, so the response
    // time is indistinguishable from a wrong-password response (both call Verify).
    // The actual content is irrelevant — Verify will return false — but the
    // call must happen so timing cannot reveal whether the email exists.
    private const string DummyHash =
        "$2a$11$dummyhashfortimingprotectionXXXXXXXXXXXXXXXXXXXXXXXXXXX";
}
