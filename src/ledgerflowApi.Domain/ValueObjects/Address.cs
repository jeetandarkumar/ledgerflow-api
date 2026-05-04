namespace ledgerflowApi.Domain.ValueObjects;

/// <summary>
/// Immutable address value object used on both Tenant (billing address)
/// and Invoice (bill-to address at the time of issuance — intentionally
/// captured as a snapshot so it doesn't change if the tenant updates their address).
/// </summary>
public sealed class Address : IEquatable<Address>
{
    public string Line1 { get; }
    public string? Line2 { get; }
    public string City { get; }
    public string? State { get; }

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. "US", "GB").</summary>
    public string CountryCode { get; }
    public string PostalCode { get; }

    private Address() { Line1 = City = CountryCode = PostalCode = string.Empty; } // EF Core

    public Address(string line1, string? line2, string city, string? state, string countryCode, string postalCode)
    {
        if (string.IsNullOrWhiteSpace(line1))
            throw new ArgumentException("Address line 1 is required.", nameof(line1));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("Country code must be a 2-character ISO 3166-1 code.", nameof(countryCode));
        if (string.IsNullOrWhiteSpace(postalCode))
            throw new ArgumentException("Postal code is required.", nameof(postalCode));

        Line1 = line1.Trim();
        Line2 = line2?.Trim();
        City = city.Trim();
        State = state?.Trim();
        CountryCode = countryCode.ToUpperInvariant();
        PostalCode = postalCode.Trim();
    }

    public override string ToString() =>
        string.Join(", ", new[] { Line1, Line2, City, State, PostalCode, CountryCode }
            .Where(s => !string.IsNullOrEmpty(s)));

    public bool Equals(Address? other) => other is not null
        && Line1 == other.Line1 && Line2 == other.Line2 && City == other.City
        && State == other.State && CountryCode == other.CountryCode && PostalCode == other.PostalCode;

    public override bool Equals(object? obj) => Equals(obj as Address);
    public override int GetHashCode() =>
        HashCode.Combine(Line1, Line2, City, State, CountryCode, PostalCode);
    public static bool operator ==(Address? l, Address? r) => l?.Equals(r) ?? r is null;
    public static bool operator !=(Address? l, Address? r) => !(l == r);
}
