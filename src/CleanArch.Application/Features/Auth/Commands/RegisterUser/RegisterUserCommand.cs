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

namespace CleanArch.Application.Features.Auth.Commands.RegisterUser;

// ── Command ──────────────────────────────────────────────────────────────────

/// <summary>
/// Creates a new user account within a tenant. Admin-only operation.
///
/// CallerUserId is the authenticated Admin performing the action,
/// used for the audit log entry.
/// </summary>
public sealed record RegisterUserCommand(
    Guid TenantId,
    Guid CallerUserId,
    string CallerUserName,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    UserRole Role
) : IRequest<Result<AuthResponse>>;

// ── Validator ────────────────────────────────────────────────────────────────

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("Tenant context is required.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.")
            .Matches(@"^[\p{L}\p{M}'\- ]+$")
            .WithMessage("First name contains invalid characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.")
            .Matches(@"^[\p{L}\p{M}'\- ]+$")
            .WithMessage("Last name contains invalid characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(72).WithMessage("Password cannot exceed 72 characters.")
            // BCrypt silently truncates at 72 bytes — we reject rather than silently truncate.
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one digit.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");

        RuleFor(x => x.Role)
            .Must(r => r != UserRole.SuperAdmin)
            .WithMessage("SuperAdmin role cannot be assigned through user registration.");
    }
}

// ── Handler ──────────────────────────────────────────────────────────────────

/// <summary>
/// Full registration flow:
///   1. Verify the tenant exists and can accept new users.
///   2. Check the email isn't already registered within this tenant.
///   3. Hash the password (BCrypt, work factor 11).
///   4. Create the User aggregate via the factory method (which fires UserCreatedEvent).
///   5. Persist user + audit log in a single transaction.
///   6. Issue tokens immediately so the caller can use the new account without a second login.
///
/// Why return tokens on registration?
/// The API is used by an Admin creating accounts for colleagues — returning a token
/// is not useful here (the Admin isn't logging in as the new user). But consistent with
/// the AuthResponse shape, we return a token for the NEW user so the Admin can
/// optionally hand the token off to the new user in an onboarding flow.
/// The new user will always need to do a proper login on their own device anyway.
/// </summary>
public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<AuthResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RegisterUserCommandHandler> _logger;

    public RegisterUserCommandHandler(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        IAuditLogRepository auditLogRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        ILogger<RegisterUserCommandHandler> logger)
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
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Verify tenant ─────────────────────────────────────────────
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            throw new NotFoundException(nameof(Tenant), request.TenantId);

        if (tenant.Status is TenantStatus.Cancelled)
            throw new DomainException(
                "Cannot register users on a cancelled tenant.");

        // ── Step 2: Check email uniqueness within tenant ──────────────────────
        var emailTaken = await _userRepository.EmailExistsAsync(
            request.TenantId,
            request.Email.ToLowerInvariant(),
            cancellationToken);

        if (emailTaken)
            return Result<AuthResponse>.Failure(
                $"The email address '{request.Email}' is already registered on this account.");

        // ── Step 3: Hash the password ─────────────────────────────────────────
        // BCrypt work factor 11 (~300ms on modern hardware) — slow enough to make
        // offline brute-force attacks impractical, fast enough for a registration endpoint.
        var passwordHash = _passwordHasher.Hash(request.Password);

        // ── Step 4: Build the domain object ───────────────────────────────────
        var user = User.Create(
            tenantId: request.TenantId,
            firstName: request.FirstName,
            lastName: request.LastName,
            email: request.Email,
            passwordHash: passwordHash,
            role: request.Role);

        // ── Step 5: Persist atomically ────────────────────────────────────────
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _userRepository.AddAsync(user, cancellationToken);

            var audit = AuditLog.Create(
                tenantId: request.TenantId,
                action: AuditAction.Created,
                entityType: nameof(User),
                entityId: user.Id,
                description: $"User '{user.Email}' registered with role '{user.Role}' by '{request.CallerUserName}'.",
                userId: request.CallerUserId,
                userDisplayName: request.CallerUserName,
                stateAfter: System.Text.Json.JsonSerializer.Serialize(new
                {
                    userId = user.Id,
                    email = user.Email,
                    role = user.Role.ToString(),
                    tenantId = request.TenantId
                }));

            await _auditLogRepository.AddAsync(audit, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "User {UserId} ({Email}) registered by {CallerUserId} on tenant {TenantId} with role {Role}",
            user.Id, user.Email, request.CallerUserId, request.TenantId, request.Role);

        // ── Step 6: Issue tokens for the new user ─────────────────────────────
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

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
}
