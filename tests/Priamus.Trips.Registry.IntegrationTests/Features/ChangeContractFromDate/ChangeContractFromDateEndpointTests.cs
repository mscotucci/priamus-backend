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

    [Fact]
    public async Task AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), Guid.NewGuid(), Guid.NewGuid(), ChangeBody(0));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
