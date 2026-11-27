namespace Priamus.Trips.Registry.Contracts;

/// <summary>
/// Everything a trip keeps when it is saved. It is the one type of this folder that carries prices: it is
/// for the trip's record and never for a driver response.
/// </summary>
/// <param name="ContractId">The contract in force that priced the trip.</param>
/// <param name="Measure">What the contract asked the trip to record about its size.</param>
/// <param name="RequiredDocuments">The names of the documents the contract required at that moment.</param>
/// <param name="Pricing">What the trip was priced with and the amount.</param>
public sealed record PricedTrip(
    Guid ContractId,
    TripMeasureKind Measure,
    IReadOnlyList<string> RequiredDocuments,
    TripPricing Pricing);
