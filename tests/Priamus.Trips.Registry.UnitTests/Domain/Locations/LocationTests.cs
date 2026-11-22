using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Locations.Events;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Locations;

public sealed class LocationTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));

    private static Location NewLocation(
        FakeClock clock,
        string? name = "Magazzino centrale",
        string? municipality = "Spoltore",
        string? address = "Via Roma 1") =>
        Location.Create(LocationId.New(), name, municipality, address, clock, Actor);

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidArguments_ReturnsActiveLocationWithAuditFieldsAndKeys()
    {
        var clock = NewClock();
        var id = LocationId.New();

        var location = Location.Create(id, "  Magazzino   Centrale ", " Spoltore ", "  Via Roma 1  ", clock, Actor);

        location.Id.ShouldBe(id);
        location.Name.ShouldBe("Magazzino   Centrale");
        location.Municipality.ShouldBe("Spoltore");
        location.Address.ShouldBe("Via Roma 1");
        location.Status.ShouldBe(LocationStatus.Active);
        location.NameKey.ShouldBe("magazzino centrale");
        location.MunicipalityKey.ShouldBe("spoltore");
        location.CreatedAt.ShouldBe(clock.UtcNow);
        location.CreatedBy.ShouldBe(Actor);
        location.LastModifiedAt.ShouldBe(clock.UtcNow);
        location.LastModifiedBy.ShouldBe(Actor);
        location.Version.ShouldBe(0);
    }

    [Fact]
    public void Create_WithValidArguments_RaisesLocationCreatedWithFullState()
    {
        var clock = NewClock();

        var location = NewLocation(clock);

        var evt = location.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LocationCreated>();
        evt.LocationId.ShouldBe(location.Id);
        evt.Name.ShouldBe("Magazzino centrale");
        evt.Municipality.ShouldBe("Spoltore");
        evt.Address.ShouldBe("Via Roma 1");
        evt.Status.ShouldBe(LocationStatus.Active);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_WithBlankName_ThrowsNameInvalid(string? name)
    {
        var act = () => NewLocation(NewClock(), name: name);

        act.ShouldThrow<DomainException>().Error.ShouldBe(LocationErrors.NameInvalid);
    }

    [Fact]
    public void Create_WithNameOf100CharsAfterTrim_Succeeds()
    {
        var location = NewLocation(NewClock(), name: " " + new string('x', 100) + " ");

        location.Name.Length.ShouldBe(100);
    }

    [Fact]
    public void Create_WithNameOf101Chars_ThrowsNameInvalid()
    {
        var act = () => NewLocation(NewClock(), name: new string('x', 101));

        act.ShouldThrow<DomainException>().Error.ShouldBe(LocationErrors.NameInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_WithBlankMunicipality_ThrowsMunicipalityInvalid(string? municipality)
    {
        var act = () => NewLocation(NewClock(), municipality: municipality);

        act.ShouldThrow<DomainException>().Error.ShouldBe(LocationErrors.MunicipalityInvalid);
    }

    [Fact]
    public void Create_WithMunicipalityOf100CharsAfterTrim_Succeeds()
    {
        var location = NewLocation(NewClock(), municipality: " " + new string('x', 100) + " ");

        location.Municipality.Length.ShouldBe(100);
    }

    [Fact]
    public void Create_WithMunicipalityOf101Chars_ThrowsMunicipalityInvalid()
    {
        var act = () => NewLocation(NewClock(), municipality: new string('x', 101));

        act.ShouldThrow<DomainException>().Error.ShouldBe(LocationErrors.MunicipalityInvalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_WithBlankAddress_StoresNone(string? address)
    {
        NewLocation(NewClock(), address: address).Address.ShouldBeNull();
    }

    [Fact]
    public void Create_WithAddressOf200CharsAfterTrim_Succeeds()
    {
        var location = NewLocation(NewClock(), address: " " + new string('x', 200) + " ");

        location.Address!.Length.ShouldBe(200);
    }

    [Fact]
    public void Create_WithAddressOf201Chars_ThrowsAddressInvalid()
    {
        var act = () => NewLocation(NewClock(), address: new string('x', 201));

        act.ShouldThrow<DomainException>().Error.ShouldBe(LocationErrors.AddressInvalid);
    }

    // ── KeyOf ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("  Magazzino   Centrale ", "magazzino centrale")]
    [InlineData("magazzino centrale", "magazzino centrale")]
    [InlineData("MAGAZZINO CENTRALE", "magazzino centrale")]
    [InlineData("Magazzino\tCentrale", "magazzino centrale")]
    [InlineData("Magazzino \t \n Centrale", "magazzino centrale")]
    public void KeyOf_IgnoresCaseOuterSpacesAndRepeatedInnerSpaces(string value, string expected)
    {
        Location.KeyOf(value).ShouldBe(expected);
    }

    [Fact]
    public void KeyOf_OfTheSameNameInDifferentMunicipalities_GivesDifferentLocationKeys()
    {
        var clock = NewClock();

        var first = NewLocation(clock, municipality: "Spoltore");
        var second = NewLocation(clock, municipality: "Penne");

        second.NameKey.ShouldBe(first.NameKey);
        second.MunicipalityKey.ShouldNotBe(first.MunicipalityKey);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WithValidArguments_ChangesDataAndKeysStampsAuditBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.ClearDomainEvents();
        clock.Advance(TimeSpan.FromHours(1));

        location.Update("  Cantiere   A14 ", " Penne ", "  ", clock, OtherActor);

        location.Name.ShouldBe("Cantiere   A14");
        location.Municipality.ShouldBe("Penne");
        location.Address.ShouldBeNull();
        location.NameKey.ShouldBe("cantiere a14");
        location.MunicipalityKey.ShouldBe("penne");
        location.LastModifiedAt.ShouldBe(clock.UtcNow);
        location.LastModifiedBy.ShouldBe(OtherActor);
        location.CreatedBy.ShouldBe(Actor);
        location.Version.ShouldBe(1);
        var evt = location.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LocationUpdated>();
        evt.LocationId.ShouldBe(location.Id);
        evt.Name.ShouldBe("Cantiere   A14");
        evt.Municipality.ShouldBe("Penne");
        evt.Address.ShouldBeNull();
        evt.Status.ShouldBe(LocationStatus.Active);
    }

    [Fact]
    public void Update_WithTheSameData_ChangesNothing()
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.ClearDomainEvents();
        var modifiedAt = location.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        location.Update(" Magazzino centrale ", "Spoltore ", " Via Roma 1", clock, OtherActor);

        location.Version.ShouldBe(0);
        location.LastModifiedAt.ShouldBe(modifiedAt);
        location.LastModifiedBy.ShouldBe(Actor);
        location.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_OnADeactivatedLocation_KeepsItDeactivated()
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.Deactivate(clock, Actor);
        location.ClearDomainEvents();

        location.Update("Nuovo nome", location.Municipality, location.Address, clock, Actor);

        location.Status.ShouldBe(LocationStatus.Deactivated);
        location.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LocationUpdated>()
            .Status.ShouldBe(LocationStatus.Deactivated);
    }

    [Theory]
    [InlineData(" ", "Penne", 10, "Registry.Location.NameInvalid")]
    [InlineData("Altro nome", " ", 10, "Registry.Location.MunicipalityInvalid")]
    [InlineData("Altro nome", "Penne", 201, "Registry.Location.AddressInvalid")]
    public void Update_WithAnInvalidField_ThrowsItsErrorAndChangesNothing(
        string name, string municipality, int addressLength, string expectedCode)
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.ClearDomainEvents();

        var act = () => location.Update(name, municipality, new string('x', addressLength), clock, Actor);

        act.ShouldThrow<DomainException>().Error.Code.ShouldBe(expectedCode);
        location.Name.ShouldBe("Magazzino centrale");
        location.Municipality.ShouldBe("Spoltore");
        location.Address.ShouldBe("Via Roma 1");
        location.NameKey.ShouldBe("magazzino centrale");
        location.MunicipalityKey.ShouldBe("spoltore");
        location.Version.ShouldBe(0);
        location.DomainEvents.ShouldBeEmpty();
    }

    // ── Deactivate / Reactivate ──────────────────────────────────────────────

    [Fact]
    public void Deactivate_OnActiveLocation_ChangesOnlyStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        location.Deactivate(clock, OtherActor);

        location.Status.ShouldBe(LocationStatus.Deactivated);
        location.Name.ShouldBe("Magazzino centrale");
        location.Municipality.ShouldBe("Spoltore");
        location.Address.ShouldBe("Via Roma 1");
        location.NameKey.ShouldBe("magazzino centrale");
        location.MunicipalityKey.ShouldBe("spoltore");
        location.Version.ShouldBe(1);
        location.LastModifiedAt.ShouldBe(clock.UtcNow);
        location.LastModifiedBy.ShouldBe(OtherActor);
        var evt = location.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LocationDeactivated>();
        evt.LocationId.ShouldBe(location.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Deactivate_OnDeactivatedLocation_ChangesNothing()
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.Deactivate(clock, Actor);
        location.ClearDomainEvents();
        var modifiedAt = location.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        location.Deactivate(clock, OtherActor);

        location.Status.ShouldBe(LocationStatus.Deactivated);
        location.Version.ShouldBe(1);
        location.LastModifiedAt.ShouldBe(modifiedAt);
        location.LastModifiedBy.ShouldBe(Actor);
        location.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Reactivate_OnDeactivatedLocation_ChangesOnlyStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.Deactivate(clock, Actor);
        location.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        location.Reactivate(clock, OtherActor);

        location.Status.ShouldBe(LocationStatus.Active);
        location.Name.ShouldBe("Magazzino centrale");
        location.Municipality.ShouldBe("Spoltore");
        location.Address.ShouldBe("Via Roma 1");
        location.NameKey.ShouldBe("magazzino centrale");
        location.MunicipalityKey.ShouldBe("spoltore");
        location.Version.ShouldBe(2);
        location.LastModifiedAt.ShouldBe(clock.UtcNow);
        location.LastModifiedBy.ShouldBe(OtherActor);
        var evt = location.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LocationReactivated>();
        evt.LocationId.ShouldBe(location.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Reactivate_OnActiveLocation_ChangesNothing()
    {
        var clock = NewClock();
        var location = NewLocation(clock);
        location.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        location.Reactivate(clock, OtherActor);

        location.Status.ShouldBe(LocationStatus.Active);
        location.Version.ShouldBe(0);
        location.LastModifiedAt.ShouldBe(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));
        location.LastModifiedBy.ShouldBe(Actor);
        location.DomainEvents.ShouldBeEmpty();
    }

    // ── Structure ────────────────────────────────────────────────────────────

    [Fact]
    public void Location_HasNoCustomerAndNoRoleMember()
    {
        var members = typeof(Location).GetProperties().Select(p => p.Name).ToList();

        members.ShouldNotContain(n => n.Contains("Customer", StringComparison.Ordinal));
        members.ShouldNotContain(n => n.Contains("Role", StringComparison.Ordinal)
            || n.Contains("Pickup", StringComparison.Ordinal)
            || n.Contains("Delivery", StringComparison.Ordinal));
    }
}
