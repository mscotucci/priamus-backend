using Priamus.Trips.Registry.Domain.Contracts.Events;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>
/// The billing rules agreed with one customer for a period (PRD §5.4, RF-16 … RF-19). A flat-rate
/// contract holds its <see cref="Rate"/>. That a customer's contracts never overlap is checked by
/// the application layer against the other contracts, and guaranteed by the database.
/// </summary>
public sealed class Contract : AggregateRoot
{
    // The rate is stored as seven nullable columns; EF maps these fields, Rate rebuilds from them.
    private decimal? _dayPrice;
    private decimal? _nightPrice;
    private decimal? _nightCallFee;
    private MinimumRuleKind? _minimumRule;
    private decimal? _minimumAmount;
    private decimal? _minimumThreshold;
    private decimal? _minimumOveragePrice;

    private Contract()
    {
        // EF only. Members are populated by EF Core via property setters/backing fields.
        Validity = null!;
        RequiredDocuments = [];
    }

    private Contract(
        ContractId id,
        CustomerId customerId,
        Validity validity,
        BillingLevel billingLevel,
        PricingMode pricingMode,
        IReadOnlyList<RequiredDocument> requiredDocuments,
        bool nightSurcharge,
        Rate? rate,
        DateTime createdAt,
        UserId createdBy)
    {
        Id = id;
        CustomerId = customerId;
        Validity = validity;
        BillingLevel = billingLevel;
        PricingMode = pricingMode;
        RequiredDocuments = requiredDocuments;
        NightSurcharge = nightSurcharge;
        Rate = rate;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        LastModifiedAt = createdAt;
        LastModifiedBy = createdBy;
    }

    /// <summary>Strongly-typed identifier.</summary>
    public ContractId Id { get; private set; }

    /// <summary>The customer the contract belongs to.</summary>
    public CustomerId CustomerId { get; private set; }

    /// <summary>The days the contract applies to.</summary>
    public Validity Validity { get; private set; }

    /// <summary>How finely the rates are set.</summary>
    public BillingLevel BillingLevel { get; private set; }

    /// <summary>How the amount of a trip is measured.</summary>
    public PricingMode PricingMode { get; private set; }

    /// <summary>The documents the contract asks for: any subset, duplicates collapsed, in canonical order.</summary>
    public IReadOnlyList<RequiredDocument> RequiredDocuments { get; private set; }

    /// <summary>Whether night trips cost more.</summary>
    public bool NightSurcharge { get; private set; }

    /// <summary>The rate; <c>null</c> for a contract of another billing level, or a flat-rate one stored without it.</summary>
    public Rate? Rate
    {
        get => _dayPrice is not { } dayPrice
            ? null
            : Rate.Create(dayPrice, _nightPrice, _nightCallFee, StoredMinimum()).Value;
        private set
        {
            _dayPrice = value?.DayPrice;
            _nightPrice = value?.NightPrice;
            _nightCallFee = value?.NightCallFee;
            _minimumRule = value?.Minimum.Kind;
            _minimumAmount = value?.Minimum.Amount;
            _minimumThreshold = value?.Minimum.Threshold;
            _minimumOveragePrice = value?.Minimum.OveragePrice;
        }
    }

    /// <summary>UTC instant at which the contract was created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Identifier of the actor that created this contract.</summary>
    public UserId CreatedBy { get; private set; }

    /// <summary>UTC instant at which the contract was last modified.</summary>
    public DateTime LastModifiedAt { get; private set; }

    /// <summary>Identifier of the actor that last modified this contract.</summary>
    public UserId LastModifiedBy { get; private set; }

    /// <summary>Creates a contract and stamps audit fields.</summary>
    /// <param name="id">Identifier of the new contract.</param>
    /// <param name="customerId">Customer the contract belongs to.</param>
    /// <param name="validity">The days the contract applies to.</param>
    /// <param name="billingLevel">Billing level.</param>
    /// <param name="pricingMode">Pricing mode.</param>
    /// <param name="requiredDocuments">Required documents; duplicates collapse, order is canonicalised.</param>
    /// <param name="nightSurcharge">Whether night trips cost more.</param>
    /// <param name="rate">The rate; required for flat rate, <c>null</c> for any other billing level.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor performing the creation.</param>
    /// <returns>The contract, or a validation failure when the rate does not fit the terms.</returns>
    public static Result<Contract> Create(
        ContractId id,
        CustomerId customerId,
        Validity validity,
        BillingLevel billingLevel,
        PricingMode pricingMode,
        IEnumerable<RequiredDocument> requiredDocuments,
        bool nightSurcharge,
        Rate? rate,
        IClock clock,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(requiredDocuments);
        ArgumentNullException.ThrowIfNull(clock);

        if (CheckRate(billingLevel, pricingMode, nightSurcharge, rate) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var contract = new Contract(
            id, customerId, validity, billingLevel, pricingMode, Canonical(requiredDocuments),
            nightSurcharge, rate, now, createdBy);
        contract.RaiseEvent(new ContractCreated(
            Guid.NewGuid(), now, id, customerId, validity.ValidFrom, validity.ValidTo, billingLevel,
            pricingMode, contract.RequiredDocuments, nightSurcharge, rate));
        return contract;
    }

    /// <summary>Replaces the editable terms of the contract. A successful call always counts as a change.</summary>
    /// <param name="validity">The days the contract applies to.</param>
    /// <param name="billingLevel">Billing level.</param>
    /// <param name="pricingMode">Pricing mode.</param>
    /// <param name="requiredDocuments">Required documents; duplicates collapse, order is canonicalised.</param>
    /// <param name="nightSurcharge">Whether night trips cost more.</param>
    /// <param name="rate">The rate; required for flat rate, <c>null</c> for any other billing level.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <returns>A validation failure when the rate does not fit the terms; the contract is then unchanged.</returns>
    public Result Update(
        Validity validity,
        BillingLevel billingLevel,
        PricingMode pricingMode,
        IEnumerable<RequiredDocument> requiredDocuments,
        bool nightSurcharge,
        Rate? rate,
        IClock clock,
        UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(requiredDocuments);
        ArgumentNullException.ThrowIfNull(clock);

        if (CheckRate(billingLevel, pricingMode, nightSurcharge, rate) is { } invalid)
        {
            return invalid;
        }

        Validity = validity;
        BillingLevel = billingLevel;
        PricingMode = pricingMode;
        RequiredDocuments = Canonical(requiredDocuments);
        NightSurcharge = nightSurcharge;
        Rate = rate;

        Touch(clock.UtcNow, modifiedBy);
        return Result.Success();
    }

    /// <summary>
    /// Ends the validity on <paramref name="lastDay"/>, as the creation of a later contract does
    /// (RF-18).
    /// </summary>
    /// <param name="lastDay">New last day of the validity.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <returns>A failure with <see cref="ContractErrors.ValidityEndBeforeStart"/> when the day is before the start; the contract is then unchanged.</returns>
    public Result CloseOn(DateOnly lastDay, IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var closed = Validity.CloseOn(lastDay);
        if (closed.IsFailure)
        {
            return closed.Error;
        }

        Validity = closed.Value;
        Touch(clock.UtcNow, modifiedBy);
        return Result.Success();
    }

    private static Error? CheckRate(
        BillingLevel billingLevel,
        PricingMode pricingMode,
        bool nightSurcharge,
        Rate? rate)
    {
        if (rate is null)
        {
            return billingLevel == BillingLevel.FlatRate ? ContractErrors.RateRequired : null;
        }

        if (billingLevel != BillingLevel.FlatRate)
        {
            return ContractErrors.RateNotApplicable;
        }

        var hasBothNightValues = rate.NightPrice is not null && rate.NightCallFee is not null;
        var hasAnyNightValue = rate.NightPrice is not null || rate.NightCallFee is not null;
        if (nightSurcharge && !hasBothNightValues)
        {
            return ContractErrors.NightRateMissing;
        }

        if (!nightSurcharge && hasAnyNightValue)
        {
            return ContractErrors.NightRateNotApplicable;
        }

        return pricingMode == PricingMode.ByTrip && rate.Minimum is not MinimumRule.None
            ? ContractErrors.MinimumNotApplicableByTrip
            : null;
    }

    private MinimumRule StoredMinimum() =>
        MinimumRule.From(_minimumRule, _minimumAmount, _minimumThreshold, _minimumOveragePrice);

    private static RequiredDocument[] Canonical(IEnumerable<RequiredDocument> documents) =>
        [.. documents.Distinct().Order()];

    private void Touch(DateTime now, UserId modifiedBy)
    {
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
        RaiseEvent(new ContractUpdated(
            Guid.NewGuid(), now, Id, CustomerId, Validity.ValidFrom, Validity.ValidTo, BillingLevel,
            PricingMode, RequiredDocuments, NightSurcharge, Rate));
    }
}
