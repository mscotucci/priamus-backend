using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Vehicles;

public sealed class LicensePlateTests
{
    [Theory]
    [InlineData("ab 456-cd", "AB456CD")]
    [InlineData("AB456CD", "AB456CD")]
    [InlineData(" ab-1 ", "AB1")]
    [InlineData("x1", "X1")]
    [InlineData("ABCDE12345", "ABCDE12345")]
    public void Create_WithValidInput_StoresNormalForm(string raw, string expected)
    {
        LicensePlate.Create(raw).Value.ShouldBe(expected);
        LicensePlate.IsValid(raw).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    [InlineData("A")]
    [InlineData("A-")]
    [InlineData("ABCDE123456")]
    [InlineData("AB 456 CD 789 X")]
    [InlineData("AB_456")]
    [InlineData("AB.456")]
    [InlineData("ÈB456")]
    [InlineData("AB456\n")]
    [InlineData("AB456\r\n")]
    public void Create_WithInvalidInput_ThrowsLicensePlateInvalid(string? raw)
    {
        var act = () => LicensePlate.Create(raw);

        act.ShouldThrow<DomainException>().Error.ShouldBe(VehicleErrors.LicensePlateInvalid);
        LicensePlate.IsValid(raw).ShouldBeFalse();
    }

    [Theory]
    [InlineData("ab 456", "AB456")]
    [InlineData("a-b", "AB")]
    [InlineData(null, "")]
    public void Normalize_DoesNotValidate(string? raw, string expected)
    {
        LicensePlate.Normalize(raw).ShouldBe(expected);
    }
}
