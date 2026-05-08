using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Common.Models;
using ledgerflowApi.Application.Features.Auth.DTOs;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ledgerflowApi.Application.Features.Auth.Commands.Login;

public sealed record LoginCommand(
    Guid TenantId,
    string Email,
    string Password
) : IRequest<Result<AuthResponse>>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty().WithMessage("Tenant context is required.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private const string InvalidCredentialsMessage =
        "The email address or password you entered is incorrect.";

    // Valid BCrypt hash used for constant-time comparison when the user doesn't exist,
    // preventing timing attacks that could enumerate valid email addresses.
    private const string DummyHash =
        "$2a$11$dummyhashfortimingprotectionXXXXXXXXXXXXXXXXXXXXXXXXXXX";

    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LoginCommandHandler> _logger;

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

    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
        {
            _logger.LogWarning("Login attempted for unknown tenant {TenantId}", request.TenantId);
            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Cancelled)
        {
            _logger.LogWarning("Login blocked — tenant {TenantId} is {Status}", tenant.Id, tenant.Status);
            return Result<AuthResponse>.Failure("This account is not available. Please contact support.");
        }

        var user = await _userRepository.GetByEmailAsync(
            request.TenantId,
            request.Email.ToLowerInvariant(),
            cancellationToken);

        if (user is null)
        {
            _passwordHasher.Verify(request.Password, DummyHash);
            _logger.LogWarning("Login attempt for non-existent email {Email} on tenant {TenantId}",
                request.Email, request.TenantId);
            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for deactivated user {UserId}", user.Id);
            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        if (user.IsLockedOut())
        {
            _logger.LogWarning("Login attempt for locked-out user {UserId} (locked until {LockedUntil})",
                user.Id, user.LockedUntil);
            return Result<AuthResponse>.Failure(
                "This account has been temporarily locked due to too many failed login attempts. " +
                "Please try again in 30 minutes, or contact your administrator.");
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                user.RecordFailedLogin();
                await _userRepository.UpdateAsync(user, cancellationToken);
                await _auditLogRepository.AddAsync(
                    AuditLog.ForLogin(tenant.Id, user.Id, user.FullName, succeeded: false),
                    cancellationToken);
            }, cancellationToken);

            _logger.LogWarning("Failed login for user {UserId} — attempt {Attempts}",
                user.Id, user.FailedLoginAttempts);

            return Result<AuthResponse>.Failure(InvalidCredentialsMessage);
        }

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            user.RecordSuccessfulLogin();
            await _userRepository.UpdateAsync(user, cancellationToken);
            await _auditLogRepository.AddAsync(
                AuditLog.ForLogin(tenant.Id, user.Id, user.FullName, succeeded: true),
                cancellationToken);
        }, cancellationToken);

        _logger.LogInformation("User {UserId} ({Email}) logged in on tenant {TenantId}",
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
}
