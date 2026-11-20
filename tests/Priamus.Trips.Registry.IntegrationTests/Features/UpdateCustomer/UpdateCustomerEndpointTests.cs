using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.UpdateCustomer;

/// <summary>
/// Endpoint integration tests for <c>PUT /api/registry/customers/{id}</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class UpdateCustomerEndpointTests : RegistryIntegrationTestBase
{
    public UpdateCustomerEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Dictionary<string, object?> UpdateBody(
        int? version,
        string companyName = "Comune di Pescara",
        string? vatNumber = VatNumberB,
        string? taxCode = "RSSMRA80A01H501U",
        string? address = "Corso Umberto 5")
    {
        var body = CustomerBody(companyName, vatNumber, taxCode, address);
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, object body) =>
        client.PutAsJsonAsync($"/api/registry/customers/{id}", body, TestJson.Options, Ct);

    private async Task<Customer> SeedAsync(
        string companyName = "Comune di Penne", string? vatNumber = VatNumberA, bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await SeedCustomerAsync(db, companyName, vatNumber, deactivated: deactivated);
    }

    private async Task<Customer> LoadAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Customers.AsNoTracking().SingleAsync(c => c.Id == new CustomerId(id), Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task ValidRequest_Returns204AndReplacesTheDataAndBumpsTheVersion()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory),
            seeded.Id.Value,
            UpdateBody(version: 0, companyName: "  Comune di Pescara  ", vatNumber: "IT 12345678903", taxCode: "rssmra80a01h501u"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.CompanyName.ShouldBe("Comune di Pescara");
        stored.VatNumber!.Value.ShouldBe(VatNumberB);
        stored.TaxCode!.Value.ShouldBe("RSSMRA80A01H501U");
        stored.Address.ShouldBe("Corso Umberto 5");
        stored.Status.ShouldBe(CustomerStatus.Active);
        stored.Version.ShouldBe(1);
        stored.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        stored.LastModifiedAt.ShouldBeGreaterThanOrEqualTo(seeded.LastModifiedAt);
    }

    [Fact]
    public async Task DeactivatedCustomer_CanBeEdited_AndStaysDeactivated()
    {
        // Arrange
        var seeded = await SeedAsync(deactivated: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, companyName: "Rinominato"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.CompanyName.ShouldBe("Rinominato");
        stored.Status.ShouldBe(CustomerStatus.Deactivated);
    }

    [Fact]
    public async Task EmptyOptionalFields_ClearTheStoredValues()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, vatNumber: "  ", taxCode: "", address: "   "));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.VatNumber.ShouldBeNull();
        stored.TaxCode.ShouldBeNull();
        stored.Address.ShouldBeNull();
    }

    [Fact]
    public async Task KeepingItsOwnVatNumber_IsNotAConflict()
    {
        var seeded = await SeedAsync(vatNumber: VatNumberA);
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, vatNumber: "IT 0145 5280683"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CompanyNameOfAnotherCustomer_IsAccepted()
    {
        await SeedAsync("Comune di Penne", VatNumberA);
        var seeded = await SeedAsync("Altro", VatNumberB);
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, companyName: "Comune di Penne", vatNumber: VatNumberB));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("omitted company name", "companyName", null)]
    [InlineData("omitted version", "version", null)]
    [InlineData("blank company name", "companyName", "   ")]
    [InlineData("company name over 150", "companyName", "{151}")]
    [InlineData("vat number too short", "vatNumber", "0145528068")]
    [InlineData("vat number with a wrong check digit", "vatNumber", "01455280684")]
    [InlineData("vat number with final newline", "vatNumber", "12345678903\n")]
    [InlineData("tax code of 12 characters", "taxCode", "MRCGLI72D45G")]
    [InlineData("tax code with final newline", "taxCode", "RSSMRA80A01H501U\n")]
    [InlineData("address over 200", "address", "{201}")]
    public async Task InvalidField_Returns400OnThatField_AndChangesNothing(string scenario, string field, string? value)
    {
        // Arrange
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();
        var body = UpdateBody(seeded.Version);
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
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        (await ErrorKeysAsync(response)).ShouldBe([char.ToUpperInvariant(field[0]) + field[1..]], scenario);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task UnknownCustomer_Returns404()
    {
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), Guid.NewGuid(), UpdateBody(version: 0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData(false, "La partita IVA 12345678903 è già registrata sul cliente Comune di Pescara.")]
    [InlineData(true, "La partita IVA 12345678903 è già registrata sul cliente Comune di Pescara (disattivato). Per riprenderlo, riattivalo dalla lista.")]
    public async Task VatNumberOfAnotherCustomer_Returns409AndNamesTheVatNumberAndTheHolder(bool otherIsDeactivated, string expectedDetail)
    {
        // Arrange
        var other = await SeedAsync("Comune di Pescara", VatNumberB, otherIsDeactivated);
        var seeded = await SeedAsync("Comune di Penne", VatNumberA);
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, vatNumber: "it 12345678903"));

        // Assert
        (await DetailAsync(response)).ShouldBe(expectedDetail);
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            otherIsDeactivated ? CustomerErrors.VatNumberTakenByDeactivated : CustomerErrors.VatNumberTaken,
            Ct);
        (await LoadAsync(seeded.Id.Value)).VatNumber!.Value.ShouldBe(VatNumberA);
        (await LoadAsync(other.Id.Value)).Version.ShouldBe(other.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("Deactivated", false)]
    public async Task VatNumberTakenBetweenTheCheckAndTheSave_Returns409AndChangesNothing(string competitorStatus, bool activeHolder)
    {
        // Arrange
        var seeded = await SeedAsync(vatNumber: VatNumberA);
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingVatInsertInterceptor(ConnectionString, VatNumberB, competitorStatus))));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version));

        // Assert
        var expected = activeHolder ? CustomerErrors.VatNumberTaken : CustomerErrors.VatNumberTakenByDeactivated;
        var detail = (await DetailAsync(response)).ShouldNotBeNull();
        detail.ShouldContain(VatNumberB);
        detail.ShouldContain("Concorrente Srl");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, expected, Ct);
        (await LoadAsync(seeded.Id.Value)).VatNumber!.Value.ShouldBe(VatNumberA);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task TwoEditsToTheSameVatNumberAtTheSameMoment_OneSucceedsTheOthersGet409()
    {
        // Arrange
        var first = await SeedAsync("Uno", VatNumberA);
        var second = await SeedAsync("Due", null);
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(
            PutAsync(client, first.Id.Value, UpdateBody(first.Version, vatNumber: VatNumberB)),
            PutAsync(client, second.Id.Value, UpdateBody(second.Version, vatNumber: VatNumberB)));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        await using var db = CreateDbContext();
        (await db.Customers.CountAsync(c => c.VatNumber == Domain.Customers.ValueObjects.VatNumber.Create(VatNumberB), Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task StaleVersion_Returns409WithTheItalianMessage_AndSavesNothing()
    {
        // Arrange: the customer is read at version 0, then someone else changes it.
        var seeded = await SeedAsync();
        await using (var db = CreateDbContext())
        {
            var other = await db.Customers.SingleAsync(Ct);
            other.Update("Modificato da altri", other.VatNumber, other.TaxCode, other.Address, new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 0));

        // Assert
        (await DetailAsync(response)).ShouldBe("Il cliente è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, CustomerErrors.ConcurrentUpdate, Ct);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.CompanyName.ShouldBe("Modificato da altri");
        stored.VatNumber!.Value.ShouldBe(VatNumberA);
        stored.Version.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task StaleVersion_WithAVatNumberTakenByAnother_GetsConcurrentUpdateNotTheVatConflict()
    {
        // Arrange
        await SeedAsync("Comune di Pescara", VatNumberB);
        var seeded = await SeedAsync("Comune di Penne", VatNumberA);
        await using (var db = CreateDbContext())
        {
            var other = await db.Customers.SingleAsync(c => c.Id == seeded.Id, Ct);
            other.Update("Modificato da altri", other.VatNumber, other.TaxCode, other.Address, new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 0, vatNumber: VatNumberB));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, CustomerErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).VatNumber!.Value.ShouldBe(VatNumberA);
    }

    [Fact]
    public async Task VersionFromTheCreateResponse_IsAcceptedOnce_ThenGoesStale()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var created = await client.PostAsJsonAsync("/api/registry/customers", CustomerBody(), TestJson.Options, Ct);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = createdBody.GetProperty("id").GetGuid();
        var version = createdBody.GetProperty("version").GetInt32();

        // Act
        var first = await PutAsync(client, id, UpdateBody(version, companyName: "Prima modifica"));
        var second = await PutAsync(client, id, UpdateBody(version, companyName: "Seconda modifica"));

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, CustomerErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(id)).CompanyName.ShouldBe("Prima modifica");
    }

    [Fact]
    public async Task UnchangedData_Returns204WithoutAuditOrVersionBump()
    {
        // Arrange: a first save changes the data, so the second one sends exactly what is stored.
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        (await PutAsync(client, seeded.Id.Value, UpdateBody(version: seeded.Version))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var auditBefore = await AuditCountAsync();

        // Act
        var response = await PutAsync(client, seeded.Id.Value, UpdateBody(version: seeded.Version + 1));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(seeded.Version + 1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task StaleVersion_IsRefusedEvenWhenTheDataIsUnchanged()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        (await PutAsync(client, seeded.Id.Value, UpdateBody(version: 0))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await PutAsync(client, seeded.Id.Value, UpdateBody(version: 0));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, CustomerErrors.ConcurrentUpdate, Ct);
    }

    [Fact]
    public async Task VersionAheadOfTheStoredOne_IsRefused()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 7));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, CustomerErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(0);
    }

    [Fact]
    public async Task SuccessfulUpdate_WritesOneAuditRowWithTheFullStateAfterTheChange()
    {
        // Arrange
        var seeded = await SeedAsync(deactivated: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory),
            seeded.Id.Value,
            UpdateBody(seeded.Version, vatNumber: "it 12345678903", taxCode: "rssmra80a01h501u"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.CustomerUpdated, Ct);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("customerId").GetGuid().ShouldBe(seeded.Id.Value);
        payload.RootElement.GetProperty("companyName").GetString().ShouldBe("Comune di Pescara");
        payload.RootElement.GetProperty("vatNumber").GetString().ShouldBe(VatNumberB);
        payload.RootElement.GetProperty("taxCode").GetString().ShouldBe("RSSMRA80A01H501U");
        payload.RootElement.GetProperty("address").GetString().ShouldBe("Corso Umberto 5");
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Deactivated");
    }

    [Fact]
    public async Task SuccessfulUpdateClearingTheOptionalFields_LeavesThemOutOfTheAuditPayload()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, vatNumber: null, taxCode: null, address: null));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.CustomerUpdated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.TryGetProperty("vatNumber", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("taxCode", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("address", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.NoContent)]
    [InlineData("Admin", HttpStatusCode.NoContent)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Update_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PutAsync(client, seeded.Id.Value, UpdateBody(seeded.Version));

        response.StatusCode.ShouldBe(expected);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(expected == HttpStatusCode.NoContent ? 1 : 0);
    }

    [Fact]
    public async Task Update_AnonymousCaller_Returns401()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(factory.CreateClient(), seeded.Id.Value, UpdateBody(seeded.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
