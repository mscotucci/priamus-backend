using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The rate of a flat-rate <see cref="Contract"/>: day price, night price and night call fee
/// (both present only with the night surcharge) and the minimum rule. It applies for the whole
/// validity of its contract and has none of its own. Amounts are euro.
/// </summary>
public sealed record Rate
{
    /// <summary>The largest amount a rate holds (the columns are <c>numeric(12,2)</c>).</summary>
    internal const decimal MaxAmount = 9999999999.99m;

    private Rate(decimal dayPrice, decimal? nightPrice, decimal? nightCallFee, MinimumRule minimum)
    {
        DayPrice = dayPrice;
        NightPrice = nightPrice;
        NightCallFee = nightCallFee;
        Minimum = minimum;
    }

    /// <summary>Price of a day trip.</summary>
    public decimal DayPrice { get; }

    /// <summary>Price of a night trip; <c>null</c> without the night surcharge.</summary>
    public decimal? NightPrice { get; }

    /// <summary>Fee for a night call; <c>null</c> without the night surcharge, may be zero.</summary>
    public decimal? NightCallFee { get; }

    /// <summary>The minimum rule.</summary>
    public MinimumRule Minimum { get; }

    /// <summary>Builds a rate, checking every amount.</summary>
    /// <param name="dayPrice">Day price, greater than 0.</param>
    /// <param name="nightPrice">Night price, greater than 0 when given.</param>
    /// <param name="nightCallFee">Night call fee, 0 or more when given.</param>
    /// <param name="minimum">The minimum rule; its amounts are greater than 0.</param>
    /// <returns>A failure with the first violated rule; every amount has at most 2 decimals and is at most <see cref="MaxAmount"/>.</returns>
    public static Result<Rate> Create(
        decimal dayPrice,
        decimal? nightPrice,
        decimal? nightCallFee,
        MinimumRule minimum)
    {
        ArgumentNullException.ThrowIfNull(minimum);

        var failure = CheckPositive(dayPrice, ContractErrors.DayPriceNotPositive)
            ?? CheckOptionalPositive(nightPrice, ContractErrors.NightPriceNotPositive)
            ?? CheckNightCallFee(nightCallFee)
            ?? CheckMinimum(minimum);
        return failure is { } error ? error : new Rate(dayPrice, nightPrice, nightCallFee, minimum);
    }

    private static Error? CheckPositive(decimal amount, Error notPositive)
    {
        if (amount <= 0)
        {
            return notPositive;
        }

        return CheckScale(amount);
    }

    private static Error? CheckOptionalPositive(decimal? amount, Error notPositive) =>
        amount is { } value ? CheckPositive(value, notPositive) : null;

    private static Error? CheckNightCallFee(decimal? fee)
    {
        if (fee is not { } value)
        {
            return null;
        }

        if (value < 0)
        {
            return ContractErrors.NightCallFeeNegative;
        }

        return CheckScale(value);
    }

    private static Error? CheckMinimum(MinimumRule minimum) => minimum switch
    {
        MinimumRule.None => null,
        MinimumRule.FixedMinimum fixedMinimum =>
            CheckPositive(fixedMinimum.Amount, ContractErrors.MinimumAmountNotPositive),
        MinimumRule.TieredOverage tiered =>
            CheckPositive(tiered.Amount, ContractErrors.MinimumAmountNotPositive)
            ?? CheckPositive(tiered.Threshold, ContractErrors.MinimumThresholdNotPositive)
            ?? CheckPositive(tiered.OveragePrice, ContractErrors.MinimumOveragePriceNotPositive),
        _ => throw new ArgumentOutOfRangeException(nameof(minimum), minimum, "Unknown minimum rule."),
    };

    private static Error? CheckScale(decimal amount)
    {
        if (HasMoreThanTwoDecimals(amount))
        {
            return ContractErrors.AmountTooPrecise;
        }

        return amount > MaxAmount ? ContractErrors.AmountTooLarge : null;
    }

    /// <summary>Whether the amount has more than the 2 decimals the rate stores.</summary>
    internal static bool HasMoreThanTwoDecimals(decimal amount) => decimal.Round(amount, 2) != amount;
}
