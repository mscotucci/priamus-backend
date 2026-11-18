namespace Priamus.Trips.Registry.Domain.Customers;

/// <summary>Strongly-typed identifier of a <see cref="Customer"/>.</summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
public readonly record struct CustomerId(Guid Value)
{
    /// <summary>Creates a new randomly-generated <see cref="CustomerId"/>.</summary>
    public static CustomerId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
