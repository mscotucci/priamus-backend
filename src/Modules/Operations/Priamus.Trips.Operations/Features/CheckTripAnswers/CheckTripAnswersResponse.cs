namespace Priamus.Trips.Operations.Features.CheckTripAnswers;

/// <summary>
/// Which answers of a trip still hold. An answer that was not sent is <c>true</c>: there is nothing to
/// empty.
/// </summary>
/// <param name="Vehicle">The leading vehicle is active and a tractor or a standalone vehicle.</param>
/// <param name="Trailer">The trailer is active, a trailer, and fits the leading vehicle.</param>
/// <param name="Customer">The customer is active and has a contract in force today.</param>
/// <param name="Pickup">The pickup location is active and accepted by the contract in force.</param>
/// <param name="Delivery">The delivery location is active and accepted by the contract in force.</param>
public sealed record CheckTripAnswersResponse(bool Vehicle, bool Trailer, bool Customer, bool Pickup, bool Delivery);
