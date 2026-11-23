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

/// <summary>What a <see cref="BillingLevel"/> says about the locations a rate names.</summary>
public static class BillingLevelExtensions
{
    /// <summary>
    /// Which of the pickup and the delivery location a rate of this level names: both for a pair,
    /// one for a pickup or delivery level, none for a flat rate.
    /// </summary>
    public static (bool Pickup, bool Delivery) NamedLocations(this BillingLevel billingLevel) =>
        (billingLevel is BillingLevel.PerPickupLocation or BillingLevel.PerLocationPair,
         billingLevel is BillingLevel.PerDeliveryLocation or BillingLevel.PerLocationPair);
}
