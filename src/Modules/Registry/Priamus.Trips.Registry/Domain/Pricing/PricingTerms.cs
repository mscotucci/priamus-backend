using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>The terms of a contract the engine prices with, saved or as written.</summary>
/// <param name="Mode">How the amount of a trip is measured.</param>
/// <param name="NightSurcharge">Whether a night trip is priced with the night price and the night call fee.</param>
/// <param name="BillingLevel">What the contract is priced per: the whole customer or a location or pair.</param>
/// <param name="Rate">The contract's rate; <c>null</c> on a location level, which has none.</param>
/// <param name="VehicleRates">The rates of single vehicles, which replace the contract's rate for them.</param>
/// <param name="LocationRates">The rates of the locations or pairs of a location level.</param>
public sealed record PricingTerms(
    PricingMode Mode,
    bool NightSurcharge,
    BillingLevel BillingLevel,
    Rate? Rate,
    IReadOnlyList<VehicleRate> VehicleRates,
    IReadOnlyList<LocationRate> LocationRates);
