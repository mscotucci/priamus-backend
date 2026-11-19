using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.CreateDriver;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/drivers</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class CreateDriverEndpointTests : RegistryIntegrationTestBase
{
    private const string Url = "/api/registry/drivers";

    public CreateDriverEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        ApiFactory.Create(ConnectionString, configureServices, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(Url, body, TestJson.Options, Ct);

    /// <summary>Creates the driver account in a host of its own, so a later host can be built around its id.</summary>
    private async Task<Guid> SeedAccountAsync()
    {
        await using var factory = CreateFactory();
        return await CreateAccountAsync(factory);
    }

    private async Task<int> CountDriversAsync()
    {
        await using var db = CreateDbContext();
        return await db.Drivers.CountAsync(Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task ValidRequest_Returns201WithAnActiveDriverAtVersionZero()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var response = await PostAsync(client, DriverBody());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("firstName").GetString().ShouldBe("Luca");
        body.GetProperty("lastName").GetString().ShouldBe("Di Febo");
        body.GetProperty("phone").GetString().ShouldBe("333 418 2291");
        body.GetProperty("account").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("status").GetString().ShouldBe("Active");
        body.GetProperty("version").GetInt32().ShouldBe(0);
        response.Headers.Location!.ToString().ShouldBe($"{Url}/{body.GetProperty("id").GetGuid()}");

        await using var db = CreateDbContext();
        var stored = await db.Drivers.SingleAsync(Ct);
        stored.Id.Value.ShouldBe(body.GetProperty("id").GetGuid());
        stored.Status.ShouldBe(DriverStatus.Active);
    }

    [Fact]
    public async Task ChosenExistingAccount_IsLinked_AndNoUserIsCreated()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);
        var usersBefore = await CountUsersAsync(ConnectionString);

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        (await db.Drivers.SingleAsync(Ct)).UserId!.Value.Value.ShouldBe(userId);
        (await CountUsersAsync(ConnectionString)).ShouldBe(usersBefore);
    }

    [Fact]
    public async Task CreatedDriver_CarriesTheAccountUserNameAndWhetherItIsDisabled()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory, "luca.difebo");

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var account = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("account");
        account.GetProperty("userId").GetGuid().ShouldBe(userId);
        account.GetProperty("username").GetString().ShouldBe("luca.difebo");
        account.GetProperty("isDisabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task DriverCreatedWithoutAnAccount_IsLinkedLaterByAnEdit()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var userId = await CreateAccountAsync(factory);
        var created = await PostAsync(client, DriverBody(userId: null));
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = createdBody.GetProperty("id").GetGuid();

        // Act
        var edit = DriverBody(userId: userId);
        edit["version"] = createdBody.GetProperty("version").GetInt32();
        var response = await client.PutAsJsonAsync($"{Url}/{id}", edit, TestJson.Options, Ct);

        // Assert
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        (await db.Drivers.SingleAsync(Ct)).UserId!.Value.Value.ShouldBe(userId);
    }

    [Theory]
    [InlineData("omitted", true)]
    [InlineData("blank", false)]
    public async Task OnlyTheNamesAreRequired_EmptyOptionalFieldsAreStoredAsNone(string scenario, bool omit)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = DriverBody(phone: "   ");
        if (omit)
        {
            body.Remove("phone");
            body.Remove("userId");
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created, scenario);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("phone").ValueKind.ShouldBe(JsonValueKind.Null);
        await using var db = CreateDbContext();
        var stored = await db.Drivers.SingleAsync(Ct);
        stored.Phone.ShouldBeNull();
        stored.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task NamesAreTrimmed_AndThePhoneIsStoredAsTyped()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(
            BackOfficeClient(factory), DriverBody(firstName: "  Luca  ", lastName: " Di Febo ", phone: " +39 333 418 2291 "));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var stored = await db.Drivers.SingleAsync(Ct);
        stored.FirstName.ShouldBe("Luca");
        stored.LastName.ShouldBe("Di Febo");
        stored.Phone.ShouldBe("+39 333 418 2291");
    }

    [Fact]
    public async Task NamesAndPhone_AreMeasuredAfterTrimming()
    {
        await using var factory = CreateFactory();
        var name = new string('n', Driver.FirstNameMaxLength);
        var phone = new string('3', Driver.PhoneMaxLength);

        var response = await PostAsync(
            BackOfficeClient(factory), DriverBody(firstName: "  " + name + "  ", lastName: "  " + name + "  ", phone: "  " + phone + "  "));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var db = CreateDbContext();
        var stored = await db.Drivers.SingleAsync(Ct);
        stored.FirstName.ShouldBe(name);
        stored.LastName.ShouldBe(name);
        stored.Phone.ShouldBe(phone);
    }

    [Fact]
    public async Task TwoDriversWithTheSameNames_AreBothSaved()
    {
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        var first = await PostAsync(client, DriverBody());
        var second = await PostAsync(client, DriverBody());

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CountDriversAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData("omitted first name", "firstName", null)]
    [InlineData("blank first name", "firstName", "   ")]
    [InlineData("first name over 60", "firstName", "{61}")]
    [InlineData("omitted last name", "lastName", null)]
    [InlineData("blank last name", "lastName", "   ")]
    [InlineData("last name over 60", "lastName", "{61}")]
    [InlineData("phone over 20", "phone", "{21}")]
    [InlineData("phone with letters", "phone", "333 abc")]
    [InlineData("phone with a plus in the middle", "phone", "333+418")]
    [InlineData("phone with dashes", "phone", "333-418-2291")]
    [InlineData("phone with two plus signs", "phone", "+39 +333")]
    public async Task InvalidField_Returns400OnThatField_AndSavesNothing(string scenario, string field, string? value)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = DriverBody();
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
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        (await ErrorKeysAsync(response)).ShouldBe([char.ToUpperInvariant(field[0]) + field[1..]], scenario);
        (await CountDriversAsync()).ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("account of another role", "BackOffice", false, false)]
    [InlineData("disabled driver account", "Driver", true, false)]
    [InlineData("account that does not exist", "Driver", false, true)]
    public async Task AccountThatCannotBeLinked_Returns409WithTheItalianMessage_AndSavesNothing(
        string scenario, string role, bool disabled, bool missing)
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = missing ? Guid.NewGuid() : await CreateAccountAsync(factory, "account.scelto", role, disabled);
        var auditBefore = await AuditCountAsync();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        (await DetailAsync(response)).ShouldBe("L'account scelto non è un account autista abilitato: scegline un altro.", scenario);
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.AccountUnavailable, Ct);
        (await CountDriversAsync()).ShouldBe(0);
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
        await using (var db = CreateDbContext())
        {
            await SeedDriverAsync(db, "Mario", "Rossi", userId: userId, deactivated: holderDeactivated);
        }

        var auditBefore = await AuditCountAsync();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        (await DetailAsync(response)).ShouldBe(expectedDetail);
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            holderDeactivated ? DriverErrors.AccountTakenByDeactivated : DriverErrors.AccountTaken,
            Ct);
        (await CountDriversAsync()).ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData("Active", false)]
    [InlineData("Deactivated", true)]
    public async Task AccountTakenBetweenTheCheckAndTheSave_Returns409(string competitorStatus, bool deactivatedHolder)
    {
        // Arrange
        var userId = await SeedAccountAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingAccountLinkInterceptor(ConnectionString, userId, competitorStatus))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        (await DetailAsync(response)).ShouldBe(deactivatedHolder
            ? "L'account luca.difebo è già collegato all'autista Mario Concorrente (disattivato)."
            : "L'account luca.difebo è già collegato all'autista Mario Concorrente.");
        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict,
            deactivatedHolder ? DriverErrors.AccountTakenByDeactivated : DriverErrors.AccountTaken,
            Ct);
        (await CountDriversAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task AccountTakenByARaceWinnerThatLetGoBeforeTheLookup_Returns409AccountUnavailable()
    {
        // Arrange
        var userId = await SeedAccountAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(
                    new CompetingAccountLinkInterceptor(ConnectionString, userId, "Active", vanishAfterFailure: true))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        (await DetailAsync(response)).ShouldBe("L'account scelto non è un account autista abilitato: scegline un altro.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.AccountUnavailable, Ct);
        (await CountDriversAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task TwoCreationsWithTheSameAccountAtTheSameMoment_OneSucceedsTheOthersGet409()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);
        var client = BackOfficeClient(factory);

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, DriverBody(userId: userId))));

        // Assert
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        (await CountDriversAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task SuccessfulCreation_WritesOneAuditRowWithTheFullStateAndTheActor()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        row.EventType.ShouldBe(AuditEventTypes.DriverCreated);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("driverId").GetGuid().ShouldBe(id);
        payload.RootElement.GetProperty("firstName").GetString().ShouldBe("Luca");
        payload.RootElement.GetProperty("lastName").GetString().ShouldBe("Di Febo");
        payload.RootElement.GetProperty("phone").GetString().ShouldBe("333 418 2291");
        payload.RootElement.GetProperty("userId").GetGuid().ShouldBe(userId);
        payload.RootElement.GetProperty("status").GetString().ShouldBe("Active");
    }

    [Fact]
    public async Task SuccessfulCreationWithoutOptionalFields_LeavesThemOutOfTheAuditPayload()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), DriverBody(phone: null, userId: null));

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        var row = await db.AuditLog.SingleAsync(a => a.SubjectId == id, Ct);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.TryGetProperty("phone", out _).ShouldBeFalse();
        payload.RootElement.TryGetProperty("userId", out _).ShouldBeFalse();
        payload.RootElement.GetProperty("firstName").GetString().ShouldBe("Luca");
    }

    [Fact]
    public async Task RefusedCreation_WritesNoAuditRow()
    {
        // Arrange
        await using var factory = CreateFactory();
        var userId = await CreateAccountAsync(factory);
        await using (var db = CreateDbContext())
        {
            await SeedDriverAsync(db, userId: userId);
        }

        var before = await AuditCountAsync();
        var client = BackOfficeClient(factory);

        // Act
        var taken = await PostAsync(client, DriverBody(userId: userId));
        var invalid = await PostAsync(client, DriverBody(firstName: "  "));

        // Assert
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuditCountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task AccountRaceLost_WritesNoAuditRowForTheLoser()
    {
        // Arrange
        var userId = await SeedAccountAsync();
        await using var factory = CreateFactory(services =>
            services.AddDbContext<RegistryDbContext>(options =>
                options.AddInterceptors(new CompetingAccountLinkInterceptor(ConnectionString, userId, "Active"))));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), DriverBody(userId: userId));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Create_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PostAsync(client, DriverBody());

        response.StatusCode.ShouldBe(expected);
        (await CountDriversAsync()).ShouldBe(expected == HttpStatusCode.Created ? 1 : 0);
    }

    [Fact]
    public async Task Create_AnonymousCaller_Returns401()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), DriverBody());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
