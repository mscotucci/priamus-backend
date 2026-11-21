using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Features.Validation;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.UpdateNightBand;

/// <summary>
/// Request body for <c>PUT /api/registry/night-band</c>. <c>Start</c> and <c>End</c> are <c>HH:mm</c>;
/// <c>Version</c> is the version the client read, and the save is refused when the band has changed since.
/// </summary>
[ValidatableType]
public sealed record UpdateNightBandRequest(
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    [TimeOfDayFormat]
    string Start,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    [TimeOfDayFormat]
    string End,
    [Required(ErrorMessageResourceType = typeof(RegistryMessages), ErrorMessageResourceName = nameof(RegistryMessages.Validation_Required))]
    int? Version) : IValidatableObject
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TimeOfDayFormatAttribute.Parse(Start) is { } start
            && TimeOfDayFormatAttribute.Parse(End) is { } end
            && NightBand.Create(start, end).IsFailure)
        {
            yield return new ValidationResult(
                RegistryMessages.Registry_NightBand_EndEqualsStart,
                [nameof(End)]);
        }
    }
}
