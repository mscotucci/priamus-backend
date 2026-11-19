using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The length and format rules shared by the create and edit requests (PRD §6.5, RF-24): the names
/// and the phone number are measured once trimmed, as the aggregate stores them; the phone rule is
/// the aggregate's own.
/// </summary>
internal static class DriverRequestValidation
{
    /// <summary>Yields one result per broken rule, each on the field it concerns.</summary>
    public static IEnumerable<ValidationResult> Validate(string? firstName, string? lastName, string? phone)
    {
        if (firstName is not null && firstName.Trim().Length > Driver.FirstNameMaxLength)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    RegistryMessages.Validation_MaxLength,
                    "FirstName",
                    Driver.FirstNameMaxLength),
                ["FirstName"]);
        }

        if (lastName is not null && lastName.Trim().Length > Driver.LastNameMaxLength)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    RegistryMessages.Validation_MaxLength,
                    "LastName",
                    Driver.LastNameMaxLength),
                ["LastName"]);
        }

        if (!Driver.IsPhoneValid(phone))
        {
            yield return new ValidationResult(RegistryMessages.Validation_Phone, ["Phone"]);
        }
    }
}
