using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// One vehicle rate of a contract request: the vehicle, the pickup and the delivery location the billing
/// level uses (none on a flat-rate contract) and the seven rate members, as sent. Every
/// member is optional here so that a missing or misplaced value is reported by
/// <see cref="ContractRequestValidation"/> on its own key, <c>VehicleRates[i].Member</c>.
/// </summary>
public sealed record ContractVehicleRateRequest(
    Guid? VehicleId,
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
    /// Builds the vehicle rates of a validated request; an absent list is an empty one.
    /// </summary>
    internal static Result<IReadOnlyList<VehicleRate>> ToVehicleRates(IReadOnlyList<ContractVehicleRateRequest>? items)
    {
        var vehicleRates = new List<VehicleRate>();
        foreach (var item in items ?? [])
        {
            // VehicleId and the items are non-null: request validation has already run.
            var rate = item.RateMembers().ToRate();
            if (rate.IsFailure)
            {
                return rate.Error;
            }

            vehicleRates.Add(new VehicleRate(
                new VehicleId(item.VehicleId!.Value),
                item.PickupLocationId is { } pickup ? new LocationId(pickup) : null,
                item.DeliveryLocationId is { } delivery ? new LocationId(delivery) : null,
                rate.Value!));
        }

        return Result<IReadOnlyList<VehicleRate>>.Success(vehicleRates);
    }
}
