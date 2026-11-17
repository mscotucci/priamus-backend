using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The cross-field rules shared by the create and edit requests (PRD RF-26, RF-27): the description
/// is measured once trimmed, as the aggregate stores it, and only a tractor may require a trailer.
/// </summary>
internal static class VehicleRequestValidation
{
    /// <summary>Yields one result per broken rule, each on the field it concerns.</summary>
    public static IEnumerable<ValidationResult> Validate(string? description, VehicleType? type, bool? requiresTrailer)
    {
        if (description is not null && description.Trim().Length > Vehicle.DescriptionMaxLength)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    RegistryMessages.Validation_MaxLength,
                    "Description",
                    Vehicle.DescriptionMaxLength),
                ["Description"]);
        }

        if (type is { } vehicleType && requiresTrailer is { } trailer
            && !Vehicle.IsRequiresTrailerAllowed(vehicleType, trailer))
        {
            yield return new ValidationResult(
                RegistryMessages.Validation_RequiresTrailerNotAllowed,
                ["RequiresTrailer"]);
        }
    }
}
