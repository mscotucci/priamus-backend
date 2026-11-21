using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

namespace Priamus.Trips.Registry.Domain.Pricing;

/// <summary>The terms of a flat-rate contract the engine prices with, saved or as written.</summary>
/// <param name="Mode">How the amount of a trip is measured.</param>
/// <param name="NightSurcharge">Whether a night trip is priced with the night price and the night call fee.</param>
/// <param name="Rate">The contract's rate.</param>
/// <param name="VehicleRates">The rates of single vehicles, which replace the contract's rate for them.</param>
public sealed record PricingTerms(
    PricingMode Mode,
    bool NightSurcharge,
    Rate Rate,
    IReadOnlyList<VehicleRate> VehicleRates);
