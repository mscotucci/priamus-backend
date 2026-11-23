using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ChangeContractFromDate;

/// <summary>
/// Endpoint integration tests for
/// <c>POST /api/registry/customers/{customerId}/contracts/{contractId}/change-from-date</c>,
/// against the per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class ChangeContractFromDateEndpointTests : RegistryIntegrationTestBase
{
    public ChangeContractFromDateEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(Guid customerId, Guid contractId) =>
        $"/api/registry/customers/{customerId}/contracts/{contractId}/change-from-date";

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid customerId, Guid contractId, object body) =>
        client.PostAsJsonAsync(Url(customerId, contractId), body, TestJson.Options, Ct);

    // The changed terms: a day price of 55 with a fixed minimum, where the seeded source has 40 and none.
    private static Dictionary<string, object?> ChangeBody(
        int? version,
        string? changeFrom = "2026-06-01",
        string? validFrom = "2026-01-01",
        string? validTo = null) =>
        new()
        {
            ["validFrom"] = validFrom,
            ["validTo"] = validTo,
            ["billingLevel"] = "FlatRate",
            ["pricingMode"] = "ByHour",
            ["requiredDocuments"] = new[] { "WasteForm" },
            ["nightSurcharge"] = false,
            ["dayPrice"] = 55m,
            ["minimumRule"] = "FixedMinimum",
            ["minimumAmount"] = 120m,
            ["version"] = version,
            ["changeFrom"] = changeFrom,
        };

    private async Task<Customer> SeedCustomerAsync(string vatNumber = VatNumberA, bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, vatNumber: vatNumber, taxCode: null, deactivated: deactivated);
    }

    // A flat-rate contract at 40 per hour with no minimum.
    private async Task<Contract> SeedAsync(CustomerId customerId, string validFrom, string? validTo)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, DateOnly.Parse(validFrom), validTo is null ? null : DateOnly.Parse(validTo),
            BillingLevel.FlatRate, PricingMode.ByHour, [RequiredDocument.DailySummary], nightSurcharge: false,
            rate: Rate.Create(40m, null, null, new MinimumRule.None()).Value);
    }

    private async Task<List<Contract>> StoredAsync(CustomerId customerId)
    {
        await using var db = CreateDbContext();
        return await db.Contracts.AsNoTracking().Where(c => c.CustomerId == customerId).ToListAsync(Ct);
    }

    private async Task<Contract> StoredOneAsync(ContractId id)
    {
        await using var db = CreateDbContext();
        return await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == id, Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task OpenContract_Returns201WithTheNewContract_AndTheSourceKeepsItsTermsUntilTheDayBefore()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value, ChangeBody(source.Version));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var newId = dto.GetProperty("id").GetGuid();
        newId.ShouldNotBe(source.Id.Value);
        dto.GetProperty("validFrom").GetString().ShouldBe("2026-06-01");
        dto.GetProperty("validTo").ValueKind.ShouldBe(JsonValueKind.Null);
        dto.GetProperty("dayPrice").GetDecimal().ShouldBe(55m);
        dto.GetProperty("minimumRule").GetString().ShouldBe("FixedMinimum");
        dto.GetProperty("minimumAmount").GetDecimal().ShouldBe(120m);
        response.Headers.Location!.ToString().ShouldEndWith($"/contracts/{newId}");

        var stored = await StoredOneAsync(source.Id);
        stored.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 1, 1));
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 5, 31));
        stored.Rate.ShouldNotBeNull().DayPrice.ShouldBe(40m);
        stored.Rate.Minimum.ShouldBeOfType<MinimumRule.None>();
        stored.RequiredDocuments.ShouldBe([RequiredDocument.DailySummary]);
        stored.Version.ShouldBe(source.Version + 1);

        var created = await StoredOneAsync(new ContractId(newId));
        created.CustomerId.ShouldBe(customer.Id);
        created.RequiredDocuments.ShouldBe([RequiredDocument.WasteForm]);
        created.Rate.ShouldNotBeNull().DayPrice.ShouldBe(55m);
    }

    [Fact]
    public async Task ClosedContract_IsCutInTwo_TheNewPieceKeepsTheWrittenEnd()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", "2026-12-31");
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value,
            ChangeBody(source.Version, changeFrom: "2026-07-01", validTo: "2026-12-31"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        dto.GetProperty("validFrom").GetString().ShouldBe("2026-07-01");
        dto.GetProperty("validTo").GetString().ShouldBe("2026-12-31");
        var stored = await StoredOneAsync(source.Id);
        stored.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 1, 1));
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public async Task WrittenStartMovedTogetherWithTheChange_TheFirstPieceTakesIt()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value,
            ChangeBody(source.Version, changeFrom: "2026-06-01", validFrom: "2026-02-01"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stored = await StoredOneAsync(source.Id);
        stored.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 2, 1));
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public async Task DateInThePast_IsAccepted()
    {
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2020-01-01", null);
        await using var factory = CreateFactory();

        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value,
            ChangeBody(source.Version, changeFrom: "2020-06-01", validFrom: "2020-01-01"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await StoredOneAsync(source.Id)).Validity.ValidTo.ShouldBe(new DateOnly(2020, 5, 31));
    }

    [Fact]
    public async Task DateOnTheWrittenEnd_IsAccepted_TheNewPieceLastsOneDay()
    {
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", "2026-12-31");
        await using var factory = CreateFactory();

        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value,
            ChangeBody(source.Version, changeFrom: "2026-12-31", validTo: "2026-12-31"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await StoredOneAsync(source.Id)).Validity.ValidTo.ShouldBe(new DateOnly(2026, 12, 30));
    }

    [Theory]
    [InlineData(null, null, "Valore obbligatorio.")]
    [InlineData("2026-01-01", null, "La data deve essere successiva all'inizio e dentro la validità del contratto.")]
    [InlineData("2025-12-31", null, "La data deve essere successiva all'inizio e dentro la validità del contratto.")]
    [InlineData("2027-01-01", "2026-12-31", "La data deve essere successiva all'inizio e dentro la validità del contratto.")]
    public async Task ChangeFromMissingOrOutsideTheWrittenValidity_Returns400UnderChangeFrom_AndChangesNothing(
        string? changeFrom, string? validTo, string expectedMessage)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", validTo);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value,
            ChangeBody(source.Version, changeFrom: changeFrom, validTo: validTo));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["ChangeFrom"]);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty("ChangeFrom")[0].GetString().ShouldBe(expectedMessage);
        (await StoredOneAsync(source.Id)).Version.ShouldBe(source.Version);
        (await StoredAsync(customer.Id)).Count.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task InvalidRateOfTheNewTerms_Returns400OnTheMember_AndChangesNothing()
    {
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        body["dayPrice"] = 0m;

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["DayPrice"]);
        (await StoredAsync(customer.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task StaleVersion_Returns409ConcurrentUpdate_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value, ChangeBody(source.Version + 1));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.ConcurrentUpdate, Ct);
        (await StoredOneAsync(source.Id)).Version.ShouldBe(source.Version);
        (await StoredAsync(customer.Id)).Count.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task ContractChangedBetweenTheLoadAndTheSave_Returns409ConcurrentUpdate_AndSavesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new BumpVersionInterceptor(ConnectionString, source.Id.Value))));

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value, ChangeBody(source.Version));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.ConcurrentUpdate, Ct);
        var stored = await StoredOneAsync(source.Id);
        stored.Version.ShouldBe(source.Version + 1);
        stored.Validity.IsOpen.ShouldBeTrue();
        (await StoredAsync(customer.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task NewPieceOverlappingALaterContract_Returns409NamingIt_AndSavesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", "2026-09-30");
        var later = await SeedAsync(customer.Id, "2026-10-01", "2026-12-31");
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act: the new piece runs to the end of December, over the later contract.
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value,
            ChangeBody(source.Version, changeFrom: "2026-07-01", validTo: "2026-12-31"));

        // Assert
        (await DetailAsync(response)).ShouldBe("La validità si sovrappone al contratto dal 01/10/2026 al 31/12/2026.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.Overlap, Ct);
        var stored = await StoredOneAsync(source.Id);
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 9, 30));
        stored.Version.ShouldBe(source.Version);
        (await StoredOneAsync(later.Id)).Version.ShouldBe(later.Version);
        (await StoredAsync(customer.Id)).Count.ShouldBe(2);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task OverlapTakenBetweenTheCheckAndTheSave_Returns409OverlapWithAnother_AndSavesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingContractInterceptor(ConnectionString, customer.Id.Value))));

        // Act: the new piece is open from March, the competing contract from June.
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value,
            ChangeBody(source.Version, changeFrom: "2026-03-01", validTo: null));

        // Assert
        (await DetailAsync(response)).ShouldBe("La validità si sovrappone a un altro contratto del cliente.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.OverlapWithAnother, Ct);
        var stored = await StoredOneAsync(source.Id);
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 3, 31));
        stored.Version.ShouldBe(source.Version);
        (await StoredAsync(customer.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task ContractOfADeactivatedCustomer_Returns409CustomerDeactivatedReadOnly_AndSavesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync(deactivated: true);
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value, ChangeBody(source.Version));

        // Assert
        (await DetailAsync(response)).ShouldBe(
            "Il cliente è disattivato: i suoi contratti non si possono modificare. Riattivalo per modificarli.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
        var stored = await StoredOneAsync(source.Id);
        stored.Version.ShouldBe(source.Version);
        stored.Validity.IsOpen.ShouldBeTrue();
        (await StoredAsync(customer.Id)).Count.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task ContractOfADeactivatedCustomer_IsRefusedBeforeItIsLoaded()
    {
        var customer = await SeedCustomerAsync(deactivated: true);
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, Guid.NewGuid(), ChangeBody(0));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
    }

    [Fact]
    public async Task SuccessfulChange_WritesAnUpdatedRowForTheSourceAndACreatedRowForTheNewContract_WithRateAndMinimum()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value, ChangeBody(source.Version));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var newId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();

        var updated = await db.AuditLog.SingleAsync(
            a => a.EventType == AuditEventTypes.ContractUpdated && a.SubjectId == source.Id.Value, Ct);
        updated.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var updatedPayload = JsonDocument.Parse(updated.Payload);
        updatedPayload.RootElement.GetProperty("validFrom").GetString().ShouldBe("2026-01-01");
        updatedPayload.RootElement.GetProperty("validTo").GetString().ShouldBe("2026-05-31");
        updatedPayload.RootElement.GetProperty("dayPrice").GetDecimal().ShouldBe(40m);
        updatedPayload.RootElement.GetProperty("minimumRule").GetString().ShouldBe("None");

        var createdRow = await db.AuditLog.SingleAsync(
            a => a.EventType == AuditEventTypes.ContractCreated && a.SubjectId == newId, Ct);
        createdRow.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var createdPayload = JsonDocument.Parse(createdRow.Payload);
        createdPayload.RootElement.GetProperty("validFrom").GetString().ShouldBe("2026-06-01");
        createdPayload.RootElement.GetProperty("dayPrice").GetDecimal().ShouldBe(55m);
        createdPayload.RootElement.GetProperty("minimumRule").GetString().ShouldBe("FixedMinimum");
        createdPayload.RootElement.GetProperty("minimumAmount").GetDecimal().ShouldBe(120m);
    }

    [Fact]
    public async Task UnknownCustomer_Returns404CustomerNotFound()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Guid.NewGuid(), Guid.NewGuid(), ChangeBody(0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Fact]
    public async Task UnknownContract_Returns404ContractNotFound()
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, Guid.NewGuid(), ChangeBody(0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
    }

    [Fact]
    public async Task ContractOfAnotherCustomer_Returns404ContractNotFound_AndChangesNothing()
    {
        var owner = await SeedCustomerAsync();
        var other = await SeedCustomerAsync(VatNumberB);
        var source = await SeedAsync(owner.Id, "2026-01-01", null);
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), other.Id.Value, source.Id.Value, ChangeBody(source.Version));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
        (await StoredOneAsync(source.Id)).Version.ShouldBe(source.Version);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoChangesAContractFromADate(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await PostAsync(client, customer.Id.Value, source.Id.Value, ChangeBody(source.Version));

        // Assert
        response.StatusCode.ShouldBe(expected);
        (await StoredAsync(customer.Id)).Count.ShouldBe(expected == HttpStatusCode.Created ? 2 : 1);
    }

    private async Task<Vehicle> SeedVehicleRowAsync(
        string plate, VehicleType type = VehicleType.Tractor, bool decommissioned = false)
    {
        await using var db = CreateDbContext();
        return await SeedVehicleAsync(db, plate, type, decommissioned: decommissioned);
    }

    private static VehicleRate DayRate(VehicleId vehicleId, decimal dayPrice = 70m) =>
        new(vehicleId, null, null, Rate.Create(dayPrice, null, null, new MinimumRule.None()).Value);

    // A flat-rate contract at 40 per hour with no minimum, holding the given vehicle rates.
    private async Task<Contract> SeedWithVehicleRatesAsync(CustomerId customerId, params VehicleRate[] vehicleRates)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, new DateOnly(2026, 1, 1), null, BillingLevel.FlatRate, PricingMode.ByHour,
            [RequiredDocument.DailySummary], nightSurcharge: false,
            rate: Rate.Create(40m, null, null, new MinimumRule.None()).Value, vehicleRates: vehicleRates);
    }

    [Fact]
    public async Task TheSourceKeepsItsVehicleRates_AndTheNewContractGetsTheWrittenOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var kept = await SeedVehicleRowAsync("AB456CD");
        var written = await SeedVehicleRowAsync("CD123EF", VehicleType.StandaloneVehicle);
        var source = await SeedWithVehicleRatesAsync(customer.Id, DayRate(kept.Id, 66m));
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(written.Id.Value, """{"dayPrice":82.5,"minimumRule":"FixedMinimum","minimumAmount":200}""") };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var returned = dto.GetProperty("vehicleRates").EnumerateArray().ShouldHaveSingleItem();
        returned.GetProperty("licensePlate").GetString().ShouldBe("CD123EF");
        returned.GetProperty("dayPrice").GetDecimal().ShouldBe(82.5m);
        (await StoredOneAsync(source.Id)).VehicleRates.ShouldBe([DayRate(kept.Id, 66m)]);
        var created = (await StoredOneAsync(new ContractId(dto.GetProperty("id").GetGuid()))).VehicleRates.ShouldHaveSingleItem();
        created.VehicleId.ShouldBe(written.Id);
        created.Rate.DayPrice.ShouldBe(82.5m);
        created.Rate.Minimum.ShouldBeOfType<MinimumRule.FixedMinimum>().Amount.ShouldBe(200m);
    }

    [Fact]
    public async Task WritingNoVehicleRates_GivesTheNewContractNone_AndTheSourceKeepsItsOwn()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var source = await SeedWithVehicleRatesAsync(customer.Id, DayRate(vehicle.Id));
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, source.Id.Value, ChangeBody(source.Version));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        dto.GetProperty("vehicleRates").GetArrayLength().ShouldBe(0);
        (await StoredOneAsync(source.Id)).VehicleRates.ShouldBe([DayRate(vehicle.Id)]);
        (await StoredOneAsync(new ContractId(dto.GetProperty("id").GetGuid()))).VehicleRates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DecommissionedVehicleOfTheSource_IsAcceptedInTheNewContract()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleRowAsync("AB456CD", decommissioned: true);
        var source = await SeedWithVehicleRatesAsync(customer.Id, DayRate(vehicle.Id, 60m));
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(vehicle.Id.Value) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        dto.GetProperty("vehicleRates").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("vehicleStatus").GetString().ShouldBe("Decommissioned");
        (await StoredOneAsync(new ContractId(dto.GetProperty("id").GetGuid()))).VehicleRates
            .ShouldBe([DayRate(vehicle.Id, 70m)]);
        (await StoredOneAsync(source.Id)).VehicleRates.ShouldBe([DayRate(vehicle.Id, 60m)]);
    }

    [Fact]
    public async Task SuccessfulChange_WritesTheVehicleRatesOfEachContractInItsOwnAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var kept = await SeedVehicleRowAsync("AB456CD");
        var written = await SeedVehicleRowAsync("CD123EF");
        var source = await SeedWithVehicleRatesAsync(customer.Id, DayRate(kept.Id, 66m));
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(written.Id.Value) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        var newId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var updated = await db.AuditLog.SingleAsync(
            a => a.EventType == AuditEventTypes.ContractUpdated && a.SubjectId == source.Id.Value, Ct);
        using var updatedPayload = JsonDocument.Parse(updated.Payload);
        var keptRate = updatedPayload.RootElement.GetProperty("vehicleRates").EnumerateArray().ShouldHaveSingleItem();
        keptRate.GetProperty("vehicleId").GetGuid().ShouldBe(kept.Id.Value);
        keptRate.GetProperty("dayPrice").GetDecimal().ShouldBe(66m);
        var createdRow = await db.AuditLog.SingleAsync(
            a => a.EventType == AuditEventTypes.ContractCreated && a.SubjectId == newId, Ct);
        using var createdPayload = JsonDocument.Parse(createdRow.Payload);
        var writtenRate = createdPayload.RootElement.GetProperty("vehicleRates").EnumerateArray().ShouldHaveSingleItem();
        writtenRate.GetProperty("vehicleId").GetGuid().ShouldBe(written.Id.Value);
        writtenRate.GetProperty("dayPrice").GetDecimal().ShouldBe(70m);
    }

    [Theory]
    [InlineData("FlatRate", "unknown")]
    [InlineData("FlatRate", "trailer")]
    [InlineData("FlatRate", "decommissioned")]
    [InlineData("PerLocationPair", "unknown")]
    [InlineData("PerLocationPair", "trailer")]
    [InlineData("PerLocationPair", "decommissioned")]
    public async Task VehicleThatCannotHaveAVehicleRate_Returns400WithTheCodeAndTheNameInTheText_AndChangesNothing(
        string level, string kind)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var seeded = await SeedWithVehicleRatesAsync(customer.Id);
        var unknown = Guid.NewGuid();
        var vehicleId = kind switch
        {
            "trailer" => (await SeedVehicleRowAsync("AB456CD", VehicleType.Trailer)).Id.Value,
            "decommissioned" => (await SeedVehicleRowAsync("AB456CD", decommissioned: true)).Id.Value,
            _ => unknown,
        };
        var (error, text) = kind switch
        {
            "trailer" => (ContractErrors.VehicleRateOnTrailer, "Il mezzo AB456CD è un rimorchio: non può avere una tariffa propria."),
            "decommissioned" => (ContractErrors.VehicleRateDecommissioned, "Il mezzo AB456CD è dismesso: non si può aggiungere una sua tariffa."),
            _ => (ContractErrors.VehicleRateUnknownVehicle, $"Il mezzo {unknown} non esiste."),
        };
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = ChangeBody(seeded.Version);
        if (level != "FlatRate")
        {
            SetLocationRates(body, level, "ByHour", false, LocationRateFor(level, a.Id.Value, b.Id.Value));
        }

        body["vehicleRates"] = new[] { VehicleRateFor(level, vehicleId, a.Id.Value, b.Id.Value) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, error, Ct);
        (await DetailAsync(response)).ShouldBe(text);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().VehicleRates.ShouldBeEmpty();
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Theory]
    [MemberData(nameof(InvalidVehicleRates), MemberType = typeof(ContractTestSupport))]
    public async Task InvalidVehicleRate_Returns400UnderTheItemKeys_AndChangesNothing(
        string billingLevel, string pricingMode, bool nightSurcharge, string rate, string keys)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedWithVehicleRatesAsync(customer.Id);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = ChangeBody(seeded.Version);
        SetRateWithVehicleRate(body, billingLevel, pricingMode, nightSurcharge, rate);

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(VehicleRateKeys(keys));
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task DuplicateAndMissingVehicle_Return400OnTheLaterItemsVehicleId()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedWithVehicleRatesAsync(customer.Id);
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        await using var factory = CreateFactory();
        var body = ChangeBody(seeded.Version);
        body["vehicleRates"] = new[]
        {
            VehicleRateItem(vehicle.Id.Value), VehicleRateItem(vehicle.Id.Value), VehicleRateItem(null),
        };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VehicleRates[1].VehicleId", "VehicleRates[2].VehicleId"]);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task Driver_Gets403WhenWritingVehicleRates_AndNothingIsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedWithVehicleRatesAsync(customer.Id);
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());
        var body = ChangeBody(seeded.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(vehicle.Id.Value) };

        // Act
        var response = await PostAsync(client, customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeactivatedCustomer_RefusesVehicleRateChanges_AndKeepsTheStoredOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync(deactivated: true);
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var other = await SeedVehicleRowAsync("CD123EF");
        var seeded = await SeedWithVehicleRatesAsync(customer.Id, DayRate(vehicle.Id));
        await using var factory = CreateFactory();
        var body = ChangeBody(seeded.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(other.Id.Value) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBe([DayRate(vehicle.Id)]);
    }

    private async Task<Location> SeedLocationRowAsync(
        string name, string municipality = "Pescara", bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await LocationTestSupport.SeedLocationAsync(db, name, municipality, deactivated: deactivated);
    }

    private static LocationRate PairRate(LocationId pickup, LocationId delivery, decimal dayPrice = 70m) =>
        new(pickup, delivery, Rate.Create(dayPrice, null, null, new MinimumRule.None()).Value);

    // An open location-pair contract priced by the hour with no night surcharge, holding the given location rates.
    private async Task<Contract> SeedWithLocationRatesAsync(CustomerId customerId, params LocationRate[] locationRates)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, new DateOnly(2026, 1, 1), null, BillingLevel.PerLocationPair, PricingMode.ByHour,
            [RequiredDocument.DailySummary], nightSurcharge: false,
            locationRates: locationRates.Length == 0 ? null : locationRates);
    }

    private static Dictionary<string, object?> PairBody(int? version, params Dictionary<string, object?>[] locationRates)
    {
        var body = ChangeBody(version);
        SetLocationRates(body, "PerLocationPair", "ByHour", false, locationRates);
        return body;
    }

    [Fact]
    public async Task TheSourceKeepsItsLocationRates_AndTheNewContractGetsTheWrittenOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord", "Chieti");
        var source = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, b.Id, 66m));
        await using var factory = CreateFactory();
        var body = PairBody(source.Version,
            LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value,
                """{"dayPrice":82.5,"minimumRule":"FixedMinimum","minimumAmount":200}"""));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var returned = dto.GetProperty("locationRates").EnumerateArray().ShouldHaveSingleItem();
        returned.GetProperty("pickup").GetProperty("name").GetString().ShouldBe("Cantiere nord");
        returned.GetProperty("delivery").GetProperty("name").GetString().ShouldBe("Magazzino centrale");
        returned.GetProperty("dayPrice").GetDecimal().ShouldBe(82.5m);
        (await StoredOneAsync(source.Id)).LocationRates.ShouldBe([PairRate(a.Id, b.Id, 66m)]);
        var created = (await StoredOneAsync(new ContractId(dto.GetProperty("id").GetGuid()))).LocationRates.ShouldHaveSingleItem();
        created.PickupLocationId.ShouldBe(b.Id);
        created.DeliveryLocationId.ShouldBe(a.Id);
        created.Rate.DayPrice.ShouldBe(82.5m);
        created.Rate.Minimum.ShouldBeOfType<MinimumRule.FixedMinimum>().Amount.ShouldBe(200m);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task WritingNoLocationRates_Returns400OnLocationRates_AndChangesNothing(string form)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var source = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, a.Id));
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = PairBody(source.Version);
        if (form == "absent")
        {
            body.Remove("locationRates");
        }
        else if (form == "null")
        {
            body["locationRates"] = null;
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["LocationRates"]);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").GetProperty("LocationRates")[0]
            .GetString().ShouldBe("Aggiungi almeno una tariffa.");
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().LocationRates.ShouldBe([PairRate(a.Id, a.Id)]);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task LegacyLocationContractWithoutRates_CannotBeChangedFromADate_AndNothingIsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        Contract legacy;
        await using (var db = CreateDbContext())
        {
            legacy = await SeedLocationContractWithoutRatesAsync(db, customer.Id, new DateOnly(2026, 1, 1), null);
        }

        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = PairBody(legacy.Version, LocationRateFor("PerLocationPair", a.Id.Value, a.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, legacy.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, ContractErrors.LocationRatesRequired, Ct);
        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        stored.Version.ShouldBe(legacy.Version);
        stored.Validity.ValidTo.ShouldBeNull();
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task DeactivatedLocationOfTheSource_IsAcceptedInTheNewContract_AlsoWhenTheOtherSideChanges()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var kept = await SeedLocationRowAsync("Cantiere nord", "Chieti", deactivated: true);
        var other = await SeedLocationRowAsync("Magazzino centrale");
        var source = await SeedWithLocationRatesAsync(customer.Id, PairRate(kept.Id, kept.Id, 60m));
        await using var factory = CreateFactory();
        var body = PairBody(source.Version,
            LocationRateFor("PerLocationPair", kept.Id.Value, kept.Id.Value),
            LocationRateFor("PerLocationPair", kept.Id.Value, other.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        dto.GetProperty("locationRates").EnumerateArray()
            .Select(r => r.GetProperty("pickup").GetProperty("status").GetString()).ShouldAllBe(status => status == "Deactivated");
        (await StoredOneAsync(new ContractId(dto.GetProperty("id").GetGuid()))).LocationRates
            .ShouldBe([PairRate(kept.Id, kept.Id), PairRate(kept.Id, other.Id)], ignoreOrder: true);
        (await StoredOneAsync(source.Id)).LocationRates.ShouldBe([PairRate(kept.Id, kept.Id, 60m)]);
    }

    [Fact]
    public async Task SuccessfulChange_WritesTheLocationRatesOfEachContractInItsOwnAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var source = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, b.Id, 66m));
        await using var factory = CreateFactory();
        var body = PairBody(source.Version, LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        var newId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var updated = await db.AuditLog.SingleAsync(
            a2 => a2.EventType == AuditEventTypes.ContractUpdated && a2.SubjectId == source.Id.Value, Ct);
        using var updatedPayload = JsonDocument.Parse(updated.Payload);
        var keptRate = updatedPayload.RootElement.GetProperty("locationRates").EnumerateArray().ShouldHaveSingleItem();
        keptRate.GetProperty("pickupLocationId").GetGuid().ShouldBe(a.Id.Value);
        keptRate.GetProperty("deliveryLocationId").GetGuid().ShouldBe(b.Id.Value);
        keptRate.GetProperty("dayPrice").GetDecimal().ShouldBe(66m);
        var createdRow = await db.AuditLog.SingleAsync(
            a2 => a2.EventType == AuditEventTypes.ContractCreated && a2.SubjectId == newId, Ct);
        using var createdPayload = JsonDocument.Parse(createdRow.Payload);
        var writtenRate = createdPayload.RootElement.GetProperty("locationRates").EnumerateArray().ShouldHaveSingleItem();
        writtenRate.GetProperty("pickupLocationId").GetGuid().ShouldBe(b.Id.Value);
        writtenRate.GetProperty("deliveryLocationId").GetGuid().ShouldBe(a.Id.Value);
        writtenRate.GetProperty("dayPrice").GetDecimal().ShouldBe(70m);
    }

    [Theory]
    [MemberData(nameof(InvalidLocationRates), MemberType = typeof(ContractTestSupport))]
    public async Task InvalidLocationRate_Returns400UnderTheItemKeys_AndChangesNothing(
        string pricingMode, bool nightSurcharge, string rate, string keys)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedWithLocationRatesAsync(customer.Id);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        SetLocationRates(body, "PerLocationPair", pricingMode, nightSurcharge,
            LocationRateFor("PerLocationPair", Guid.NewGuid(), Guid.NewGuid(), rate));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(LocationRateKeys(keys));
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().Version.ShouldBe(source.Version);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Theory]
    [InlineData("PerPickupLocation", "LocationRates[1].PickupLocationId")]
    [InlineData("PerDeliveryLocation", "LocationRates[1].DeliveryLocationId")]
    [InlineData("PerLocationPair", "LocationRates[1].DeliveryLocationId")]
    public async Task SameLocationOrPairTwice_Returns400OnTheLaterItem_AndChangesNothing(string level, string key)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var source = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        SetLocationRates(body, level, "ByHour", false,
            LocationRateFor(level, a.Id.Value, b.Id.Value), LocationRateFor(level, a.Id.Value, b.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task OppositePairsAndAPairOfTheSameLocation_AreDifferentPairsAndAreSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var source = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = PairBody(source.Version,
            LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value),
            LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value),
            LocationRateFor("PerLocationPair", a.Id.Value, a.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var newId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        (await StoredOneAsync(new ContractId(newId))).LocationRates
            .Select(r => (r.PickupLocationId, r.DeliveryLocationId))
            .ShouldBe([(a.Id, b.Id), (b.Id, a.Id), (a.Id, a.Id)], ignoreOrder: true);
    }

    [Theory]
    [InlineData("PerPickupLocation", false, false, "LocationRates[0].PickupLocationId")]
    [InlineData("PerPickupLocation", true, true, "LocationRates[0].DeliveryLocationId")]
    [InlineData("PerDeliveryLocation", false, false, "LocationRates[0].DeliveryLocationId")]
    [InlineData("PerDeliveryLocation", true, true, "LocationRates[0].PickupLocationId")]
    [InlineData("PerLocationPair", false, true, "LocationRates[0].PickupLocationId")]
    [InlineData("PerLocationPair", true, false, "LocationRates[0].DeliveryLocationId")]
    public async Task LocationRateWithTheWrongLocationsForTheLevel_Returns400OnTheMember(
        string level, bool pickupSent, bool deliverySent, string key)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var source = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        SetLocationRates(body, level, "ByHour", false,
            LocationRateFor("PerLocationPair", pickupSent ? Guid.NewGuid() : null, deliverySent ? Guid.NewGuid() : null));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LocationRatesOnAFlatRateContract_Return400UnderLocationRates_AndChangeNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationRowAsync("Magazzino centrale");
        var source = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        body["locationRates"] = new[] { LocationRateFor("PerPickupLocation", location.Id.Value, null) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["LocationRates"]);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LocationContractWithARateOfItsOwn_Returns400OnThoseMembers_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var source = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, a.Id));
        await using var factory = CreateFactory();
        var body = PairBody(source.Version, LocationRateFor("PerLocationPair", a.Id.Value, a.Id.Value));
        body["dayPrice"] = 45m;
        body["minimumRule"] = "None";

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["DayPrice", "MinimumRule"]);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("deactivated")]
    public async Task UnknownOrNewlyRatedDeactivatedLocation_Returns400WithTheCodeAndTheLocationInTheText_AndChangesNothing(
        string kind)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var active = await SeedLocationRowAsync("Magazzino centrale");
        var deactivated = await SeedLocationRowAsync("Cantiere nord", "Chieti", deactivated: true);
        var unknown = Guid.NewGuid();
        var source = await SeedWithLocationRatesAsync(customer.Id, PairRate(active.Id, active.Id));
        var before = await AuditCountAsync();
        var (error, text, delivery) = kind == "unknown"
            ? (ContractErrors.LocationRateUnknownLocation, $"Il luogo {unknown} non esiste.", unknown)
            : (ContractErrors.LocationRateDeactivatedLocation,
                "Il luogo Cantiere nord, Chieti è disattivato: non si può aggiungere una sua tariffa.", deactivated.Id.Value);
        await using var factory = CreateFactory();
        var body = PairBody(source.Version, LocationRateFor("PerLocationPair", active.Id.Value, delivery));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, error, Ct);
        (await DetailAsync(response)).ShouldBe(text);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().LocationRates.ShouldBe([PairRate(active.Id, active.Id)]);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Driver_Gets403WhenWritingLocationRates_AndNothingIsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationRowAsync("Magazzino centrale");
        var source = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());
        var body = PairBody(source.Version, LocationRateFor("PerLocationPair", location.Id.Value, location.Id.Value));

        // Act
        var response = await PostAsync(client, customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().LocationRates.ShouldBe(source.LocationRates);
    }

    [Fact]
    public async Task DeactivatedCustomer_RefusesLocationRateChanges_AndKeepsTheStoredOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync(deactivated: true);
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var source = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, b.Id));
        await using var factory = CreateFactory();
        var body = PairBody(source.Version, LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().LocationRates.ShouldBe([PairRate(a.Id, b.Id)]);
    }

    private static VehicleRate PairVehicleRate(VehicleId vehicleId, LocationId pickup, LocationId delivery, decimal dayPrice = 70m) =>
        new(vehicleId, pickup, delivery, Rate.Create(dayPrice, null, null, new MinimumRule.None()).Value);

    // An open location-pair contract priced by the hour with no night surcharge, holding the given location and vehicle rates.
    private async Task<Contract> SeedWithLocationAndVehicleRatesAsync(
        CustomerId customerId, LocationRate[] locationRates, params VehicleRate[] vehicleRates)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, new DateOnly(2026, 1, 1), null, BillingLevel.PerLocationPair, PricingMode.ByHour,
            [RequiredDocument.DailySummary], nightSurcharge: false,
            locationRates: locationRates, vehicleRates: vehicleRates);
    }

    [Fact]
    public async Task TheSourceKeepsItsVehicleRatesOnLocations_AndTheNewContractGetsTheWrittenOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var c = await SeedLocationRowAsync("Deposito sud");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var source = await SeedWithLocationAndVehicleRatesAsync(
            customer.Id, [PairRate(a.Id, b.Id), PairRate(b.Id, c.Id), PairRate(c.Id, a.Id)],
            PairVehicleRate(vehicle.Id, a.Id, b.Id, 66m));
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        SetThreeLocationsWithVehicleRates(body, "PerLocationPair", a.Id.Value, b.Id.Value, c.Id.Value,
            VehicleRateFor("PerLocationPair", vehicle.Id.Value, b.Id.Value, c.Id.Value),
            VehicleRateFor("PerLocationPair", vehicle.Id.Value, c.Id.Value, a.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        dto.GetProperty("vehicleRates").GetArrayLength().ShouldBe(2);
        (await StoredOneAsync(source.Id)).VehicleRates.ShouldBe([PairVehicleRate(vehicle.Id, a.Id, b.Id, 66m)]);
        (await StoredOneAsync(new ContractId(dto.GetProperty("id").GetGuid()))).VehicleRates
            .Select(r => (r.VehicleId, r.PickupLocationId, r.DeliveryLocationId, r.Rate.DayPrice))
            .ShouldBe([(vehicle.Id, b.Id, c.Id, 70m), (vehicle.Id, c.Id, a.Id, 70m)], ignoreOrder: true);
    }

    [Fact]
    public async Task ChangingOnlyTheLocationsOfAVehicleRate_LandsInTheNewContract_AndTheSourceKeepsTheOldOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var c = await SeedLocationRowAsync("Deposito sud");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var source = await SeedWithLocationAndVehicleRatesAsync(
            customer.Id, [PairRate(a.Id, b.Id), PairRate(b.Id, c.Id), PairRate(c.Id, a.Id)],
            PairVehicleRate(vehicle.Id, a.Id, b.Id));
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        SetThreeLocationsWithVehicleRates(body, "PerLocationPair", a.Id.Value, b.Id.Value, c.Id.Value,
            VehicleRateFor("PerLocationPair", vehicle.Id.Value, b.Id.Value, c.Id.Value));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await StoredOneAsync(source.Id)).VehicleRates.ShouldBe([PairVehicleRate(vehicle.Id, a.Id, b.Id)]);
        (await StoredOneAsync(new ContractId(dto.GetProperty("id").GetGuid()))).VehicleRates
            .ShouldBe([PairVehicleRate(vehicle.Id, b.Id, c.Id)]);
    }

    [Theory]
    [InlineData("PerPickupLocation", "VehicleRates[1].PickupLocationId")]
    [InlineData("PerDeliveryLocation", "VehicleRates[1].DeliveryLocationId")]
    [InlineData("PerLocationPair", "VehicleRates[1].DeliveryLocationId")]
    public async Task SameVehicleOnTheSameLocationOrPairTwice_Returns400OnTheLaterItem_InItalian_AndChangesNothing(
        string level, string key)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var c = await SeedLocationRowAsync("Deposito sud");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var source = await SeedWithLocationAndVehicleRatesAsync(customer.Id, [PairRate(a.Id, b.Id)]);
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        SetThreeLocationsWithVehicleRates(body, level, a.Id.Value, b.Id.Value, c.Id.Value,
            VehicleRateFor(level, vehicle.Id.Value, a.Id.Value, b.Id.Value),
            VehicleRateFor(level, vehicle.Id.Value, a.Id.Value, b.Id.Value, """{"dayPrice":82.5,"minimumRule":"None"}"""));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty(key)[0].GetString().ShouldBe("Il mezzo ha già una tariffa propria per questo luogo o questa coppia.");
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("PerPickupLocation", "VehicleRates[0].PickupLocationId")]
    [InlineData("PerDeliveryLocation", "VehicleRates[0].DeliveryLocationId")]
    [InlineData("PerLocationPair", "VehicleRates[0].DeliveryLocationId")]
    public async Task VehicleRateOnALocationOrPairWithoutALocationRate_Returns400OnTheMember_InItalian_AndChangesNothing(
        string level, string key)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var c = await SeedLocationRowAsync("Deposito sud");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var source = await SeedWithLocationAndVehicleRatesAsync(customer.Id, [PairRate(a.Id, b.Id)]);
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        SetLocationRates(body, level, "ByHour", false, LocationRateFor(level, a.Id.Value, b.Id.Value));
        body["vehicleRates"] = new[] { VehicleRateFor(level, vehicle.Id.Value, b.Id.Value, c.Id.Value) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty(key)[0].GetString().ShouldBe("Il contratto non ha una tariffa per questo luogo o questa coppia.");
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task VehicleRateOfAFlatRateContractWithALocation_Returns400OnTheMember_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var source = await SeedWithVehicleRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = ChangeBody(source.Version);
        var item = VehicleRateItem(vehicle.Id.Value);
        item["deliveryLocationId"] = a.Id.Value;
        body["vehicleRates"] = new[] { item };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, source.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VehicleRates[0].DeliveryLocationId"]);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), Guid.NewGuid(), Guid.NewGuid(), ChangeBody(0));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
