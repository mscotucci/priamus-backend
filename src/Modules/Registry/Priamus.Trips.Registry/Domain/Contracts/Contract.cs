using Priamus.Trips.Registry.Domain.Contracts.Events;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>
/// The billing rules agreed with one customer for a period (PRD §5.4, RF-16 … RF-19). A flat-rate
/// contract holds its <see cref="Rate"/>, a location contract its location rates. That a customer's contracts never overlap is checked by
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

    // The vehicle rates are stored as rows owned by the contract; VehicleRates rebuilds from them.
    private List<StoredVehicleRate> _storedVehicleRates = [];

    // The location rates are stored as rows owned by the contract; LocationRates rebuilds from them.
    private List<StoredLocationRate> _storedLocationRates = [];

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
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<LocationRate> locationRates,
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
        VehicleRates = vehicleRates;
        LocationRates = locationRates;
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
            : Rate.Restore(dayPrice, _nightPrice, _nightCallFee, StoredMinimum());
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

    /// <summary>
    /// The rates some vehicles have of their own, at most one per vehicle in a flat-rate contract and one per vehicle and location or pair in the others; empty when there are none.
    /// They apply for the whole validity of the contract and have none of their own.
    /// </summary>
    public IReadOnlyList<VehicleRate> VehicleRates
    {
        get => [.. _storedVehicleRates.Select(r => r.ToVehicleRate())];
        private set => _storedVehicleRates = [.. value.Select(r => new StoredVehicleRate(r))];
    }

    /// <summary>
    /// The rates of the locations or pairs of locations of a contract priced per location, at most one
    /// per location or ordered pair; empty when there are none. They apply for the whole validity of
    /// the contract and have none of their own.
    /// </summary>
    public IReadOnlyList<LocationRate> LocationRates
    {
        get => [.. _storedLocationRates.Select(r => r.ToLocationRate())];
        private set => _storedLocationRates = [.. value.Select(r => new StoredLocationRate(r))];
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
    /// <param name="vehicleRates">The vehicle rates; each vehicle at most once in a flat-rate contract (no locations), at most once on each location or pair the contract has a rate for in the other billing levels.</param>
    /// <param name="locationRates">The location rates; required (at least one) for a contract priced per location or pair, none for any other billing level, each location or ordered pair at most once.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor performing the creation.</param>
    /// <returns>The contract, or a validation failure when the rate, a vehicle rate or a location rate does not fit the terms.</returns>
    public static Result<Contract> Create(
        ContractId id,
        CustomerId customerId,
        Validity validity,
        BillingLevel billingLevel,
        PricingMode pricingMode,
        IEnumerable<RequiredDocument> requiredDocuments,
        bool nightSurcharge,
        Rate? rate,
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<LocationRate> locationRates,
        IClock clock,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(requiredDocuments);
        ArgumentNullException.ThrowIfNull(vehicleRates);
        ArgumentNullException.ThrowIfNull(locationRates);
        ArgumentNullException.ThrowIfNull(clock);

        if ((CheckRate(billingLevel, pricingMode, nightSurcharge, rate)
            ?? CheckVehicleRates(billingLevel, pricingMode, nightSurcharge, vehicleRates, locationRates)
            ?? CheckLocationRates(billingLevel, pricingMode, nightSurcharge, locationRates)) is { } invalid)
        {
            return invalid;
        }

        var now = clock.UtcNow;
        var contract = new Contract(
            id, customerId, validity, billingLevel, pricingMode, Canonical(requiredDocuments),
            nightSurcharge, rate, [.. vehicleRates], [.. locationRates], now, createdBy);
        contract.RaiseEvent(new ContractCreated(
            Guid.NewGuid(), now, id, customerId, validity.ValidFrom, validity.ValidTo, billingLevel,
            pricingMode, contract.RequiredDocuments, nightSurcharge, rate, contract.VehicleRates,
            contract.LocationRates));
        return contract;
    }

    /// <summary>
    /// Replaces the editable terms of the contract. A no-op when the terms are the same: no event, no
    /// version bump, so no audit row.
    /// </summary>
    /// <param name="validity">The days the contract applies to.</param>
    /// <param name="billingLevel">Billing level.</param>
    /// <param name="pricingMode">Pricing mode.</param>
    /// <param name="requiredDocuments">Required documents; duplicates collapse, order is canonicalised.</param>
    /// <param name="nightSurcharge">Whether night trips cost more.</param>
    /// <param name="rate">The rate; required for flat rate, <c>null</c> for any other billing level.</param>
    /// <param name="vehicleRates">The vehicle rates; each vehicle at most once in a flat-rate contract (no locations), at most once on each location or pair the contract has a rate for in the other billing levels.</param>
    /// <param name="locationRates">The location rates; required (at least one) for a contract priced per location or pair, none for any other billing level, each location or ordered pair at most once.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <returns>A validation failure when the rate, a vehicle rate or a location rate does not fit the terms; the contract is then unchanged.</returns>
    public Result Update(
        Validity validity,
        BillingLevel billingLevel,
        PricingMode pricingMode,
        IEnumerable<RequiredDocument> requiredDocuments,
        bool nightSurcharge,
        Rate? rate,
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<LocationRate> locationRates,
        IClock clock,
        UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(requiredDocuments);
        ArgumentNullException.ThrowIfNull(vehicleRates);
        ArgumentNullException.ThrowIfNull(locationRates);
        ArgumentNullException.ThrowIfNull(clock);

        if ((CheckRate(billingLevel, pricingMode, nightSurcharge, rate)
            ?? CheckVehicleRates(billingLevel, pricingMode, nightSurcharge, vehicleRates, locationRates)
            ?? CheckLocationRates(billingLevel, pricingMode, nightSurcharge, locationRates)) is { } invalid)
        {
            return invalid;
        }

        var documents = Canonical(requiredDocuments);
        if (SameTerms(validity, billingLevel, pricingMode, documents, nightSurcharge, rate, vehicleRates, locationRates))
        {
            return Result.Success();
        }

        Validity = validity;
        BillingLevel = billingLevel;
        PricingMode = pricingMode;
        RequiredDocuments = documents;
        NightSurcharge = nightSurcharge;
        Rate = rate;
        VehicleRates = [.. vehicleRates];
        LocationRates = [.. locationRates];

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

        return CheckRateTerms(pricingMode, nightSurcharge, rate);
    }

    private static Error? CheckVehicleRates(
        BillingLevel billingLevel,
        PricingMode pricingMode,
        bool nightSurcharge,
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<LocationRate> locationRates)
    {
        var ratedLocations = locationRates
            .Select(r => (r.PickupLocationId, r.DeliveryLocationId))
            .ToHashSet();
        var (usesPickup, usesDelivery) = billingLevel.NamedLocations();
        var seen = new HashSet<(VehicleId, LocationId?, LocationId?)>();
        foreach (var vehicleRate in vehicleRates)
        {
            if ((vehicleRate.PickupLocationId is not null) != usesPickup
                || (vehicleRate.DeliveryLocationId is not null) != usesDelivery)
            {
                return ContractErrors.VehicleRateLocationsMismatch;
            }

            if (CheckRateTerms(pricingMode, nightSurcharge, vehicleRate.Rate) is { } invalid)
            {
                return invalid;
            }

            if (!seen.Add((vehicleRate.VehicleId, vehicleRate.PickupLocationId, vehicleRate.DeliveryLocationId)))
            {
                return ContractErrors.VehicleRateDuplicate;
            }

            if (billingLevel != BillingLevel.FlatRate
                && !ratedLocations.Contains((vehicleRate.PickupLocationId, vehicleRate.DeliveryLocationId)))
            {
                return ContractErrors.VehicleRateWithoutLocationRate;
            }
        }

        return null;
    }

    private static Error? CheckLocationRates(
        BillingLevel billingLevel,
        PricingMode pricingMode,
        bool nightSurcharge,
        IReadOnlyList<LocationRate> locationRates)
    {
        if (locationRates.Count == 0)
        {
            return billingLevel == BillingLevel.FlatRate ? null : ContractErrors.LocationRatesRequired;
        }

        if (billingLevel == BillingLevel.FlatRate)
        {
            return ContractErrors.LocationRateNotApplicable;
        }

        var (usesPickup, usesDelivery) = billingLevel.NamedLocations();
        var seen = new HashSet<(LocationId?, LocationId?)>();
        foreach (var locationRate in locationRates)
        {
            if ((locationRate.PickupLocationId is not null) != usesPickup
                || (locationRate.DeliveryLocationId is not null) != usesDelivery)
            {
                return ContractErrors.LocationRateLocationsMismatch;
            }

            if (CheckRateTerms(pricingMode, nightSurcharge, locationRate.Rate) is { } invalid)
            {
                return invalid;
            }

            if (!seen.Add((locationRate.PickupLocationId, locationRate.DeliveryLocationId)))
            {
                return ContractErrors.LocationRateDuplicate;
            }
        }

        return null;
    }

    private static Error? CheckRateTerms(PricingMode pricingMode, bool nightSurcharge, Rate rate)
    {
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

    // The vehicle rates and the location rates compare as sets: each vehicle, location or pair appears at most once, and their order carries no meaning.
    private bool SameTerms(
        Validity validity,
        BillingLevel billingLevel,
        PricingMode pricingMode,
        RequiredDocument[] requiredDocuments,
        bool nightSurcharge,
        Rate? rate,
        IReadOnlyList<VehicleRate> vehicleRates,
        IReadOnlyList<LocationRate> locationRates)
    {
        var current = VehicleRates;
        var currentLocationRates = LocationRates;
        return Validity == validity
            && BillingLevel == billingLevel
            && PricingMode == pricingMode
            && RequiredDocuments.SequenceEqual(requiredDocuments)
            && NightSurcharge == nightSurcharge
            && Rate == rate
            && current.Count == vehicleRates.Count
            && vehicleRates.All(current.Contains)
            && currentLocationRates.Count == locationRates.Count
            && locationRates.All(currentLocationRates.Contains);
    }

    private static RequiredDocument[] Canonical(IEnumerable<RequiredDocument> documents) =>
        [.. documents.Distinct().Order()];

    private void Touch(DateTime now, UserId modifiedBy)
    {
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
        RaiseEvent(new ContractUpdated(
            Guid.NewGuid(), now, Id, CustomerId, Validity.ValidFrom, Validity.ValidTo, BillingLevel,
            PricingMode, RequiredDocuments, NightSurcharge, Rate, VehicleRates, LocationRates));
    }
}
