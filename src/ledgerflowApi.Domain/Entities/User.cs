using ledgerflowApi.Domain.Common;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Events;
using ledgerflowApi.Domain.Exceptions;

namespace ledgerflowApi.Domain.Entities;

/// <summary>
/// A human user of the platform, always scoped to a single Tenant.
///
/// Design rules:
/// - Email is the unique identifier within a tenant (two tenants can have
///   the same email — it's not globally unique by design).
/// - Password hashing is an infrastructure concern. The domain stores
///   only the hash, never the plaintext.
/// - A deactivated user cannot log in but their historical records remain
///   intact — financial audit trails must not be broken by deleting users.
/// - Role escalation to SuperAdmin is blocked at the domain level.
///   That role is only assignable via seeding, not through user flows.
/// </summary>
public class User : TenantEntity
{
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Bcrypt/Argon2 hash of the password. The domain just stores it.
    /// The infrastructure layer (IPasswordHasher) is responsible for
    /// generating and verifying hashes.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; } = UserRole.Member;
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Number of consecutive failed login attempts.
    /// Account is locked after 5 failures to prevent brute-force attacks.
    /// </summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>When the account was locked. Null means not locked.</summary>
    public DateTime? LockedUntil { get; private set; }

    /// <summary>When this user last logged in successfully.</summary>
    public DateTime? LastLoginAt { get; private set; }

    // Navigation
    public Tenant Tenant { get; private set; } = null!;

    private User() { } // EF Core

    public static User Create(
        Guid tenantId,
        string firstName,
        string lastName,
        string email,
        string passwordHash,
        UserRole role = UserRole.Member)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        ValidateEmail(email);

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash is required.");

        // SuperAdmin cannot be assigned through normal user creation flows.
        if (role == UserRole.SuperAdmin)
            throw new DomainException("SuperAdmin role cannot be assigned through user creation. Use the platform seeding process.");

        var user = new User
        {
            TenantId = tenantId,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Email = email.ToLowerInvariant().Trim(),
            PasswordHash = passwordHash,
            Role = role
        };

        user.AddDomainEvent(new UserCreatedEvent(user.Id, user.TenantId, user.Email));
        return user;
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    public void UpdateProfile(string firstName, string lastName)
    {
        ValidateName(firstName, nameof(firstName));
        ValidateName(lastName, nameof(lastName));
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        SetUpdatedAt();
    }

    public void UpdateEmail(string email)
    {
        ValidateEmail(email);
        Email = email.ToLowerInvariant().Trim();
        SetUpdatedAt();
    }

    public void UpdatePasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new DomainException("Password hash is required.");
        PasswordHash = newHash;
        SetUpdatedAt();
    }

    // ── Role ──────────────────────────────────────────────────────────────────

    public void ChangeRole(UserRole newRole)
    {
        if (newRole == UserRole.SuperAdmin)
            throw new DomainException("SuperAdmin role cannot be assigned through this operation.");
        Role = newRole;
        SetUpdatedAt();
    }

    // ── Account State ─────────────────────────────────────────────────────────

    public void Deactivate()
    {
        if (!IsActive) return; // idempotent
        IsActive = false;
        SetUpdatedAt();
        AddDomainEvent(new UserDeactivatedEvent(Id, TenantId));
    }

    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
        SetUpdatedAt();
    }

    // ── Login / Security ──────────────────────────────────────────────────────

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Returns true if this user is currently locked out.
    /// Lock expires automatically after LockoutDuration — we don't need a background job.
    /// </summary>
    public bool IsLockedOut() =>
        LockedUntil.HasValue && LockedUntil.Value > DateTime.UtcNow;

    /// <summary>Records a successful login: clears failure counter and updates last login time.</summary>
    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
        LastLoginAt = DateTime.UtcNow;
        SetUpdatedAt();
    }

    /// <summary>
    /// Records a failed login attempt.
    /// After MaxFailedAttempts consecutive failures, the account is locked.
    /// </summary>
    public void RecordFailedLogin()
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedAttempts)
        {
            LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
            AddDomainEvent(new UserLockedOutEvent(Id, TenantId));
        }

        SetUpdatedAt();
    }

    public string FullName => $"{FirstName} {LastName}";

    // ── Guards ────────────────────────────────────────────────────────────────

    private static void ValidateName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{fieldName} is required.");
        if (value.Length > 100)
            throw new DomainException($"{fieldName} cannot exceed 100 characters.");
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");
        if (email.Length > 256)
            throw new DomainException("Email cannot exceed 256 characters.");
        if (!email.Contains('@'))
            throw new DomainException("Email must be a valid email address.");
    }
}
