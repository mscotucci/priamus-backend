using System.Globalization;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Contracts;

public sealed class RateTests
{
    [Fact]
    public void Create_WithDayPriceOnly_HoldsTheRateWithoutMinimum()
    {
        var result = Rate.Create(45.50m, null, null, new MinimumRule.None());

        result.IsSuccess.ShouldBeTrue();
        result.Value.DayPrice.ShouldBe(45.50m);
        result.Value.NightPrice.ShouldBeNull();
        result.Value.NightCallFee.ShouldBeNull();
        result.Value.Minimum.ShouldBe(new MinimumRule.None());
    }

    [Fact]
    public void Create_WithNightValuesAndTieredOverage_HoldsEveryValue()
    {
        var minimum = new MinimumRule.TieredOverage(300m, 4m, 60m);

        var result = Rate.Create(45m, 55m, 0m, minimum);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NightPrice.ShouldBe(55m);
        result.Value.NightCallFee.ShouldBe(0m);
        result.Value.Minimum.ShouldBe(minimum);
        result.Value.Minimum.Kind.ShouldBe(MinimumRuleKind.TieredOverage);
    }

    [Fact]
    public void Create_WithFixedMinimum_HoldsTheAmount()
    {
        var result = Rate.Create(45m, null, null, new MinimumRule.FixedMinimum(120m));

        result.Value.Minimum.ShouldBe(new MinimumRule.FixedMinimum(120m));
        result.Value.Minimum.Kind.ShouldBe(MinimumRuleKind.FixedMinimum);
    }

    [Fact]
    public void Rate_HasNoValidityOfItsOwn()
    {
        typeof(Rate).GetProperties().Select(p => p.Name).ShouldBe(
            ["DayPrice", "NightPrice", "NightCallFee", "Minimum"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Create_WithDayPriceNotPositive_Fails(string dayPrice)
    {
        var result = Rate.Create(decimal.Parse(dayPrice, CultureInfo.InvariantCulture), null, null, new MinimumRule.None());

        result.Error.ShouldBe(ContractErrors.DayPriceNotPositive);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Create_WithNightPriceNotPositive_Fails(string nightPrice)
    {
        var result = Rate.Create(45m, decimal.Parse(nightPrice, CultureInfo.InvariantCulture), 0m, new MinimumRule.None());

        result.Error.ShouldBe(ContractErrors.NightPriceNotPositive);
    }

    [Fact]
    public void Create_WithNegativeNightCallFee_Fails()
    {
        var result = Rate.Create(45m, 55m, -0.01m, new MinimumRule.None());

        result.Error.ShouldBe(ContractErrors.NightCallFeeNegative);
    }

    [Fact]
    public void Create_WithZeroNightCallFee_Succeeds()
    {
        Rate.Create(45m, 55m, 0m, new MinimumRule.None()).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("45.123", null, null, null)]
    [InlineData("45", "55.001", "1", null)]
    [InlineData("45", "55", "1.005", null)]
    [InlineData("45", null, null, "100.001")]
    public void Create_WithMoreThanTwoDecimals_Fails(string day, string? night, string? fee, string? fixedAmount)
    {
        MinimumRule minimum = fixedAmount is null
            ? new MinimumRule.None()
            : new MinimumRule.FixedMinimum(decimal.Parse(fixedAmount, CultureInfo.InvariantCulture));

        var result = Rate.Create(
            decimal.Parse(day, CultureInfo.InvariantCulture),
            night is null ? null : decimal.Parse(night, CultureInfo.InvariantCulture),
            fee is null ? null : decimal.Parse(fee, CultureInfo.InvariantCulture),
            minimum);

        result.Error.ShouldBe(ContractErrors.AmountTooPrecise);
    }

    [Theory]
    [InlineData("10000000000", null, null, null, null, null)]
    [InlineData("45", "10000000000", "1", null, null, null)]
    [InlineData("45", "55", "10000000000", null, null, null)]
    [InlineData("45", null, null, "10000000000", null, null)]
    [InlineData("45", null, null, "300", "10000000000", "60")]
    [InlineData("45", null, null, "300", "4", "10000000000")]
    public void Create_WithAnAmountOverTheBound_Fails(
        string day, string? night, string? fee, string? amount, string? threshold, string? overage)
    {
        static decimal? D(string? v) => v is null ? null : decimal.Parse(v, CultureInfo.InvariantCulture);
        MinimumRule minimum = threshold is not null
            ? new MinimumRule.TieredOverage(D(amount)!.Value, D(threshold)!.Value, D(overage)!.Value)
            : amount is not null ? new MinimumRule.FixedMinimum(D(amount)!.Value) : new MinimumRule.None();

        var result = Rate.Create(D(day)!.Value, D(night), D(fee), minimum);

        result.Error.ShouldBe(ContractErrors.AmountTooLarge);
    }

    [Fact]
    public void Create_WithEveryAmountAtTheBound_Succeeds()
    {
        var result = Rate.Create(
            9999999999.99m, 9999999999.99m, 9999999999.99m,
            new MinimumRule.TieredOverage(9999999999.99m, 9999999999.99m, 9999999999.99m));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithFixedMinimumNotPositive_Fails()
    {
        var result = Rate.Create(45m, null, null, new MinimumRule.FixedMinimum(0m));

        result.Error.ShouldBe(ContractErrors.MinimumAmountNotPositive);
    }

    [Fact]
    public void Create_WithTieredAmountNotPositive_Fails()
    {
        var result = Rate.Create(45m, null, null, new MinimumRule.TieredOverage(0m, 4m, 60m));

        result.Error.ShouldBe(ContractErrors.MinimumAmountNotPositive);
    }

    [Fact]
    public void Create_WithTieredThresholdNotPositive_Fails()
    {
        var result = Rate.Create(45m, null, null, new MinimumRule.TieredOverage(300m, 0m, 60m));

        result.Error.ShouldBe(ContractErrors.MinimumThresholdNotPositive);
    }

    [Fact]
    public void Create_WithTieredOveragePriceNotPositive_Fails()
    {
        var result = Rate.Create(45m, null, null, new MinimumRule.TieredOverage(300m, 4m, -1m));

        result.Error.ShouldBe(ContractErrors.MinimumOveragePriceNotPositive);
    }

    [Fact]
    public void Create_WithTieredValueWithMoreThanTwoDecimals_Fails()
    {
        var result = Rate.Create(45m, null, null, new MinimumRule.TieredOverage(300m, 4.555m, 60m));

        result.Error.ShouldBe(ContractErrors.AmountTooPrecise);
    }

    [Fact]
    public void MinimumRule_ExposesItsAmountsThroughTheBaseType()
    {
        MinimumRule none = new MinimumRule.None();
        MinimumRule fixedMinimum = new MinimumRule.FixedMinimum(120m);
        MinimumRule tiered = new MinimumRule.TieredOverage(300m, 4m, 60m);

        (none.Amount, none.Threshold, none.OveragePrice).ShouldBe((null, null, null));
        (fixedMinimum.Amount, fixedMinimum.Threshold, fixedMinimum.OveragePrice).ShouldBe((120m, null, null));
        (tiered.Amount, tiered.Threshold, tiered.OveragePrice).ShouldBe((300m, 4m, 60m));
    }
}
