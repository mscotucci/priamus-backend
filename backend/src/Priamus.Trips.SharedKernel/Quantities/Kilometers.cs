namespace Priamus.Trips.SharedKernel.Quantities;

/// <summary>
/// A non-negative distance in kilometers. Internal precision in the database is
/// <c>numeric(10,2)</c>; this type does not round during arithmetic.
/// </summary>
public readonly record struct Kilometers
{
    /// <summary>
    /// Creates a new <see cref="Kilometers"/> value after validating non-negativity.
    /// </summary>
    /// <param name="value">Non-negative number of kilometers.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public Kilometers(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Kilometers cannot be negative.");
        }
        Value = value;
    }

    /// <summary>The numeric value in kilometers.</summary>
    public decimal Value { get; }

    /// <summary>Adds two <see cref="Kilometers"/> values.</summary>
    public static Kilometers operator +(Kilometers left, Kilometers right) => new(left.Value + right.Value);

    /// <summary>Subtracts two <see cref="Kilometers"/> values.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Kilometers operator -(Kilometers left, Kilometers right) => new(left.Value - right.Value);

    /// <summary>Multiplies a <see cref="Kilometers"/> by a scalar <see cref="decimal"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Kilometers operator *(Kilometers quantity, decimal scalar) => new(quantity.Value * scalar);

    /// <summary>Multiplies a scalar <see cref="decimal"/> by a <see cref="Kilometers"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Kilometers operator *(decimal scalar, Kilometers quantity) => new(quantity.Value * scalar);

    /// <summary>Strict less-than comparison.</summary>
    public static bool operator <(Kilometers left, Kilometers right) => left.Value < right.Value;

    /// <summary>Strict greater-than comparison.</summary>
    public static bool operator >(Kilometers left, Kilometers right) => left.Value > right.Value;

    /// <summary>Less-than-or-equal comparison.</summary>
    public static bool operator <=(Kilometers left, Kilometers right) => left.Value <= right.Value;

    /// <summary>Greater-than-or-equal comparison.</summary>
    public static bool operator >=(Kilometers left, Kilometers right) => left.Value >= right.Value;

    /// <inheritdoc />
    public override string ToString() => $"{Value} km";
}
