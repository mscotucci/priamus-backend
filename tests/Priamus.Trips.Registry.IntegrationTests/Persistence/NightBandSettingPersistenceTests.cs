using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Persistence;

/// <summary>
/// Persistence of the system night band against the per-test PostgreSQL database: the row seeded
/// by the migration, the update round trip, the concurrency token and the audit rows.
/// </summary>
public sealed class NightBandSettingPersistenceTests : RegistryIntegrationTestBase
{
    public NightBandSettingPersistenceTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NightBand Band(int startHour, int endHour) =>
        NightBand.Create(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)).Value;

    private async Task ChangeAsync(NightBand band)
    {
        await using var db = CreateDbContext();
        var setting = await db.NightBandSettings.SingleAsync(Ct);
        setting.Update(band, new SystemClock(), FakeCurrentUser.ActorId);
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task NightBandSetting_AsSeededByTheMigration_IsTheDefaultBandWithoutAuditRow()
    {
        // Arrange
        await using var db = CreateDbContext();

        // Act
        var setting = await db.NightBandSettings.SingleAsync(Ct);

        // Assert
        setting.Id.ShouldBe(NightBandSetting.SingleId);
        setting.Band.Start.ShouldBe(new TimeOnly(22, 0));
        setting.Band.End.ShouldBe(new TimeOnly(6, 0));
        setting.Version.ShouldBe(0);
        (await db.AuditLog.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Update_WithAnotherBand_RoundTripsAndWritesOneAuditRow()
    {
        // Arrange
        var band = NightBand.Create(new TimeOnly(21, 30), new TimeOnly(5, 15)).Value;

        // Act
        await ChangeAsync(band);

        // Assert
        await using var read = CreateDbContext();
        var setting = await read.NightBandSettings.SingleAsync(Ct);
        setting.Band.ShouldBe(band);
        setting.Version.ShouldBe(1);
        setting.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        var row = (await read.AuditLog.ToListAsync(Ct)).ShouldHaveSingleItem();
        row.EventType.ShouldBe(AuditEventTypes.NightBandUpdated);
        row.SubjectId.ShouldBe(NightBandSetting.SingleId);
        row.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        var payload = JsonDocument.Parse(row.Payload).RootElement;
        payload.GetProperty("start").GetString().ShouldBe("21:30");
        payload.GetProperty("end").GetString().ShouldBe("05:15");
    }

    [Fact]
    public async Task Update_WithTheBandAlreadySaved_WritesNothingAndNoAuditRow()
    {
        // Arrange
        await using var before = CreateDbContext();
        var savedAt = (await before.NightBandSettings.SingleAsync(Ct)).LastModifiedAt;

        // Act
        await ChangeAsync(NightBand.Default);

        // Assert
        await using var read = CreateDbContext();
        var setting = await read.NightBandSettings.SingleAsync(Ct);
        setting.Version.ShouldBe(0);
        setting.LastModifiedAt.ShouldBe(savedAt);
        (await read.AuditLog.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Update_FromAStaleCopy_FailsOnTheConcurrencyToken()
    {
        // Arrange
        await using var stale = CreateDbContext();
        var staleSetting = await stale.NightBandSettings.SingleAsync(Ct);
        await ChangeAsync(Band(21, 5));
        staleSetting.Update(Band(20, 4), new SystemClock(), FakeCurrentUser.ActorId);

        // Act
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync(Ct));

        // Assert
        await using var read = CreateDbContext();
        (await read.NightBandSettings.SingleAsync(Ct)).Band.ShouldBe(Band(21, 5));
    }

    [Fact]
    public async Task NightBandSettings_SecondRow_IsRefusedByTheSingleRowConstraint()
    {
        // Arrange
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO registry.night_band_settings (id, start_time, end_time, last_modified_at, last_modified_by, version) " +
            "VALUES (gen_random_uuid(), '20:00', '04:00', now(), gen_random_uuid(), 0)";

        // Act
        var exception = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        // Assert
        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.ShouldBe("chk_night_band_settings_single_row");
    }
}
