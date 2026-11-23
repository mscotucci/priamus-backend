using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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

namespace Priamus.Trips.Registry.IntegrationTests.Features.UpdateContract;

/// <summary>
/// Endpoint integration tests for <c>PUT /api/registry/customers/{customerId}/contracts/{contractId}</c>,
/// against the per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class UpdateContractEndpointTests : RegistryIntegrationTestBase
{
    public UpdateContractEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(Guid customerId, Guid contractId) =>
        $"/api/registry/customers/{customerId}/contracts/{contractId}";

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid customerId, Guid contractId, object body) =>
        client.PutAsJsonAsync(Url(customerId, contractId), body, TestJson.Options, Ct);

    private static Dictionary<string, object?> UpdateBody(
        int? version,
        string? validFrom = "2026-04-01",
        string? validTo = "2026-12-31",
        string billingLevel = "FlatRate",
        string pricingMode = "ByHour",
        string[]? requiredDocuments = null,
        bool nightSurcharge = false) =>
        new()
        {
            ["validFrom"] = validFrom,
            ["validTo"] = validTo,
            ["billingLevel"] = billingLevel,
            ["pricingMode"] = pricingMode,
            ["requiredDocuments"] = requiredDocuments ?? ["WasteForm"],
            ["nightSurcharge"] = nightSurcharge,
            ["dayPrice"] = billingLevel == "FlatRate" ? 45.5m : null,
            ["minimumRule"] = billingLevel == "FlatRate" ? "None" : null,
            ["version"] = version,
        };

    private async Task<Customer> SeedCustomerAsync(string vatNumber = VatNumberA, bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, vatNumber: vatNumber, taxCode: null, deactivated: deactivated);
    }

    private async Task<Contract> SeedAsync(CustomerId customerId, string validFrom, string? validTo)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, DateOnly.Parse(validFrom), validTo is null ? null : DateOnly.Parse(validTo));
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
    public async Task ValidRequest_Returns204AndReplacesEveryTermAndBumpsTheVersion()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value,
            UpdateBody(seeded.Version, validFrom: "2026-04-01", validTo: null, requiredDocuments: ["WasteForm", "WasteForm"]));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 4, 1));
        stored.Validity.IsOpen.ShouldBeTrue();
        stored.BillingLevel.ShouldBe(BillingLevel.FlatRate);
        stored.PricingMode.ShouldBe(PricingMode.ByHour);
        stored.RequiredDocuments.ShouldBe([RequiredDocument.WasteForm]);
        stored.NightSurcharge.ShouldBeFalse();
        stored.Version.ShouldBe(seeded.Version + 1);
    }

    [Fact]
    public async Task EmptySetOfDocuments_IsAccepted()
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, UpdateBody(seeded.Version, requiredDocuments: []));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(seeded.Id)).RequiredDocuments.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("validFrom", "ValidFrom")]
    [InlineData("billingLevel", "BillingLevel")]
    [InlineData("pricingMode", "PricingMode")]
    [InlineData("requiredDocuments", "RequiredDocuments")]
    [InlineData("nightSurcharge", "NightSurcharge")]
    [InlineData("version", "Version")]
    public async Task MissingRequiredMember_Returns400UnderItsKey_AndChangesNothing(string member, string key)
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body.Remove(member);

        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Theory]
    [InlineData("billingLevel")]
    [InlineData("pricingMode")]
    public async Task UnknownEnumName_Returns400_AndChangesNothing(string member)
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body[member] = "NoSuchValue";

        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task EndBeforeStart_Returns400UnderValidTo_AndChangesNothing()
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value,
            UpdateBody(seeded.Version, validFrom: "2026-04-01", validTo: "2026-03-31"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["ValidTo"]);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task StartAndEndOnTheSameDay_Returns204()
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value,
            UpdateBody(seeded.Version, validFrom: "2026-04-01", validTo: "2026-04-01"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("2026-04-01", "2026-12-31", "dal 01/07/2026 al 30/09/2026")]
    [InlineData("2026-01-01", "2026-07-01", "dal 01/07/2026 al 30/09/2026")]
    [InlineData("2026-09-30", null, "dal 01/07/2026 al 30/09/2026")]
    public async Task EditOverlappingAnotherContract_Returns409NamingItsValidity_AndChangesNothing(
        string validFrom, string? validTo, string expectedValidity)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var edited = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        var other = await SeedAsync(customer.Id, "2026-07-01", "2026-09-30");
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, edited.Id.Value,
            UpdateBody(edited.Version, validFrom: validFrom, validTo: validTo));

        // Assert
        (await DetailAsync(response)).ShouldBe($"La validità si sovrappone al contratto {expectedValidity}.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.Overlap, Ct);
        var storedEdited = await StoredOneAsync(edited.Id);
        storedEdited.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 1, 1));
        storedEdited.Validity.ValidTo.ShouldBe(new DateOnly(2026, 3, 31));
        storedEdited.Version.ShouldBe(edited.Version);
        (await StoredOneAsync(other.Id)).Version.ShouldBe(other.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task EditOverlappingAnOpenContract_Returns409NamingItOpen()
    {
        var customer = await SeedCustomerAsync();
        var edited = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await SeedAsync(customer.Id, "2027-01-01", null);
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, edited.Id.Value,
            UpdateBody(edited.Version, validFrom: "2026-06-01", validTo: "2027-01-01"));

        (await DetailAsync(response)).ShouldBe("La validità si sovrappone al contratto dal 01/01/2027, senza scadenza.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.Overlap, Ct);
    }

    [Fact]
    public async Task EditAgainstItsOwnPreviousValidity_IsNotAnOverlap()
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value,
            UpdateBody(seeded.Version, validFrom: "2026-02-01", validTo: "2026-04-30"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 2, 1));
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 4, 30));
    }

    [Fact]
    public async Task EditNeverClosesAnotherContract()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var edited = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        var open = await SeedAsync(customer.Id, "2026-07-01", null);
        await using var factory = CreateFactory();

        // Act: moves the edited contract after the open one's start; the open one is not closed.
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, edited.Id.Value,
            UpdateBody(edited.Version, validFrom: "2026-08-01", validTo: "2026-08-31"));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.Overlap, Ct);
        var storedOpen = await StoredOneAsync(open.Id);
        storedOpen.Validity.IsOpen.ShouldBeTrue();
        storedOpen.Version.ShouldBe(open.Version);

        // Act: a legitimate edit leaves the open contract untouched too.
        var ok = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, edited.Id.Value,
            UpdateBody(edited.Version, validFrom: "2026-02-01", validTo: "2026-04-30"));

        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterOk = await StoredOneAsync(open.Id);
        afterOk.Validity.IsOpen.ShouldBeTrue();
        afterOk.Version.ShouldBe(open.Version);
    }

    [Fact]
    public async Task StaleVersion_Returns409ConcurrentUpdate_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, UpdateBody(seeded.Version + 5));

        // Assert
        (await DetailAsync(response)).ShouldBe(
            "Il contratto è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.ConcurrentUpdate, Ct);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task StaleVersionWithAnOverlappingValidity_StillReturnsConcurrentUpdate()
    {
        var customer = await SeedCustomerAsync();
        var edited = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await SeedAsync(customer.Id, "2026-07-01", "2026-09-30");
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, edited.Id.Value,
            UpdateBody(edited.Version + 1, validFrom: "2026-08-01", validTo: "2026-08-31"));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.ConcurrentUpdate, Ct);
    }

    [Fact]
    public async Task ContractChangedBetweenTheLoadAndTheSave_Returns409ConcurrentUpdate()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new BumpVersionInterceptor(ConnectionString, seeded.Id.Value))));

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.ConcurrentUpdate, Ct);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Version.ShouldBe(seeded.Version + 1);
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public async Task OverlapTakenBetweenTheCheckAndTheSave_Returns409OverlapWithAnother_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingContractInterceptor(ConnectionString, customer.Id.Value))));

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value,
            UpdateBody(seeded.Version, validFrom: "2026-04-01", validTo: "2026-12-31"));

        // Assert
        (await DetailAsync(response)).ShouldBe("La validità si sovrappone a un altro contratto del cliente.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.OverlapWithAnother, Ct);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 3, 31));
        stored.Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task ConcurrentEditsOnOneCustomer_LeaveNoOverlap()
    {
        // Arrange: each edit is clear of the other contract as stored, but the two new validities share May.
        var customer = await SeedCustomerAsync();
        var first = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        var second = await SeedAsync(customer.Id, "2026-07-01", "2026-09-30");
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(
            PutAsync(client, customer.Id.Value, first.Id.Value,
                UpdateBody(first.Version, validFrom: "2026-04-01", validTo: "2026-05-31")),
            PutAsync(client, customer.Id.Value, second.Id.Value,
                UpdateBody(second.Version, validFrom: "2026-05-01", validTo: "2026-06-30")));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.NoContent))
        {
            loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var code = (await loser.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
            code.ShouldBeOneOf(
                ContractErrors.Overlap.Code, ContractErrors.OverlapWithAnother.Code, ContractErrors.ConcurrentUpdate.Code);
        }

        var stored = await StoredAsync(customer.Id);
        for (var i = 0; i < stored.Count; i++)
        {
            for (var j = i + 1; j < stored.Count; j++)
            {
                stored[i].Validity.Overlaps(stored[j].Validity).ShouldBeFalse();
            }
        }

        await using var db = CreateDbContext();
        (await db.AuditLog.CountAsync(a => a.EventType == AuditEventTypes.ContractUpdated, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task SuccessfulEdit_WritesOneUpdatedRowWithTheFullStateAndTheActor()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value,
            UpdateBody(seeded.Version, validFrom: "2026-04-01", validTo: "2026-12-31", requiredDocuments: ["DailySummary", "WasteForm"]));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.EventType == AuditEventTypes.ContractUpdated, Ct);
        row.SubjectId.ShouldBe(seeded.Id.Value);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("contractId").GetGuid().ShouldBe(seeded.Id.Value);
        payload.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        payload.RootElement.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        payload.RootElement.GetProperty("validTo").GetString().ShouldBe("2026-12-31");
        payload.RootElement.GetProperty("billingLevel").GetString().ShouldBe("FlatRate");
        payload.RootElement.GetProperty("pricingMode").GetString().ShouldBe("ByHour");
        payload.RootElement.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString())
            .ShouldBe(["WasteForm", "DailySummary"]);
        payload.RootElement.GetProperty("nightSurcharge").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task RefusedEdits_WriteNoAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var edited = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await SeedAsync(customer.Id, "2026-07-01", "2026-09-30");
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var overlap = await PutAsync(client, customer.Id.Value, edited.Id.Value,
            UpdateBody(edited.Version, validFrom: "2026-08-01", validTo: "2026-08-31"));
        var invalid = await PutAsync(client, customer.Id.Value, edited.Id.Value,
            UpdateBody(edited.Version, validFrom: "2026-05-01", validTo: "2026-04-01"));
        var stale = await PutAsync(client, customer.Id.Value, edited.Id.Value, UpdateBody(edited.Version + 1));

        // Assert
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task ContractOfADeactivatedCustomer_Returns409CustomerDeactivatedReadOnly_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync(deactivated: true);
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        (await DetailAsync(response)).ShouldBe(
            "Il cliente è disattivato: i suoi contratti non si possono modificare. Riattivalo per modificarli.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Version.ShouldBe(seeded.Version);
        stored.BillingLevel.ShouldBe(BillingLevel.PerLocationPair);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task ContractOfADeactivatedCustomer_IsRefusedBeforeItIsLoaded()
    {
        var customer = await SeedCustomerAsync(deactivated: true);
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, Guid.NewGuid(), UpdateBody(0));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
    }

    [Theory]
    [MemberData(nameof(ValidRates), MemberType = typeof(ContractTestSupport))]
    public async Task ValidRate_Returns204AndStoresTheRateWithTheContract(
        string billingLevel, string pricingMode, bool nightSurcharge, string rate)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body.Remove("dayPrice");
        body.Remove("minimumRule");
        SetRate(body, billingLevel, pricingMode, nightSurcharge, rate);
        var location = await SeedLocationRowAsync("Magazzino centrale");
        WithLocationRateForTheLevel(body, billingLevel, nightSurcharge, location.Id.Value, location.Id.Value);

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await StoredOneAsync(seeded.Id);
        using var members = JsonDocument.Parse(rate);
        decimal? Member(string name) =>
            members.RootElement.TryGetProperty(name, out var value) ? value.GetDecimal() : null;
        (stored.Rate is null).ShouldBe(billingLevel != "FlatRate");
        stored.Rate?.DayPrice.ShouldBe(Member("dayPrice")!.Value);
        stored.Rate?.NightPrice.ShouldBe(Member("nightPrice"));
        stored.Rate?.NightCallFee.ShouldBe(Member("nightCallFee"));
        stored.Rate?.Minimum.Amount.ShouldBe(Member("minimumAmount"));
        stored.Rate?.Minimum.Threshold.ShouldBe(Member("minimumThreshold"));
        stored.Rate?.Minimum.OveragePrice.ShouldBe(Member("minimumOveragePrice"));
    }

    [Theory]
    [MemberData(nameof(InvalidRates), MemberType = typeof(ContractTestSupport))]
    public async Task InvalidRate_Returns400UnderTheMembersKeys_AndChangesNothing(
        string billingLevel, string pricingMode, bool nightSurcharge, string rate, string keys)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body.Remove("dayPrice");
        body.Remove("minimumRule");
        SetRate(body, billingLevel, pricingMode, nightSurcharge, rate);
        WithLocationRateForTheLevel(body, billingLevel, nightSurcharge);

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(keys.Split(','));
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task RateErrors_AreInItalian()
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetRate(body, "FlatRate", "ByHour", false,
            """{"dayPrice":0,"nightPrice":55,"minimumRule":"FixedMinimum","minimumAmount":10.123}""");

        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty("DayPrice")[0].GetString().ShouldBe("Inserisci un importo maggiore di zero.");
        errors.GetProperty("NightPrice")[0].GetString().ShouldBe("Questo valore non si applica al contratto.");
        errors.GetProperty("MinimumAmount")[0].GetString().ShouldBe("Usa al massimo due decimali.");
    }

    [Fact]
    public async Task MissingPricingModeWithAFixedMinimum_ReportsOnlyThePricingMode()
    {
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body.Remove("pricingMode");
        body["minimumRule"] = "FixedMinimum";
        body["minimumAmount"] = 100m;

        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["PricingMode"]);
    }

    [Fact]
    public async Task FlatRateContractStoredWithoutARate_IsRefusedWithoutOne_AndSavedWithOne()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        Contract legacy;
        await using (var db = CreateDbContext())
        {
            legacy = await SeedFlatRateWithoutRateAsync(db, customer.Id, new DateOnly(2026, 1, 1), null);
        }

        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var withoutRate = UpdateBody(legacy.Version, validTo: null);
        withoutRate.Remove("dayPrice");
        withoutRate.Remove("minimumRule");

        // Act
        var refused = await PutAsync(client, customer.Id.Value, legacy.Id.Value, withoutRate);
        var saved = await PutAsync(client, customer.Id.Value, legacy.Id.Value, UpdateBody(legacy.Version, validTo: null));

        // Assert
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(refused)).ShouldBe(["DayPrice", "MinimumRule"]);
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(legacy.Id)).Rate.ShouldNotBeNull().DayPrice.ShouldBe(45.5m);
    }

    [Fact]
    public async Task SuccessfulEditWithARate_WritesTheRateInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetRate(body, "FlatRate", "ByHour", true,
            """{"dayPrice":45,"nightPrice":55.5,"nightCallFee":0,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":4,"minimumOveragePrice":60.25}""");

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.EventType == AuditEventTypes.ContractUpdated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var root = payload.RootElement;
        root.GetProperty("dayPrice").GetDecimal().ShouldBe(45m);
        root.GetProperty("nightPrice").GetDecimal().ShouldBe(55.5m);
        root.GetProperty("nightCallFee").GetDecimal().ShouldBe(0m);
        root.GetProperty("minimumRule").GetString().ShouldBe("TieredOverage");
        root.GetProperty("minimumAmount").GetDecimal().ShouldBe(300m);
        root.GetProperty("minimumThreshold").GetDecimal().ShouldBe(4m);
        root.GetProperty("minimumOveragePrice").GetDecimal().ShouldBe(60.25m);
    }

    [Fact]
    public async Task UnknownCustomer_Returns404CustomerNotFound()
    {
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), Guid.NewGuid(), Guid.NewGuid(), UpdateBody(0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Fact]
    public async Task UnknownContract_Returns404ContractNotFound()
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, Guid.NewGuid(), UpdateBody(0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
    }

    [Fact]
    public async Task ContractOfAnotherCustomer_Returns404ContractNotFound_AndChangesNothing()
    {
        // Arrange
        var owner = await SeedCustomerAsync();
        var other = await SeedCustomerAsync(VatNumberB);
        var seeded = await SeedAsync(owner.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), other.Id.Value, seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.NoContent)]
    [InlineData("Admin", HttpStatusCode.NoContent)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoEditsAContract(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await PutAsync(client, customer.Id.Value, seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        response.StatusCode.ShouldBe(expected);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(
            expected == HttpStatusCode.NoContent ? seeded.Version + 1 : seeded.Version);
    }

    private async Task<Vehicle> SeedVehicleRowAsync(
        string plate, VehicleType type = VehicleType.Tractor, bool decommissioned = false)
    {
        await using var db = CreateDbContext();
        return await SeedVehicleAsync(db, plate, type, decommissioned: decommissioned);
    }

    private static VehicleRate DayRate(VehicleId vehicleId, decimal dayPrice = 70m) =>
        new(vehicleId, null, null, Rate.Create(dayPrice, null, null, new MinimumRule.None()).Value);

    private async Task<Contract> SeedWithVehicleRatesAsync(CustomerId customerId, params VehicleRate[] vehicleRates)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), BillingLevel.FlatRate, PricingMode.ByHour,
            nightSurcharge: false, rate: Rate.Create(45.5m, null, null, new MinimumRule.None()).Value,
            vehicleRates: vehicleRates);
    }

    [Fact]
    public async Task UnchangedTermsWithTheVehicleRatesInAnotherOrder_Return204WithoutAuditOrVersionBump()
    {
        // Arrange: a first save changes the terms, so the second one sends what is stored, rates reversed.
        var customer = await SeedCustomerAsync();
        var first = await SeedVehicleRowAsync("AB456CD");
        var second = await SeedVehicleRowAsync("CD123EF", VehicleType.StandaloneVehicle);
        var seeded = await SeedWithVehicleRatesAsync(customer.Id, DayRate(first.Id));
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var firstItem = VehicleRateItem(first.Id.Value, """{"dayPrice":70,"minimumRule":"None"}""");
        var secondItem = VehicleRateItem(second.Id.Value, """{"dayPrice":82.5,"minimumRule":"FixedMinimum","minimumAmount":200}""");
        var changing = UpdateBody(seeded.Version);
        changing["vehicleRates"] = new[] { firstItem, secondItem };
        (await PutAsync(client, customer.Id.Value, seeded.Id.Value, changing)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var auditBefore = await AuditCountAsync();
        var unchanged = UpdateBody(seeded.Version + 1);
        unchanged["vehicleRates"] = new[] { secondItem, firstItem };

        // Act
        var response = await PutAsync(client, customer.Id.Value, seeded.Id.Value, unchanged);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Version.ShouldBe(seeded.Version + 1);
        stored.VehicleRates.Count.ShouldBe(2);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task Correction_ReplacesTheVehicleRates_AndLeavingThemOutRemovesThemAll()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedVehicleRowAsync("AB456CD");
        var second = await SeedVehicleRowAsync("CD123EF", VehicleType.StandaloneVehicle);
        var seeded = await SeedWithVehicleRatesAsync(customer.Id, DayRate(first.Id));
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var replacing = UpdateBody(seeded.Version);
        replacing["vehicleRates"] = new[] { VehicleRateItem(second.Id.Value, """{"dayPrice":82.5,"minimumRule":"FixedMinimum","minimumAmount":200}""") };

        // Act
        var replaced = await PutAsync(client, customer.Id.Value, seeded.Id.Value, replacing);
        var afterReplace = await StoredOneAsync(seeded.Id);
        var removed = await PutAsync(client, customer.Id.Value, seeded.Id.Value, UpdateBody(afterReplace.Version));

        // Assert
        replaced.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var rate = afterReplace.VehicleRates.ShouldHaveSingleItem();
        rate.VehicleId.ShouldBe(second.Id);
        rate.Rate.DayPrice.ShouldBe(82.5m);
        rate.Rate.Minimum.ShouldBeOfType<MinimumRule.FixedMinimum>().Amount.ShouldBe(200m);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBeEmpty();
    }

    [Fact]
    public async Task DecommissionedVehicleAlreadyInTheContract_IsKeptAndMayBeRepriced()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleRowAsync("AB456CD", decommissioned: true);
        var seeded = await SeedWithVehicleRatesAsync(customer.Id, DayRate(vehicle.Id, 60m));
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(vehicle.Id.Value) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBe([DayRate(vehicle.Id, 70m)]);
    }

    [Fact]
    public async Task SuccessfulEditWithVehicleRates_WritesThemInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var seeded = await SeedWithVehicleRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body["vehicleRates"] = new[]
        {
            VehicleRateItem(vehicle.Id.Value, """{"dayPrice":80.5,"minimumRule":"FixedMinimum","minimumAmount":200}"""),
        };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.EventType == AuditEventTypes.ContractUpdated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var rate = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().ShouldHaveSingleItem();
        rate.GetProperty("vehicleId").GetGuid().ShouldBe(vehicle.Id.Value);
        rate.GetProperty("dayPrice").GetDecimal().ShouldBe(80.5m);
        rate.GetProperty("minimumRule").GetString().ShouldBe("FixedMinimum");
        rate.GetProperty("minimumAmount").GetDecimal().ShouldBe(200m);
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
        var body = UpdateBody(seeded.Version);
        if (level != "FlatRate")
        {
            SetLocationRates(body, level, "ByHour", false, LocationRateFor(level, a.Id.Value, b.Id.Value));
        }

        body["vehicleRates"] = new[] { VehicleRateFor(level, vehicleId, a.Id.Value, b.Id.Value) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, error, Ct);
        (await DetailAsync(response)).ShouldBe(text);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBeEmpty();
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
        var body = UpdateBody(seeded.Version);
        SetRateWithVehicleRate(body, billingLevel, pricingMode, nightSurcharge, rate);

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

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
        var body = UpdateBody(seeded.Version);
        body["vehicleRates"] = new[]
        {
            VehicleRateItem(vehicle.Id.Value), VehicleRateItem(vehicle.Id.Value), VehicleRateItem(null),
        };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

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
        var body = UpdateBody(seeded.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(vehicle.Id.Value) };

        // Act
        var response = await PutAsync(client, customer.Id.Value, seeded.Id.Value, body);

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
        var body = UpdateBody(seeded.Version);
        body["vehicleRates"] = new[] { VehicleRateItem(other.Id.Value) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

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

    // A location-pair contract priced by the hour with no night surcharge, holding the given location rates.
    private async Task<Contract> SeedWithLocationRatesAsync(CustomerId customerId, params LocationRate[] locationRates)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), BillingLevel.PerLocationPair,
            PricingMode.ByHour, [RequiredDocument.WasteForm], nightSurcharge: false,
            locationRates: locationRates.Length == 0 ? null : locationRates);
    }

    private static Dictionary<string, object?> PairBody(int? version, params Dictionary<string, object?>[] locationRates)
    {
        var body = UpdateBody(version);
        SetLocationRates(body, "PerLocationPair", "ByHour", false, locationRates);
        return body;
    }

    [Fact]
    public async Task Correction_ReplacesTheLocationRates()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var seeded = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, b.Id));
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var replacing = PairBody(seeded.Version,
            LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value,
                """{"dayPrice":82.5,"minimumRule":"FixedMinimum","minimumAmount":200}"""));

        // Act
        var replaced = await PutAsync(client, customer.Id.Value, seeded.Id.Value, replacing);
        var afterReplace = await StoredOneAsync(seeded.Id);

        // Assert
        replaced.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var rate = afterReplace.LocationRates.ShouldHaveSingleItem();
        rate.PickupLocationId.ShouldBe(b.Id);
        rate.DeliveryLocationId.ShouldBe(a.Id);
        rate.Rate.DayPrice.ShouldBe(82.5m);
        rate.Rate.Minimum.ShouldBeOfType<MinimumRule.FixedMinimum>().Amount.ShouldBe(200m);
    }

    [Theory]
    [InlineData("PerLocationPair", "absent")]
    [InlineData("PerLocationPair", "null")]
    [InlineData("PerPickupLocation", "empty")]
    [InlineData("PerDeliveryLocation", "empty")]
    public async Task CorrectionOfALocationContractWithoutLocationRates_Returns400OnLocationRates_AndChangesNothing(
        string level, string form)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedWithLocationRatesAsync(customer.Id);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetLocationRates(body, level, "ByHour", false);
        if (form == "absent")
        {
            body.Remove("locationRates");
        }
        else if (form == "null")
        {
            body["locationRates"] = null;
        }

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["LocationRates"]);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").GetProperty("LocationRates")[0]
            .GetString().ShouldBe("Aggiungi almeno una tariffa.");
        var stored = await StoredOneAsync(seeded.Id);
        stored.Version.ShouldBe(seeded.Version);
        stored.LocationRates.ShouldBe(seeded.LocationRates);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task LegacyLocationContractWithoutRates_IsRefusedWithoutRates_AndCorrectedWithThem()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        Contract legacy;
        await using (var db = CreateDbContext())
        {
            legacy = await SeedLocationContractWithoutRatesAsync(
                db, customer.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        }

        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var emptyBefore = (await StoredOneAsync(legacy.Id)).LocationRates;
        var refused = await PutAsync(client, customer.Id.Value, legacy.Id.Value, PairBody(legacy.Version));
        var corrected = await PutAsync(
            client, customer.Id.Value, legacy.Id.Value,
            PairBody(legacy.Version, LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value)));

        // Assert
        emptyBefore.ShouldBeEmpty();
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(refused)).ShouldBe(["LocationRates"]);
        corrected.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var rate = (await StoredOneAsync(legacy.Id)).LocationRates.ShouldHaveSingleItem();
        rate.PickupLocationId.ShouldBe(a.Id);
        rate.DeliveryLocationId.ShouldBe(b.Id);
    }

    [Fact]
    public async Task UnchangedTermsWithTheLocationRatesInAnotherOrder_Return204WithoutAuditOrVersionBump()
    {
        // Arrange: a first save changes the terms, so the second one sends what is stored, rates reversed.
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var seeded = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, b.Id));
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var abItem = LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value);
        var baItem = LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value,
            """{"dayPrice":82.5,"minimumRule":"FixedMinimum","minimumAmount":200}""");
        (await PutAsync(client, customer.Id.Value, seeded.Id.Value, PairBody(seeded.Version, abItem, baItem)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var auditBefore = await AuditCountAsync();

        // Act
        var response = await PutAsync(client, customer.Id.Value, seeded.Id.Value, PairBody(seeded.Version + 1, baItem, abItem));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Version.ShouldBe(seeded.Version + 1);
        stored.LocationRates.Count.ShouldBe(2);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [MemberData(nameof(InvalidLocationRates), MemberType = typeof(ContractTestSupport))]
    public async Task InvalidLocationRate_Returns400UnderTheItemKeys_AndChangesNothing(
        string pricingMode, bool nightSurcharge, string rate, string keys)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var seeded = await SeedWithLocationRatesAsync(customer.Id);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetLocationRates(body, "PerLocationPair", pricingMode, nightSurcharge,
            LocationRateFor("PerLocationPair", Guid.NewGuid(), Guid.NewGuid(), rate));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(LocationRateKeys(keys));
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
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
        var seeded = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetLocationRates(body, level, "ByHour", false,
            LocationRateFor(level, a.Id.Value, b.Id.Value), LocationRateFor(level, a.Id.Value, b.Id.Value));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe(seeded.LocationRates);
    }

    [Fact]
    public async Task OppositePairsAndAPairOfTheSameLocation_AreDifferentPairsAndAreSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var seeded = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = PairBody(seeded.Version,
            LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value),
            LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value),
            LocationRateFor("PerLocationPair", a.Id.Value, a.Id.Value));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(seeded.Id)).LocationRates
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
        var seeded = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetLocationRates(body, level, "ByHour", false,
            LocationRateFor("PerLocationPair", pickupSent ? Guid.NewGuid() : null, deliverySent ? Guid.NewGuid() : null));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe(seeded.LocationRates);
    }

    [Fact]
    public async Task LocationRatesOnAFlatRateContract_Return400UnderLocationRates_AndChangeNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationRowAsync("Magazzino centrale");
        var seeded = await SeedAsync(customer.Id, "2026-01-01", "2026-03-31");
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        body["locationRates"] = new[] { LocationRateFor("PerPickupLocation", location.Id.Value, null) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["LocationRates"]);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe(seeded.LocationRates);
    }

    [Fact]
    public async Task LocationContractWithARateOfItsOwn_Returns400OnThoseMembers_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var seeded = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, b.Id));
        await using var factory = CreateFactory();
        var body = PairBody(seeded.Version, LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value));
        body["dayPrice"] = 45m;
        body["minimumRule"] = "None";

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["DayPrice", "MinimumRule"]);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe([PairRate(a.Id, b.Id)]);
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
        var seeded = await SeedWithLocationRatesAsync(customer.Id, PairRate(active.Id, active.Id));
        var before = await AuditCountAsync();
        var (error, text, delivery) = kind == "unknown"
            ? (ContractErrors.LocationRateUnknownLocation, $"Il luogo {unknown} non esiste.", unknown)
            : (ContractErrors.LocationRateDeactivatedLocation,
                "Il luogo Cantiere nord, Chieti è disattivato: non si può aggiungere una sua tariffa.", deactivated.Id.Value);
        await using var factory = CreateFactory();
        var body = PairBody(seeded.Version, LocationRateFor("PerLocationPair", active.Id.Value, delivery));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, error, Ct);
        (await DetailAsync(response)).ShouldBe(text);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe([PairRate(active.Id, active.Id)]);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task DeactivatedLocationAlreadyInTheContract_IsKeptAndMayBeRepriced_AlsoWhenTheOtherSideChanges()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var kept = await SeedLocationRowAsync("Cantiere nord", "Chieti", deactivated: true);
        var other = await SeedLocationRowAsync("Magazzino centrale");
        var seeded = await SeedWithLocationRatesAsync(customer.Id, PairRate(kept.Id, kept.Id, 60m));
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var repriced = PairBody(seeded.Version, LocationRateFor("PerLocationPair", kept.Id.Value, kept.Id.Value));

        // Act
        var first = await PutAsync(client, customer.Id.Value, seeded.Id.Value, repriced);
        var afterFirst = await StoredOneAsync(seeded.Id);
        var otherSide = PairBody(afterFirst.Version, LocationRateFor("PerLocationPair", kept.Id.Value, other.Id.Value));
        var second = await PutAsync(client, customer.Id.Value, seeded.Id.Value, otherSide);

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        afterFirst.LocationRates.ShouldBe([PairRate(kept.Id, kept.Id, 70m)]);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe([PairRate(kept.Id, other.Id, 70m)]);
    }

    [Fact]
    public async Task SuccessfulEditWithLocationRates_WritesThemInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var seeded = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = PairBody(seeded.Version,
            LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value,
                """{"dayPrice":80.5,"minimumRule":"FixedMinimum","minimumAmount":200}"""));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(
            entry => entry.EventType == AuditEventTypes.ContractUpdated && entry.SubjectId == seeded.Id.Value, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var rate = payload.RootElement.GetProperty("locationRates").EnumerateArray().ShouldHaveSingleItem();
        rate.GetProperty("pickupLocationId").GetGuid().ShouldBe(a.Id.Value);
        rate.GetProperty("deliveryLocationId").GetGuid().ShouldBe(b.Id.Value);
        rate.GetProperty("dayPrice").GetDecimal().ShouldBe(80.5m);
        rate.GetProperty("minimumRule").GetString().ShouldBe("FixedMinimum");
        rate.GetProperty("minimumAmount").GetDecimal().ShouldBe(200m);
    }

    [Fact]
    public async Task Driver_Gets403WhenWritingLocationRates_AndNothingIsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var location = await SeedLocationRowAsync("Magazzino centrale");
        var seeded = await SeedWithLocationRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());
        var body = PairBody(seeded.Version, LocationRateFor("PerLocationPair", location.Id.Value, location.Id.Value));

        // Act
        var response = await PutAsync(client, customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe(seeded.LocationRates);
    }

    [Fact]
    public async Task DeactivatedCustomer_RefusesLocationRateChanges_AndKeepsTheStoredOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync(deactivated: true);
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var seeded = await SeedWithLocationRatesAsync(customer.Id, PairRate(a.Id, b.Id));
        await using var factory = CreateFactory();
        var body = PairBody(seeded.Version, LocationRateFor("PerLocationPair", b.Id.Value, a.Id.Value));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
        (await StoredOneAsync(seeded.Id)).LocationRates.ShouldBe([PairRate(a.Id, b.Id)]);
    }

    private static VehicleRate PairVehicleRate(VehicleId vehicleId, LocationId pickup, LocationId delivery, decimal dayPrice = 70m) =>
        new(vehicleId, pickup, delivery, Rate.Create(dayPrice, null, null, new MinimumRule.None()).Value);

    // A location-pair contract priced by the hour with no night surcharge, holding the given location and vehicle rates.
    private async Task<Contract> SeedWithLocationAndVehicleRatesAsync(
        CustomerId customerId, LocationRate[] locationRates, params VehicleRate[] vehicleRates)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(
            db, customerId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), BillingLevel.PerLocationPair,
            PricingMode.ByHour, [RequiredDocument.WasteForm], nightSurcharge: false,
            locationRates: locationRates, vehicleRates: vehicleRates);
    }

    [Fact]
    public async Task Correction_WritesTheVehicleRatesOfALocationContractWithTheirLocations()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var c = await SeedLocationRowAsync("Deposito sud");
        var first = await SeedVehicleRowAsync("AB456CD");
        var second = await SeedVehicleRowAsync("CD123EF", VehicleType.StandaloneVehicle);
        var seeded = await SeedWithLocationAndVehicleRatesAsync(
            customer.Id, [PairRate(a.Id, b.Id), PairRate(b.Id, c.Id), PairRate(c.Id, a.Id)]);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetThreeLocationsWithVehicleRates(body, "PerLocationPair", a.Id.Value, b.Id.Value, c.Id.Value,
            VehicleRateFor("PerLocationPair", first.Id.Value, a.Id.Value, b.Id.Value),
            VehicleRateFor("PerLocationPair", first.Id.Value, b.Id.Value, c.Id.Value),
            VehicleRateFor("PerLocationPair", second.Id.Value, a.Id.Value, b.Id.Value, """{"dayPrice":82.5,"minimumRule":"None"}"""));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(seeded.Id)).VehicleRates
            .Select(r => (r.VehicleId, r.PickupLocationId, r.DeliveryLocationId, r.Rate.DayPrice))
            .ShouldBe(
            [
                (first.Id, a.Id, b.Id, 70m),
                (first.Id, b.Id, c.Id, 70m),
                (second.Id, a.Id, b.Id, 82.5m),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public async Task ChangingOnlyTheLocationsOfAVehicleRate_IsAChange_WithANewVersionAndAnAuditRow()
    {
        // Arrange: a first save settles the terms, the second moves the vehicle rate to another location.
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var c = await SeedLocationRowAsync("Deposito sud");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var seeded = await SeedWithLocationAndVehicleRatesAsync(
            customer.Id, [PairRate(a.Id, b.Id), PairRate(b.Id, c.Id), PairRate(c.Id, a.Id)]);
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var settling = UpdateBody(seeded.Version);
        SetThreeLocationsWithVehicleRates(settling, "PerLocationPair", a.Id.Value, b.Id.Value, c.Id.Value,
            VehicleRateFor("PerLocationPair", vehicle.Id.Value, a.Id.Value, b.Id.Value));
        (await PutAsync(client, customer.Id.Value, seeded.Id.Value, settling)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var settled = await StoredOneAsync(seeded.Id);
        var auditBefore = await AuditCountAsync();
        var moving = UpdateBody(settled.Version);
        SetThreeLocationsWithVehicleRates(moving, "PerLocationPair", a.Id.Value, b.Id.Value, c.Id.Value,
            VehicleRateFor("PerLocationPair", vehicle.Id.Value, b.Id.Value, c.Id.Value));

        // Act
        var response = await PutAsync(client, customer.Id.Value, seeded.Id.Value, moving);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await StoredOneAsync(seeded.Id);
        stored.Version.ShouldBe(settled.Version + 1);
        stored.VehicleRates.ShouldBe([PairVehicleRate(vehicle.Id, b.Id, c.Id)]);
        (await AuditCountAsync()).ShouldBe(auditBefore + 1);
    }

    [Fact]
    public async Task DecommissionedVehicleAlreadyInALocationContract_IsKeptAndMayBeRepriced()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var vehicle = await SeedVehicleRowAsync("AB456CD", decommissioned: true);
        var seeded = await SeedWithLocationAndVehicleRatesAsync(
            customer.Id, [PairRate(a.Id, b.Id)], PairVehicleRate(vehicle.Id, a.Id, b.Id, 60m));
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetLocationRates(body, "PerLocationPair", "ByHour", false, LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value));
        body["vehicleRates"] = new[] { VehicleRateFor("PerLocationPair", vehicle.Id.Value, a.Id.Value, b.Id.Value) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBe([PairVehicleRate(vehicle.Id, a.Id, b.Id)]);
    }

    [Fact]
    public async Task DeactivatedCustomer_RefusesVehicleRateChangesOnALocationContract_AndKeepsTheStoredOnes()
    {
        // Arrange
        var customer = await SeedCustomerAsync(deactivated: true);
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var seeded = await SeedWithLocationAndVehicleRatesAsync(
            customer.Id, [PairRate(a.Id, b.Id)], PairVehicleRate(vehicle.Id, a.Id, b.Id));
        await using var factory = CreateFactory();
        var body = PairBody(seeded.Version, LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value));
        body["vehicleRates"] = Array.Empty<Dictionary<string, object?>>();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivatedReadOnly, Ct);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBe([PairVehicleRate(vehicle.Id, a.Id, b.Id)]);
    }

    [Fact]
    public async Task Driver_Gets403WhenWritingVehicleRatesOfALocationContract_AndNothingIsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var seeded = await SeedWithLocationAndVehicleRatesAsync(customer.Id, [PairRate(a.Id, b.Id)]);
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());
        var body = PairBody(seeded.Version, LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value));
        body["vehicleRates"] = new[] { VehicleRateFor("PerLocationPair", vehicle.Id.Value, a.Id.Value, b.Id.Value) };

        // Act
        var response = await PutAsync(client, customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StoredOneAsync(seeded.Id)).VehicleRates.ShouldBeEmpty();
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
        var seeded = await SeedWithLocationAndVehicleRatesAsync(customer.Id, [PairRate(a.Id, b.Id)]);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetThreeLocationsWithVehicleRates(body, level, a.Id.Value, b.Id.Value, c.Id.Value,
            VehicleRateFor(level, vehicle.Id.Value, a.Id.Value, b.Id.Value),
            VehicleRateFor(level, vehicle.Id.Value, a.Id.Value, b.Id.Value, """{"dayPrice":82.5,"minimumRule":"None"}"""));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty(key)[0].GetString().ShouldBe("Il mezzo ha già una tariffa propria per questo luogo o questa coppia.");
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
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
        var seeded = await SeedWithLocationAndVehicleRatesAsync(customer.Id, [PairRate(a.Id, b.Id)]);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        SetLocationRates(body, level, "ByHour", false, LocationRateFor(level, a.Id.Value, b.Id.Value));
        body["vehicleRates"] = new[] { VehicleRateFor(level, vehicle.Id.Value, b.Id.Value, c.Id.Value) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty(key)[0].GetString().ShouldBe("Il contratto non ha una tariffa per questo luogo o questa coppia.");
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task VehicleRatesOfALocationContractWhoseLocationRateLacksItsLocation_ReportOnlyTheLocationRate()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var seeded = await SeedWithLocationAndVehicleRatesAsync(customer.Id, [PairRate(a.Id, b.Id)]);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        var locationRate = LocationRateFor("PerPickupLocation", a.Id.Value, null);
        locationRate["pickupLocationId"] = null;
        SetLocationRates(body, "PerPickupLocation", "ByHour", false, locationRate);
        body["vehicleRates"] = new[] { VehicleRateFor("PerPickupLocation", vehicle.Id.Value, a.Id.Value, null) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["LocationRates[0].PickupLocationId"]);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task VehicleRateOfALocationContractWithoutLocations_Returns400OnBothMembers_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var b = await SeedLocationRowAsync("Cantiere nord");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var seeded = await SeedWithLocationAndVehicleRatesAsync(customer.Id, [PairRate(a.Id, b.Id)]);
        await using var factory = CreateFactory();
        var body = PairBody(seeded.Version, LocationRateFor("PerLocationPair", a.Id.Value, b.Id.Value));
        body["vehicleRates"] = new[] { VehicleRateItem(vehicle.Id.Value) };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VehicleRates[0].DeliveryLocationId", "VehicleRates[0].PickupLocationId"]);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task VehicleRateOfAFlatRateContractWithALocation_Returns400OnTheMember_AndChangesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var a = await SeedLocationRowAsync("Magazzino centrale");
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        var seeded = await SeedWithVehicleRatesAsync(customer.Id);
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
        var item = VehicleRateItem(vehicle.Id.Value);
        item["pickupLocationId"] = a.Id.Value;
        body["vehicleRates"] = new[] { item };

        // Act
        var response = await PutAsync(BackOfficeClient(factory), customer.Id.Value, seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VehicleRates[0].PickupLocationId"]);
        (await StoredOneAsync(seeded.Id)).Version.ShouldBe(seeded.Version);
    }

    [Fact]
    public async Task AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PutAsync(factory.CreateClient(), Guid.NewGuid(), Guid.NewGuid(), UpdateBody(0));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
