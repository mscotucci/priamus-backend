namespace Priamus.Trips.Operations.Domain.Trips;

/// <summary>Strongly-typed identifier of a <see cref="Trip"/>; the app generates it.</summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
public readonly record struct TripId(Guid Value)
{
    /// <summary>Creates a new randomly-generated <see cref="TripId"/>.</summary>
    public static TripId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
