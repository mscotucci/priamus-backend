using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.CreateCustomer;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/customers</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class CreateCustomerEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/customers";

    public CreateCustomerEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(Url, body, TestJson.Options, Ct);

    private async Task<int> CountCustomersAsync()
    {
        await using var db = CreateDbContext();
        return await db.Customers.CountAsync(Ct);
    }

    [Fact]
    public async Task ValidRequest_Returns201WithAnActiveCustomerAtVersionZero()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var response = await PostAsync(client, CustomerBody());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("companyName").GetString().ShouldBe("Comune di Penne");
        body.GetProperty("vatNumber").GetString().ShouldBe(VatNumberA);
        body.GetProperty("taxCode").GetString().ShouldBe("MRCGLI72D45G438K");
        body.GetProperty("address").GetString().ShouldBe("Via Roma 1, Penne");
        body.GetProperty("status").GetString().ShouldBe("Active");
        body.GetProperty("version").GetInt32().ShouldBe(0);
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{body.GetProperty("id").GetGuid()}");

        await using var db = CreateDbContext();
        var stored = await db.Customers.SingleAsync(Ct);
        stored.Id.Value.ShouldBe(body.GetProperty("id").GetGuid());
        stored.Status.ShouldBe(CustomerStatus.Active);
    }

    [Theory]
    [InlineData("omitted", true)]
    [InlineData("blank", false)]
    public async Task OnlyTheCompanyNameIsRequired_EmptyOptionalFieldsAreStoredAsNone(string scenario, bool omit)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = CustomerBody(vatNumber: "   ", taxCode: "  ", address: "   ");
        if (omit)
        {
            body.Remove("vatNumber");
            body.Remove("taxCode");
            body.Remove("address");
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created, scenario);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        json.GetProperty("vatNumber").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("taxCode").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("address").ValueKind.ShouldBe(JsonValueKind.Null);
        await using var db = CreateDbContext();
        var stored = await db.Customers.SingleAsync(Ct);
        stored.VatNumber.ShouldBeNull();
        stored.TaxCode.ShouldBeNull();
        stored.Address.ShouldBeNull();
    }

    [Fact]
    public async Task Fields_AreStoredInTheirNormalForms()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory),
            CustomerBody(companyName: "  Comune di Penne  ", vatNumber: "it 0145 5280683", taxCode: " mrc gli72d45 g438k ", address: "  Via Roma 1  "));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var stored = await db.Customers.SingleAsync(Ct);
        stored.CompanyName.ShouldBe("Comune di Penne");
        stored.VatNumber!.Value.ShouldBe(VatNumberA);
        stored.TaxCode!.Value.ShouldBe("MRCGLI72D45G438K");
        stored.Address.ShouldBe("Via Roma 1");
    }

    [Fact]
    public async Task CompanyNameAndAddress_AreMeasuredAfterTrimming()
    {
        await using var factory = CreateFactory();
        var name = new string('n', Customer.CompanyNameMaxLength);
        var address = new string('a', Customer.AddressMaxLength);

        var response = await PostAsync(
            BackOfficeClient(factory), CustomerBody(companyName: "  " + name + "  ", address: "  " + address + "  "));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var stored = await db.Customers.SingleAsync(Ct);
        stored.CompanyName.ShouldBe(name);
        stored.Address.ShouldBe(address);
    }

    [Fact]
    public async Task TwoCustomersWithTheSameCompanyName_AreBothSaved()
    {
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        var first = await PostAsync(client, CustomerBody(vatNumber: VatNumberA));
        var second = await PostAsync(client, CustomerBody(vatNumber: VatNumberB));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CountCustomersAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task CustomersWithoutVatNumber_AndWithTheSameTaxCode_AreAllSaved()
    {
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        var first = await PostAsync(client, CustomerBody(companyName: "Uno", vatNumber: null, taxCode: VatNumberA));
        var second = await PostAsync(client, CustomerBody(companyName: "Due", vatNumber: VatNumberA, taxCode: VatNumberA));
        var third = await PostAsync(client, CustomerBody(companyName: "Tre", vatNumber: null, taxCode: VatNumberA));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        third.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CountCustomersAsync()).ShouldBe(3);
    }

    [Theory]
    [InlineData("omitted company name", "companyName", null)]
    [InlineData("blank company name", "companyName", "   ")]
    [InlineData("company name over 150", "companyName", "{151}")]
    [InlineData("vat number too short", "vatNumber", "0145528068")]
    [InlineData("vat number with letters", "vatNumber", "0145528068A")]
    [InlineData("vat number with a wrong check digit", "vatNumber", "01455280684")]
    [InlineData("vat number with final newline", "vatNumber", "01455280683\n")]
    [InlineData("vat number with accented digits", "vatNumber", "0145528068٣")]
    [InlineData("tax code of 12 characters", "taxCode", "MRCGLI72D45G")]
    [InlineData("tax code with symbols", "taxCode", "MRCGLI72D45G438!")]
    [InlineData("tax code with final newline", "taxCode", "MRCGLI72D45G438K\n")]
    [InlineData("address over 200", "address", "{201}")]
    public async Task InvalidField_Returns400OnThatField_AndSavesNothing(string scenario, string field, string? value)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = CustomerBody();
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            // "{n}" stands for a run of n characters.
            body[field] = value.StartsWith('{') ? new string('x', int.Parse(value.Trim('{', '}'))) : value;
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        (await ErrorKeysAsync(response)).ShouldBe([char.ToUpperInvariant(field[0]) + field[1..]], scenario);
        (await CountCustomersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task VatNumberWithAWrongCheckDigit_Returns400UnderVatNumberWithTheItalianMessage()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), CustomerBody(vatNumber: "01455280684"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VatNumber"]);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("cifra di controllo");
    }

    [Theory]
    [InlineData(VatNumberA, false)]
    [InlineData("it 0145 5280683", false)]
    [InlineData(VatNumberA, true)]
    public async Task VatNumberOfAnotherCustomer_Returns409AndNamesTheVatNumberAndTheHolder(string vatNumber, bool deactivated)
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedCustomerAsync(db, "Comune di Penne", VatNumberA, deactivated: deactivated);
        }

        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), CustomerBody(companyName: "Altro", vatNumber: vatNumber));

        // Assert
        (await DetailAsync(response)).ShouldBe(deactivated
            ? $"La partita IVA {VatNumberA} è già registrata sul cliente Comune di Penne (disattivato). Per riprenderlo, riattivalo dalla lista."
            : $"La partita IVA {VatNumberA} è già registrata sul cliente Comune di Penne.");
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            deactivated ? CustomerErrors.VatNumberTakenByDeactivated : CustomerErrors.VatNumberTaken,
            Ct);
        (await CountCustomersAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Deactivated", false)]
    public async Task VatNumberTakenBetweenTheCheckAndTheSave_Returns409(string competitorStatus, bool activeHolder)
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingVatInsertInterceptor(ConnectionString, VatNumberA, competitorStatus))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), CustomerBody());

        // Assert
        var expected = activeHolder ? CustomerErrors.VatNumberTaken : CustomerErrors.VatNumberTakenByDeactivated;
        var detail = (await DetailAsync(response)).ShouldNotBeNull();
        detail.ShouldContain(VatNumberA);
        detail.ShouldContain("Concorrente Srl");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, expected, Ct);
        (await CountCustomersAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task VatNumberTakenByARaceWinnerThatIsGoneBeforeTheLookup_Returns409NamingTheVatNumber()
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(
                    new CompetingVatInsertInterceptor(ConnectionString, VatNumberA, "Active", vanishAfterFailure: true))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), CustomerBody());

        // Assert
        (await DetailAsync(response)).ShouldBe($"La partita IVA {VatNumberA} è già registrata su un altro cliente.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, CustomerErrors.VatNumberTakenByAnother, Ct);
        (await CountCustomersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task TwoCreationsWithTheSameVatNumberAtTheSameMoment_OneSucceedsTheOtherGets409()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, CustomerBody())));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        (await CountCustomersAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task SuccessfulCreation_WritesOneAuditRowWithTheFullStateAndTheActor()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), CustomerBody(vatNumber: "it 0145 5280683", taxCode: "mrcgli72d45g438k"));

        // Assert
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        row.EventType.ShouldBe(AuditEventTypes.CustomerCreated);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("customerId").GetGuid().ShouldBe(id);
        payload.RootElement.GetProperty("companyName").GetString().ShouldBe("Comune di Penne");
        payload.RootElement.GetProperty("vatNumber").GetString().ShouldBe(VatNumberA);
        payload.RootElement.GetProperty("taxCode").GetString().ShouldBe("MRCGLI72D45G438K");
        payload.RootElement.GetProperty("address").GetString().ShouldBe("Via Roma 1, Penne");
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public async Task SuccessfulCreationWithoutOptionalFields_LeavesThemOutOfTheAuditPayload()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), CustomerBody(vatNumber: null, taxCode: null, address: null));

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.TryGetProperty("vatNumber", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("taxCode", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("address", out _).ShouldBeFalse();
        payload.RootElement.GetProperty("companyName").GetString().ShouldBe("Comune di Penne");
    }

    [Fact]
    public async Task RefusedCreation_WritesNoAuditRow()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            await SeedCustomerAsync(db);
        }

        int before;
        await using (var auditCountDb = CreateDbContext())
        {
            before = await auditCountDb.AuditLog.CountAsync(Ct);
        }

        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var taken = await PostAsync(client, CustomerBody());
        var invalid = await PostAsync(client, CustomerBody(vatNumber: "01455280684"));

        // Assert
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await using var db2 = CreateDbContext();
        (await db2.AuditLog.CountAsync(Ct)).ShouldBe(before);
    }

    [Fact]
    public async Task VatNumberRaceLost_WritesNoAuditRowForTheLoser()
    {
        // Arrange
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingVatInsertInterceptor(ConnectionString, VatNumberA, "Active"))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), CustomerBody());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var db = CreateDbContext();
        (await db.AuditLog.CountAsync(Ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Create_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PostAsync(client, CustomerBody());

        response.StatusCode.ShouldBe(expected);
        (await CountCustomersAsync()).ShouldBe(expected == HttpStatusCode.Created ? 1 : 0);
    }

    [Fact]
    public async Task Create_AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), CustomerBody());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
