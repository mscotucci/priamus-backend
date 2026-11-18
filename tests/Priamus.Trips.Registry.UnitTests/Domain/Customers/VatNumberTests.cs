using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Customers;

public sealed class VatNumberTests
{
    [Theory]
    [InlineData("01455280683", "01455280683")]
    [InlineData("IT 01455280683", "01455280683")]
    [InlineData("it01455280683", "01455280683")]
    [InlineData("It 014 552 806 83", "01455280683")]
    [InlineData("  01455280683  ", "01455280683")]
    public void Create_WithValidInput_StoresNormalForm(string raw, string expected)
    {
        VatNumber.Create(raw).Value.ShouldBe(expected);
        VatNumber.IsValid(raw).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("IT")]
    [InlineData("01455280684")]
    [InlineData("0145528068")]
    [InlineData("014552806830")]
    [InlineData("0145528068A")]
    [InlineData("ITIT01455280683")]
    [InlineData("DE01455280683")]
    [InlineData("01455280683\n")]
    [InlineData("0145528068٣")]
    public void Create_WithInvalidInput_ThrowsVatNumberInvalid(string? raw)
    {
        var act = () => VatNumber.Create(raw);

        act.ShouldThrow<DomainException>().Error.ShouldBe(CustomerErrors.VatNumberInvalid);
        VatNumber.IsValid(raw).ShouldBeFalse();
    }

    [Theory]
    [InlineData("it 0145", "0145")]
    [InlineData("IT 0145", "0145")]
    [InlineData("a b", "ab")]
    [InlineData(null, "")]
    public void Normalize_DoesNotValidate(string? raw, string expected)
    {
        VatNumber.Normalize(raw).ShouldBe(expected);
    }
}
