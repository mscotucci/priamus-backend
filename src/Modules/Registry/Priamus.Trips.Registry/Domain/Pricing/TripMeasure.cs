using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>
/// What of a trip the engine measures, one case per <see cref="PricingMode"/>; only the start
/// time classifies the trip as day or night (PRD §5.8).
/// </summary>
public abstract record TripMeasure
{
    /// <summary>The most kilograms a trip by weight holds.</summary>
    public const int MaxKilograms = 99_999;

    /// <summary>The most kilometres a trip by distance holds.</summary>
    public const int MaxKilometres = 9_999;

    private TripMeasure(TimeOnly start) => Start = start;

    /// <summary>The pricing mode the measure belongs to.</summary>
    public abstract PricingMode Mode { get; }

    /// <summary>The time the trip starts.</summary>
    public TimeOnly Start { get; }

    /// <summary>A trip by hour: from its start to the end time, the next day when the end is earlier.</summary>
    public sealed record ByHour : TripMeasure
    {
        private ByHour(TimeOnly start, TimeOnly end, int minutes)
            : base(start)
        {
            End = end;
            Minutes = minutes;
        }

        /// <inheritdoc />
        public override PricingMode Mode => PricingMode.ByHour;

        /// <summary>The time the trip ends.</summary>
        public TimeOnly End { get; }

        /// <summary>The exact duration in whole minutes, from 1 to 1439.</summary>
        public int Minutes { get; }

        /// <summary>Builds the measure.</summary>
        /// <param name="start">The start time.</param>
        /// <param name="end">The end time; earlier than the start means the next day.</param>
        /// <returns>A failure when the end is the same minute as the start.</returns>
        public static Result<ByHour> Create(TimeOnly start, TimeOnly end)
        {
            var startMinute = (start.Hour * 60) + start.Minute;
            var endMinute = (end.Hour * 60) + end.Minute;
            if (startMinute == endMinute)
            {
                return PricingErrors.TripEndEqualsStart;
            }

            var minutes = endMinute > startMinute ? endMinute - startMinute : endMinute + (24 * 60) - startMinute;
            return new ByHour(start, end, minutes);
        }
    }

    /// <summary>A trip by weight, in whole kilograms.</summary>
    public sealed record ByWeight : TripMeasure
    {
        private ByWeight(int kilograms, TimeOnly start)
            : base(start) => Kilograms = kilograms;

        /// <inheritdoc />
        public override PricingMode Mode => PricingMode.ByWeight;

        /// <summary>The weight, from 1 to <see cref="TripMeasure.MaxKilograms"/>.</summary>
        public int Kilograms { get; }

        /// <summary>Builds the measure.</summary>
        /// <param name="kilograms">Whole kilograms, from 1 to <see cref="TripMeasure.MaxKilograms"/>.</param>
        /// <param name="start">The start time.</param>
        /// <returns>A failure when the kilograms are out of range.</returns>
        public static Result<ByWeight> Create(int kilograms, TimeOnly start)
        {
            if (kilograms <= 0)
            {
                return PricingErrors.QuantityNotPositive;
            }

            return kilograms > MaxKilograms ? PricingErrors.KilogramsTooLarge : new ByWeight(kilograms, start);
        }
    }

    /// <summary>A trip by distance, in whole kilometres.</summary>
    public sealed record ByDistance : TripMeasure
    {
        private ByDistance(int kilometres, TimeOnly start)
            : base(start) => Kilometres = kilometres;

        /// <inheritdoc />
        public override PricingMode Mode => PricingMode.ByDistance;

        /// <summary>The distance, from 1 to <see cref="TripMeasure.MaxKilometres"/>.</summary>
        public int Kilometres { get; }

        /// <summary>Builds the measure.</summary>
        /// <param name="kilometres">Whole kilometres, from 1 to <see cref="TripMeasure.MaxKilometres"/>.</param>
        /// <param name="start">The start time.</param>
        /// <returns>A failure when the kilometres are out of range.</returns>
        public static Result<ByDistance> Create(int kilometres, TimeOnly start)
        {
            if (kilometres <= 0)
            {
                return PricingErrors.QuantityNotPositive;
            }

            return kilometres > MaxKilometres ? PricingErrors.KilometresTooLarge : new ByDistance(kilometres, start);
        }
    }

    /// <summary>A trip priced by the trip itself.</summary>
    public sealed record ByTrip : TripMeasure
    {
        /// <summary>Builds the measure.</summary>
        /// <param name="start">The start time.</param>
        public ByTrip(TimeOnly start)
            : base(start)
        {
        }

        /// <inheritdoc />
        public override PricingMode Mode => PricingMode.ByTrip;
    }
}
