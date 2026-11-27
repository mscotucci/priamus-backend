using Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.Features.GetTripQuestions;

/// <summary>
/// What the contract of a customer today asks of the driver, never its terms.
/// </summary>
/// <param name="LocationsAsked">Which locations the contract asks for.</param>
/// <param name="Places">The locations or pairs the contract offers; empty when it asks for none.</param>
/// <param name="LastPlace">The place of the driver's latest trip for the customer, when the contract
/// still offers it; <c>null</c> otherwise.</param>
public sealed record TripQuestionsDto(
    TripLocationsAsked LocationsAsked,
    IReadOnlyList<TripPlaceDto> Places,
    TripPlaceDto? LastPlace);
