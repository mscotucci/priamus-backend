namespace Priamus.Trips.SharedKernel.Quantities;

/// <summary>
/// A non-negative mass in metric tons. Internal precision in the database is
/// <c>numeric(10,3)</c>; this type does not round during arithmetic.
/// </summary>
public readonly record struct Tons
{
    /// <summary>
    /// Creates a new <see cref="Tons"/> value after validating non-negativity.
    /// </summary>
    /// <param name="value">Non-negative number of tons.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public Tons(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Tons cannot be negative.");
        }
        Value = value;
    }

    /// <summary>The numeric value in tons.</summary>
    public decimal Value { get; }

    /// <summary>Adds two <see cref="Tons"/> values.</summary>
    public static Tons operator +(Tons left, Tons right) => new(left.Value + right.Value);

    /// <summary>Subtracts two <see cref="Tons"/> values.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Tons operator -(Tons left, Tons right) => new(left.Value - right.Value);

    /// <summary>Multiplies a <see cref="Tons"/> by a scalar <see cref="decimal"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Tons operator *(Tons quantity, decimal scalar) => new(quantity.Value * scalar);

    /// <summary>Multiplies a scalar <see cref="decimal"/> by a <see cref="Tons"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Tons operator *(decimal scalar, Tons quantity) => new(quantity.Value * scalar);

    /// <summary>Strict less-than comparison.</summary>
    public static bool operator <(Tons left, Tons right) => left.Value < right.Value;

    /// <summary>Strict greater-than comparison.</summary>
    public static bool operator >(Tons left, Tons right) => left.Value > right.Value;

    /// <summary>Less-than-or-equal comparison.</summary>
    public static bool operator <=(Tons left, Tons right) => left.Value <= right.Value;

    /// <summary>Greater-than-or-equal comparison.</summary>
    public static bool operator >=(Tons left, Tons right) => left.Value >= right.Value;

    /// <inheritdoc />
    public override string ToString() => $"{Value} t";
}
