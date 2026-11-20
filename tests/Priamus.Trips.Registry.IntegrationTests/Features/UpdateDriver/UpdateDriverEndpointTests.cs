using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.UpdateDriver;

/// <summary>
/// Endpoint integration tests for <c>PUT /api/registry/drivers/{id}</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class UpdateDriverEndpointTests : RegistryIntegrationTestBase
{
    public UpdateDriverEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Dictionary<string, object?> UpdateBody(
        int? version,
        string firstName = "Marco",
        string lastName = "Rossi",
        string? phone = "+39 085 123456",
        Guid? userId = null)
    {
        var body = DriverBody(firstName, lastName, phone, userId);
        if (version is not null)
        {
            body["version"] = version;
        }

        return body;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, object body) =>
        client.PutAsJsonAsync($"/api/registry/drivers/{id}", body, TestJson.Options, Ct);

    private async Task<Guid> SeedAccountAsync(string username = "luca.difebo")
    {
        await using var factory = CreateFactory();
        return await CreateAccountAsync(factory, username);
    }

    private async Task<Driver> SeedAsync(
        string firstName = "Luca", string lastName = "Di Febo", Guid? userId = null, bool deactivated = false)
    {
        await using var db = CreateDbContext();
        return await SeedDriverAsync(db, firstName, lastName, userId: userId, deactivated: deactivated);
    }

    private async Task<Driver> LoadAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Drivers.AsNoTracking().SingleAsync(d => d.Id == new DriverId(id), Ct);
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
        var userId = await CreateAccountAsync(factory);

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory),
            seeded.Id.Value,
            UpdateBody(version: 0, firstName: "  Marco  ", lastName: " Rossi ", phone: " +39 085 123456 ", userId: userId));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.FirstName.ShouldBe("Marco");
        stored.LastName.ShouldBe("Rossi");
        stored.Phone.ShouldBe("+39 085 123456");
        stored.UserId!.Value.Value.ShouldBe(userId);
        stored.Status.ShouldBe(DriverStatus.Active);
        stored.Version.ShouldBe(1);
        stored.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        stored.LastModifiedAt.ShouldBeGreaterThanOrEqualTo(seeded.LastModifiedAt);
    }

    [Fact]
    public async Task DeactivatedDriver_CanBeEdited_AndStaysDeactivated()
    {
        // Arrange
        var seeded = await SeedAsync(deactivated: true);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, firstName: "Rinominato"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.FirstName.ShouldBe("Rinominato");
        stored.Status.ShouldBe(DriverStatus.Deactivated);
    }

    [Fact]
    public async Task EmptyPhoneAndNoAccount_ClearTheStoredValues()
    {
        // Arrange
        var userId = await SeedAccountAsync();
        var seeded = await SeedAsync(userId: userId);
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, phone: "   ", userId: null));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Phone.ShouldBeNull();
        stored.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task KeepingAnAccountThatIsNowDisabled_IsAllowed()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory, disabled: true);
        var seeded = await SeedAsync(userId: userId);

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, firstName: "Rinominato", userId: userId));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.FirstName.ShouldBe("Rinominato");
        stored.UserId!.Value.Value.ShouldBe(userId);
    }

    [Fact]
    public async Task SwitchingToADisabledAccount_Returns409AccountUnavailable()
    {
        // Arrange
        await using var factory = CreateFactory();
        var current = await CreateAccountAsync(factory, "account.attuale");
        var disabled = await CreateAccountAsync(factory, "account.spento", disabled: true);
        var seeded = await SeedAsync(userId: current);

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, userId: disabled));

        // Assert
        (await DetailAsync(response)).ShouldBe("L'account scelto non è un account autista abilitato: scegline un altro.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.AccountUnavailable, Ct);
        (await LoadAsync(seeded.Id.Value)).UserId!.Value.Value.ShouldBe(current);
    }

    [Theory]
    [InlineData("account of another role", "BackOffice", false)]
    [InlineData("account that does not exist", "Driver", true)]
    public async Task AccountThatCannotBeLinked_Returns409_AndChangesNothing(string scenario, string role, bool missing)
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = missing ? Guid.NewGuid() : await CreateAccountAsync(factory, "account.scelto", role);
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, userId: userId));

        // Assert
        (await DetailAsync(response)).ShouldBe("L'account scelto non è un account autista abilitato: scegline un altro.", scenario);
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.AccountUnavailable, Ct);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.UserId.ShouldBeNull();
        stored.Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData(false, "L'account luca.difebo è già collegato all'autista Mario Rossi.")]
    [InlineData(true, "L'account luca.difebo è già collegato all'autista Mario Rossi (disattivato).")]
    public async Task AccountOfAnotherDriver_Returns409AndNamesTheAccountAndTheHolder(bool holderDeactivated, string expectedDetail)
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory, "luca.difebo");
        var holder = await SeedAsync("Mario", "Rossi", userId, holderDeactivated);
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, userId: userId));

        // Assert
        (await DetailAsync(response)).ShouldBe(expectedDetail);
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            holderDeactivated ? DriverErrors.AccountTakenByDeactivated : DriverErrors.AccountTaken,
            Ct);
        (await LoadAsync(seeded.Id.Value)).UserId.ShouldBeNull();
        (await LoadAsync(holder.Id.Value)).Version.ShouldBe(holder.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData("Active", false)]
    [InlineData("Deactivated", true)]
    public async Task AccountTakenBetweenTheCheckAndTheSave_Returns409AndChangesNothing(string competitorStatus, bool deactivatedHolder)
    {
        // Arrange
        var userId = await SeedAccountAsync();
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingAccountLinkInterceptor(ConnectionString, userId, competitorStatus))));

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, userId: userId));

        // Assert
        (await DetailAsync(response)).ShouldBe(deactivatedHolder
            ? "L'account luca.difebo è già collegato all'autista Mario Concorrente (disattivato)."
            : "L'account luca.difebo è già collegato all'autista Mario Concorrente.");
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            deactivatedHolder ? DriverErrors.AccountTakenByDeactivated : DriverErrors.AccountTaken,
            Ct);
        (await LoadAsync(seeded.Id.Value)).UserId.ShouldBeNull();
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task TwoEditsToTheSameAccountAtTheSameMoment_OneSucceedsTheOtherGets409()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);
        var first = await SeedAsync("Uno", "Primo");
        var second = await SeedAsync("Due", "Secondo");
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(
            PutAsync(client, first.Id.Value, UpdateBody(first.Version, userId: userId)),
            PutAsync(client, second.Id.Value, UpdateBody(second.Version, userId: userId)));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        await using var db = CreateDbContext();
        (await db.Drivers.CountAsync(d => d.UserId == new Priamus.Trips.SharedKernel.Security.UserId(userId), Ct)).ShouldBe(1);
    }

    [Theory]
    [InlineData("omitted first name", "firstName", null)]
    [InlineData("blank first name", "firstName", "   ")]
    [InlineData("first name over 60", "firstName", "{61}")]
    [InlineData("omitted last name", "lastName", null)]
    [InlineData("last name over 60", "lastName", "{61}")]
    [InlineData("phone over 20", "phone", "{21}")]
    [InlineData("phone with letters", "phone", "333 abc")]
    [InlineData("omitted version", "version", null)]
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
            // "{n}" stands for a run of n characters; for the phone, digits.
            body[field] = value.StartsWith('{')
                ? new string(field == "phone" ? '3' : 'x', int.Parse(value.Trim('{', '}')))
                : value;
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
    public async Task EqualNamesOfAnotherDriver_AreAccepted()
    {
        await SeedAsync("Marco", "Rossi");
        var seeded = await SeedAsync("Luca", "Di Febo");
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UnknownDriver_Returns404()
    {
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), Guid.NewGuid(), UpdateBody(version: 0));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, DriverErrors.NotFound, Ct);
    }

    [Fact]
    public async Task StaleVersion_Returns409WithTheItalianMessage_AndSavesNothing()
    {
        // Arrange: the driver is read at version 0, then someone else changes it.
        var seeded = await SeedAsync();
        await using (var db = CreateDbContext())
        {
            var other = await db.Drivers.SingleAsync(Ct);
            other.Update("Modificato", "Da Altri", other.Phone, other.UserId, new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 0));

        // Assert
        (await DetailAsync(response)).ShouldBe("L'autista è stato modificato da un altro utente. Ricarica la lista e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.FirstName.ShouldBe("Modificato");
        stored.Version.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task StaleVersion_WithAnAccountTakenByAnother_GetsConcurrentUpdateNotTheAccountConflict()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);
        await SeedAsync("Mario", "Rossi", userId);
        var seeded = await SeedAsync();
        await using (var db = CreateDbContext())
        {
            var other = await db.Drivers.SingleAsync(d => d.Id == seeded.Id, Ct);
            other.Update("Modificato", "Da Altri", other.Phone, other.UserId, new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 0, userId: userId));

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).UserId.ShouldBeNull();
    }

    [Fact]
    public async Task VersionFromTheCreateResponse_IsAcceptedOnce_ThenGoesStale()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var created = await client.PostAsJsonAsync("/api/registry/drivers", DriverBody(), TestJson.Options, Ct);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = createdBody.GetProperty("id").GetGuid();
        var version = createdBody.GetProperty("version").GetInt32();

        // Act
        var first = await PutAsync(client, id, UpdateBody(version, firstName: "Prima"));
        var second = await PutAsync(client, id, UpdateBody(version, firstName: "Seconda"));

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(id)).FirstName.ShouldBe("Prima");
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

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
    }

    [Fact]
    public async Task VersionAheadOfTheStoredOne_IsRefused()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(version: 7));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(0);
    }

    [Fact]
    public async Task ChosenExistingAccount_IsLinked_AndNoUserIsCreated()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);
        var usersBefore = await CountUsersAsync(ConnectionString);
        var seeded = await SeedAsync();

        // Act
        var response = await PutAsync(BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, userId: userId));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoadAsync(seeded.Id.Value)).UserId!.Value.Value.ShouldBe(userId);
        (await CountUsersAsync(ConnectionString)).ShouldBe(usersBefore);
    }

    [Fact]
    public async Task SuccessfulUpdate_WritesOneAuditRowWithTheFullStateAfterTheChange()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);
        var seeded = await SeedAsync(deactivated: true);

        // Act
        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, userId: userId));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.DriverUpdated, Ct);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("driverId").GetGuid().ShouldBe(seeded.Id.Value);
        payload.RootElement.GetProperty("firstName").GetString().ShouldBe("Marco");
        payload.RootElement.GetProperty("lastName").GetString().ShouldBe("Rossi");
        payload.RootElement.GetProperty("phone").GetString().ShouldBe("+39 085 123456");
        payload.RootElement.GetProperty("userId").GetGuid().ShouldBe(userId);
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Deactivated");
    }

    [Fact]
    public async Task SuccessfulUpdateClearingTheOptionalFields_LeavesThemOutOfTheAuditPayload()
    {
        var userId = await SeedAccountAsync();
        var seeded = await SeedAsync(userId: userId);
        await using var factory = CreateFactory();

        var response = await PutAsync(
            BackOfficeClient(factory), seeded.Id.Value, UpdateBody(seeded.Version, phone: null, userId: null));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.DriverUpdated, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.TryGetProperty("phone", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("userId", out _).ShouldBeFalse();
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
