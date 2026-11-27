namespace Priamus.Trips.Registry.Contracts;

/// <summary>
/// Which answers of a trip still hold. An answer that was not given is <c>true</c>: there is nothing to
/// empty. A customer that does not hold makes the locations not hold.
/// </summary>
/// <param name="Vehicle">The leading vehicle is active and a tractor or a standalone vehicle.</param>
/// <param name="Trailer">The trailer is active, a trailer, and fits the leading vehicle (never with a standalone vehicle).</param>
/// <param name="Customer">The customer is active and has a contract in force on the day.</param>
/// <param name="Pickup">The pickup location is active and accepted by the contract in force.</param>
/// <param name="Delivery">The delivery location is active and accepted by the contract in force.</param>
public sealed record TripAnswersCheck(bool Vehicle, bool Trailer, bool Customer, bool Pickup, bool Delivery);
