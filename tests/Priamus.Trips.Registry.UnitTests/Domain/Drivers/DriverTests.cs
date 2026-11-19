using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Domain.Drivers.Events;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Drivers;

public sealed class DriverTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));
    private static readonly UserId Account = new(new Guid("b0000000-0000-0000-0000-000000000001"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));

    private static Driver NewDriver(
        FakeClock clock,
        string? firstName = "Luca",
        string? lastName = "Di Febo",
        string? phone = "333 418 2291",
        UserId? userId = null) =>
        Driver.Create(DriverId.New(), firstName, lastName, phone, userId, clock, Actor);

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidArguments_ReturnsActiveDriverWithAuditFields()
    {
        var clock = NewClock();
        var id = DriverId.New();

        var driver = Driver.Create(id, "  Luca ", " Di Febo  ", " 333 418 2291 ", Account, clock, Actor);

        driver.Id.ShouldBe(id);
        driver.FirstName.ShouldBe("Luca");
        driver.LastName.ShouldBe("Di Febo");
        driver.Phone.ShouldBe("333 418 2291");
        driver.UserId.ShouldBe(Account);
        driver.Status.ShouldBe(DriverStatus.Active);
        driver.CreatedAt.ShouldBe(clock.UtcNow);
        driver.CreatedBy.ShouldBe(Actor);
        driver.LastModifiedAt.ShouldBe(clock.UtcNow);
        driver.LastModifiedBy.ShouldBe(Actor);
        driver.Version.ShouldBe(0);
    }

    [Fact]
    public void Create_WithValidArguments_RaisesDriverCreatedWithFullState()
    {
        var clock = NewClock();

        var driver = NewDriver(clock, userId: Account);

        var evt = driver.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DriverCreated>();
        evt.DriverId.ShouldBe(driver.Id);
        evt.FirstName.ShouldBe("Luca");
        evt.LastName.ShouldBe("Di Febo");
        evt.Phone.ShouldBe("333 418 2291");
        evt.UserId.ShouldBe(Account);
        evt.Status.ShouldBe(DriverStatus.Active);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Create_WithoutOptionalData_StoresNone()
    {
        var driver = NewDriver(NewClock(), phone: "   ", userId: null);

        driver.Phone.ShouldBeNull();
        driver.UserId.ShouldBeNull();
    }

    [Fact]
    public void Create_TwoDriversWithTheSameName_AreBothValid()
    {
        var clock = NewClock();

        var first = NewDriver(clock);
        var second = NewDriver(clock);

        first.Id.ShouldNotBe(second.Id);
        second.FirstName.ShouldBe(first.FirstName);
        second.LastName.ShouldBe(first.LastName);
    }

    [Fact]
    public void Create_WithNameOfMaximumLength_Succeeds()
    {
        var driver = NewDriver(NewClock(), firstName: new string('a', 60), lastName: new string('b', 60));

        driver.FirstName.Length.ShouldBe(60);
        driver.LastName.Length.ShouldBe(60);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidFirstName_Throws(string? firstName)
    {
        var act = () => NewDriver(NewClock(), firstName: firstName);

        act.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.FirstNameInvalid);
    }

    [Fact]
    public void Create_WithFirstNameLongerThanMaximum_Throws()
    {
        var act = () => NewDriver(NewClock(), firstName: new string('x', 61));

        act.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.FirstNameInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidLastName_Throws(string? lastName)
    {
        var act = () => NewDriver(NewClock(), lastName: lastName);

        act.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.LastNameInvalid);
    }

    [Fact]
    public void Create_WithLastNameLongerThanMaximum_Throws()
    {
        var act = () => NewDriver(NewClock(), lastName: new string('x', 61));

        act.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.LastNameInvalid);
    }

    [Theory]
    [InlineData("333 418 2291", "333 418 2291")]
    [InlineData("+39 333 418 2291", "+39 333 418 2291")]
    [InlineData("  3334182291  ", "3334182291")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Create_WithValidPhone_KeepsItTrimmedAsTyped(string? phone, string? expected)
    {
        var driver = NewDriver(NewClock(), phone: phone);

        driver.Phone.ShouldBe(expected);
    }

    [Theory]
    [InlineData("333-418-2291")]
    [InlineData("333 418 229a")]
    [InlineData("39+333")]
    [InlineData("++39333")]
    [InlineData("+")]
    [InlineData("333\n418")]
    [InlineData("+39 333 418 2291 00000")]
    public void Create_WithInvalidPhone_Throws(string phone)
    {
        var act = () => NewDriver(NewClock(), phone: phone);

        act.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.PhoneInvalid);
    }

    [Fact]
    public void Create_WithPhoneOfMaximumLength_SucceedsAndOneMoreThrows()
    {
        NewDriver(NewClock(), phone: new string('1', 20)).Phone.ShouldBe(new string('1', 20));

        var act = () => NewDriver(NewClock(), phone: new string('1', 21));

        act.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.PhoneInvalid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" +39 333 ", true)]
    [InlineData("12-3", false)]
    [InlineData("123456789012345678901", false)]
    public void IsPhoneValid_AppliesTheSameRuleAsTheAggregate(string? phone, bool expected)
    {
        Driver.IsPhoneValid(phone).ShouldBe(expected);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WithValidArguments_ChangesAllDataIncludingTheLinkBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var driver = NewDriver(clock, userId: Account);
        driver.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        driver.Update(" Marco ", " Rossi ", " +39 320 111 2222 ", null, clock, OtherActor);

        driver.FirstName.ShouldBe("Marco");
        driver.LastName.ShouldBe("Rossi");
        driver.Phone.ShouldBe("+39 320 111 2222");
        driver.UserId.ShouldBeNull();
        driver.Status.ShouldBe(DriverStatus.Active);
        driver.CreatedAt.ShouldBe(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));
        driver.CreatedBy.ShouldBe(Actor);
        driver.LastModifiedAt.ShouldBe(clock.UtcNow);
        driver.LastModifiedBy.ShouldBe(OtherActor);
        driver.Version.ShouldBe(1);
        var evt = driver.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DriverUpdated>();
        evt.DriverId.ShouldBe(driver.Id);
        evt.FirstName.ShouldBe("Marco");
        evt.LastName.ShouldBe("Rossi");
        evt.Phone.ShouldBe("+39 320 111 2222");
        evt.UserId.ShouldBeNull();
        evt.Status.ShouldBe(DriverStatus.Active);
    }

    [Fact]
    public void Update_OnADeactivatedDriver_KeepsItDeactivated()
    {
        var clock = NewClock();
        var driver = NewDriver(clock);
        driver.Deactivate(clock, Actor);
        driver.ClearDomainEvents();

        driver.Update("Marco", "Rossi", null, Account, clock, Actor);

        driver.Status.ShouldBe(DriverStatus.Deactivated);
        driver.UserId.ShouldBe(Account);
        driver.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DriverUpdated>()
            .Status.ShouldBe(DriverStatus.Deactivated);
    }

    [Fact]
    public void Update_WithInvalidData_ThrowsAndChangesNothing()
    {
        var clock = NewClock();
        var driver = NewDriver(clock, userId: Account);
        driver.ClearDomainEvents();

        var noFirstName = () => driver.Update(" ", "Rossi", null, null, clock, Actor);
        var longLastName = () => driver.Update("Marco", new string('x', 61), null, null, clock, Actor);
        var badPhone = () => driver.Update("Marco", "Rossi", "abc", null, clock, Actor);

        noFirstName.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.FirstNameInvalid);
        longLastName.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.LastNameInvalid);
        badPhone.ShouldThrow<DomainException>().Error.ShouldBe(DriverErrors.PhoneInvalid);
        driver.FirstName.ShouldBe("Luca");
        driver.LastName.ShouldBe("Di Febo");
        driver.Phone.ShouldBe("333 418 2291");
        driver.UserId.ShouldBe(Account);
        driver.Version.ShouldBe(0);
        driver.DomainEvents.ShouldBeEmpty();
    }

    // ── Deactivate / Reactivate ──────────────────────────────────────────────

    [Fact]
    public void Deactivate_OnActiveDriver_ChangesOnlyStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var driver = NewDriver(clock, userId: Account);
        driver.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        driver.Deactivate(clock, OtherActor);

        driver.Status.ShouldBe(DriverStatus.Deactivated);
        driver.FirstName.ShouldBe("Luca");
        driver.LastName.ShouldBe("Di Febo");
        driver.Phone.ShouldBe("333 418 2291");
        driver.UserId.ShouldBe(Account);
        driver.Version.ShouldBe(1);
        driver.LastModifiedAt.ShouldBe(clock.UtcNow);
        driver.LastModifiedBy.ShouldBe(OtherActor);
        var evt = driver.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DriverDeactivated>();
        evt.DriverId.ShouldBe(driver.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Deactivate_OnDeactivatedDriver_ChangesNothing()
    {
        var clock = NewClock();
        var driver = NewDriver(clock);
        driver.Deactivate(clock, Actor);
        driver.ClearDomainEvents();
        var modifiedAt = driver.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        driver.Deactivate(clock, OtherActor);

        driver.Status.ShouldBe(DriverStatus.Deactivated);
        driver.Version.ShouldBe(1);
        driver.LastModifiedAt.ShouldBe(modifiedAt);
        driver.LastModifiedBy.ShouldBe(Actor);
        driver.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Reactivate_OnDeactivatedDriver_ChangesOnlyStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var driver = NewDriver(clock, userId: Account);
        driver.Deactivate(clock, Actor);
        driver.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        driver.Reactivate(clock, OtherActor);

        driver.Status.ShouldBe(DriverStatus.Active);
        driver.FirstName.ShouldBe("Luca");
        driver.LastName.ShouldBe("Di Febo");
        driver.Phone.ShouldBe("333 418 2291");
        driver.UserId.ShouldBe(Account);
        driver.Version.ShouldBe(2);
        driver.LastModifiedAt.ShouldBe(clock.UtcNow);
        driver.LastModifiedBy.ShouldBe(OtherActor);
        var evt = driver.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<DriverReactivated>();
        evt.DriverId.ShouldBe(driver.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Reactivate_OnActiveDriver_ChangesNothing()
    {
        var clock = NewClock();
        var driver = NewDriver(clock);
        driver.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        driver.Reactivate(clock, OtherActor);

        driver.Status.ShouldBe(DriverStatus.Active);
        driver.Version.ShouldBe(0);
        driver.LastModifiedAt.ShouldBe(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));
        driver.LastModifiedBy.ShouldBe(Actor);
        driver.DomainEvents.ShouldBeEmpty();
    }
}
