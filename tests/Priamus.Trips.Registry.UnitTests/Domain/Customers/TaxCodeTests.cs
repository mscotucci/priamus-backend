using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Customers;

public sealed class TaxCodeTests
{
    [Theory]
    [InlineData("mrcgli72d45g438k", "MRCGLI72D45G438K")]
    [InlineData("MRC GLI 72D45 G438K", "MRCGLI72D45G438K")]
    [InlineData("01455280683", "01455280683")]
    [InlineData(" 014 5528 0683 ", "01455280683")]
    public void Create_WithValidInput_StoresNormalForm(string raw, string expected)
    {
        TaxCode.Create(raw).Value.ShouldBe(expected);
        TaxCode.IsValid(raw).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MRCGLI72D45G438")]
    [InlineData("MRCGLI72D45G438KX")]
    [InlineData("0145528068")]
    [InlineData("014552806831")]
    [InlineData("MRCGLI72D45G438-")]
    [InlineData("MRCGLI72D45G43È8")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("MRCGLI72D45G438K\n")]
    public void Create_WithInvalidInput_ThrowsTaxCodeInvalid(string? raw)
    {
        var act = () => TaxCode.Create(raw);

        act.ShouldThrow<DomainException>().Error.ShouldBe(CustomerErrors.TaxCodeInvalid);
        TaxCode.IsValid(raw).ShouldBeFalse();
    }

    [Theory]
    [InlineData("mrc gli", "MRCGLI")]
    [InlineData(null, "")]
    public void Normalize_DoesNotValidate(string? raw, string expected)
    {
        TaxCode.Normalize(raw).ShouldBe(expected);
    }
}
