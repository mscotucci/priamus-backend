using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Domain.Trips;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// Gives Operations the choices of a trip and the names of what a trip refers to, judges the answers of a
/// trip and prices it (ADR 0017). The choices answer in the types of <c>Registry.Contracts</c>, which carry no
/// term of a contract (PRD §5.4, §5.5); only <see cref="PricedTrip"/> carries prices.
/// </summary>
internal sealed class TripRegistry : ITripRegistry
{
    private readonly RegistryDbContext _db;

    public TripRegistry(RegistryDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<TripDriver?> FindDriverAsync(Guid driverId, CancellationToken cancellationToken)
    {
        var id = new DriverId(driverId);
        return await _db.Drivers
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new TripDriver(d.FirstName, d.LastName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TripVehicle>> ListVehiclesAsync(
        TripVehicleRole role, string? search, CancellationToken cancellationToken)
    {
        // RF-08ter, RF-27ter: a trip is led by a tractor or a standalone vehicle, and a trailer follows.
        var query = _db.Vehicles.AsNoTracking().Where(v => v.Status == VehicleStatus.Active);
        query = role == TripVehicleRole.Leading
            ? query.Where(v => v.Type != VehicleType.Trailer)
            : query.Where(v => v.Type == VehicleType.Trailer);

        var text = search?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            query = VehicleSearch.Matching(query, text);
        }

        return await query
            .OrderBy(v => v.LicensePlate)
            .Select(ToTripVehicle)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TripCustomer>> ListCustomersAsync(
        DateOnly day, string? search, CancellationToken cancellationToken)
    {
        var withContract = ContractsInForce(day).Select(c => c.CustomerId);
        var query = _db.Customers
            .AsNoTracking()
            .Where(c => c.Status == CustomerStatus.Active && withContract.Contains(c.Id));

        var needle = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(needle))
        {
            // The VAT number is normalised like the stored value; a needle left empty by it (a bare "IT")
            // would match every row, so it is skipped.
            var vatNeedle = VatNumber.Normalize(needle);
            query = vatNeedle.Length == 0
                ? query.Where(c => c.CompanyName.ToLower().Contains(needle))
                : query.Where(c => c.CompanyName.ToLower().Contains(needle)
                    || c.VatNumber.Unwrap().Contains(vatNeedle));
        }

        var customers = await query
            .OrderBy(c => c.CompanyName)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.CompanyName, c.VatNumber })
            .ToListAsync(cancellationToken);

        return [.. customers.Select(c => new TripCustomer(c.Id.Value, c.CompanyName, c.VatNumber?.Value))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TripLocation>> ListLocationsAsync(
        string? search, CancellationToken cancellationToken)
    {
        var query = _db.Locations.AsNoTracking().Where(l => l.Status == LocationStatus.Active);

        var needle = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(needle))
        {
            query = query.Where(l => l.Name.ToLower().Contains(needle)
                || l.Municipality.ToLower().Contains(needle));
        }

        return await query
            .OrderBy(l => l.Name)
            .ThenBy(l => l.Municipality)
            .ThenBy(l => l.Id)
            .Select(l => new TripLocation(l.Id.Value, l.Name, l.Municipality))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TripQuestions?> FindQuestionsAsync(
        Guid customerId, DateOnly day, CancellationToken cancellationToken)
    {
        var id = new CustomerId(customerId);
        var isActive = await _db.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == id && c.Status == CustomerStatus.Active, cancellationToken);
        if (!isActive)
        {
            return null;
        }

        var contract = await ContractsInForce(day)
            .Where(c => c.CustomerId == id)
            .FirstOrDefaultAsync(cancellationToken);
        if (contract is null)
        {
            return null;
        }

        var (pickup, delivery) = contract.BillingLevel.NamedLocations();
        var locationsAsked = (pickup, delivery) switch
        {
            (true, true) => TripLocationsAsked.Both,
            (true, false) => TripLocationsAsked.Pickup,
            (false, true) => TripLocationsAsked.Delivery,
            _ => TripLocationsAsked.None,
        };

        return new TripQuestions(locationsAsked, MeasureKindOf(contract.PricingMode), await PlacesAsync(contract, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<TripMeasureKind?> FindMeasureAsync(
        Guid customerId, DateOnly day, CancellationToken cancellationToken)
    {
        var contract = await FindContractInForceAsync(new CustomerId(customerId), day, cancellationToken);
        return contract is null ? null : MeasureKindOf(contract.PricingMode);
    }

    /// <inheritdoc />
    public async Task<TripNames> DescribeAsync(
        IReadOnlyCollection<Guid> vehicleIds,
        IReadOnlyCollection<Guid> customerIds,
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken)
    {
        var vehicleKeys = vehicleIds.Select(i => new VehicleId(i)).ToList();
        var customerKeys = customerIds.Select(i => new CustomerId(i)).ToList();
        var locationKeys = locationIds.Select(i => new LocationId(i)).ToList();

        var vehicles = await _db.Vehicles
            .AsNoTracking()
            .Where(v => vehicleKeys.Contains(v.Id))
            .Select(ToTripVehicle)
            .ToListAsync(cancellationToken);

        var customers = await _db.Customers
            .AsNoTracking()
            .Where(c => customerKeys.Contains(c.Id))
            .Select(c => new { c.Id, c.CompanyName, c.VatNumber })
            .ToListAsync(cancellationToken);

        var locations = await _db.Locations
            .AsNoTracking()
            .Where(l => locationKeys.Contains(l.Id))
            .Select(l => new TripLocation(l.Id.Value, l.Name, l.Municipality))
            .ToListAsync(cancellationToken);

        return new TripNames(
            vehicles,
            [.. customers.Select(c => new TripCustomer(c.Id.Value, c.CompanyName, c.VatNumber?.Value))],
            locations);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TripDriverName>> DescribeDriversAsync(
        IReadOnlyCollection<Guid> driverIds, CancellationToken cancellationToken)
    {
        if (driverIds.Count == 0)
        {
            return [];
        }

        var keys = driverIds.Select(i => new DriverId(i)).ToList();
        var drivers = await _db.Drivers
            .AsNoTracking()
            .Where(d => keys.Contains(d.Id))
            .Select(d => new { d.Id, d.FirstName, d.LastName })
            .ToListAsync(cancellationToken);

        return [.. drivers.Select(d => new TripDriverName(d.Id.Value, d.FirstName, d.LastName))];
    }

    /// <inheritdoc />
    public async Task<TripSearchMatches> FindMatchesAsync(string search, CancellationToken cancellationToken)
    {
        var text = search.Trim();
        if (text.Length == 0)
        {
            return new TripSearchMatches([], [], [], []);
        }

        var needle = text.ToLowerInvariant();

        // The VAT number is normalised like the stored value; a needle left empty by it (a bare "IT") would
        // match every row, so it is skipped.
        var vatNeedle = VatNumber.Normalize(needle);
        var customerQuery = _db.Customers.AsNoTracking();
        customerQuery = vatNeedle.Length == 0
            ? customerQuery.Where(c => c.CompanyName.ToLower().Contains(needle))
            : customerQuery.Where(c => c.CompanyName.ToLower().Contains(needle)
                || c.VatNumber.Unwrap().Contains(vatNeedle));
        var customers = await customerQuery.Select(c => c.Id).ToListAsync(cancellationToken);

        var drivers = await _db.Drivers
            .AsNoTracking()
            .Where(d => d.FirstName.ToLower().Contains(needle)
                || d.LastName.ToLower().Contains(needle)
                || (d.FirstName.ToLower() + " " + d.LastName.ToLower()).Contains(needle))
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var vehicles = await VehicleSearch.Matching(_db.Vehicles.AsNoTracking(), text)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        var locations = await _db.Locations
            .AsNoTracking()
            .Where(l => l.Name.ToLower().Contains(needle) || l.Municipality.ToLower().Contains(needle))
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        return new TripSearchMatches(
            [.. customers.Select(i => i.Value)],
            [.. drivers.Select(i => i.Value)],
            [.. vehicles.Select(i => i.Value)],
            [.. locations.Select(i => i.Value)]);
    }

    /// <inheritdoc />
    public async Task<TripAnswersCheck> CheckAnswersAsync(TripAnswers answers, CancellationToken cancellationToken)
    {
        var facts = await LoadFactsAsync(
            answers.VehicleId, answers.TrailerId, answers.CustomerId, answers.Day,
            answers.PickupLocationId, answers.DeliveryLocationId, cancellationToken);

        var vehicle = answers.VehicleId is null || (facts.Leading is { } leading && TripAnswerRules.CanLead(leading));
        var trailer = answers.TrailerId is null
            || (facts.Trailer is { } followed && TripAnswerRules.TrailerHolds(facts.Leading, followed));
        var customer = answers.CustomerId is null || facts.Contract is not null;

        var pickup = answers.PickupLocationId is { } p ? new LocationId(p) : (LocationId?)null;
        var delivery = answers.DeliveryLocationId is { } d ? new LocationId(d) : (LocationId?)null;
        var (pickupHolds, deliveryHolds) = answers.CustomerId is null
            ? TripAnswerRules.LocationsHold(BillingLevel.FlatRate, [], pickup, delivery, facts.ActiveLocations)
            : facts.Contract is { } contract
                ? TripAnswerRules.LocationsHold(
                    contract.BillingLevel, contract.LocationRates, pickup, delivery, facts.ActiveLocations)
                : (pickup is null, delivery is null);

        return new TripAnswersCheck(vehicle, trailer, customer, pickupHolds, deliveryHolds);
    }

    /// <inheritdoc />
    public async Task<Result> ValidateTripAsync(TripAnswers answers, CancellationToken cancellationToken)
    {
        if (answers.VehicleId is null || answers.CustomerId is null)
        {
            return TripErrors.AnswersChanged;
        }

        var facts = await LoadFactsAsync(
            answers.VehicleId, answers.TrailerId, answers.CustomerId, answers.Day,
            answers.PickupLocationId, answers.DeliveryLocationId, cancellationToken);

        if (facts.Leading is not { } leading || !TripAnswerRules.CanLead(leading)
            || facts.Contract is not { } contract
            || (answers.TrailerId is not null
                && (facts.Trailer is not { } followed || !TripAnswerRules.TrailerHolds(leading, followed)))
            || TripAnswerRules.TrailerMissing(leading, answers.TrailerId is not null))
        {
            return TripErrors.AnswersChanged;
        }

        var pickup = answers.PickupLocationId is { } pickupId ? new LocationId(pickupId) : (LocationId?)null;
        var delivery = answers.DeliveryLocationId is { } deliveryId ? new LocationId(deliveryId) : (LocationId?)null;
        var (pickupHolds, deliveryHolds) = TripAnswerRules.LocationsHold(
            contract.BillingLevel, contract.LocationRates, pickup, delivery, facts.ActiveLocations);
        if (TripAnswerRules.LocationMissing(contract.BillingLevel, pickup, delivery) || !pickupHolds || !deliveryHolds)
        {
            return TripErrors.AnswersChanged;
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<PricedTrip>> PriceTripAsync(TripToPrice trip, CancellationToken cancellationToken)
    {
        // The statuses of the vehicles, the customer and the locations are not asked again: the trip was
        // judged when it was added and started, and what it carries is priced.
        var contract = await FindContractInForceAsync(new CustomerId(trip.CustomerId), trip.Day, cancellationToken);
        if (contract is null)
        {
            return TripErrors.NotPriceable;
        }

        var pickup = trip.PickupLocationId is { } pickupId ? new LocationId(pickupId) : (LocationId?)null;
        var delivery = trip.DeliveryLocationId is { } deliveryId ? new LocationId(deliveryId) : (LocationId?)null;

        // The kilograms and the kilometres must be exactly the ones the pricing mode of the contract in force
        // measures: a missing or an extra one means the contract changed while the trip was under way.
        var (needsKilograms, needsKilometres) = contract.PricingMode switch
        {
            PricingMode.ByWeight => (true, false),
            PricingMode.ByDistance => (false, true),
            _ => (false, false),
        };
        if ((trip.Kilograms is not null) != needsKilograms || (trip.Kilometres is not null) != needsKilometres)
        {
            return TripErrors.MeasureChanged;
        }

        var measure = TripMeasureFactory.Create(
            contract.PricingMode, trip.Start, trip.End, trip.Kilograms, trip.Kilometres);
        if (measure.IsFailure)
        {
            return measure.Error;
        }

        var setting = await _db.NightBandSettings.AsNoTracking().SingleAsync(cancellationToken);
        var terms = new PricingTerms(
            contract.PricingMode,
            contract.NightSurcharge,
            contract.BillingLevel,
            contract.Rate,
            contract.VehicleRates,
            contract.LocationRates);

        var calculation = PricingEngine.Calculate(
            terms, new VehicleId(trip.LeadingVehicleId), pickup, delivery, measure.Value, setting.Band);
        if (calculation.IsFailure)
        {
            return calculation.Error == PricingErrors.NoRateForLocation || calculation.Error == PricingErrors.AmountTooLarge
                ? TripErrors.NotPriceable
                : calculation.Error;
        }

        return new PricedTrip(
            contract.Id.Value,
            MeasureKindOf(contract.PricingMode),
            [.. contract.RequiredDocuments.Select(d => d.ToString())],
            ToTripPricing(calculation.Value, terms, pickup, delivery));
    }

    /// <summary>
    /// The contracts in force on <paramref name="day"/>: started on or before it and not ended before it.
    /// The contracts of one customer never overlap, so a customer has at most one.
    /// </summary>
    private IQueryable<Contract> ContractsInForce(DateOnly day) =>
        _db.Contracts
            .AsNoTracking()
            .Where(c => c.Validity.ValidFrom <= day
                && (c.Validity.ValidTo == null || c.Validity.ValidTo >= day));

    /// <summary>
    /// The contract in force for the customer on <paramref name="day"/>, whatever the status of the customer:
    /// the one lookup the end of a trip prices with and tells the measure by.
    /// </summary>
    private async Task<Contract?> FindContractInForceAsync(
        CustomerId customerId, DateOnly day, CancellationToken cancellationToken) =>
        await ContractsInForce(day)
            .Where(c => c.CustomerId == customerId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The locations, or pairs, the contract has a rate for, kept only when every location a place names is
    /// active, by pickup name then delivery name, compared culture-aware as the database sorts the other lists.
    /// </summary>
    private async Task<IReadOnlyList<TripPlace>> PlacesAsync(Contract contract, CancellationToken cancellationToken)
    {
        var rates = contract.LocationRates;
        if (rates.Count == 0)
        {
            return [];
        }

        var ids = rates
            .SelectMany(r => new[] { r.PickupLocationId, r.DeliveryLocationId })
            .OfType<LocationId>()
            .Distinct()
            .ToList();

        var active = await _db.Locations
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id) && l.Status == LocationStatus.Active)
            .Select(l => new TripLocation(l.Id.Value, l.Name, l.Municipality))
            .ToListAsync(cancellationToken);
        var byId = active.ToDictionary(l => l.Id);

        var places = new List<TripPlace>();
        foreach (var rate in rates)
        {
            // A place whose location is not active is not offered.
            TripLocation? pickup = null;
            TripLocation? delivery = null;
            if ((rate.PickupLocationId is { } p && !byId.TryGetValue(p.Value, out pickup))
                || (rate.DeliveryLocationId is { } d && !byId.TryGetValue(d.Value, out delivery)))
            {
                continue;
            }

            places.Add(new TripPlace(pickup, delivery));
        }

        return [.. places
            .OrderBy(p => p.Pickup?.Name, StringComparer.InvariantCulture)
            .ThenBy(p => p.Delivery?.Name, StringComparer.InvariantCulture)];
    }

    /// <summary>The vehicles and locations the answers name, and the customer's contract in force on the day.</summary>
    private async Task<TripFacts> LoadFactsAsync(
        Guid? leadingId,
        Guid? trailerId,
        Guid? customerId,
        DateOnly day,
        Guid? pickupId,
        Guid? deliveryId,
        CancellationToken cancellationToken)
    {
        var vehicleKeys = new[] { leadingId, trailerId }.OfType<Guid>().Select(i => new VehicleId(i)).ToList();
        var vehicles = await _db.Vehicles
            .AsNoTracking()
            .Where(v => vehicleKeys.Contains(v.Id))
            .ToListAsync(cancellationToken);

        var locationKeys = new[] { pickupId, deliveryId }.OfType<Guid>().Select(i => new LocationId(i)).ToList();
        var active = await _db.Locations
            .AsNoTracking()
            .Where(l => locationKeys.Contains(l.Id) && l.Status == LocationStatus.Active)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        Contract? contract = null;
        if (customerId is { } customer)
        {
            var id = new CustomerId(customer);
            var isActive = await _db.Customers
                .AsNoTracking()
                .AnyAsync(c => c.Id == id && c.Status == CustomerStatus.Active, cancellationToken);
            if (isActive)
            {
                contract = await ContractsInForce(day)
                    .Where(c => c.CustomerId == id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        return new TripFacts(
            vehicles.FirstOrDefault(v => v.Id.Value == leadingId),
            vehicles.FirstOrDefault(v => v.Id.Value == trailerId),
            contract,
            active.ToHashSet());
    }

    private static TripMeasureKind MeasureKindOf(PricingMode mode) => mode switch
    {
        PricingMode.ByHour => TripMeasureKind.StartAndEnd,
        PricingMode.ByWeight => TripMeasureKind.Weight,
        PricingMode.ByDistance => TripMeasureKind.Distance,
        PricingMode.ByTrip => TripMeasureKind.StartOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown pricing mode."),
    };

    /// <summary>
    /// Writes down the calculation with the rate it used. The engine found that rate by the locations the
    /// billing level names, so those are the locations it came from.
    /// </summary>
    private static TripPricing ToTripPricing(
        PriceCalculation calculation, PricingTerms terms, LocationId? pickup, LocationId? delivery)
    {
        var (namesPickup, namesDelivery) = terms.BillingLevel.NamedLocations();
        var ratePickup = namesPickup ? pickup : null;
        var rateDelivery = namesDelivery ? delivery : null;

        var rate = calculation.RateSource switch
        {
            RateSource.Contract => terms.Rate!,
            RateSource.Vehicle => terms.VehicleRates.First(r =>
                r.VehicleId == calculation.VehicleId
                && r.PickupLocationId == ratePickup
                && r.DeliveryLocationId == rateDelivery).Rate,
            _ => terms.LocationRates.First(r =>
                r.PickupLocationId == ratePickup && r.DeliveryLocationId == rateDelivery).Rate,
        };

        return new TripPricing(
            calculation.RateSource switch
            {
                RateSource.Contract => TripRateSource.Contract,
                RateSource.Vehicle => TripRateSource.Vehicle,
                RateSource.Location => TripRateSource.Location,
                _ => TripRateSource.LocationPair,
            },
            calculation.VehicleId?.Value,
            ratePickup?.Value,
            rateDelivery?.Value,
            rate.DayPrice,
            rate.NightPrice,
            rate.NightCallFee,
            calculation.Minimum.Kind switch
            {
                MinimumRuleKind.FixedMinimum => TripMinimumKind.FixedMinimum,
                MinimumRuleKind.TieredOverage => TripMinimumKind.TieredOverage,
                _ => TripMinimumKind.None,
            },
            calculation.Minimum.Amount,
            calculation.Minimum.Threshold,
            calculation.Minimum.OveragePrice,
            calculation.NightBand.Start,
            calculation.NightBand.End,
            calculation.NightSurcharge,
            calculation.Band == TimeBand.Night,
            calculation.Quantity,
            calculation.UnitPrice,
            calculation.BaseAmount,
            calculation.AmountAfterMinimum,
            calculation.MinimumApplied,
            calculation.NightCallFee,
            calculation.Total);
    }

    private sealed record TripFacts(
        Vehicle? Leading, Vehicle? Trailer, Contract? Contract, IReadOnlySet<LocationId> ActiveLocations);

    private static readonly Expression<Func<Vehicle, TripVehicle>> ToTripVehicle = v => new TripVehicle(
        v.Id.Value,
        v.LicensePlate.Value,
        v.Description,
        v.Type == VehicleType.Tractor ? TripVehicleKind.Tractor
            : v.Type == VehicleType.Trailer ? TripVehicleKind.Trailer
            : TripVehicleKind.StandaloneVehicle,
        v.RequiresTrailer);
}
