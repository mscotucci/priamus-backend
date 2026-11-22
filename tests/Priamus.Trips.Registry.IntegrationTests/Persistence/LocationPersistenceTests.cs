using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Persistence;

/// <summary>
/// Persistence of locations against the per-test PostgreSQL database: the mapping and the composite
/// unique index on the name and municipality keys.
/// </summary>
public sealed class LocationPersistenceTests : RegistryIntegrationTestBase
{
    public LocationPersistenceTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Location NewLocation(
        string name = "Magazzino centrale",
        string municipality = "Spoltore",
        string? address = "Via Roma 1") =>
        Location.Create(LocationId.New(), name, municipality, address, new SystemClock(), FakeCurrentUser.ActorId);

    private async Task<Location> SeedLocationAsync(string name = "Magazzino centrale", string municipality = "Spoltore")
    {
        await using var db = CreateDbContext();
        var location = NewLocation(name, municipality);
        db.Locations.Add(location);
        await db.SaveChangesAsync(Ct);
        return location;
    }

    [Fact]
    public async Task Location_RoundTripsThroughTheDatabase()
    {
        // Arrange
        var location = await SeedLocationAsync("  Magazzino   centrale ", " Spoltore ");

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Locations.SingleAsync(l => l.Id == location.Id, Ct);

        // Assert
        loaded.Name.ShouldBe("Magazzino   centrale");
        loaded.Municipality.ShouldBe("Spoltore");
        loaded.Address.ShouldBe("Via Roma 1");
        loaded.NameKey.ShouldBe("magazzino centrale");
        loaded.MunicipalityKey.ShouldBe("spoltore");
        loaded.Status.ShouldBe(LocationStatus.Active);
        loaded.CreatedBy.ShouldBe(FakeCurrentUser.ActorId);
        loaded.LastModifiedBy.ShouldBe(FakeCurrentUser.ActorId);
        loaded.Version.ShouldBe(0);
    }

    [Fact]
    public async Task Location_WithoutAddress_RoundTripsAsNull()
    {
        // Arrange
        await using (var db = CreateDbContext())
        {
            db.Locations.Add(NewLocation(address: null));
            await db.SaveChangesAsync(Ct);
        }

        // Act
        await using var read = CreateDbContext();
        var loaded = await read.Locations.SingleAsync(Ct);

        // Assert
        loaded.Address.ShouldBeNull();
    }

    [Fact]
    public async Task SecondLocationWithTheSameKeys_IsRefusedByTheUniqueIndex()
    {
        // Arrange
        await SeedLocationAsync("Magazzino centrale", "Spoltore");
        await using var db = CreateDbContext();
        db.Locations.Add(NewLocation("  MAGAZZINO    Centrale ", "spoltore"));

        // Act
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        // Assert
        var pg = exception.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.ShouldBe("uq_locations_name_municipality_key");
        await using var read = CreateDbContext();
        (await read.Locations.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task SameNameInAnotherMunicipality_IsSaved()
    {
        // Arrange
        await SeedLocationAsync("Magazzino centrale", "Spoltore");

        // Act
        await SeedLocationAsync("Magazzino centrale", "Penne");

        // Assert
        await using var read = CreateDbContext();
        (await read.Locations.CountAsync(l => l.NameKey == "magazzino centrale", Ct)).ShouldBe(2);
    }
}
