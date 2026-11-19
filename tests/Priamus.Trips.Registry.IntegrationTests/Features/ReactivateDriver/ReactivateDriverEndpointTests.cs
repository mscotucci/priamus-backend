using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.DriverTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.ReactivateDriver;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/drivers/{id}/reactivate</c>, against the
/// per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class ReactivateDriverEndpointTests : RegistryIntegrationTestBase
{
    public ReactivateDriverEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly Guid LinkedUser = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, object body) =>
        client.PostAsJsonAsync($"/api/registry/drivers/{id}/reactivate", body, TestJson.Options, Ct);

    /// <summary>A driver in the status the action starts from.</summary>
    private Task<Driver> SeedAsync() => SeedWithStatusAsync(deactivated: true);

    private async Task<Driver> SeedWithStatusAsync(bool deactivated)
    {
        await using var db = CreateDbContext();
        return await SeedDriverAsync(db, userId: LinkedUser, deactivated: deactivated);
    }

    private async Task<Driver> LoadAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Drivers.AsNoTracking().SingleAsync(c => c.Id == new DriverId(id), Ct);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task MatchingVersion_Returns204AndMovesTheStatusAndNothingElse()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Status.ShouldBe(DriverStatus.Active);
        stored.Version.ShouldBe(seeded.Version + 1);
        stored.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        stored.FirstName.ShouldBe(seeded.FirstName);
        stored.LastName.ShouldBe(seeded.LastName);
        stored.Phone.ShouldBe(seeded.Phone);
        stored.UserId.ShouldBe(seeded.UserId);
    }

    [Fact]
    public async Task SameVersionSentTwice_SecondCallIsRefusedAsStale()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);

        // Act
        var first = await PostAsync(client, seeded.Id.Value, new { version = seeded.Version });
        var second = await PostAsync(client, seeded.Id.Value, new { version = seeded.Version });

        // Assert
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(seeded.Version + 1);
    }

    [Fact]
    public async Task StaleVersionOnADriverAlreadyInTheTargetStatus_Returns409AndChangesNothing()
    {
        // Arrange: the client read the driver, then someone else already moved it to the target status.
        var seeded = await SeedAsync();
        var staleVersion = seeded.Version;
        await using (var db = CreateDbContext())
        {
            var other = await db.Drivers.SingleAsync(Ct);
            other.Reactivate(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = staleVersion });

        // Assert
        (await DetailAsync(response)).ShouldBe("L'autista è stato modificato da un altro utente. Ricarica la lista e riprova.");
        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
        var stored = await LoadAsync(seeded.Id.Value);
        stored.Status.ShouldBe(DriverStatus.Active);
        stored.Version.ShouldBe(staleVersion + 1);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task VersionAheadOfTheStoredOne_IsRefused()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version + 7 });

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, DriverErrors.ConcurrentUpdate, Ct);
        (await LoadAsync(seeded.Id.Value)).Status.ShouldBe(DriverStatus.Deactivated);
    }

    [Fact]
    public async Task MissingVersion_Returns400OnVersion_AndChangesNothing()
    {
        // Arrange
        var seeded = await SeedAsync();
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new Dictionary<string, object?>());

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["Version"]);
        (await LoadAsync(seeded.Id.Value)).Status.ShouldBe(DriverStatus.Deactivated);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task UnknownDriver_Returns404()
    {
        await using var factory = CreateFactory();

        var response = await PostAsync(BackOfficeClient(factory), Guid.NewGuid(), new { version = 0 });

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, DriverErrors.NotFound, Ct);
    }

    [Fact]
    public async Task DriverAlreadyInTheTargetStatusWithMatchingVersion_Returns204WithoutAuditOrVersionBump()
    {
        // Arrange
        var seeded = await SeedWithStatusAsync(deactivated: false);
        var auditBefore = await AuditCountAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoadAsync(seeded.Id.Value)).Version.ShouldBe(seeded.Version);
        (await AuditCountAsync()).ShouldBe(auditBefore);
    }

    [Fact]
    public async Task Success_WritesOneAuditRowWithTheEventTypeAndTheDriverId()
    {
        // Arrange
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), seeded.Id.Value, new { version = seeded.Version });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = CreateDbContext();
        var rows = await db.AuditLog
            .Where(a => a.SubjectId == seeded.Id.Value && a.EventType == AuditEventTypes.DriverReactivated)
            .ToListAsync(Ct);
        var row = rows.ShouldHaveSingleItem();
        row.EventType.ShouldBe("registry.driver.reactivated");
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("driverId").GetGuid().ShouldBe(seeded.Id.Value);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.NoContent)]
    [InlineData("Admin", HttpStatusCode.NoContent)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Reactivate_IsAllowedToBackOfficeAndAdminAndForbiddenToDriver(string role, HttpStatusCode expected)
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        var response = await PostAsync(client, seeded.Id.Value, new { version = seeded.Version });

        response.StatusCode.ShouldBe(expected);
        (await LoadAsync(seeded.Id.Value)).Status.ShouldBe(
            expected == HttpStatusCode.NoContent ? DriverStatus.Active : DriverStatus.Deactivated);
    }

    [Fact]
    public async Task Reactivate_AnonymousCaller_Returns401()
    {
        var seeded = await SeedAsync();
        await using var factory = CreateFactory();

        var response = await PostAsync(factory.CreateClient(), seeded.Id.Value, new { version = seeded.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
