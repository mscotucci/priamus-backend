using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>Builds the <see cref="TripMeasure"/> of a pricing mode from the values of a trip.</summary>
internal static class TripMeasureFactory
{
    /// <summary>Builds the measure of <paramref name="mode"/>; the values the mode does not use are ignored.</summary>
    /// <param name="mode">The pricing mode.</param>
    /// <param name="start">The start time.</param>
    /// <param name="end">The end time, needed by a trip priced by hour.</param>
    /// <param name="kilograms">The kilograms, needed by a trip priced by weight.</param>
    /// <param name="kilometres">The kilometres, needed by a trip priced by distance.</param>
    /// <returns>The measure, <see cref="PricingErrors.MeasureMissing"/> when a value the mode needs is absent, or the error of the measure.</returns>
    public static Result<TripMeasure> Create(
        PricingMode mode, TimeOnly start, TimeOnly? end, int? kilograms, int? kilometres) => mode switch
    {
        PricingMode.ByHour => end is { } e ? Widen(TripMeasure.ByHour.Create(start, e)) : PricingErrors.MeasureMissing,
        PricingMode.ByWeight => kilograms is { } kg ? Widen(TripMeasure.ByWeight.Create(kg, start)) : PricingErrors.MeasureMissing,
        PricingMode.ByDistance => kilometres is { } km ? Widen(TripMeasure.ByDistance.Create(km, start)) : PricingErrors.MeasureMissing,
        _ => new TripMeasure.ByTrip(start),
    };

    private static Result<TripMeasure> Widen<T>(Result<T> measure)
        where T : TripMeasure =>
        measure.IsFailure ? measure.Error : measure.Value;
}
