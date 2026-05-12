namespace Priamus.Trips.SharedKernel.Money;

/// <summary>
/// A monetary amount expressed in a single <see cref="Currency"/>. Used everywhere
/// the system handles money; never use bare <see cref="decimal"/> for monetary values.
/// Internal precision in the database is <c>numeric(12,2)</c>; this type does not
/// round implicitly during arithmetic — rounding happens at persistence or presentation.
/// </summary>
/// <param name="Amount">The amount, as a <see cref="decimal"/> with implementation-defined precision.</param>
/// <param name="Currency">The currency in which <paramref name="Amount"/> is expressed.</param>
public readonly record struct Money(decimal Amount, Currency Currency)
{
    /// <summary>
    /// Returns a zero-valued <see cref="Money"/> in the given <paramref name="currency"/>.
    /// </summary>
    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>Adds two <see cref="Money"/> values of the same currency.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the operands have different currencies.</exception>
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "add");
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    /// <summary>Subtracts two <see cref="Money"/> values of the same currency.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the operands have different currencies.</exception>
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "subtract");
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    /// <summary>Multiplies a <see cref="Money"/> by a scalar <see cref="decimal"/>.</summary>
    public static Money operator *(Money money, decimal scalar) => new(money.Amount * scalar, money.Currency);

    /// <summary>Multiplies a scalar <see cref="decimal"/> by a <see cref="Money"/>.</summary>
    public static Money operator *(decimal scalar, Money money) => new(money.Amount * scalar, money.Currency);

    /// <summary>True when <paramref name="left"/> is strictly less than <paramref name="right"/> (same currency).</summary>
    /// <exception cref="InvalidOperationException">Thrown when operands have different currencies.</exception>
    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount < right.Amount;
    }

    /// <summary>True when <paramref name="left"/> is strictly greater than <paramref name="right"/> (same currency).</summary>
    /// <exception cref="InvalidOperationException">Thrown when operands have different currencies.</exception>
    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount > right.Amount;
    }

    /// <summary>True when <paramref name="left"/> is less than or equal to <paramref name="right"/> (same currency).</summary>
    /// <exception cref="InvalidOperationException">Thrown when operands have different currencies.</exception>
    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount <= right.Amount;
    }

    /// <summary>True when <paramref name="left"/> is greater than or equal to <paramref name="right"/> (same currency).</summary>
    /// <exception cref="InvalidOperationException">Thrown when operands have different currencies.</exception>
    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right, "compare");
        return left.Amount >= right.Amount;
    }

    private static void EnsureSameCurrency(Money left, Money right, string operation)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} Money values in different currencies: '{left.Currency}' and '{right.Currency}'.");
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Amount} {Currency}";
}
