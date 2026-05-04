using ledgerflowApi.Domain.Common;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Events;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.ValueObjects;

namespace ledgerflowApi.Domain.Entities;

/// <summary>
/// The root of the multi-tenancy hierarchy.
/// Every other financial record hangs off a Tenant.
///
/// Design rules:
/// - A Tenant can have many Users, Invoices, and Payments.
/// - A Suspended or Cancelled tenant cannot create new Invoices.
/// - Slug is a URL-safe identifier (e.g. "acme-corp") used in subdomains/paths.
///   It is set once at creation and never changes (rename-safe via the name field).
/// </summary>
public class Tenant : BaseEntity
{
    private readonly List<User> _users = [];
    private readonly List<Invoice> _invoices = [];

    /// <summary>Display name of the company or individual.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Immutable URL-safe slug, e.g. "acme-corp".
    /// Set once at creation. Changing it would break customer bookmarks/subdomains.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Primary billing contact email for the tenant account.</summary>
    public string BillingEmail { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; } = TenantStatus.Trial;

    /// <summary>Default currency for invoices created within this tenant (ISO 4217).</summary>
    public string DefaultCurrency { get; private set; } = "USD";

    /// <summary>
    /// Billing address used as the default "from" address on invoices.
    /// Stored as a value object so it's a snapshot — changing the address
    /// only affects future invoices, not historical ones.
    /// </summary>
    public Address? BillingAddress { get; private set; }

    /// <summary>When the trial period expires. Null means no trial (direct subscription).</summary>
    public DateTime? TrialEndsAt { get; private set; }

    /// <summary>
    /// When the tenant account was cancelled (if applicable).
    /// Used to calculate the 90-day data retention window.
    /// </summary>
    public DateTime? CancelledAt { get; private set; }

    // Navigation properties — private setters so EF Core can populate them
    // but application code must go through methods on this aggregate.
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();
    public IReadOnlyCollection<Invoice> Invoices => _invoices.AsReadOnly();

    private Tenant() { } // EF Core

    /// <summary>
    /// Creates a new tenant with a trial subscription starting immediately.
    /// </summary>
    public static Tenant Create(
        string name,
        string slug,
        string billingEmail,
        string defaultCurrency = "USD",
        int trialDays = 14)
    {
        ValidateName(name);
        ValidateSlug(slug);
        ValidateEmail(billingEmail);
        ValidateCurrency(defaultCurrency);

        if (trialDays < 0)
            throw new DomainException("Trial period cannot be negative.");

        var tenant = new Tenant
        {
            Name = name.Trim(),
            Slug = slug.ToLowerInvariant().Trim(),
            BillingEmail = billingEmail.ToLowerInvariant().Trim(),
            DefaultCurrency = defaultCurrency.ToUpperInvariant(),
            Status = TenantStatus.Trial,
            TrialEndsAt = trialDays > 0 ? DateTime.UtcNow.AddDays(trialDays) : null
        };

        tenant.AddDomainEvent(new TenantCreatedEvent(tenant.Id, tenant.Name));
        return tenant;
    }

    // ── Mutations ────────────────────────────────────────────────────────────

    public void UpdateDetails(string name, string billingEmail, string defaultCurrency)
    {
        ValidateName(name);
        ValidateEmail(billingEmail);
        ValidateCurrency(defaultCurrency);

        Name = name.Trim();
        BillingEmail = billingEmail.ToLowerInvariant().Trim();
        DefaultCurrency = defaultCurrency.ToUpperInvariant();
        SetUpdatedAt();
    }

    public void SetBillingAddress(Address address)
    {
        BillingAddress = address ?? throw new ArgumentNullException(nameof(address));
        SetUpdatedAt();
    }

    /// <summary>Activates a trial tenant after successful payment setup.</summary>
    public void Activate()
    {
        if (Status == TenantStatus.Active)
            return; // idempotent

        if (Status == TenantStatus.Cancelled)
            throw new DomainException("A cancelled tenant cannot be reactivated. Create a new account.");

        Status = TenantStatus.Active;
        TrialEndsAt = null;
        SetUpdatedAt();
        AddDomainEvent(new TenantActivatedEvent(Id));
    }

    /// <summary>Suspends the tenant, usually due to failed payment.</summary>
    public void Suspend(string reason)
    {
        if (Status == TenantStatus.Cancelled)
            throw new DomainException("A cancelled tenant cannot be suspended.");

        if (Status == TenantStatus.Suspended)
            return; // idempotent

        Status = TenantStatus.Suspended;
        SetUpdatedAt();
        AddDomainEvent(new TenantSuspendedEvent(Id, reason));
    }

    /// <summary>
    /// Cancels the tenant account. This is irreversible.
    /// Data retention clock starts from this timestamp.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == TenantStatus.Cancelled)
            return; // idempotent

        Status = TenantStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        SetUpdatedAt();
        AddDomainEvent(new TenantCancelledEvent(Id, reason));
    }

    /// <summary>
    /// Returns true if this tenant is allowed to create new invoices.
    /// Suspended and cancelled tenants are blocked — they must resolve
    /// their billing issue or reactivate first.
    /// </summary>
    public bool CanCreateInvoices() =>
        Status is TenantStatus.Trial or TenantStatus.Active;

    // ── Guards ───────────────────────────────────────────────────────────────

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Tenant name is required.");
        if (name.Length > 200)
            throw new DomainException("Tenant name cannot exceed 200 characters.");
    }

    private static void ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Tenant slug is required.");
        if (slug.Length > 63)
            throw new DomainException("Tenant slug cannot exceed 63 characters.");

        // Only lowercase letters, numbers, and hyphens — safe for DNS/URLs
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            throw new DomainException(
                "Tenant slug must contain only lowercase letters, numbers, and hyphens, and cannot start or end with a hyphen.");
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Billing email is required.");
        if (!email.Contains('@'))
            throw new DomainException("Billing email must be a valid email address.");
    }

    private static void ValidateCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException("Default currency must be a 3-character ISO 4217 code.");
    }
}
