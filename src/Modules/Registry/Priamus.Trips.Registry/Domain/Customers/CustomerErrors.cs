using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Customers;

/// <summary>
/// Errors raised by the <see cref="Customer"/> aggregate, its value objects and the
/// application-layer lookups on customers.
/// </summary>
/// <remarks>
/// Codes follow the <c>Registry.Customer.{ErrorName}</c> PascalCase convention.
/// </remarks>
public static class CustomerErrors
{
    // ─── Value-object and aggregate validation ────────────────────────────

    /// <summary>
    /// The company name is empty or longer than <see cref="Customer.CompanyNameMaxLength"/>
    /// characters once trimmed (PRD §4, RF-15).
    /// </summary>
    public static readonly Error CompanyNameInvalid = Error.Validation(
        "Registry.Customer.CompanyNameInvalid",
        "Company name is required and must be at most 150 characters.");

    /// <summary>
    /// The address is longer than <see cref="Customer.AddressMaxLength"/> characters once trimmed
    /// (PRD §4, RF-15).
    /// </summary>
    public static readonly Error AddressInvalid = Error.Validation(
        "Registry.Customer.AddressInvalid",
        "Address must be at most 200 characters.");

    /// <summary>
    /// The VAT number, once normalised, is not 11 digits with a valid Italian check digit
    /// (PRD §4, RF-15).
    /// </summary>
    public static readonly Error VatNumberInvalid = Error.Validation(
        "Registry.Customer.VatNumberInvalid",
        "VAT number must be 11 digits with a valid check digit, spaces and a leading IT aside.");

    /// <summary>
    /// The tax code, once normalised, is neither 16 letters and digits nor 11 digits
    /// (PRD §4, RF-15).
    /// </summary>
    public static readonly Error TaxCodeInvalid = Error.Validation(
        "Registry.Customer.TaxCodeInvalid",
        "Tax code must be 16 letters and digits or 11 digits, spaces aside.");

    // ─── Application-layer lookups ────────────────────────────────────────

    /// <summary>The requested customer was not found.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Registry.Customer.NotFound",
        "The requested customer was not found.");

    /// <summary>
    /// The VAT number belongs to another active customer. Uniqueness is enforced by a unique
    /// index and checked upfront by the application layer (PRD §4, RF-15).
    /// </summary>
    public static readonly Error VatNumberTaken = Error.Conflict(
        "Registry.Customer.VatNumberTaken",
        "The VAT number is already registered on another customer.");

    /// <summary>
    /// The VAT number belongs to a deactivated customer, which can be reactivated instead
    /// (PRD §4, RF-15).
    /// </summary>
    public static readonly Error VatNumberTakenByDeactivated = Error.Conflict(
        "Registry.Customer.VatNumberTakenByDeactivated",
        "The VAT number is already registered on a deactivated customer.");

    /// <summary>
    /// The VAT number was taken by a save that won the race on the unique index, and the holder can
    /// no longer be found to be named (PRD §4, RF-15).
    /// </summary>
    public static readonly Error VatNumberTakenByAnother = Error.Conflict(
        "Registry.Customer.VatNumberTakenByAnother",
        "The VAT number is already registered on another customer.");

    /// <summary>
    /// The version sent with a write does not match the stored one: someone else changed
    /// the customer in the meantime.
    /// </summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Registry.Customer.ConcurrentUpdate",
        "The customer was modified by another user in the meantime.");
}
