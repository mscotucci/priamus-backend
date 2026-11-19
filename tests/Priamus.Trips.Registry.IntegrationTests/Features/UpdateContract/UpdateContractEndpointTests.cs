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
using Priamus.Trips.Registry.Domain.Customers;
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

    [Fact]
    public async Task AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PutAsync(factory.CreateClient(), Guid.NewGuid(), Guid.NewGuid(), UpdateBody(0));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
