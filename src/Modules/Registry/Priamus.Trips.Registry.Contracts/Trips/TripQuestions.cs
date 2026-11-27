namespace Priamus.Trips.Registry.Contracts;

/// <summary>What the contract in force for a customer on a day asks of a trip.</summary>
/// <param name="LocationsAsked">Which locations the trip must name.</param>
/// <param name="Measure">What the trip must record about its size.</param>
/// <param name="Places">
/// The places the contract accepts, all of them active locations; empty when no location is asked.
/// </param>
public sealed record TripQuestions(
    TripLocationsAsked LocationsAsked,
    TripMeasureKind Measure,
    IReadOnlyList<TripPlace> Places);
