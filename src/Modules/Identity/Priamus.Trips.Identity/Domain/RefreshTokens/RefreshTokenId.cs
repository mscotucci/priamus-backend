namespace Priamus.Trips.Identity.Domain.RefreshTokens;

/// <summary>
/// Strongly-typed identifier for a <see cref="RefreshToken"/> aggregate.
/// </summary>
/// <param name="Value">The underlying <see cref="Guid"/>.</param>
public readonly record struct RefreshTokenId(Guid Value)
{
    /// <summary>Creates a fresh random identifier.</summary>
    public static RefreshTokenId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
