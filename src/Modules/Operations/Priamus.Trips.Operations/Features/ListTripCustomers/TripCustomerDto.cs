namespace Priamus.Trips.Operations.Features.ListTripCustomers;

/// <summary>
/// A customer the driver can choose for a trip.
/// </summary>
/// <param name="Id">The customer's identifier.</param>
/// <param name="CompanyName">The company name.</param>
/// <param name="VatNumber">The VAT number; <c>null</c> when the customer has none.</param>
public sealed record TripCustomerDto(Guid Id, string CompanyName, string? VatNumber);
