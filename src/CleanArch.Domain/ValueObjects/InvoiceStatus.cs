namespace CleanArch.Domain.ValueObjects;

/// <summary>
/// Strongly-typed value object for invoice lifecycle status.
///
/// Why a value object rather than a plain enum?
/// - The transition rules live here, not scattered across handlers.
/// - Adding a new status means adding it here and updating AllowedTransitions,
///   rather than hunting for switch/if chains across the codebase.
///
/// Valid invoice lifecycle:
///
///   Draft ──► Issued ──► PartiallyPaid ──► Paid
///                │                          │
///                └──────────────────────────┘
///                         Overdue
///                           │
///                           ▼
///                        Voided
///                           │
///              (also reachable from Draft and Issued)
/// </summary>
public sealed class InvoiceStatus : IEquatable<InvoiceStatus>
{
    public string Value { get; }

    // Static instances act like a controlled enum but with behaviour attached
    public static readonly InvoiceStatus Draft         = new("Draft");
    public static readonly InvoiceStatus Issued        = new("Issued");
    public static readonly InvoiceStatus PartiallyPaid = new("PartiallyPaid");
    public static readonly InvoiceStatus Paid          = new("Paid");
    public static readonly InvoiceStatus Overdue       = new("Overdue");
    public static readonly InvoiceStatus Voided        = new("Voided");

    private static readonly Dictionary<string, InvoiceStatus[]> AllowedTransitions = new()
    {
        // A draft can be issued (sent to customer) or voided before it ever goes out.
        [Draft.Value]         = [Issued, Voided],

        // Once issued, it can receive partial/full payment, go overdue, or be voided.
        [Issued.Value]        = [PartiallyPaid, Paid, Overdue, Voided],

        // Partial payment can progress to fully paid, go overdue, or be voided.
        [PartiallyPaid.Value] = [Paid, Overdue, Voided],

        // A paid invoice transitions to Overdue only via manual correction (e.g. chargeback).
        // In practice this path is rare and requires an explicit admin action.
        [Paid.Value]          = [],

        // Overdue invoices can still receive payment (late payment) or be voided (written off).
        [Overdue.Value]       = [PartiallyPaid, Paid, Voided],

        // Voided is a terminal state — no transitions out.
        [Voided.Value]        = []
    };

    private InvoiceStatus() { Value = string.Empty; } // EF Core

    private InvoiceStatus(string value) => Value = value;

    /// <summary>
    /// Parses a status string. Use this when reading from the database or API input.
    /// Throws if the value is unrecognised — we prefer a loud failure over silently
    /// defaulting to an incorrect state on a financial record.
    /// </summary>
    public static InvoiceStatus From(string value)
    {
        var status = All.FirstOrDefault(s => s.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        return status ?? throw new ArgumentException(
            $"'{value}' is not a valid InvoiceStatus. Valid values: {string.Join(", ", All.Select(s => s.Value))}");
    }

    /// <summary>Returns true when moving from this status to <paramref name="next"/> is a legal transition.</summary>
    public bool CanTransitionTo(InvoiceStatus next) =>
        AllowedTransitions.TryGetValue(Value, out var allowed) && allowed.Contains(next);

    public bool IsDraft => this == Draft;
    public bool IsIssued => this == Issued;
    public bool IsPaid => this == Paid;
    public bool IsVoided => this == Voided;
    public bool IsTerminal => this == Paid || this == Voided;

    public static IReadOnlyCollection<InvoiceStatus> All =>
        [Draft, Issued, PartiallyPaid, Paid, Overdue, Voided];

    public override string ToString() => Value;

    public bool Equals(InvoiceStatus? other) =>
        other is not null && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as InvoiceStatus);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(InvoiceStatus? l, InvoiceStatus? r) => l?.Equals(r) ?? r is null;
    public static bool operator !=(InvoiceStatus? l, InvoiceStatus? r) => !(l == r);
}
