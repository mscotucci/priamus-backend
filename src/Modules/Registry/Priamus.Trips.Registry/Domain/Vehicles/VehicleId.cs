namespace Priamus.Trips.Registry.Domain.Vehicles;

/// <summary>Strongly-typed identifier of a <see cref="Vehicle"/>.</summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
public readonly record struct VehicleId(Guid Value)
{
    /// <summary>Creates a new randomly-generated <see cref="VehicleId"/>.</summary>
    public static VehicleId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
