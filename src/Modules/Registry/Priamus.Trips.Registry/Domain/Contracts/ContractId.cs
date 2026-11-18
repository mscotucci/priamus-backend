namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>Strongly-typed identifier of a <see cref="Contract"/>.</summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
public readonly record struct ContractId(Guid Value)
{
    /// <summary>Creates a new randomly-generated <see cref="ContractId"/>.</summary>
    public static ContractId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
