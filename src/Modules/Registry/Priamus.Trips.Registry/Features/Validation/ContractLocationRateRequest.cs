using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// One location rate of a contract request: the pickup and the delivery location the billing level
/// uses and the seven rate members, as sent. Every member is optional here so that a missing or
/// misplaced value is reported by <see cref="ContractRequestValidation"/> on its own key,
/// <c>LocationRates[i].Member</c>.
/// </summary>
public sealed record ContractLocationRateRequest(
    Guid? PickupLocationId,
    Guid? DeliveryLocationId,
    decimal? DayPrice,
    decimal? NightPrice,
    decimal? NightCallFee,
    MinimumRuleKind? MinimumRule,
    decimal? MinimumAmount,
    decimal? MinimumThreshold,
    decimal? MinimumOveragePrice)
{
    /// <summary>The rate members, gathered for validation and for building the rate.</summary>
    internal ContractRateMembers RateMembers() =>
        new(DayPrice, NightPrice, NightCallFee, MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice);

    /// <summary>
    /// Builds the location rates of a validated request; an absent list is an empty one.
    /// </summary>
    internal static Result<IReadOnlyList<LocationRate>> ToLocationRates(IReadOnlyList<ContractLocationRateRequest>? items)
    {
        var locationRates = new List<LocationRate>();
        foreach (var item in items ?? [])
        {
            // The items and their day prices are non-null: request validation has already run.
            var rate = item.RateMembers().ToRate();
            if (rate.IsFailure)
            {
                return rate.Error;
            }

            locationRates.Add(new LocationRate(
                item.PickupLocationId is { } pickup ? new LocationId(pickup) : null,
                item.DeliveryLocationId is { } delivery ? new LocationId(delivery) : null,
                rate.Value!));
        }

        return Result<IReadOnlyList<LocationRate>>.Success(locationRates);
    }
}
