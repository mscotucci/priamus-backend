using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>
/// The amount of a trip on a flat-rate contract (PRD §5.5–§5.9, ADR 0003): a pure function, no
/// state, no I/O, no clock. The night call fee is added after the minimum rule, which sees the
/// base amount alone.
/// </summary>
internal static class PricingEngine
{
    /// <summary>Computes the amount of a trip.</summary>
    /// <param name="terms">The terms of the contract.</param>
    /// <param name="vehicle">The trip's leading vehicle; <c>null</c> or without a rate of its own uses the contract's rate.</param>
    /// <param name="trip">The trip; its mode must be the mode of <paramref name="terms"/>.</param>
    /// <param name="band">The night band.</param>
    /// <returns>The amount and its steps, or <see cref="PricingErrors.AmountTooLarge"/>.</returns>
    /// <exception cref="ArgumentException">The mode of the trip differs from the mode of the terms.</exception>
    /// <exception cref="InvalidOperationException">The terms lack a night value they need, or a trip priced by trip has a tiered overage.</exception>
    public static Result<PriceCalculation> Calculate(
        PricingTerms terms,
        VehicleId? vehicle,
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

        var vehicleRate = vehicle is { } id ? terms.VehicleRates.FirstOrDefault(r => r.VehicleId == id) : null;
        var rate = vehicleRate?.Rate ?? terms.Rate;

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
            vehicleRate is null ? RateSource.Contract : RateSource.Vehicle,
            vehicleRate?.VehicleId,
            QuantityOf(trip),
            unitPrice,
            baseAmount,
            rate.Minimum,
            afterMinimum,
            afterMinimum != baseAmount,
            nightCallFee);
    }

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
