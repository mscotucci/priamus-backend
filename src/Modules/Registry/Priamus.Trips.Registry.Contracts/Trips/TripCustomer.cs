namespace Priamus.Trips.Registry.Contracts;

/// <summary>A customer a trip is carried for.</summary>
/// <param name="Id">The identifier of the customer.</param>
/// <param name="CompanyName">The name the customer is known by.</param>
/// <param name="VatNumber">The VAT number, when the customer has one.</param>
public sealed record TripCustomer(Guid Id, string CompanyName, string? VatNumber);
