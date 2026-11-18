namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>How the amount of a trip is measured under a <see cref="Contract"/> (PRD §5.4).</summary>
public enum PricingMode
{
    /// <summary>A ore.</summary>
    ByHour,

    /// <summary>A peso.</summary>
    ByWeight,

    /// <summary>A km.</summary>
    ByDistance,

    /// <summary>A viaggio.</summary>
    ByTrip,
}
