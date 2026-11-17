using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests;

/// <summary>
/// Persistence round-trip tests for the <see cref="User"/> aggregate.
/// Per ADR 0009, all tests interact with <see cref="IdentityDbContext"/> directly.
/// Each test phase uses a separate DI scope for a fresh DbContext and empty change tracker.
/// </summary>
public sealed class UserPersistenceTests : IdentityIntegrationTestBase
{
    public UserPersistenceTests(PostgresFixture fixture) : base(fixture) { }
    // ── Basic round-trip ─────────────────────────────────────────────────────

    [Fact]
    public async Task User_RoundTrip_WritesAuditLog()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var userId = UserId.New();
        var username = Username.Create("testuser42");
        var passwordHash = PasswordHash.Create(new string('a', 64));
        var clock = new SystemClock();
        var createdBy = FakeCurrentUser.ActorId;

        var user = User.Create(userId, username, passwordHash, UserRole.Driver, clock, createdBy);

        // Act — save in one scope
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Assert — reload in a fresh scope (no tracking)
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var reloaded = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            reloaded.ShouldNotBeNull();
            reloaded.Username.Value.ShouldBe("testuser42");
            reloaded.Role.ShouldBe(UserRole.Driver);
            reloaded.Status.ShouldBe(UserStatus.Active);

            var auditRows = await db.AuditLog
                .AsNoTracking()
                .Where(e => e.SubjectId == userId.Value)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1);
            auditRows[0].EventType.ShouldBe(AuditEventTypes.UserCreated);
            auditRows[0].SubjectId.ShouldBe(userId.Value);
            auditRows[0].ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
        }
    }

    [Fact]
    public async Task User_RoundTrip_PreservesAllValueObjectContents()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — use distinctive values to confirm columns are not swapped
        var userId = UserId.New();
        var createdBy = FakeCurrentUser.ActorId;
        var hash = new string('z', 64);

        var user = TestDataBuilders.AUser()
            .WithId(userId)
            .WithUsername("roundtrip" + userId.Value.ToString("N")[..6])
            .WithPasswordHash(hash)
            .WithRole(UserRole.Admin)
            .WithCreatedBy(createdBy)
            .Build();

        // Act
        using (var arrangeScope = CreateScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var reloaded = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);

            reloaded.Id.ShouldBe(userId);
            reloaded.PasswordHash.Value.ShouldBe(hash);
            reloaded.Role.ShouldBe(UserRole.Admin);
            reloaded.Status.ShouldBe(UserStatus.Active);
            reloaded.CreatedBy.ShouldBe(createdBy);
            reloaded.LastModifiedBy.ShouldBe(createdBy);
            reloaded.CreatedAt.ShouldBe(user.CreatedAt, TimeSpan.FromSeconds(1));
            reloaded.LastModifiedAt.ShouldBe(user.LastModifiedAt, TimeSpan.FromSeconds(1));
        }
    }

    // ── Enums stored by name ──────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Admin, "Admin")]
    [InlineData(UserRole.BackOffice, "BackOffice")]
    [InlineData(UserRole.Driver, "Driver")]
    public async Task User_RoleAndStatus_AreStoredAsMemberNames(UserRole role, string expectedRole)
    {
        // The raw columns hold the member names, not the enum's integer values.
        var ct = TestContext.Current.CancellationToken;

        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).WithRole(role).Build();
        user.Disable(new SystemClock(), FakeCurrentUser.ActorId);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT role, status FROM identity.users WHERE id = @id";
        cmd.Parameters.AddWithValue("id", userId.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        (await reader.ReadAsync(ct)).ShouldBeTrue();
        reader.GetString(0).ShouldBe(expectedRole);
        reader.GetString(1).ShouldBe("Disabled");
    }

    [Theory]
    [InlineData("role", "1")]
    [InlineData("role", "'Superuser'")]
    [InlineData("status", "1")]
    [InlineData("status", "'Banned'")]
    public async Task Users_RoleOrStatusOutsideTheMemberNames_IsRejectedByTheCheckConstraint(string column, string value)
    {
        // The check constraints are written on the member names.
        var ct = TestContext.Current.CancellationToken;

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var role = column == "role" ? value : "'Driver'";
        var status = column == "status" ? value : "'Active'";
        cmd.CommandText =
            "INSERT INTO identity.users (id, username, password_hash, role, status, created_at, created_by, " +
            "last_modified_at, last_modified_by) VALUES " +
            $"(gen_random_uuid(), 'badenum', 'x', {role}, {status}, now(), gen_random_uuid(), now(), gen_random_uuid())";

        var ex = await Should.ThrowAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync(ct));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    // ── Version round-trip ────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_AfterPersistence_IncrementsVersionInDatabase()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — create and save a user
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();

        using (var arrangeScope = CreateScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Verify initial version
        using (var checkScope = CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var initial = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
            initial.Version.ShouldBe(0);
        }

        // Act — load, mutate, save
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.ChangePassword(PasswordHash.Create(new string('z', 64)), new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Assert — version is 1 in DB
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var updated = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
            updated.Version.ShouldBe(1);
        }
    }

    // ── Unique constraint on username ─────────────────────────────────────────

    [Fact]
    public async Task User_DuplicateUsername_ThrowsDbUpdateException()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — create and save the first user
        var sharedUsername = "dupuser" + Guid.NewGuid().ToString("N")[..6];

        var user1 = TestDataBuilders.AUser().WithUsername(sharedUsername).Build();
        var user2 = TestDataBuilders.AUser().WithUsername(sharedUsername).Build();

        using (var arrangeScope = CreateScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user1);
            await db.SaveChangesAsync(ct);
        }

        // Act — try to save a second user with the same username
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user2);

            var act = async () => await db.SaveChangesAsync(ct);

            // Assert
            await act.ShouldThrowAsync<DbUpdateException>();
        }
    }

    // ── Optimistic concurrency ─────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentUpdate_SecondSave_ThrowsDbUpdateConcurrencyException()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — create and persist a user
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();

        using (var arrangeScope = CreateScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Load the same aggregate in two parallel scopes
        User firstScopeUser;
        User secondScopeUser;
        IdentityDbContext firstScopeDb;
        IdentityDbContext secondScopeDb;

        var firstScope = CreateScope();
        var secondScope = CreateScope();

        firstScopeDb = firstScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        secondScopeDb = secondScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        firstScopeUser = await firstScopeDb.Users.FirstAsync(u => u.Id == userId, ct);
        secondScopeUser = await secondScopeDb.Users.FirstAsync(u => u.Id == userId, ct);

        // Act — first scope wins
        firstScopeUser.ChangePassword(PasswordHash.Create(new string('x', 64)), new SystemClock(), FakeCurrentUser.ActorId);
        await firstScopeDb.SaveChangesAsync(ct);

        // Act — second scope loses (stale version)
        secondScopeUser.ChangePassword(PasswordHash.Create(new string('y', 64)), new SystemClock(), FakeCurrentUser.ActorId);

        var act = async () => await secondScopeDb.SaveChangesAsync(ct);

        // Assert
        await act.ShouldThrowAsync<DbUpdateConcurrencyException>();

        firstScope.Dispose();
        secondScope.Dispose();
    }
}
