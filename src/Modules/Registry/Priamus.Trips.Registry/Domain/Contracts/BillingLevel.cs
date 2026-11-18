namespace Priamus.Trips.Registry.Domain.Contracts;

/// <summary>
/// How finely the rates of a <see cref="Contract"/> are set, and so which locations a trip must
/// name (PRD §5.4).
/// </summary>
public enum BillingLevel
{
    /// <summary>Forfettario: one rate for the customer.</summary>
    FlatRate,

    /// <summary>Per luogo di partenza.</summary>
    PerPickupLocation,

    /// <summary>Per luogo di consegna.</summary>
    PerDeliveryLocation,

    /// <summary>Per coppia di luoghi.</summary>
    PerLocationPair,
}
