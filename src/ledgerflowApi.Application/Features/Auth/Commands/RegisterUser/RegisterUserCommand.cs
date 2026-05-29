using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Common.Models;
using ledgerflowApi.Application.Features.Auth.DTOs;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ledgerflowApi.Application.Features.Auth.Commands.RegisterUser;

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

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100)
            .Matches(@"^[\p{L}\p{M}'\- ]+$").WithMessage("First name contains invalid characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100)
            .Matches(@"^[\p{L}\p{M}'\- ]+$").WithMessage("Last name contains invalid characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(72).WithMessage("Password cannot exceed 72 characters.")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"\d").WithMessage("Password must contain at least one digit.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");

        RuleFor(x => x.Role)
            .Must(r => r != UserRole.SuperAdmin)
            .WithMessage("SuperAdmin role cannot be assigned through user registration.");
    }
}

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

    public async Task<Result<AuthResponse>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            throw new NotFoundException(nameof(Tenant), request.TenantId);

        if (tenant.Status is TenantStatus.Cancelled)
            throw new DomainException("Cannot register users on a cancelled tenant.");

        var emailTaken = await _userRepository.EmailExistsAsync(
            request.TenantId,
            request.Email.ToLowerInvariant(),
            cancellationToken);

        if (emailTaken)
            return Result<AuthResponse>.Failure(
                $"The email address '{request.Email}' is already registered on this account.");

        var passwordHash = _passwordHasher.Hash(request.Password);

        var user = User.Create(
            tenantId: request.TenantId,
            firstName: request.FirstName,
            lastName: request.LastName,
            email: request.Email,
            passwordHash: passwordHash,
            role: request.Role);

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

        _logger.LogInformation("User {UserId} ({Email}) registered by {CallerUserId} on tenant {TenantId} with role {Role}",
            user.Id, user.Email, request.CallerUserId, request.TenantId, request.Role);

        return Result<AuthResponse>.Success(new AuthResponse
        {
            AccessToken = _tokenService.GenerateAccessToken(user),
            RefreshToken = _tokenService.GenerateRefreshToken(),
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
