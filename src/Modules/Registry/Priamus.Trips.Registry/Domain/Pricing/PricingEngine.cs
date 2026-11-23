using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>
/// The amount of a trip (PRD §5.5–§5.9, ADR 0003): a pure function, no
/// state, no I/O, no clock. The night call fee is added after the minimum rule, which sees the
/// base amount alone.
/// </summary>
internal static class PricingEngine
{
    /// <summary>Computes the amount of a trip.</summary>
    /// <param name="terms">The terms of the contract.</param>
    /// <param name="vehicle">The trip's leading vehicle; <c>null</c>, or a vehicle without a rate of its own on the trip's location, uses the contract's rate on a flat-rate contract and the rate of the location or pair on a location level.</param>
    /// <param name="pickup">The trip's pickup location; a flat-rate contract ignores it.</param>
    /// <param name="delivery">The trip's delivery location; a flat-rate contract ignores it.</param>
    /// <param name="trip">The trip; its mode must be the mode of <paramref name="terms"/>.</param>
    /// <param name="band">The night band.</param>
    /// <returns>The amount and its steps, <see cref="PricingErrors.NoRateForLocation"/> or <see cref="PricingErrors.AmountTooLarge"/>.</returns>
    /// <exception cref="ArgumentException">The mode of the trip differs from the mode of the terms.</exception>
    /// <exception cref="InvalidOperationException">The terms lack a rate or a night value they need, or a trip priced by trip has a tiered overage.</exception>
    public static Result<PriceCalculation> Calculate(
        PricingTerms terms,
        VehicleId? vehicle,
        LocationId? pickup,
        LocationId? delivery,
        TripMeasure trip,
        NightBand band)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(band);
        if (trip.Mode != terms.Mode)
        {
            throw new ArgumentException("The trip is measured in a different pricing mode than the terms.", nameof(trip));
        }

        VehicleRate? vehicleRate = null;
        Rate rate;
        RateSource rateSource;
        if (terms.BillingLevel == BillingLevel.FlatRate)
        {
            vehicleRate = vehicle is { } id ? terms.VehicleRates.FirstOrDefault(r => r.VehicleId == id) : null;
            rate = vehicleRate?.Rate ?? terms.Rate ?? throw new InvalidOperationException("A flat-rate contract needs a rate.");
            rateSource = vehicleRate is null ? RateSource.Contract : RateSource.Vehicle;
        }
        else
        {
            var locationRate = terms.LocationRates.FirstOrDefault(r => IsRateOf(terms.BillingLevel, r, pickup, delivery));
            if (locationRate is null)
            {
                return PricingErrors.NoRateForLocation;
            }

            vehicleRate = vehicle is { } id
                ? terms.VehicleRates.FirstOrDefault(r =>
                    r.VehicleId == id
                    && r.PickupLocationId == locationRate.PickupLocationId
                    && r.DeliveryLocationId == locationRate.DeliveryLocationId)
                : null;
            rate = vehicleRate?.Rate ?? locationRate.Rate;
            rateSource = vehicleRate is not null
                ? RateSource.Vehicle
                : terms.BillingLevel == BillingLevel.PerLocationPair ? RateSource.LocationPair : RateSource.Location;
        }

        var isNight = band.IsNight(trip.Start);
        var nightPriced = isNight && terms.NightSurcharge;
        var unitPrice = nightPriced
            ? rate.NightPrice ?? throw new InvalidOperationException("A night surcharge needs a night price.")
            : rate.DayPrice;

        var baseAmount = trip switch
        {
            TripMeasure.ByHour h => Round(h.Minutes * unitPrice / 60),
            TripMeasure.ByWeight w => Round(w.Kilograms * unitPrice / 1000),
            TripMeasure.ByDistance d => Round(d.Kilometres * unitPrice),
            _ => unitPrice,
        };

        var afterMinimum = ApplyMinimum(rate.Minimum, trip, baseAmount);

        decimal? nightCallFee = nightPriced
            ? rate.NightCallFee ?? throw new InvalidOperationException("A night surcharge needs a night call fee.")
            : null;

        var total = afterMinimum + (nightCallFee ?? 0m);
        if (total > Rate.MaxAmount)
        {
            return PricingErrors.AmountTooLarge;
        }

        return new PriceCalculation(
            total,
            isNight ? TimeBand.Night : TimeBand.Day,
            terms.NightSurcharge,
            band,
            rateSource,
            vehicleRate?.VehicleId,
            QuantityOf(trip),
            unitPrice,
            baseAmount,
            rate.Minimum,
            afterMinimum,
            afterMinimum != baseAmount,
            nightCallFee);
    }

    // An absent location never matches: the key of the level must be present in the trip.
    private static bool IsRateOf(BillingLevel level, LocationRate rate, LocationId? pickup, LocationId? delivery) => level switch
    {
        BillingLevel.PerPickupLocation => pickup is not null && rate.PickupLocationId == pickup,
        BillingLevel.PerDeliveryLocation => delivery is not null && rate.DeliveryLocationId == delivery,
        _ => pickup is not null && delivery is not null && rate.PickupLocationId == pickup && rate.DeliveryLocationId == delivery,
    };

    private static int? QuantityOf(TripMeasure trip) => trip switch
    {
        TripMeasure.ByHour h => h.Minutes,
        TripMeasure.ByWeight w => w.Kilograms,
        TripMeasure.ByDistance d => d.Kilometres,
        _ => null,
    };

    private static decimal ApplyMinimum(MinimumRule minimum, TripMeasure trip, decimal baseAmount) => minimum switch
    {
        MinimumRule.FixedMinimum f => Math.Max(baseAmount, f.Amount),
        MinimumRule.TieredOverage t => t.Amount + Round(Overage(t, trip)),
        _ => baseAmount,
    };

    // The threshold is in hours, tons or kilometres and the overage price per hour, ton or kilometre:
    // minutes and kilograms are brought to the same unit, multiplying before dividing.
    private static decimal Overage(MinimumRule.TieredOverage tiered, TripMeasure trip) => trip switch
    {
        TripMeasure.ByHour h => Math.Max(0m, h.Minutes - (tiered.Threshold * 60)) * tiered.OveragePrice / 60,
        TripMeasure.ByWeight w => Math.Max(0m, w.Kilograms - (tiered.Threshold * 1000)) * tiered.OveragePrice / 1000,
        TripMeasure.ByDistance d => Math.Max(0m, d.Kilometres - tiered.Threshold) * tiered.OveragePrice,
        _ => throw new InvalidOperationException("A trip priced by trip has no tiered overage."),
    };

    private static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
