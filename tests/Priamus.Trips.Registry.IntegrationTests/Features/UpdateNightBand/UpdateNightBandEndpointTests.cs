using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.UpdateNightBand;

/// <summary>
/// Endpoint integration tests for <c>PUT /api/registry/night-band</c>, against the per-test PostgreSQL
/// database and the test authentication scheme.
/// </summary>
public sealed class UpdateNightBandEndpointTests : RegistryIntegrationTestBase
{
    public UpdateNightBandEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private static Dictionary<string, object?> BandBody(string start = "21:30", string end = "05:15", int version = 0) =>
        new()
        {
            ["start"] = start,
            ["end"] = end,
            ["version"] = version,
        };

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, object body) =>
        client.PutAsJsonAsync("/api/registry/night-band", body, TestJson.Options, Ct);

    private async Task<NightBandSetting> LoadAsync()
    {
        await using var db = CreateDbContext();
        return await db.NightBandSettings.AsNoTracking().SingleAsync(Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task Admin_ChangingTheBand_Returns204StoresItAtOnceAndWritesOneAuditRow()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = AdminClient(factory);

        // Act
        var response = await PutAsync(client, BandBody("21:30", "05:15"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync();
        stored.Band.Start.ShouldBe(new TimeOnly(21, 30));
        stored.Band.End.ShouldBe(new TimeOnly(5, 15));
        stored.Version.ShouldBe(1);
        stored.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        await using var db = CreateDbContext();
        var row = (await db.AuditLog.ToListAsync(Ct)).ShouldHaveSingleItem();
        row.EventType.ShouldBe(AuditEventTypes.NightBandUpdated);
        var read = await client.GetFromJsonAsync<JsonElement>("/api/registry/night-band", Ct);
        read.GetProperty("start").GetString().ShouldBe("21:30");
        read.GetProperty("end").GetString().ShouldBe("05:15");
        read.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task SameBandAsStored_Returns204WithoutAuditRowOrVersionBump()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(AdminClient(factory), BandBody("22:00", "06:00", version: 0));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoadAsync()).Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("BackOffice")]
    [InlineData("Driver")]
    public async Task NonAdminRole_Returns403AndChangesNothing(string role)
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await PutAsync(client, BandBody());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await LoadAsync()).Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task EndEqualToStart_Returns400UnderEndWithTheItalianMessage_AndChangesNothing()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(AdminClient(factory), BandBody("22:00", "22:00"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = document.RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).ShouldBe(["End"]);
        errors.GetProperty("End")[0].GetString().ShouldBe("La fine deve essere diversa dall'inizio.");
        (await LoadAsync()).Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("omitted start", "start", null, "Start")]
    [InlineData("omitted end", "end", null, "End")]
    [InlineData("omitted version", "version", null, "Version")]
    [InlineData("start with seconds", "start", "22:00:00", "Start")]
    [InlineData("start without colon", "start", "2200", "Start")]
    [InlineData("end hour out of range", "end", "24:00", "End")]
    [InlineData("end minute out of range", "end", "06:60", "End")]
    [InlineData("end not a time", "end", "sera", "End")]
    public async Task InvalidField_Returns400OnThatField_AndChangesNothing(
        string scenario, string field, string? value, string expectedKey)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = BandBody("22:00", "06:00", version: 0);
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            body[field] = value;
        }

        // Act
        var response = await PutAsync(AdminClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scenario);
        (await ErrorKeysAsync(response)).ShouldBe([expectedKey], scenario);
        (await LoadAsync()).Version.ShouldBe(0);
        (await AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task BadTimeFormat_AnswersInItalianUnderTheField()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(AdminClient(factory), BandBody("sera", "06:00"));

        // Assert
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        document.RootElement.GetProperty("errors").GetProperty("Start")[0].GetString().ShouldBe("Inserisci un orario.");
    }

    [Fact]
    public async Task StaleVersion_Returns409WithTheItalianMessage_AndSavesNothing()
    {
        // Arrange: the band is read at version 0, then someone else changes it.
        await using (var db = CreateDbContext())
        {
            var setting = await db.NightBandSettings.SingleAsync(Ct);
            setting.Update(
                NightBand.Create(new TimeOnly(20, 0), new TimeOnly(5, 0)).Value,
                new SystemClock(),
                FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PutAsync(AdminClient(factory), BandBody("21:30", "05:15", version: 0));

        // Assert
        (await DetailAsync(response)).ShouldBe(
            "La fascia notturna è stata modificata da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, NightBandErrors.ConcurrentUpdate, Ct);
        var stored = await LoadAsync();
        stored.Band.Start.ShouldBe(new TimeOnly(20, 0));
        stored.Version.ShouldBe(1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }
}
