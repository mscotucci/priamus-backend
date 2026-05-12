namespace Priamus.Trips.SharedKernel.Security;

/// <summary>
/// Strongly-typed identifier for the authenticated user. Lives in SharedKernel
/// because authorization checks across modules need to compare user identities.
/// </summary>
/// <param name="Value">The underlying <see cref="Guid"/> value.</param>
public readonly record struct UserId(Guid Value)
{
    /// <summary>Creates a new randomly-generated <see cref="UserId"/>.</summary>
    public static UserId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
