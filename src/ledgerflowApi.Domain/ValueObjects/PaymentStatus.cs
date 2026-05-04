namespace ledgerflowApi.Domain.ValueObjects;

/// <summary>
/// Strongly-typed value object for payment lifecycle status.
///
/// Payment lifecycle:
///
///   Pending ──► Completed
///      │
///      └──► Failed ──► Pending   (retry)
///      │
///      └──► Cancelled            (terminal)
///
///   Completed ──► Refunded       (terminal — refunds create a new Payment record
///                                 rather than mutating the original)
/// </summary>
public sealed class PaymentStatus : IEquatable<PaymentStatus>
{
    public string Value { get; }

    public static readonly PaymentStatus Pending   = new("Pending");
    public static readonly PaymentStatus Completed = new("Completed");
    public static readonly PaymentStatus Failed    = new("Failed");
    public static readonly PaymentStatus Cancelled = new("Cancelled");
    public static readonly PaymentStatus Refunded  = new("Refunded");

    private static readonly Dictionary<string, PaymentStatus[]> AllowedTransitions = new()
    {
        // Payment processor initially creates the record as Pending before confirmation.
        [Pending.Value]   = [Completed, Failed, Cancelled],

        // A completed payment can be refunded (creates a linked refund Payment record).
        [Completed.Value] = [Refunded],

        // Failed payments can be retried (back to Pending) or abandoned (Cancelled).
        [Failed.Value]    = [Pending, Cancelled],

        // Terminal states — no further transitions allowed.
        [Cancelled.Value] = [],
        [Refunded.Value]  = []
    };

    private PaymentStatus() { Value = string.Empty; } // EF Core

    private PaymentStatus(string value) => Value = value;

    public static PaymentStatus From(string value)
    {
        var status = All.FirstOrDefault(s => s.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        return status ?? throw new ArgumentException(
            $"'{value}' is not a valid PaymentStatus. Valid values: {string.Join(", ", All.Select(s => s.Value))}");
    }

    public bool CanTransitionTo(PaymentStatus next) =>
        AllowedTransitions.TryGetValue(Value, out var allowed) && allowed.Contains(next);

    public bool IsPending => this == Pending;
    public bool IsCompleted => this == Completed;
    public bool IsFailed => this == Failed;
    public bool IsTerminal => this == Cancelled || this == Refunded;

    public static IReadOnlyCollection<PaymentStatus> All =>
        [Pending, Completed, Failed, Cancelled, Refunded];

    public override string ToString() => Value;

    public bool Equals(PaymentStatus? other) =>
        other is not null && Value == other.Value;
    public override bool Equals(object? obj) => Equals(obj as PaymentStatus);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(PaymentStatus? l, PaymentStatus? r) => l?.Equals(r) ?? r is null;
    public static bool operator !=(PaymentStatus? l, PaymentStatus? r) => !(l == r);
}
