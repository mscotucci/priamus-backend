using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Features.Validation;

/// <summary>
/// The seven rate members of a contract request, as sent: all optional, so that a missing or
/// misplaced value is reported by <see cref="ContractRequestValidation"/> on its own member.
/// </summary>
internal readonly record struct ContractRateMembers(
    decimal? DayPrice,
    decimal? NightPrice,
    decimal? NightCallFee,
    MinimumRuleKind? MinimumRule,
    decimal? MinimumAmount,
    decimal? MinimumThreshold,
    decimal? MinimumOveragePrice)
{
    /// <summary>
    /// Builds the rate of a validated request; <c>null</c> when the request has no day price, which
    /// validation allows only for a contract that is not flat rate.
    /// </summary>
    public Result<Rate?> ToRate()
    {
        if (DayPrice is not { } dayPrice)
        {
            return Result<Rate?>.Success(null);
        }

        var minimum = Domain.Contracts.ValueObjects.MinimumRule.From(
            MinimumRule, MinimumAmount, MinimumThreshold, MinimumOveragePrice);

        var rate = Rate.Create(dayPrice, NightPrice, NightCallFee, minimum);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        return Result<Rate?>.Success(rate.Value);
    }
}
