namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The stored columns of a <see cref="Rate"/> held by a row owned by its <see cref="Contract"/>: the
/// vehicle rates and the location rates. The rate is held as its stored columns, as the contract
/// holds its own.
/// </summary>
internal abstract class StoredRate
{
    protected StoredRate()
    {
        // EF only.
    }

    protected StoredRate(Rate rate)
    {
        DayPrice = rate.DayPrice;
        NightPrice = rate.NightPrice;
        NightCallFee = rate.NightCallFee;
        MinimumRule = rate.Minimum.Kind;
        MinimumAmount = rate.Minimum.Amount;
        MinimumThreshold = rate.Minimum.Threshold;
        MinimumOveragePrice = rate.Minimum.OveragePrice;
    }

    internal decimal DayPrice { get; private set; }

    internal decimal? NightPrice { get; private set; }

    internal decimal? NightCallFee { get; private set; }

    internal MinimumRuleKind MinimumRule { get; private set; }

    internal decimal? MinimumAmount { get; private set; }

    internal decimal? MinimumThreshold { get; private set; }

    internal decimal? MinimumOveragePrice { get; private set; }

    /// <summary>Rebuilds the rate from the stored columns; it never judges them.</summary>
    protected Rate ToRate() => Rate.Restore(
        DayPrice,
        NightPrice,
        NightCallFee,
        ValueObjects.MinimumRule.From(MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice));
}
