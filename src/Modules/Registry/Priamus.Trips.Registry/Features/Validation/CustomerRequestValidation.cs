using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The length rules shared by the create and edit requests (PRD §4, RF-15): the company name and the
/// address are measured once trimmed, as the aggregate stores them.
/// </summary>
internal static class CustomerRequestValidation
{
    /// <summary>Yields one result per broken rule, each on the field it concerns.</summary>
    public static IEnumerable<ValidationResult> Validate(string? companyName, string? address)
    {
        if (companyName is not null && companyName.Trim().Length > Customer.CompanyNameMaxLength)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    RegistryMessages.Validation_MaxLength,
                    "CompanyName",
                    Customer.CompanyNameMaxLength),
                ["CompanyName"]);
        }

        if (address is not null && address.Trim().Length > Customer.AddressMaxLength)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    RegistryMessages.Validation_MaxLength,
                    "Address",
                    Customer.AddressMaxLength),
                ["Address"]);
        }
    }
}
