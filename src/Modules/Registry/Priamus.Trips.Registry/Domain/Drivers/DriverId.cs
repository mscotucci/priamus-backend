namespace Priamus.Trips.Registry.Domain.Drivers;

/// <summary>Strongly-typed identifier of a <see cref="Driver"/>.</summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
public readonly record struct DriverId(Guid Value)
{
    /// <summary>Creates a new randomly-generated <see cref="DriverId"/>.</summary>
    public static DriverId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
