using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.SimulatePrice;

/// <summary>
/// Handles <c>POST /api/registry/contracts/simulation</c>: prices a sample trip on the terms of the
/// request with the current night band, through the same engine as a real trip.
/// </summary>
/// <remarks>
/// The terms come from the request, never from a stored contract, so a contract not yet saved can
/// be tried. Nothing is written.
/// </remarks>
internal sealed class SimulatePriceHandler
{
    private readonly RegistryDbContext _db;

    public SimulatePriceHandler(RegistryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Computes the amount of the sample trip.
    /// </summary>
    public async Task<Result<PriceSimulationDto>> HandleAsync(
        SimulatePriceRequest request,
        CancellationToken cancellationToken)
    {
        // Request validation has already run: the members the billing level needs are present.
        var level = request.BillingLevel!.Value;
        Rate? rate = null;
        if (level == BillingLevel.FlatRate)
        {
            var flatRate = request.RateMembers().ToRate();
            if (flatRate.IsFailure)
            {
                return flatRate.Error;
            }

            rate = flatRate.Value;
        }

        var vehicleRates = ContractVehicleRateRequest.ToVehicleRates(request.VehicleRates);
        if (vehicleRates.IsFailure)
        {
            return vehicleRates.Error;
        }

        var locationRates = ContractLocationRateRequest.ToLocationRates(request.LocationRates);
        if (locationRates.IsFailure)
        {
            return locationRates.Error;
        }

        var terms = new PricingTerms(
            request.PricingMode!.Value,
            request.NightSurcharge!.Value,
            level,
            rate,
            vehicleRates.Value,
            locationRates.Value);

        var trip = BuildTrip(request);
        if (trip.IsFailure)
        {
            return trip.Error;
        }

        var setting = await _db.NightBandSettings.AsNoTracking().SingleAsync(cancellationToken);
        VehicleId? vehicle = request.VehicleId is { } id ? new VehicleId(id) : null;

        LocationId? pickup = request.PickupLocationId is { } pickupId ? new LocationId(pickupId) : null;
        LocationId? delivery = request.DeliveryLocationId is { } deliveryId ? new LocationId(deliveryId) : null;

        var calculation = PricingEngine.Calculate(terms, vehicle, pickup, delivery, trip.Value, setting.Band);
        if (calculation.IsFailure)
        {
            return calculation.Error;
        }

        return PriceSimulationDto.From(calculation.Value);
    }

    private static Result<TripMeasure> BuildTrip(SimulatePriceRequest request)
    {
        var start = TimeOfDayFormatAttribute.Parse(request.StartTime)!.Value;
        return TripMeasureFactory.Create(
            request.PricingMode!.Value,
            start,
            TimeOfDayFormatAttribute.Parse(request.EndTime),
            (int?)request.Kilograms,
            (int?)request.Kilometres);
    }
}
