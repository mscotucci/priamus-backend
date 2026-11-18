using System.ComponentModel.DataAnnotations;
using Priamus.Trips.Registry.Resources;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The cross-field rules shared by the create and edit requests (PRD §5.4, RF-16): the end of the
/// validity is not before its start.
/// </summary>
internal static class ContractRequestValidation
{
    /// <summary>Yields one result per broken rule, each on the field it concerns.</summary>
    public static IEnumerable<ValidationResult> Validate(DateOnly? validFrom, DateOnly? validTo)
    {
        if (validFrom is { } from && validTo is { } to && to < from)
        {
            yield return new ValidationResult(
                RegistryMessages.Registry_Contract_ValidityEndBeforeStart,
                ["ValidTo"]);
        }
    }
}
