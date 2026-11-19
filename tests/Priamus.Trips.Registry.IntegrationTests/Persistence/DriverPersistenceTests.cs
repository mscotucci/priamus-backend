using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Persistence;

/// <summary>
/// Persistence of drivers against the per-test PostgreSQL database: the mapping, the unique index
/// on the linked user and the audit rows written in the same save.
/// </summary>
public sealed class DriverPersistenceTests : RegistryIntegrationTestBase
{
    private static readonly UserId AccountA = new(new Guid("b0000000-0000-0000-0000-00000000000a"));

    public DriverPersistenceTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Driver NewDriver(
        string firstName = "Luca",
        string lastName = "Di Febo",
        string? phone = "333 418 2291",
        UserId? userId = null) =>
        Driver.Create(DriverId.New(), firstName, lastName, phone, userId, new SystemClock(), FakeCurrentUser.ActorId);

    private async Task<Driver> SeedDriverAsync(UserId? userId = null)
    {
        await using var db = CreateDbContext();
        var driver = NewDriver(userId: userId);
        db.Drivers.Add(driver);
        await db.SaveChangesAsync(Ct);
        return driver;
    }

    [Fact]
    public async Task Driver_RoundTripsThroughTheDatabase()
    {
        // Arrange
        var driver = await SeedDriverAsync(AccountA);

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Drivers.SingleAsync(d => d.Id == driver.Id, Ct);

        // Assert
        loaded.FirstName.ShouldBe("Luca");
        loaded.LastName.ShouldBe("Di Febo");
        loaded.Phone.ShouldBe("333 418 2291");
        loaded.UserId.ShouldBe(AccountA);
        loaded.Status.ShouldBe(DriverStatus.Active);
        loaded.CreatedBy.ShouldBe(FakeCurrentUser.ActorId);
        loaded.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        loaded.Version.ShouldBe(0);
    }

    [Fact]
    public async Task Driver_WithoutPhoneAndAccount_RoundTripsAsNull()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            db.Drivers.Add(NewDriver(phone: null));
            await db.SaveChangesAsync(Ct);
        }

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Drivers.SingleAsync(Ct);

        // Assert
        loaded.Phone.ShouldBeNull();
        loaded.UserId.ShouldBeNull();
    }

    [Fact]
    public async Task SeveralDriversWithoutAccount_AreAccepted()
    {
        // Arrange
        await SeedDriverAsync();
        await SeedDriverAsync();

        // Act
        await using var db = CreateDbContext();

        // Assert
        (await db.Drivers.CountAsync(d => d.UserId == null, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task SecondDriverWithTheSameAccount_IsRefusedByTheUniqueIndex()
    {
        // Arrange
        await SeedDriverAsync(AccountA);
        await using var db = CreateDbContext();
        db.Drivers.Add(NewDriver(firstName: "Mario", userId: AccountA));

        // Act
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        // Assert
        var pg = exception.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("uq_drivers_user_id");
        await using var read = CreateDbContext();
        (await read.Drivers.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task DriverLifecycle_WritesOneAuditRowPerEventWithTheEventPayload()
    {
        // Arrange
        var driver = NewDriver(userId: AccountA);
        await using (var db = CreateDbContext())
        {
            db.Drivers.Add(driver);
            await db.SaveChangesAsync(Ct);
        }

        // Act
        await ChangeAsync(driver.Id, d => d.Update("Luca", "Di Febo", null, null, new SystemClock(), FakeCurrentUser.ActorId));
        await ChangeAsync(driver.Id, d => d.Deactivate(new SystemClock(), FakeCurrentUser.ActorId));
        await ChangeAsync(driver.Id, d => d.Reactivate(new SystemClock(), FakeCurrentUser.ActorId));

        // Assert
        await using var read = CreateDbContext();
        string[] lifecycle =
        [
            AuditEventTypes.DriverCreated,
            AuditEventTypes.DriverUpdated,
            AuditEventTypes.DriverDeactivated,
            AuditEventTypes.DriverReactivated,
        ];
        var rows = (await read.AuditLog.Where(a => a.SubjectId == driver.Id.Value).ToListAsync(Ct))
            .OrderBy(a => a.OccurredAt)
            .ThenBy(a => Array.IndexOf(lifecycle, a.EventType))
            .ToList();
        rows.Select(r => r.EventType).ShouldBe(lifecycle);
        rows.ShouldAllBe(r => r.ActorId == FakeCurrentUser.ActorId.Value);

        var created = JsonDocument.Parse(rows[0].Payload).RootElement;
        created.GetProperty("driverId").GetGuid().ShouldBe(driver.Id.Value);
        created.GetProperty("firstName").GetString().ShouldBe("Luca");
        created.GetProperty("lastName").GetString().ShouldBe("Di Febo");
        created.GetProperty("phone").GetString().ShouldBe("333 418 2291");
        created.GetProperty("userId").GetGuid().ShouldBe(AccountA.Value);
        created.GetProperty("status").GetString().ShouldBe("Active");

        var updated = JsonDocument.Parse(rows[1].Payload).RootElement;
        updated.GetProperty("firstName").GetString().ShouldBe("Luca");
        updated.GetProperty("status").GetString().ShouldBe("Active");
        updated.TryGetProperty("phone", out _).ShouldBeFalse();
        updated.TryGetProperty("userId", out _).ShouldBeFalse();

        foreach (var row in rows.Skip(2))
        {
            var payload = JsonDocument.Parse(row.Payload).RootElement;
            payload.EnumerateObject().Select(p => p.Name).ShouldBe(["driverId"]);
            payload.GetProperty("driverId").GetGuid().ShouldBe(driver.Id.Value);
        }
    }

    [Fact]
    public async Task RefusedSave_WritesNoAuditRow()
    {
        // Arrange
        await SeedDriverAsync(AccountA);
        await using var db = CreateDbContext();
        var rejected = NewDriver(firstName: "Mario", userId: AccountA);
        db.Drivers.Add(rejected);

        // Act
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        // Assert
        await using var read = CreateDbContext();
        (await read.AuditLog.CountAsync(a => a.SubjectId == rejected.Id.Value, Ct)).ShouldBe(0);
    }

    private async Task ChangeAsync(DriverId id, Action<Driver> change)
    {
        await using var db = CreateDbContext();
        var driver = await db.Drivers.SingleAsync(d => d.Id == id, Ct);
        change(driver);
        await db.SaveChangesAsync(Ct);
    }
}
