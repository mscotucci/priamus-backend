using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Domain.Contracts.ValueObjects;

/// <summary>
/// The stored shape of a <see cref="VehicleRate"/>: one row of <c>registry.contract_vehicle_rates</c>,
/// owned by its <see cref="Contract"/>. The rate is held as its stored columns, as the contract holds its own.
/// </summary>
internal sealed class StoredVehicleRate
{
    private StoredVehicleRate()
    {
        // EF only.
    }

    internal StoredVehicleRate(VehicleRate vehicleRate)
    {
        var rate = vehicleRate.Rate;
        VehicleId = vehicleRate.VehicleId;
        DayPrice = rate.DayPrice;
        NightPrice = rate.NightPrice;
        NightCallFee = rate.NightCallFee;
        MinimumRule = rate.Minimum.Kind;
        MinimumAmount = rate.Minimum.Amount;
        MinimumThreshold = rate.Minimum.Threshold;
        MinimumOveragePrice = rate.Minimum.OveragePrice;
    }

    internal VehicleId VehicleId { get; private set; }

    internal decimal DayPrice { get; private set; }

    internal decimal? NightPrice { get; private set; }

    internal decimal? NightCallFee { get; private set; }

    internal MinimumRuleKind MinimumRule { get; private set; }

    internal decimal? MinimumAmount { get; private set; }

    internal decimal? MinimumThreshold { get; private set; }

    internal decimal? MinimumOveragePrice { get; private set; }

    /// <summary>Rebuilds the vehicle rate from the stored columns.</summary>
    internal VehicleRate ToVehicleRate() => new(
        VehicleId,
        Rate.Create(
            DayPrice,
            NightPrice,
            NightCallFee,
            ValueObjects.MinimumRule.From(MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice)).Value);
}
