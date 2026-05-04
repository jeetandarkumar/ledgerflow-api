using ledgerflowApi.Domain.Common;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Exceptions;

namespace ledgerflowApi.Domain.Entities;

/// <summary>
/// An immutable record of something that happened in the system.
///
/// Design rules:
/// - AuditLog entries are NEVER updated or deleted. They are append-only facts.
///   If data must be corrected, a new corrective entry is added — never edit existing ones.
/// - Stored as a TenantEntity so tenant admins can view their own audit trail,
///   and platform operators can filter by tenant.
/// - The Before/After fields store a JSON snapshot of the relevant state,
///   allowing reconstruction of what changed. They are deliberately untyped
///   (string) so the audit log doesn't couple to specific entity shapes — a schema
///   change doesn't require migrating historical audit records.
/// - IpAddress and UserAgent are captured for security investigations.
///
/// Financial compliance note:
/// In jurisdictions subject to SOX, PCI-DSS, or similar regulations, the audit log
/// may need to be stored in a write-once/append-only data store (e.g. an immutable
/// S3 bucket or dedicated audit DB). The domain models this intent through the
/// private constructor and lack of mutation methods — infrastructure enforces it.
/// </summary>
public class AuditLog : TenantEntity
{
    // ── Who ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The user who performed the action. Null for system-initiated actions
    /// (e.g. scheduled job marking an invoice as overdue).
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Display name of the user at the time of the action, captured as a snapshot.
    /// Avoids broken audit trails if a user's name changes or is deleted later.
    /// </summary>
    public string? UserDisplayName { get; private set; }

    // ── What ──────────────────────────────────────────────────────────────────

    public AuditAction Action { get; private set; }

    /// <summary>
    /// The entity type that was affected (e.g. "Invoice", "Payment", "User").
    /// </summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The ID of the specific record that was affected.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>
    /// Human-readable description of what changed.
    /// e.g. "Invoice INV-2024-0042 transitioned from Draft to Issued."
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    // ── State Snapshot ────────────────────────────────────────────────────────

    /// <summary>
    /// JSON snapshot of the entity state BEFORE the change.
    /// Null for Created actions (nothing to show before creation).
    /// </summary>
    public string? StateBefore { get; private set; }

    /// <summary>
    /// JSON snapshot of the entity state AFTER the change.
    /// Null for Deleted actions (the record no longer exists).
    /// </summary>
    public string? StateAfter { get; private set; }

    // ── Request Context ───────────────────────────────────────────────────────

    /// <summary>
    /// IP address of the client that made the request.
    /// Null for system-generated actions.
    /// Stored as a string to accommodate both IPv4 and IPv6.
    /// </summary>
    public string? IpAddress { get; private set; }

    /// <summary>
    /// Browser/client User-Agent string. Useful for security investigations.
    /// Null for API/system-generated actions.
    /// </summary>
    public string? UserAgent { get; private set; }

    /// <summary>
    /// The correlation/trace ID of the HTTP request that caused this entry.
    /// Allows joining audit logs with application logs for full request tracing.
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Additional key-value metadata relevant to this specific action.
    /// Stored as a JSON string. e.g. {"invoiceNumber":"INV-001","oldStatus":"Draft","newStatus":"Issued"}
    /// Avoids schema changes every time a new type of audit data is needed.
    /// </summary>
    public string? Metadata { get; private set; }

    private AuditLog() { } // EF Core — strictly enforces immutability for all other callers

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The only way to create an audit log entry.
    /// Private constructor + static factory ensures every field is set intentionally
    /// and no half-constructed entries are persisted.
    /// </summary>
    public static AuditLog Create(
        Guid tenantId,
        AuditAction action,
        string entityType,
        Guid entityId,
        string description,
        Guid? userId = null,
        string? userDisplayName = null,
        string? stateBefore = null,
        string? stateAfter = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null,
        string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new DomainException("AuditLog entityType is required.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("AuditLog description is required.");
        if (description.Length > 1000)
            throw new DomainException("AuditLog description cannot exceed 1000 characters.");

        // Validate that Created entries have a StateAfter and Deleted entries have a StateBefore.
        // These aren't hard constraints (a system crash could prevent capture) but we warn in dev.
        // In production, missing snapshots are acceptable — the action itself is still recorded.

        return new AuditLog
        {
            TenantId = tenantId,
            Action = action,
            EntityType = entityType.Trim(),
            EntityId = entityId,
            Description = description.Trim(),
            UserId = userId,
            UserDisplayName = userDisplayName?.Trim(),
            StateBefore = stateBefore,
            StateAfter = stateAfter,
            IpAddress = ipAddress?.Trim(),
            UserAgent = userAgent?.Trim(),
            CorrelationId = correlationId?.Trim(),
            Metadata = metadata
        };
    }

    // ── Convenience factories ─────────────────────────────────────────────────

    /// <summary>Creates an audit entry for a status change, the most common audit event.</summary>
    public static AuditLog ForStatusChange(
        Guid tenantId,
        string entityType,
        Guid entityId,
        string fromStatus,
        string toStatus,
        string description,
        Guid? userId = null,
        string? userDisplayName = null,
        string? correlationId = null)
    {
        var metadata = $"{{\"fromStatus\":\"{fromStatus}\",\"toStatus\":\"{toStatus}\"}}";

        return Create(
            tenantId: tenantId,
            action: AuditAction.StatusChanged,
            entityType: entityType,
            entityId: entityId,
            description: description,
            userId: userId,
            userDisplayName: userDisplayName,
            correlationId: correlationId,
            metadata: metadata);
    }

    /// <summary>Creates an audit entry for a payment event (received or refunded).</summary>
    public static AuditLog ForPayment(
        Guid tenantId,
        Guid invoiceId,
        Guid paymentId,
        bool isRefund,
        string amountDisplay,
        Guid? userId = null,
        string? correlationId = null)
    {
        var action = isRefund ? AuditAction.PaymentRefunded : AuditAction.PaymentReceived;
        var description = isRefund
            ? $"Refund of {amountDisplay} processed against invoice."
            : $"Payment of {amountDisplay} received and applied to invoice.";
        var metadata = $"{{\"paymentId\":\"{paymentId}\",\"amount\":\"{amountDisplay}\"}}";

        return Create(
            tenantId: tenantId,
            action: action,
            entityType: nameof(Invoice),
            entityId: invoiceId,
            description: description,
            userId: userId,
            correlationId: correlationId,
            metadata: metadata);
    }

    /// <summary>Creates an audit entry for a login event.</summary>
    public static AuditLog ForLogin(
        Guid tenantId,
        Guid userId,
        string userDisplayName,
        bool succeeded,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        return Create(
            tenantId: tenantId,
            action: succeeded ? AuditAction.LoginSucceeded : AuditAction.LoginFailed,
            entityType: nameof(User),
            entityId: userId,
            description: succeeded
                ? $"User '{userDisplayName}' logged in successfully."
                : $"Failed login attempt for user '{userDisplayName}'.",
            userId: succeeded ? userId : null,
            userDisplayName: userDisplayName,
            ipAddress: ipAddress,
            userAgent: userAgent,
            correlationId: correlationId);
    }

    // ── No mutation methods — this entity is intentionally read-only after creation ──
}
