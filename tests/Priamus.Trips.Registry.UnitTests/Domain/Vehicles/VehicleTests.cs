using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.Events;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Domain.Vehicles;

public sealed class VehicleTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly UserId OtherActor = new(new Guid("a0000000-0000-0000-0000-000000000002"));

    private static FakeClock NewClock() => new(new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc));

    private static Vehicle NewVehicle(
        FakeClock clock,
        string plate = "AB456CD",
        string description = "Iveco Stralis 480",
        VehicleType type = VehicleType.Tractor,
        bool requiresTrailer = false) =>
        Vehicle.Create(VehicleId.New(), LicensePlate.Create(plate), description, type, requiresTrailer, clock, Actor);

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidArguments_ReturnsActiveVehicleWithAuditFields()
    {
        var clock = NewClock();
        var id = VehicleId.New();

        var vehicle = Vehicle.Create(id, LicensePlate.Create("ab 456-cd"), "  Iveco Stralis 480  ",
            VehicleType.Tractor, true, clock, Actor);

        vehicle.Id.ShouldBe(id);
        vehicle.LicensePlate.Value.ShouldBe("AB456CD");
        vehicle.Description.ShouldBe("Iveco Stralis 480");
        vehicle.Type.ShouldBe(VehicleType.Tractor);
        vehicle.RequiresTrailer.ShouldBeTrue();
        vehicle.Status.ShouldBe(VehicleStatus.Active);
        vehicle.CreatedAt.ShouldBe(clock.UtcNow);
        vehicle.CreatedBy.ShouldBe(Actor);
        vehicle.LastModifiedAt.ShouldBe(clock.UtcNow);
        vehicle.LastModifiedBy.ShouldBe(Actor);
        vehicle.Version.ShouldBe(0);
    }

    [Fact]
    public void Create_WithValidArguments_RaisesVehicleCreatedWithFullState()
    {
        var clock = NewClock();

        var vehicle = NewVehicle(clock, type: VehicleType.Tractor, requiresTrailer: true);

        var evt = vehicle.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<VehicleCreated>();
        evt.VehicleId.ShouldBe(vehicle.Id);
        evt.LicensePlate.Value.ShouldBe("AB456CD");
        evt.Description.ShouldBe("Iveco Stralis 480");
        evt.Type.ShouldBe(VehicleType.Tractor);
        evt.RequiresTrailer.ShouldBeTrue();
        evt.Status.ShouldBe(VehicleStatus.Active);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_WithBlankDescription_ThrowsDescriptionInvalid(string? description)
    {
        var act = () => Vehicle.Create(VehicleId.New(), LicensePlate.Create("AB456CD"), description,
            VehicleType.Tractor, false, NewClock(), Actor);

        act.ShouldThrow<DomainException>().Error.ShouldBe(VehicleErrors.DescriptionInvalid);
    }

    [Fact]
    public void Create_WithDescriptionOf100CharsAfterTrim_Succeeds()
    {
        var vehicle = NewVehicle(NewClock(), description: " " + new string('x', 100) + " ");

        vehicle.Description.Length.ShouldBe(100);
    }

    [Fact]
    public void Create_WithDescriptionOf101Chars_ThrowsDescriptionInvalid()
    {
        var act = () => NewVehicle(NewClock(), description: new string('x', 101));

        act.ShouldThrow<DomainException>().Error.ShouldBe(VehicleErrors.DescriptionInvalid);
    }

    [Theory]
    [InlineData(VehicleType.Trailer)]
    [InlineData(VehicleType.StandaloneVehicle)]
    public void Create_RequiringTrailerOnANonTractor_ThrowsRequiresTrailerNotAllowed(VehicleType type)
    {
        var act = () => NewVehicle(NewClock(), type: type, requiresTrailer: true);

        act.ShouldThrow<DomainException>().Error.ShouldBe(VehicleErrors.RequiresTrailerNotAllowed);
    }

    [Theory]
    [InlineData(VehicleType.Trailer)]
    [InlineData(VehicleType.StandaloneVehicle)]
    public void Create_NotRequiringTrailerOnANonTractor_Succeeds(VehicleType type)
    {
        NewVehicle(NewClock(), type: type).RequiresTrailer.ShouldBeFalse();
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WithValidArguments_ChangesDataStampsAuditBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock);
        vehicle.ClearDomainEvents();
        clock.Advance(TimeSpan.FromHours(1));

        vehicle.Update(LicensePlate.Create("zz 99"), "  Rimorchio frigo ", VehicleType.Trailer, false, clock, OtherActor);

        vehicle.LicensePlate.Value.ShouldBe("ZZ99");
        vehicle.Description.ShouldBe("Rimorchio frigo");
        vehicle.Type.ShouldBe(VehicleType.Trailer);
        vehicle.RequiresTrailer.ShouldBeFalse();
        vehicle.LastModifiedAt.ShouldBe(clock.UtcNow);
        vehicle.LastModifiedBy.ShouldBe(OtherActor);
        vehicle.CreatedBy.ShouldBe(Actor);
        vehicle.Version.ShouldBe(1);
        var evt = vehicle.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<VehicleUpdated>();
        evt.VehicleId.ShouldBe(vehicle.Id);
        evt.LicensePlate.Value.ShouldBe("ZZ99");
        evt.Description.ShouldBe("Rimorchio frigo");
        evt.Type.ShouldBe(VehicleType.Trailer);
        evt.RequiresTrailer.ShouldBeFalse();
        evt.Status.ShouldBe(VehicleStatus.Active);
    }

    [Fact]
    public void Update_WithTheSameData_ChangesNothing()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock, requiresTrailer: true);
        vehicle.ClearDomainEvents();
        var modifiedAt = vehicle.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        vehicle.Update(LicensePlate.Create("ab 456-cd"), "  Iveco Stralis 480 ", VehicleType.Tractor, true, clock, OtherActor);

        vehicle.Version.ShouldBe(0);
        vehicle.LastModifiedAt.ShouldBe(modifiedAt);
        vehicle.LastModifiedBy.ShouldBe(Actor);
        vehicle.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_OnADecommissionedVehicle_KeepsItDecommissioned()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock);
        vehicle.Decommission(clock, Actor);
        vehicle.ClearDomainEvents();

        vehicle.Update(vehicle.LicensePlate, "Nuova descrizione", VehicleType.Tractor, false, clock, Actor);

        vehicle.Status.ShouldBe(VehicleStatus.Decommissioned);
        vehicle.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<VehicleUpdated>()
            .Status.ShouldBe(VehicleStatus.Decommissioned);
    }

    [Fact]
    public void Update_WithInvalidDescription_ThrowsAndChangesNothing()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock);
        vehicle.ClearDomainEvents();

        var act = () => vehicle.Update(LicensePlate.Create("ZZ99"), " ", VehicleType.Trailer, false, clock, Actor);

        act.ShouldThrow<DomainException>().Error.ShouldBe(VehicleErrors.DescriptionInvalid);
        vehicle.LicensePlate.Value.ShouldBe("AB456CD");
        vehicle.Type.ShouldBe(VehicleType.Tractor);
        vehicle.Version.ShouldBe(0);
        vehicle.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Update_ToANonTractorStillRequiringTrailer_ThrowsAndChangesNothing()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock, requiresTrailer: true);
        vehicle.ClearDomainEvents();

        var act = () => vehicle.Update(vehicle.LicensePlate, "x", VehicleType.StandaloneVehicle, true, clock, Actor);

        act.ShouldThrow<DomainException>().Error.ShouldBe(VehicleErrors.RequiresTrailerNotAllowed);
        vehicle.Type.ShouldBe(VehicleType.Tractor);
        vehicle.RequiresTrailer.ShouldBeTrue();
        vehicle.Version.ShouldBe(0);
        vehicle.DomainEvents.ShouldBeEmpty();
    }

    // ── Decommission / Reactivate ────────────────────────────────────────────

    [Fact]
    public void Decommission_OnActiveVehicle_ChangesStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock);
        vehicle.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        vehicle.Decommission(clock, OtherActor);

        vehicle.Status.ShouldBe(VehicleStatus.Decommissioned);
        vehicle.Version.ShouldBe(1);
        vehicle.LastModifiedAt.ShouldBe(clock.UtcNow);
        vehicle.LastModifiedBy.ShouldBe(OtherActor);
        var evt = vehicle.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<VehicleDecommissioned>();
        evt.VehicleId.ShouldBe(vehicle.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Decommission_OnDecommissionedVehicle_ChangesNothing()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock);
        vehicle.Decommission(clock, Actor);
        vehicle.ClearDomainEvents();
        var modifiedAt = vehicle.LastModifiedAt;
        clock.Advance(TimeSpan.FromMinutes(5));

        vehicle.Decommission(clock, OtherActor);

        vehicle.Status.ShouldBe(VehicleStatus.Decommissioned);
        vehicle.Version.ShouldBe(1);
        vehicle.LastModifiedAt.ShouldBe(modifiedAt);
        vehicle.LastModifiedBy.ShouldBe(Actor);
        vehicle.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Reactivate_OnDecommissionedVehicle_ChangesStatusBumpsVersionAndRaisesEvent()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock);
        vehicle.Decommission(clock, Actor);
        vehicle.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        vehicle.Reactivate(clock, OtherActor);

        vehicle.Status.ShouldBe(VehicleStatus.Active);
        vehicle.Version.ShouldBe(2);
        vehicle.LastModifiedAt.ShouldBe(clock.UtcNow);
        vehicle.LastModifiedBy.ShouldBe(OtherActor);
        var evt = vehicle.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<VehicleReactivated>();
        evt.VehicleId.ShouldBe(vehicle.Id);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Reactivate_OnActiveVehicle_ChangesNothing()
    {
        var clock = NewClock();
        var vehicle = NewVehicle(clock);
        vehicle.ClearDomainEvents();
        clock.Advance(TimeSpan.FromMinutes(5));

        vehicle.Reactivate(clock, OtherActor);

        vehicle.Status.ShouldBe(VehicleStatus.Active);
        vehicle.Version.ShouldBe(0);
        vehicle.LastModifiedAt.ShouldBe(new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc));
        vehicle.LastModifiedBy.ShouldBe(Actor);
        vehicle.DomainEvents.ShouldBeEmpty();
    }
}
