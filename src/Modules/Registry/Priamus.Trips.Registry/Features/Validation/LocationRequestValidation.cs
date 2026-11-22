using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The length rules shared by the create and edit requests (PRD §5.1, RF-23): the name, the
/// municipality and the address are measured once trimmed, as the aggregate stores them.
/// </summary>
internal static class LocationRequestValidation
{
    /// <summary>Yields one result per broken rule, each on the field it concerns.</summary>
    public static IEnumerable<ValidationResult> Validate(string? name, string? municipality, string? address)
    {
        if (name is not null && name.Trim().Length > Location.NameMaxLength)
        {
            yield return MaxLength("Name", Location.NameMaxLength);
        }

        if (municipality is not null && municipality.Trim().Length > Location.MunicipalityMaxLength)
        {
            yield return MaxLength("Municipality", Location.MunicipalityMaxLength);
        }

        if (address is not null && address.Trim().Length > Location.AddressMaxLength)
        {
            yield return MaxLength("Address", Location.AddressMaxLength);
        }
    }

    private static ValidationResult MaxLength(string field, int maxLength) =>
        new(
            string.Format(CultureInfo.InvariantCulture, RegistryMessages.Validation_MaxLength, field, maxLength),
            [field]);
}
