using System.Text.Json;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.Pricing;
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
/// The audit row each vehicle and customer event produces: event type, subject, actor, timestamp and
/// a payload that writes the plate, VAT number and tax code as plain strings and enums by name.
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

    private static Customer NewCustomer() =>
        Customer.Create(
            CustomerId.New(),
            "  Comune di Penne ",
            VatNumber.Create("IT 01455280683"),
            TaxCode.Create("mrcgli72d45g438k"),
            " Corso Alessandrini 1 ",
            new FakeClock(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc)),
            Actor);

    private static AuditLogEntry MapSingleEvent(Customer customer) =>
        AuditInterceptor.MapToAuditLogEntry(customer.DomainEvents.Single(), ActorGuid);

    [Fact]
    public void CustomerCreated_WritesFullStateWithPlainCodesAndEnumsByName()
    {
        var customer = NewCustomer();

        var entry = MapSingleEvent(customer);

        entry.EventType.ShouldBe(AuditEventTypes.CustomerCreated);
        entry.EventType.ShouldBe("registry.customer.created");
        entry.SubjectId.ShouldBe(customer.Id.Value);
        entry.ActorId.ShouldBe(ActorGuid);
        entry.OccurredAt.ShouldBe(customer.CreatedAt);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        root.GetProperty("companyName").GetString().ShouldBe("Comune di Penne");
        root.GetProperty("vatNumber").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("vatNumber").GetString().ShouldBe("01455280683");
        root.GetProperty("taxCode").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("taxCode").GetString().ShouldBe("MRCGLI72D45G438K");
        root.GetProperty("address").GetString().ShouldBe("Corso Alessandrini 1");
        root.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public void CustomerCreated_WithoutCodesAndAddress_OmitsThem()
    {
        var customer = Customer.Create(
            CustomerId.New(), "Acme GmbH", null, null, null,
            new FakeClock(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc)), Actor);

        var entry = MapSingleEvent(customer);

        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.TryGetProperty("vatNumber", out _).ShouldBeFalse();
        root.TryGetProperty("taxCode", out _).ShouldBeFalse();
        root.TryGetProperty("address", out _).ShouldBeFalse();
    }

    [Fact]
    public void CustomerUpdated_WritesFullStateAfterTheChange()
    {
        var customer = NewCustomer();
        customer.ClearDomainEvents();
        customer.Update(
            "Acme Srl", VatNumber.Create("00743110157"), null, null,
            new FakeClock(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc)), Actor);

        var entry = MapSingleEvent(customer);

        entry.EventType.ShouldBe("registry.customer.updated");
        entry.SubjectId.ShouldBe(customer.Id.Value);
        entry.OccurredAt.ShouldBe(customer.LastModifiedAt);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("companyName").GetString().ShouldBe("Acme Srl");
        root.GetProperty("vatNumber").GetString().ShouldBe("00743110157");
        root.TryGetProperty("taxCode", out _).ShouldBeFalse();
        root.TryGetProperty("address", out _).ShouldBeFalse();
        root.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public void CustomerDeactivatedAndReactivated_WriteTheCustomerIdUnderTheirOwnEventType()
    {
        var customer = NewCustomer();
        var clock = new FakeClock(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc));
        customer.ClearDomainEvents();

        customer.Deactivate(clock, Actor);
        var deactivated = MapSingleEvent(customer);
        customer.ClearDomainEvents();
        customer.Reactivate(clock, Actor);
        var reactivated = MapSingleEvent(customer);

        deactivated.EventType.ShouldBe("registry.customer.deactivated");
        reactivated.EventType.ShouldBe("registry.customer.reactivated");
        foreach (var entry in new[] { deactivated, reactivated })
        {
            entry.SubjectId.ShouldBe(customer.Id.Value);
            using var payload = JsonDocument.Parse(entry.Payload);
            payload.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        }
    }

    private static readonly UserId DriverAccount = new(new Guid("b0000000-0000-0000-0000-000000000001"));

    private static Location NewLocation() =>
        Location.Create(
            LocationId.New(),
            "  Magazzino   centrale ",
            " Spoltore ",
            " Via Roma 1 ",
            new FakeClock(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc)),
            Actor);

    private static AuditLogEntry MapSingleEvent(Location location) =>
        AuditInterceptor.MapToAuditLogEntry(location.DomainEvents.Single(), ActorGuid);

    [Fact]
    public void LocationCreated_WritesFullStateWithEnumsByNameAndNoKeys()
    {
        var location = NewLocation();

        var entry = MapSingleEvent(location);

        entry.EventType.ShouldBe(AuditEventTypes.LocationCreated);
        entry.EventType.ShouldBe("registry.location.created");
        entry.SubjectId.ShouldBe(location.Id.Value);
        entry.ActorId.ShouldBe(ActorGuid);
        entry.OccurredAt.ShouldBe(location.CreatedAt);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["locationId", "name", "municipality", "address", "status"], ignoreOrder: true);
        root.GetProperty("locationId").GetGuid().ShouldBe(location.Id.Value);
        root.GetProperty("name").GetString().ShouldBe("Magazzino   centrale");
        root.GetProperty("municipality").GetString().ShouldBe("Spoltore");
        root.GetProperty("address").GetString().ShouldBe("Via Roma 1");
        root.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public void LocationCreated_WithoutAddress_OmitsIt()
    {
        var location = Location.Create(
            LocationId.New(), "Discarica", "Penne", null,
            new FakeClock(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc)), Actor);

        var entry = MapSingleEvent(location);

        using var payload = JsonDocument.Parse(entry.Payload);
        payload.RootElement.TryGetProperty("address", out _).ShouldBeFalse();
        payload.RootElement.GetProperty("name").GetString().ShouldBe("Discarica");
    }

    [Fact]
    public void LocationUpdated_WritesFullStateAfterTheChange()
    {
        var location = NewLocation();
        location.ClearDomainEvents();
        location.Update(
            "Cantiere Nord", "Penne", null,
            new FakeClock(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc)), Actor);

        var entry = MapSingleEvent(location);

        entry.EventType.ShouldBe("registry.location.updated");
        entry.SubjectId.ShouldBe(location.Id.Value);
        entry.OccurredAt.ShouldBe(location.LastModifiedAt);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("name").GetString().ShouldBe("Cantiere Nord");
        root.GetProperty("municipality").GetString().ShouldBe("Penne");
        root.TryGetProperty("address", out _).ShouldBeFalse();
        root.GetProperty("status").GetString().ShouldBe("Active");
        root.TryGetProperty("nameKey", out _).ShouldBeFalse();
        root.TryGetProperty("municipalityKey", out _).ShouldBeFalse();
    }

    [Fact]
    public void LocationDeactivatedAndReactivated_WriteTheLocationIdUnderTheirOwnEventType()
    {
        var location = NewLocation();
        var clock = new FakeClock(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc));
        location.ClearDomainEvents();

        location.Deactivate(clock, Actor);
        var deactivated = MapSingleEvent(location);
        location.ClearDomainEvents();
        location.Reactivate(clock, Actor);
        var reactivated = MapSingleEvent(location);

        deactivated.EventType.ShouldBe("registry.location.deactivated");
        reactivated.EventType.ShouldBe("registry.location.reactivated");
        foreach (var entry in new[] { deactivated, reactivated })
        {
            entry.SubjectId.ShouldBe(location.Id.Value);
            using var payload = JsonDocument.Parse(entry.Payload);
            payload.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["locationId"]);
            payload.RootElement.GetProperty("locationId").GetGuid().ShouldBe(location.Id.Value);
        }
    }

    private static Driver NewDriver() =>
        Driver.Create(
            DriverId.New(),
            "  Luca ",
            " Di Febo ",
            " 333 418 2291 ",
            DriverAccount,
            new FakeClock(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc)),
            Actor);

    private static AuditLogEntry MapSingleEvent(Driver driver) =>
        AuditInterceptor.MapToAuditLogEntry(driver.DomainEvents.Single(), ActorGuid);

    [Fact]
    public void DriverCreated_WritesFullStateWithPlainAccountIdAndEnumsByName()
    {
        var driver = NewDriver();

        var entry = MapSingleEvent(driver);

        entry.EventType.ShouldBe(AuditEventTypes.DriverCreated);
        entry.EventType.ShouldBe("registry.driver.created");
        entry.SubjectId.ShouldBe(driver.Id.Value);
        entry.ActorId.ShouldBe(ActorGuid);
        entry.OccurredAt.ShouldBe(driver.CreatedAt);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("driverId").GetGuid().ShouldBe(driver.Id.Value);
        root.GetProperty("firstName").GetString().ShouldBe("Luca");
        root.GetProperty("lastName").GetString().ShouldBe("Di Febo");
        root.GetProperty("phone").GetString().ShouldBe("333 418 2291");
        root.GetProperty("userId").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("userId").GetGuid().ShouldBe(DriverAccount.Value);
        root.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public void DriverCreated_WithoutPhoneAndAccount_OmitsThem()
    {
        var driver = Driver.Create(
            DriverId.New(), "Luca", "Di Febo", null, null,
            new FakeClock(new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc)), Actor);

        var entry = MapSingleEvent(driver);

        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.TryGetProperty("phone", out _).ShouldBeFalse();
        root.TryGetProperty("userId", out _).ShouldBeFalse();
    }

    [Fact]
    public void DriverUpdated_WritesFullStateAfterTheChange()
    {
        var driver = NewDriver();
        driver.ClearDomainEvents();
        driver.Update(
            "Marco", "Rossi", null, null,
            new FakeClock(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc)), Actor);

        var entry = MapSingleEvent(driver);

        entry.EventType.ShouldBe("registry.driver.updated");
        entry.SubjectId.ShouldBe(driver.Id.Value);
        entry.OccurredAt.ShouldBe(driver.LastModifiedAt);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("driverId").GetGuid().ShouldBe(driver.Id.Value);
        root.GetProperty("firstName").GetString().ShouldBe("Marco");
        root.GetProperty("lastName").GetString().ShouldBe("Rossi");
        root.TryGetProperty("phone", out _).ShouldBeFalse();
        root.TryGetProperty("userId", out _).ShouldBeFalse();
        root.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public void DriverDeactivatedAndReactivated_WriteTheDriverIdUnderTheirOwnEventType()
    {
        var driver = NewDriver();
        var clock = new FakeClock(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
        driver.ClearDomainEvents();

        driver.Deactivate(clock, Actor);
        var deactivated = MapSingleEvent(driver);
        driver.ClearDomainEvents();
        driver.Reactivate(clock, Actor);
        var reactivated = MapSingleEvent(driver);

        deactivated.EventType.ShouldBe("registry.driver.deactivated");
        reactivated.EventType.ShouldBe("registry.driver.reactivated");
        foreach (var entry in new[] { deactivated, reactivated })
        {
            entry.SubjectId.ShouldBe(driver.Id.Value);
            using var payload = JsonDocument.Parse(entry.Payload);
            payload.RootElement.GetProperty("driverId").GetGuid().ShouldBe(driver.Id.Value);
        }
    }

    [Fact]
    public void NightBandUpdated_WritesTheBandAsHoursAndMinutes()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
        var setting = NightBandSetting.Create(NightBand.Default, clock, Actor);
        setting.Update(NightBand.Create(new TimeOnly(21, 30), new TimeOnly(5, 5)).Value, clock, Actor);

        var entry = AuditInterceptor.MapToAuditLogEntry(setting.DomainEvents.Single(), ActorGuid);

        entry.EventType.ShouldBe(AuditEventTypes.NightBandUpdated);
        entry.EventType.ShouldBe("registry.night_band.updated");
        entry.SubjectId.ShouldBe(NightBandSetting.SingleId);
        entry.ActorId.ShouldBe(ActorGuid);
        entry.OccurredAt.ShouldBe(clock.UtcNow);
        using var payload = JsonDocument.Parse(entry.Payload);
        var root = payload.RootElement;
        root.GetProperty("settingId").GetGuid().ShouldBe(NightBandSetting.SingleId);
        root.GetProperty("start").GetString().ShouldBe("21:30");
        root.GetProperty("end").GetString().ShouldBe("05:05");
    }

    [Fact]
    public void AnonymousActor_WritesNullActorId()
    {
        var vehicle = NewVehicle();

        var entry = AuditInterceptor.MapToAuditLogEntry(vehicle.DomainEvents.Single(), actorId: null);

        entry.ActorId.ShouldBeNull();
    }
}
