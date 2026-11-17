using System.Text.Json;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.Registry.Persistence.Interceptors;
using Priamus.Trips.Registry.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.UnitTests.Persistence;

/// <summary>
/// The audit row each vehicle event produces: event type, subject, actor, timestamp and a payload
/// that writes the plate as a plain string and enums by name.
/// </summary>
public sealed class AuditInterceptorMappingTests
{
    private static readonly UserId Actor = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    private static readonly Guid ActorGuid = Actor.Value;

    private static Vehicle NewVehicle() =>
        Vehicle.Create(
            VehicleId.New(),
            LicensePlate.Create("ab 456-cd"),
            "Iveco Stralis 480",
            VehicleType.Tractor,
            true,
            new FakeClock(new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc)),
            Actor);

    private static AuditLogEntry MapSingleEvent(Vehicle vehicle) =>
        AuditInterceptor.MapToAuditLogEntry(vehicle.DomainEvents.Single(), ActorGuid);

    [Fact]
    public void VehicleCreated_WritesFullStateWithPlainPlateAndEnumsByName()
    {
        var vehicle = NewVehicle();

        var entry = MapSingleEvent(vehicle);

        entry.EventType.ShouldBe(AuditEventTypes.VehicleCreated);
        entry.EventType.ShouldBe("registry.vehicle.created");
        entry.SubjectId.ShouldBe(vehicle.Id.Value);
        entry.ActorId.ShouldBe(ActorGuid);
        entry.OccurredAt.ShouldBe(vehicle.CreatedAt);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("vehicleId").GetGuid().ShouldBe(vehicle.Id.Value);
        root.GetProperty("licensePlate").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("licensePlate").GetString().ShouldBe("AB456CD");
        root.GetProperty("description").GetString().ShouldBe("Iveco Stralis 480");
        root.GetProperty("type").GetString().ShouldBe("Tractor");
        root.GetProperty("requiresTrailer").GetBoolean().ShouldBeTrue();
        root.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public void VehicleUpdated_WritesFullStateAfterTheChange()
    {
        var vehicle = NewVehicle();
        vehicle.ClearDomainEvents();
        vehicle.Update(
            LicensePlate.Create("XY 999 ZZ"), "Fiat Ducato", VehicleType.StandaloneVehicle, false,
            new FakeClock(new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc)), Actor);

        var entry = MapSingleEvent(vehicle);

        entry.EventType.ShouldBe("registry.vehicle.updated");
        entry.SubjectId.ShouldBe(vehicle.Id.Value);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("licensePlate").GetString().ShouldBe("XY999ZZ");
        root.GetProperty("description").GetString().ShouldBe("Fiat Ducato");
        root.GetProperty("type").GetString().ShouldBe("StandaloneVehicle");
        root.GetProperty("requiresTrailer").GetBoolean().ShouldBeFalse();
        root.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public void VehicleDecommissionedAndReactivated_WriteTheVehicleIdUnderTheirOwnEventType()
    {
        var vehicle = NewVehicle();
        var clock = new FakeClock(new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc));
        vehicle.ClearDomainEvents();

        vehicle.Decommission(clock, Actor);
        var decommissioned = MapSingleEvent(vehicle);
        vehicle.ClearDomainEvents();
        vehicle.Reactivate(clock, Actor);
        var reactivated = MapSingleEvent(vehicle);

        decommissioned.EventType.ShouldBe("registry.vehicle.decommissioned");
        reactivated.EventType.ShouldBe("registry.vehicle.reactivated");
        foreach (var entry in new[] { decommissioned, reactivated })
        {
            entry.SubjectId.ShouldBe(vehicle.Id.Value);
            using var payload = JsonDocument.Parse(entry.Payload);
            payload.RootElement.GetProperty("vehicleId").GetGuid().ShouldBe(vehicle.Id.Value);
        }
    }

    [Fact]
    public void AnonymousActor_WritesNullActorId()
    {
        var vehicle = NewVehicle();

        var entry = AuditInterceptor.MapToAuditLogEntry(vehicle.DomainEvents.Single(), actorId: null);

        entry.ActorId.ShouldBeNull();
    }
}
