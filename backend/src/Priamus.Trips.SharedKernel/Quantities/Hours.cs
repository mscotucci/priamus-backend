namespace Priamus.Trips.SharedKernel.Quantities;

/// <summary>
/// A non-negative duration in hours. Internal precision in the database is
/// <c>numeric(8,2)</c>; this type does not round during arithmetic.
/// </summary>
public readonly record struct Hours
{
    /// <summary>
    /// Creates a new <see cref="Hours"/> value after validating non-negativity.
    /// </summary>
    /// <param name="value">Non-negative number of hours.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public Hours(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Hours cannot be negative.");
        }
        Value = value;
    }

    /// <summary>The numeric value in hours.</summary>
    public decimal Value { get; }

    /// <summary>Adds two <see cref="Hours"/> values.</summary>
    public static Hours operator +(Hours left, Hours right) => new(left.Value + right.Value);

    /// <summary>Subtracts two <see cref="Hours"/> values.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Hours operator -(Hours left, Hours right) => new(left.Value - right.Value);

    /// <summary>Multiplies an <see cref="Hours"/> by a scalar <see cref="decimal"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Hours operator *(Hours quantity, decimal scalar) => new(quantity.Value * scalar);

    /// <summary>Multiplies a scalar <see cref="decimal"/> by an <see cref="Hours"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the result would be negative.</exception>
    public static Hours operator *(decimal scalar, Hours quantity) => new(quantity.Value * scalar);

    /// <summary>Strict less-than comparison.</summary>
    public static bool operator <(Hours left, Hours right) => left.Value < right.Value;

    /// <summary>Strict greater-than comparison.</summary>
    public static bool operator >(Hours left, Hours right) => left.Value > right.Value;

    /// <summary>Less-than-or-equal comparison.</summary>
    public static bool operator <=(Hours left, Hours right) => left.Value <= right.Value;

    /// <summary>Greater-than-or-equal comparison.</summary>
    public static bool operator >=(Hours left, Hours right) => left.Value >= right.Value;

    /// <inheritdoc />
    public override string ToString() => $"{Value} h";
}
