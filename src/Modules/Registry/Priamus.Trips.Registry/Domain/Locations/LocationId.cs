namespace Priamus.Trips.Registry.Domain.Locations;

/// <summary>Strongly-typed identifier of a <see cref="Location"/>.</summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
public readonly record struct LocationId(Guid Value)
{
    /// <summary>Creates a new randomly-generated <see cref="LocationId"/>.</summary>
    public static LocationId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
