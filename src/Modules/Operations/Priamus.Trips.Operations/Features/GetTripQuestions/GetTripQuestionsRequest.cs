using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Validation;
using Priamus.Trips.Operations.Resources;

namespace Priamus.Trips.Operations.Features.GetTripQuestions;

/// <summary>
/// Query-string parameters for <c>GET /api/operations/driver/trip-questions</c>.
/// </summary>
/// <remarks>
/// Mutable class (not record) required by ASP.NET Core query-string model binding.
/// Bound via <c>[AsParameters]</c> in the endpoint delegate signature.
/// </remarks>
[ValidatableType]
public sealed class GetTripQuestionsRequest
{
    /// <summary>The customer the trip is for.</summary>
    [Required(ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Required))]
    public Guid? CustomerId { get; set; }
}
