namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// What a trip costs at least under a <see cref="Rate"/>. Amounts are euro; the amounts of the cases are checked
/// when the <see cref="Rate"/> that holds the rule is built.
/// </summary>
public abstract record MinimumRule
{
    private MinimumRule()
    {
    }

    /// <summary>The kind of the rule, as stored and exchanged.</summary>
    public abstract MinimumRuleKind Kind { get; }

    /// <summary>The minimum amount M; <c>null</c> for the case without one.</summary>
    public decimal? Amount => this switch
    {
        FixedMinimum f => f.Amount,
        TieredOverage t => t.Amount,
        _ => null,
    };

    /// <summary>The threshold X; <c>null</c> unless the case is tiered overage.</summary>
    public decimal? Threshold => (this as TieredOverage)?.Threshold;

    /// <summary>The overage price Y; <c>null</c> unless the case is tiered overage.</summary>
    public decimal? OveragePrice => (this as TieredOverage)?.OveragePrice;

    /// <summary>Builds the rule of a kind from its stored or requested values.</summary>
    /// <param name="kind">The kind; <c>null</c> means no minimum.</param>
    /// <param name="amount">M, required for the fixed and tiered kinds.</param>
    /// <param name="threshold">X, required for the tiered kind.</param>
    /// <param name="overage">Y, required for the tiered kind.</param>
    public static MinimumRule From(MinimumRuleKind? kind, decimal? amount, decimal? threshold, decimal? overage) => kind switch
    {
        MinimumRuleKind.FixedMinimum => new FixedMinimum(amount!.Value),
        MinimumRuleKind.TieredOverage => new TieredOverage(amount!.Value, threshold!.Value, overage!.Value),
        _ => new None(),
    };

    /// <summary>No minimum.</summary>
    public sealed record None : MinimumRule
    {
        /// <inheritdoc />
        public override MinimumRuleKind Kind => MinimumRuleKind.None;
    }

    /// <summary>A trip never costs less than <see cref="Amount"/>.</summary>
    public sealed record FixedMinimum : MinimumRule
    {
        /// <summary>Builds the rule.</summary>
        /// <param name="amount">The minimum amount, M.</param>
        public FixedMinimum(decimal amount) => Amount = amount;

        /// <summary>The minimum amount, M.</summary>
        public new decimal Amount { get; }

        /// <inheritdoc />
        public override MinimumRuleKind Kind => MinimumRuleKind.FixedMinimum;
    }

    /// <summary>
    /// A trip costs <see cref="Amount"/> up to <see cref="Threshold"/> (in the unit of
    /// the pricing mode), and <see cref="OveragePrice"/> per unit beyond it.
    /// </summary>
    public sealed record TieredOverage : MinimumRule
    {
        /// <summary>Builds the rule.</summary>
        /// <param name="amount">The amount up to the threshold, M.</param>
        /// <param name="threshold">The threshold, X.</param>
        /// <param name="overagePrice">The price of each unit beyond the threshold, Y.</param>
        public TieredOverage(decimal amount, decimal threshold, decimal overagePrice)
        {
            Amount = amount;
            Threshold = threshold;
            OveragePrice = overagePrice;
        }

        /// <summary>The amount up to the threshold, M.</summary>
        public new decimal Amount { get; }

        /// <summary>The threshold, X.</summary>
        public new decimal Threshold { get; }

        /// <summary>The price of each unit beyond the threshold, Y.</summary>
        public new decimal OveragePrice { get; }

        /// <inheritdoc />
        public override MinimumRuleKind Kind => MinimumRuleKind.TieredOverage;
    }
}

/// <summary>The kinds of <see cref="MinimumRule"/>, for requests, responses and storage.</summary>
public enum MinimumRuleKind
{
    /// <summary>No minimum.</summary>
    None,

    /// <summary>A fixed minimum amount.</summary>
    FixedMinimum,

    /// <summary>An amount up to a threshold, a price per unit beyond it.</summary>
    TieredOverage,
}
