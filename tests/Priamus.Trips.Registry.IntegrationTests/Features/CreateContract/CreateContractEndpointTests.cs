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
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.CreateContract;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/customers/{customerId}/contracts</c>, against
/// the per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class CreateContractEndpointTests : RegistryIntegrationTestBase
{
    private static readonly DateTime Midday = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    public CreateContractEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(Guid customerId) => $"/api/registry/customers/{customerId}/contracts";

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid customerId, object body) =>
        client.PostAsJsonAsync(Url(customerId), body, TestJson.Options, Ct);

    private static Dictionary<string, object?> ContractBody(
        string? validFrom = "2026-04-01",
        string? validTo = "2026-12-31",
        string billingLevel = "PerLocationPair",
        string pricingMode = "ByTrip",
        string[]? requiredDocuments = null,
        bool nightSurcharge = true) =>
        new()
        {
            ["validFrom"] = validFrom,
            ["validTo"] = validTo,
            ["billingLevel"] = billingLevel,
            ["pricingMode"] = pricingMode,
            ["requiredDocuments"] = requiredDocuments ?? ["DailySummary", "DeliveryNote"],
            ["nightSurcharge"] = nightSurcharge,
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

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task ValidRequest_Returns201WithEveryTermStoredAndReturned()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value,
            ContractBody(requiredDocuments: ["DailySummary", "DeliveryNote", "DailySummary"]));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        body.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        body.GetProperty("validTo").GetString().ShouldBe("2026-12-31");
        body.GetProperty("billingLevel").GetString().ShouldBe("PerLocationPair");
        body.GetProperty("pricingMode").GetString().ShouldBe("ByTrip");
        body.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString())
            .ShouldBe(["DeliveryNote", "DailySummary"]);
        body.GetProperty("nightSurcharge").GetBoolean().ShouldBeTrue();
        body.GetProperty("version").GetInt32().ShouldBe(0);
        response.Headers.Location!.ToString().ShouldBe($"{Url(customer.Id.Value)}/{id}");

        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        stored.Id.Value.ShouldBe(id);
        stored.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 4, 1));
        stored.Validity.ValidTo.ShouldBe(new DateOnly(2026, 12, 31));
        stored.BillingLevel.ShouldBe(BillingLevel.PerLocationPair);
        stored.PricingMode.ShouldBe(PricingMode.ByTrip);
        stored.RequiredDocuments.ShouldBe([RequiredDocument.DeliveryNote, RequiredDocument.DailySummary]);
        stored.NightSurcharge.ShouldBeTrue();
    }

    [Fact]
    public async Task OpenContract_IsStoredWithoutAnEnd()
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        body.Remove("validTo");

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("validTo").ValueKind.ShouldBe(JsonValueKind.Null);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().Validity.IsOpen.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("WasteForm")]
    [InlineData("DeliveryNote,DeliverySlip,WasteForm,DailySummary")]
    public async Task AnySubsetOfDocuments_IsAccepted(string list)
    {
        var documents = list.Length == 0 ? [] : list.Split(',');
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, ContractBody(requiredDocuments: documents));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("requiredDocuments")
            .EnumerateArray().Select(d => d.GetString()).ShouldBe(documents);
    }

    [Theory]
    [InlineData("validFrom", "ValidFrom")]
    [InlineData("billingLevel", "BillingLevel")]
    [InlineData("pricingMode", "PricingMode")]
    [InlineData("requiredDocuments", "RequiredDocuments")]
    [InlineData("nightSurcharge", "NightSurcharge")]
    public async Task MissingRequiredMember_Returns400UnderItsKey_AndSavesNothing(string member, string key)
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        body.Remove(member);

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([key]);
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("billingLevel")]
    [InlineData("pricingMode")]
    public async Task UnknownEnumName_Returns400_AndSavesNothing(string member)
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        body[member] = "NoSuchValue";

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task UnknownRequiredDocumentName_Returns400_AndSavesNothing()
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(requiredDocuments: ["DeliveryNote", "NoSuchValue"]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task EndBeforeStart_Returns400UnderValidTo_AndSavesNothing()
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: "2026-04-01", validTo: "2026-03-31"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["ValidTo"]);
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task StartAndEndOnTheSameDay_Returns201()
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: "2026-04-01", validTo: "2026-04-01"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("2026-12-31", "2027-03-31", "dal 01/04/2026 al 31/12/2026")]
    [InlineData("2025-12-01", "2026-04-01", "dal 01/04/2026 al 31/12/2026")]
    [InlineData("2026-05-01", "2026-05-31", "dal 01/04/2026 al 31/12/2026")]
    public async Task OverlapWithAClosedContract_Returns409NamingItsValidity_AndSavesNothing(
        string validFrom, string validTo, string expectedValidity)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await SeedAsync(customer.Id, "2026-04-01", "2026-12-31");
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: validFrom, validTo: validTo));

        // Assert
        (await DetailAsync(response)).ShouldBe($"La validità si sovrappone al contratto {expectedValidity}.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.Overlap, Ct);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task OverlapWithAnOpenContractStartingLater_Returns409NamingItOpen_AndSavesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var open = await SeedAsync(customer.Id, "2027-01-01", null);
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: "2026-06-01", validTo: "2027-01-01"));

        // Assert
        (await DetailAsync(response)).ShouldBe("La validità si sovrappone al contratto dal 01/01/2027, senza scadenza.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.Overlap, Ct);
        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        stored.Id.ShouldBe(open.Id);
        stored.Version.ShouldBe(0);
    }

    [Fact]
    public async Task AdjacentValidities_AreAccepted()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await SeedAsync(customer.Id, "2026-04-01", "2026-12-31");
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var after = await PostAsync(client, customer.Id.Value, ContractBody(validFrom: "2027-01-01", validTo: "2027-06-30"));
        var before = await PostAsync(client, customer.Id.Value, ContractBody(validFrom: "2026-01-01", validTo: "2026-03-31"));

        // Assert
        after.StatusCode.ShouldBe(HttpStatusCode.Created);
        before.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await StoredAsync(customer.Id)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task NewContractStartingAfterTheOpenOne_ClosesItTheDayBefore_AndAuditsBoth()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var open = await SeedAsync(customer.Id, "2026-01-01", null);
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: "2026-07-01", validTo: null));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var newId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var stored = await StoredAsync(customer.Id);
        stored.Count.ShouldBe(2);
        var closed = stored.Single(c => c.Id == open.Id);
        closed.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 1, 1));
        closed.Validity.ValidTo.ShouldBe(new DateOnly(2026, 6, 30));
        closed.Version.ShouldBe(1);
        stored.Single(c => c.Id.Value == newId).Validity.IsOpen.ShouldBeTrue();

        await using var db = CreateDbContext();
        var rows = await db.AuditLog.OrderBy(a => a.OccurredAt).ToListAsync(Ct);
        rows.Count.ShouldBe(auditBefore + 2);
        var created = rows.Single(r => r.EventType == AuditEventTypes.ContractCreated && r.SubjectId == newId);
        var updated = rows.Single(r => r.EventType == AuditEventTypes.ContractUpdated && r.SubjectId == open.Id.Value);
        created.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(updated.Payload);
        payload.RootElement.GetProperty("validTo").GetString().ShouldBe("2026-06-30");
    }

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2025-09-01")]
    public async Task NewContractStartingOnOrBeforeTheOpenOne_Overlaps_AndNothingIsSaved(string validFrom)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var open = await SeedAsync(customer.Id, "2026-01-01", null);
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: validFrom, validTo: null));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.Overlap, Ct);
        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        stored.Id.ShouldBe(open.Id);
        stored.Validity.IsOpen.ShouldBeTrue();
        stored.Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task OpenContractChangedBetweenTheLoadAndTheSave_Returns409ConcurrentUpdate_AndNothingIsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var open = await SeedAsync(customer.Id, "2026-01-01", null);
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new BumpVersionInterceptor(ConnectionString, open.Id.Value))));

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: "2026-07-01", validTo: null));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.ConcurrentUpdate, Ct);
        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        stored.Validity.IsOpen.ShouldBeTrue();
        stored.Version.ShouldBe(1);
    }

    [Fact]
    public async Task OverlapTakenBetweenTheCheckAndTheSave_Returns409OverlapWithAnother_AndSavesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingContractInterceptor(ConnectionString, customer.Id.Value))));

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, ContractBody(validFrom: "2026-04-01", validTo: "2026-12-31"));

        // Assert
        (await DetailAsync(response)).ShouldBe("La validità si sovrappone a un altro contratto del cliente.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.OverlapWithAnother, Ct);
        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        stored.Validity.ValidFrom.ShouldBe(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public async Task ConcurrentCreatesOnOneCustomer_LeaveNoOverlap()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            PostAsync(client, customer.Id.Value, ContractBody(validFrom: "2026-04-01", validTo: null))));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            loser.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var code = (await loser.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();
            code.ShouldBeOneOf(
                ContractErrors.Overlap.Code, ContractErrors.OverlapWithAnother.Code, ContractErrors.ConcurrentUpdate.Code);
        }

        var stored = await StoredAsync(customer.Id);
        stored.Count.ShouldBe(1);
        await using var db = CreateDbContext();
        (await db.AuditLog.CountAsync(a => a.EventType.StartsWith("registry.contract"), Ct)).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(ValidRates), MemberType = typeof(ContractTestSupport))]
    public async Task ValidRate_Returns201AndStoresAndReturnsTheRateWithTheContract(
        string billingLevel, string pricingMode, bool nightSurcharge, string rate)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        SetRate(body, billingLevel, pricingMode, nightSurcharge, rate);

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var returned = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem();
        using var members = JsonDocument.Parse(rate);
        decimal? Sent(string name) =>
            members.RootElement.TryGetProperty(name, out var value) ? value.GetDecimal() : null;
        decimal? Returned(string name) =>
            returned.GetProperty(name) is { ValueKind: JsonValueKind.Number } value ? value.GetDecimal() : null;

        foreach (var member in new[] { "dayPrice", "nightPrice", "nightCallFee", "minimumAmount", "minimumThreshold", "minimumOveragePrice" })
        {
            Returned(member).ShouldBe(Sent(member), member);
        }

        (stored.Rate is null).ShouldBe(billingLevel != "FlatRate");
        stored.Rate?.DayPrice.ShouldBe(Sent("dayPrice")!.Value);
        stored.Rate?.NightPrice.ShouldBe(Sent("nightPrice"));
        stored.Rate?.NightCallFee.ShouldBe(Sent("nightCallFee"));
        stored.Rate?.Minimum.Amount.ShouldBe(Sent("minimumAmount"));
        stored.Rate?.Minimum.Threshold.ShouldBe(Sent("minimumThreshold"));
        stored.Rate?.Minimum.OveragePrice.ShouldBe(Sent("minimumOveragePrice"));
        var expectedRule = billingLevel != "FlatRate" ? null
            : members.RootElement.TryGetProperty("minimumRule", out var rule) ? rule.GetString() : "None";
        returned.GetProperty("minimumRule").GetString().ShouldBe(expectedRule);
    }

    [Theory]
    [MemberData(nameof(InvalidRates), MemberType = typeof(ContractTestSupport))]
    public async Task InvalidRate_Returns400UnderTheMembersKeys_AndSavesNothing(
        string billingLevel, string pricingMode, bool nightSurcharge, string rate, string keys)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        SetRate(body, billingLevel, pricingMode, nightSurcharge, rate);

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(keys.Split(','));
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task RateErrors_AreInItalian()
    {
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        SetRate(body, "FlatRate", "ByHour", false,
            """{"dayPrice":0,"nightPrice":55,"minimumRule":"FixedMinimum","minimumAmount":10.123}""");

        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty("DayPrice")[0].GetString().ShouldBe("Inserisci un importo maggiore di zero.");
        errors.GetProperty("NightPrice")[0].GetString().ShouldBe("Questo valore non si applica al contratto.");
        errors.GetProperty("MinimumAmount")[0].GetString().ShouldBe("Usa al massimo due decimali.");
    }

    [Fact]
    public async Task SuccessfulCreationWithARate_WritesTheRateInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        SetRate(body, "FlatRate", "ByHour", true,
            """{"dayPrice":45,"nightPrice":55.5,"nightCallFee":0,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":4,"minimumOveragePrice":60.25}""");

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
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
    public async Task SuccessfulCreation_WritesOneAuditRowWithTheFullStateAndTheActor()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, ContractBody());

        // Assert
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        row.EventType.ShouldBe(AuditEventTypes.ContractCreated);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("contractId").GetGuid().ShouldBe(id);
        payload.RootElement.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        payload.RootElement.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        payload.RootElement.GetProperty("validTo").GetString().ShouldBe("2026-12-31");
        payload.RootElement.GetProperty("billingLevel").GetString().ShouldBe("PerLocationPair");
        payload.RootElement.GetProperty("pricingMode").GetString().ShouldBe("ByTrip");
        payload.RootElement.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString())
            .ShouldBe(["DeliveryNote", "DailySummary"]);
        payload.RootElement.GetProperty("nightSurcharge").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task RefusedCreations_WriteNoAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await SeedAsync(customer.Id, "2026-04-01", "2026-12-31");
        var deactivated = await SeedCustomerAsync(VatNumberB, deactivated: true);
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var overlap = await PostAsync(client, customer.Id.Value, ContractBody(validFrom: "2026-05-01"));
        var invalid = await PostAsync(client, customer.Id.Value, ContractBody(validFrom: "2027-02-01", validTo: "2027-01-01"));
        var refused = await PostAsync(client, deactivated.Id.Value, ContractBody());

        // Assert
        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task DeactivatedCustomer_Returns409CustomerDeactivated_AndSavesNothing()
    {
        // Arrange
        var customer = await SeedCustomerAsync(deactivated: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, ContractBody());

        // Assert
        (await DetailAsync(response)).ShouldBe(
            "Il cliente è disattivato: non si possono aggiungere contratti. Riattivalo per aggiungerne uno.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, ContractErrors.CustomerDeactivated, Ct);
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task UnknownCustomer_Returns404CustomerNotFound()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Guid.NewGuid(), ContractBody());

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoCreatesAContract(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await PostAsync(client, customer.Id.Value, ContractBody());

        // Assert
        response.StatusCode.ShouldBe(expected);
        (await StoredAsync(customer.Id)).Count.ShouldBe(expected == HttpStatusCode.Created ? 1 : 0);
    }

    private async Task<Vehicle> SeedVehicleRowAsync(
        string plate, VehicleType type = VehicleType.Tractor, bool decommissioned = false)
    {
        await using var db = CreateDbContext();
        return await SeedVehicleAsync(db, plate, type, decommissioned: decommissioned);
    }

    private static Dictionary<string, object?> FlatBody(params Dictionary<string, object?>[] vehicleRates)
    {
        var body = ContractBody();
        SetRate(body, "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"None"}""");
        body["vehicleRates"] = vehicleRates;
        return body;
    }

    [Theory]
    [MemberData(nameof(ValidVehicleRates), MemberType = typeof(ContractTestSupport))]
    public async Task ValidVehicleRate_Returns201AndStoresAndReturnsItWithTheContract(
        string pricingMode, bool nightSurcharge, string rate)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        await using var factory = CreateFactory();
        var body = ContractBody();
        SetRate(body, "FlatRate", pricingMode, nightSurcharge,
            nightSurcharge
                ? """{"dayPrice":45,"nightPrice":55,"nightCallFee":5,"minimumRule":"None"}"""
                : """{"dayPrice":45,"minimumRule":"None"}""");
        body["vehicleRates"] = new[] { VehicleRateItem(vehicle.Id.Value, rate) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var returned = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("vehicleRates").EnumerateArray().ShouldHaveSingleItem();
        var stored = (await StoredAsync(customer.Id)).ShouldHaveSingleItem().VehicleRates.ShouldHaveSingleItem();
        using var members = JsonDocument.Parse(rate);
        decimal? Sent(string name) =>
            members.RootElement.TryGetProperty(name, out var value) ? value.GetDecimal() : null;
        decimal? Returned(string name) =>
            returned.GetProperty(name) is { ValueKind: JsonValueKind.Number } value ? value.GetDecimal() : null;

        returned.GetProperty("vehicleId").GetGuid().ShouldBe(vehicle.Id.Value);
        returned.GetProperty("licensePlate").GetString().ShouldBe("AB456CD");
        stored.VehicleId.ShouldBe(vehicle.Id);
        stored.Rate.DayPrice.ShouldBe(Sent("dayPrice")!.Value);
        stored.Rate.NightPrice.ShouldBe(Sent("nightPrice"));
        stored.Rate.NightCallFee.ShouldBe(Sent("nightCallFee"));
        stored.Rate.Minimum.Amount.ShouldBe(Sent("minimumAmount"));
        stored.Rate.Minimum.Threshold.ShouldBe(Sent("minimumThreshold"));
        stored.Rate.Minimum.OveragePrice.ShouldBe(Sent("minimumOveragePrice"));
        foreach (var member in new[] { "dayPrice", "nightPrice", "nightCallFee", "minimumAmount", "minimumThreshold", "minimumOveragePrice" })
        {
            Returned(member).ShouldBe(Sent(member), member);
        }
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task ContractWithoutVehicleRates_IsValidWhetherTheMemberIsAbsentNullOrEmpty(string form)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory();
        var body = FlatBody();
        if (form == "absent")
        {
            body.Remove("vehicleRates");
        }
        else if (form == "null")
        {
            body["vehicleRates"] = null;
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("vehicleRates").GetArrayLength().ShouldBe(0);
        (await StoredAsync(customer.Id)).ShouldHaveSingleItem().VehicleRates.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidRates), MemberType = typeof(ContractTestSupport))]
    public async Task InvalidVehicleRate_Returns400UnderTheItemKeys_AndSavesNothing(
        string billingLevel, string pricingMode, bool nightSurcharge, string rate, string keys)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var before = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = ContractBody();
        SetRateWithVehicleRate(body, billingLevel, pricingMode, nightSurcharge, rate);

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(VehicleRateKeys(billingLevel, keys));
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task DuplicateAndMissingVehicle_Return400OnTheLaterItemsVehicleId_InItalian()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        await using var factory = CreateFactory();
        var body = FlatBody(
            VehicleRateItem(vehicle.Id.Value), VehicleRateItem(vehicle.Id.Value), VehicleRateItem(null));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VehicleRates[1].VehicleId", "VehicleRates[2].VehicleId"]);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
        errors.GetProperty("VehicleRates[1].VehicleId")[0].GetString()
            .ShouldBe("Il mezzo ha già una tariffa propria in questo contratto.");
        errors.GetProperty("VehicleRates[2].VehicleId")[0].GetString().ShouldBe("Valore obbligatorio.");
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("trailer")]
    [InlineData("decommissioned")]
    public async Task VehicleThatCannotHaveAVehicleRate_Returns400WithTheCodeAndTheNameInTheText_AndSavesNothing(string kind)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var unknown = Guid.NewGuid();
        var vehicleId = kind switch
        {
            "trailer" => (await SeedVehicleRowAsync("AB456CD", VehicleType.Trailer)).Id.Value,
            "decommissioned" => (await SeedVehicleRowAsync("AB456CD", decommissioned: true)).Id.Value,
            _ => unknown,
        };
        var before = await AuditCountAsync();
        var (error, text) = kind switch
        {
            "trailer" => (ContractErrors.VehicleRateOnTrailer, "Il mezzo AB456CD è un rimorchio: non può avere una tariffa propria."),
            "decommissioned" => (ContractErrors.VehicleRateDecommissioned, "Il mezzo AB456CD è dismesso: non si può aggiungere una sua tariffa."),
            _ => (ContractErrors.VehicleRateUnknownVehicle, $"Il mezzo {unknown} non esiste."),
        };
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), customer.Id.Value, FlatBody(VehicleRateItem(vehicleId)));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, error, Ct);
        (await DetailAsync(response)).ShouldBe(text);
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task SuccessfulCreationWithVehicleRates_WritesThemInTheAuditRow()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var first = await SeedVehicleRowAsync("AB456CD");
        var second = await SeedVehicleRowAsync("CD123EF", VehicleType.StandaloneVehicle);
        await using var factory = CreateFactory();
        var body = FlatBody(
            VehicleRateItem(first.Id.Value),
            VehicleRateItem(second.Id.Value, """{"dayPrice":80.5,"minimumRule":"FixedMinimum","minimumAmount":200}"""));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), customer.Id.Value, body);

        // Assert
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        var rates = payload.RootElement.GetProperty("vehicleRates").EnumerateArray().ToList();
        rates.Count.ShouldBe(2);
        var priced = rates.Single(r => r.GetProperty("vehicleId").GetGuid() == second.Id.Value);
        priced.GetProperty("dayPrice").GetDecimal().ShouldBe(80.5m);
        priced.GetProperty("minimumRule").GetString().ShouldBe("FixedMinimum");
        priced.GetProperty("minimumAmount").GetDecimal().ShouldBe(200m);
        rates.Single(r => r.GetProperty("vehicleId").GetGuid() == first.Id.Value)
            .GetProperty("dayPrice").GetDecimal().ShouldBe(70m);
    }

    [Fact]
    public async Task Driver_Gets403WhenCreatingAContractWithVehicleRates_AndNothingIsSaved()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var vehicle = await SeedVehicleRowAsync("AB456CD");
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());

        // Act
        var response = await PostAsync(client, customer.Id.Value, FlatBody(VehicleRateItem(vehicle.Id.Value)));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await StoredAsync(customer.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), Guid.NewGuid(), ContractBody());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
