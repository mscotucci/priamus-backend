using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.EnableUser;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.EnableUser;

/// <summary>
/// Integration tests for <c>POST /api/identity/users/{id:guid}/enable</c> (endpoint slice) and
/// the <see cref="EnableUserHandler"/> (handler slice).
///
/// Handler-level tests interact directly with the handler through a real
/// <see cref="IdentityDbContext"/> backed by the per-test PostgreSQL database.
/// Endpoint-level tests drive the full HTTP stack via <see cref="WebApplicationFactory{TEntryPoint}"/>.
/// </summary>
public sealed class EnableUserEndpointTests : IdentityIntegrationTestBase
{

    private static readonly DateTime FixedNow =
        new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    public EnableUserEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ── Factory helpers ───────────────────────────────────────────────────────

    private WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return ApiFactory.Create(ConnectionString, configureServices);
    }

    /// <summary>
    /// Seeds a user with the given <paramref name="role"/> and a BCrypt hash of
    /// <paramref name="plainPassword"/> into the isolated test database.
    /// Pass <paramref name="disabled"/> = <c>true</c> to seed the user in the Disabled
    /// state with no audit rows (domain events are cleared before <c>SaveChangesAsync</c>).
    /// </summary>
    private async Task<(User User, string PlainPassword)> SeedUserAsync(
        UserRole role,
        string plainPassword,
        string? username = null,
        bool disabled = false,
        CancellationToken ct = default)
    {
        var passwordHasher = new PasswordHasher<User>();
        var stub = username is not null
            ? TestDataBuilders.AUser().WithRole(role).WithUsername(username).Build()
            : TestDataBuilders.AUser().WithRole(role).Build();
        var hashString = passwordHasher.HashPassword(stub, plainPassword);

        var user = User.Create(
            stub.Id,
            Username.Create(stub.Username.Value),
            PasswordHash.Create(hashString),
            role,
            new SystemClock(),
            TestDataBuilders.ActorId);

        if (disabled)
        {
            user.Disable(new SystemClock(), TestDataBuilders.ActorId);
            // Clear domain events so that seeding does not produce audit rows that
            // would interfere with audit-count assertions in the tests.
            user.ClearDomainEvents();
        }

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        return (user, plainPassword);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handler-level integration tests (real DB, no HTTP layer)
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Handler Test 1: Happy path ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WithExistingDisabledUser_ReturnsSuccessAndEnablesUser()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: scope #1 — seed a disabled user (no audit row from seeding)
        var targetUserId = UserId.New();
        int targetVersion;
        await using (var arrangeScope = CreateAsyncScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = TestDataBuilders.AUser().WithId(targetUserId).Build();
            user.Disable(new SystemClock(), TestDataBuilders.ActorId);
            targetVersion = user.Version;
            user.ClearDomainEvents(); // suppress seeding audit rows
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Act: scope #2 — invoke the handler
        Result actResult;
        await using (var actScope = CreateAsyncScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var handler = new EnableUserHandler(
                db,
                new FakeCurrentUser(),
                new FixedClockStub(FixedNow),
                NullLogger<EnableUserHandler>.Instance);

            actResult = await handler.HandleAsync(targetUserId.Value, new EnableUserRequest(targetVersion), ct);
        }

        // Assert: success result
        actResult.IsSuccess.ShouldBeTrue(
            "The handler must return success when the target user exists and is disabled.");

        // Assert: scope #3 — verify persisted state and audit log
        await using (var assertScope = CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
            user.ShouldNotBeNull();
            user.Status.ShouldBe(UserStatus.Active,
                "User.Status must be Active after a successful enable operation.");

            // Exactly one UserEnabled audit row must be present
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.SubjectId == targetUserId.Value
                         && a.EventType == AuditEventTypes.UserEnabled)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1,
                "Exactly one UserEnabled audit row must be written on the first enable.");

            var auditRow = auditRows[0];
            auditRow.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value,
                "actor_id must equal the current user's UserId.");
            auditRow.SubjectId.ShouldBe(targetUserId.Value,
                "subject_id must equal the enabled user's Id.");
        }
    }

    // ── Handler Test 2: Not found ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenUserDoesNotExist_ReturnsNotFoundError()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var unknownId = Guid.NewGuid(); // never persisted

        await using var actScope = CreateAsyncScope();
        var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = new EnableUserHandler(
            db,
            new FakeCurrentUser(),
            new FixedClockStub(FixedNow),
            NullLogger<EnableUserHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(unknownId, new EnableUserRequest(1), ct);

        // Assert
        result.IsFailure.ShouldBeTrue(
            "The handler must return failure when no user matches the given id.");
        result.Error.ShouldBe(UserErrors.NotFound,
            "The error must be UserErrors.NotFound for an unknown user id.");
    }

    // ── Handler Test 3: Idempotent — already enabled ──────────────────────────

    [Fact]
    public async Task HandleAsync_WhenUserAlreadyEnabled_ReturnsSuccessAndWritesNoNewAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: scope #1 — seed a user that is already Active (factory default).
        // Clear events before saving so that the UserCreated audit row does not
        // inflate the count we assert against for UserEnabled.
        var targetUserId = UserId.New();
        int targetVersion;

        await using (var arrangeScope = CreateAsyncScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = TestDataBuilders.AUser().WithId(targetUserId).Build();
            targetVersion = user.Version;
            user.ClearDomainEvents(); // suppress UserCreated audit row from seeding
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Confirm zero UserEnabled audit rows for this subject before the Act phase.
        await using (var preActScope = CreateAsyncScope())
        {
            var db = preActScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var preActCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUserId.Value
                              && a.EventType == AuditEventTypes.UserEnabled, ct);
            preActCount.ShouldBe(0,
                "Precondition: no UserEnabled audit rows must exist for this subject before the idempotent call.");
        }

        // Act: scope #2 — invoke the handler on an already-enabled user
        Result actResult;
        await using (var actScope = CreateAsyncScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var handler = new EnableUserHandler(
                db,
                new FakeCurrentUser(),
                new FixedClockStub(FixedNow),
                NullLogger<EnableUserHandler>.Instance);

            actResult = await handler.HandleAsync(targetUserId.Value, new EnableUserRequest(targetVersion), ct);
        }

        // Assert: scope #3
        actResult.IsSuccess.ShouldBeTrue(
            "The handler must return success even when the user is already enabled (idempotent).");

        await using (var assertScope = CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var postActCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUserId.Value
                              && a.EventType == AuditEventTypes.UserEnabled, ct);

            postActCount.ShouldBe(0,
                "No new UserEnabled audit row must be written when Enable is called on an already-active user " +
                "(idempotent no-op: domain method raises no event, AuditInterceptor writes no row).");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Endpoint integration tests (full HTTP layer via WebApplicationFactory)
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Endpoint Test 1: 204 happy path ──────────────────────────────────────

    // With the current version the status changes, the version increases and one audit row is written.
    [Fact]
    public async Task PostEnable_WithAdminJwtAndExistingDisabledUser_Returns204AndPersistsEnabledStatus()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed an admin (to authenticate) and a disabled Driver (to enable)
        var (adminUser, adminPassword) =
            await SeedUserAsync(UserRole.Admin, "Adm1n!Pass1", ct: ct);
        var (targetUser, _) =
            await SeedUserAsync(UserRole.Driver, "Driv3r!Pass1", disabled: true, ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/identity/users/{targetUser.Id.Value:D}/enable", new EnableUserRequest(targetUser.Version), ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent,
            "A successful enable must return 204 No Content.");

        // Assert — DB state and audit log in a fresh scope
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var persisted = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == targetUser.Id, ct);
            persisted.ShouldNotBeNull();
            persisted.Status.ShouldBe(UserStatus.Active,
                "User.Status must be Active after a successful enable.");
            persisted.Version.ShouldBe(targetUser.Version + 1,
                "An enable with the current version must increase the version.");

            // Exactly one new audit row for UserEnabled
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.SubjectId == targetUser.Id.Value
                         && a.EventType == AuditEventTypes.UserEnabled)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1,
                "Exactly one UserEnabled audit row must be written on a real status transition.");

            var auditRow = auditRows[0];
            auditRow.SubjectId.ShouldBe(targetUser.Id.Value,
                "subject_id must equal the enabled user's Id.");
            auditRow.ActorId.ShouldNotBeNull(
                "actor_id must be populated from the authenticated admin's JWT claim.");
            auditRow.ActorId!.Value.ShouldBe(adminUser.Id.Value,
                "actor_id must equal the admin who sent the request.");
        }
    }

    // ── Endpoint Test 2: 204 idempotent — no new audit row ───────────────────

    [Fact]
    public async Task PostEnable_WhenUserAlreadyEnabledWithMatchingVersion_Returns204AndWritesNoNewAuditRowOrVersionBump()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed an admin and an already-Active target (no UserEnabled audit row)
        var (adminUser, adminPassword) =
            await SeedUserAsync(UserRole.Admin, "Adm1n!Pass2", ct: ct);
        var (targetUser, _) =
            await SeedUserAsync(UserRole.Driver, "Driv3r!Pass2", ct: ct); // Active by default

        // Verify precondition: no UserEnabled audit row exists for the target.
        await using (var preScope = CreateAsyncScope())
        {
            var db = preScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var preCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUser.Id.Value
                              && a.EventType == AuditEventTypes.UserEnabled, ct);
            preCount.ShouldBe(0,
                "Precondition: the seeded active user must have no UserEnabled audit row.");
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/identity/users/{targetUser.Id.Value:D}/enable", new EnableUserRequest(targetUser.Version), ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent,
            "Enabling an already-active user must still return 204 (idempotent).");

        // Assert — no new audit row written
        await using (var assertScope = CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var postCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUser.Id.Value
                              && a.EventType == AuditEventTypes.UserEnabled, ct);

            var persisted = await db.Users.AsNoTracking()
                .FirstAsync(u => u.Id == targetUser.Id, ct);
            persisted.Version.ShouldBe(targetUser.Version,
                "A matching-version no-op enable must not bump the version.");

            postCount.ShouldBe(0,
                "No UserEnabled audit row must be written on an idempotent no-op enable. " +
                "This is a regression-sensitive guarantee: if User.Enable emits an event " +
                "on already-active users, this count will be 1 instead of 0.");
        }
    }

    // ── Endpoint Test 3: 404 unknown id ──────────────────────────────────────

    [Fact]
    public async Task PostEnable_WhenUserIdDoesNotExist_Returns404WithNotFoundCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) =
            await SeedUserAsync(UserRole.Admin, "Adm1n!Pass3", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/identity/users/{nonExistentId:D}/enable", new EnableUserRequest(1), ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound,
            "An enable request for an unknown user id must return 404 Not Found.");

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
            .ShouldBe(UserErrors.NotFound.Code,
                "The ProblemDetails errorCode must be UserErrors.NotFound.Code.");
    }

    // ── Endpoint Test 4: 401 unauthenticated ─────────────────────────────────

    [Fact]
    public async Task PostEnable_WithNoAuthorizationHeader_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var anyId = Guid.NewGuid();

        // Act — no Authorization header
        var response = await client.PostAsync(
            $"/api/identity/users/{anyId:D}/enable", content: null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "Requests without a JWT must be rejected with 401.");
    }

    // ── Endpoint Test 5: 403 Driver JWT ──────────────────────────────────────

    [Fact]
    public async Task PostEnable_WithDriverJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: Driver role does not satisfy the Admin policy
        var (driverUser, driverPassword) =
            await SeedUserAsync(UserRole.Driver, "Driv3r!Pass3", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, driverUser.Username.Value, driverPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var anyId = Guid.NewGuid();

        // Act
        var response = await client.PostAsync(
            $"/api/identity/users/{anyId:D}/enable", content: null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "A Driver JWT must not satisfy the Admin policy.");
    }

    // ── Endpoint Test 6: 403 BackOffice JWT ──────────────────────────────────

    [Fact]
    public async Task PostEnable_WithBackOfficeJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: BackOffice role does not satisfy the Admin policy
        var (backOfficeUser, backOfficePassword) =
            await SeedUserAsync(UserRole.BackOffice, "B4ckOffice!Pass1", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, backOfficeUser.Username.Value, backOfficePassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var anyId = Guid.NewGuid();

        // Act
        var response = await client.PostAsync(
            $"/api/identity/users/{anyId:D}/enable", content: null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "A BackOffice JWT must not satisfy the Admin policy.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Version rules
    // Create takes no version: these rules do not apply to it.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PostEnable_WithTheSameVersionSentTwice_SecondCallIsRefusedAsStale()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass5", ct: ct);
        var (target, _) = await SeedUserAsync(UserRole.Driver, "Driv3r!Pass5", disabled: true, ct: ct);
        await using var factory = CreateFactory();
        var client = await AdminClientAsync(factory, admin, adminPassword, ct);
        var url = $"/api/identity/users/{target.Id.Value:D}/enable";

        var first = await client.PostAsJsonAsync(url, new EnableUserRequest(target.Version), ct);
        var second = await client.PostAsJsonAsync(url, new EnableUserRequest(target.Version), ct);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second, ct)).ShouldBe(UserErrors.ConcurrentUpdate.Code);
        var (stored, audit) = await LoadAsync(target.Id, AuditEventTypes.UserEnabled, ct);
        stored.Version.ShouldBe(target.Version + 1, "Only the first call changes the user.");
        audit.ShouldBe(1, "The refused second call must write no audit row.");
    }

    [Fact]
    public async Task PostEnable_WithStaleVersionOnAUserAlreadyEnabled_Returns409AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass6", ct: ct);
        var (target, _) = await SeedUserAsync(UserRole.Driver, "Driv3r!Pass6", ct: ct);
        await BumpVersionKeepingStatusAsync(target.Id, ct);
        await using var factory = CreateFactory();
        var client = await AdminClientAsync(factory, admin, adminPassword, ct);

        var response = await client.PostAsJsonAsync(
            $"/api/identity/users/{target.Id.Value:D}/enable", new EnableUserRequest(target.Version), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response, ct)).ShouldBe(UserErrors.ConcurrentUpdate.Code);
        var (stored, audit) = await LoadAsync(target.Id, AuditEventTypes.UserEnabled, ct);
        stored.Version.ShouldBe(target.Version + 2);
        stored.Status.ShouldBe(UserStatus.Active);
        audit.ShouldBe(0, "A refused request writes no audit row.");
    }

    [Fact]
    public async Task PostEnable_WithAVersionAheadOfTheStoredOne_Returns409AndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass7", ct: ct);
        var (target, _) = await SeedUserAsync(UserRole.Driver, "Driv3r!Pass7", disabled: true, ct: ct);
        await using var factory = CreateFactory();
        var client = await AdminClientAsync(factory, admin, adminPassword, ct);

        var response = await client.PostAsJsonAsync(
            $"/api/identity/users/{target.Id.Value:D}/enable", new EnableUserRequest(target.Version + 7), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response, ct)).ShouldBe(UserErrors.ConcurrentUpdate.Code);
        var (stored, audit) = await LoadAsync(target.Id, AuditEventTypes.UserEnabled, ct);
        stored.Status.ShouldBe(UserStatus.Disabled);
        stored.Version.ShouldBe(target.Version);
        audit.ShouldBe(0);
    }

    [Fact]
    public async Task PostEnable_WithMissingVersion_Returns400OnVersionAndChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass8", ct: ct);
        var (target, _) = await SeedUserAsync(UserRole.Driver, "Driv3r!Pass8", disabled: true, ct: ct);
        await using var factory = CreateFactory();
        var client = await AdminClientAsync(factory, admin, adminPassword, ct);

        var response = await client.PostAsJsonAsync(
            $"/api/identity/users/{target.Id.Value:D}/enable", new Dictionary<string, object?>(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        doc.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).ToArray()
            .ShouldBe(["Version"]);
        var (stored, audit) = await LoadAsync(target.Id, AuditEventTypes.UserEnabled, ct);
        stored.Status.ShouldBe(UserStatus.Disabled);
        audit.ShouldBe(0);
    }

    [Fact]
    public async Task PostEnable_OnAnUnknownUserWithAStaleVersion_ReturnsNotFoundBeforeTheVersionCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        var (admin, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass10", ct: ct);
        await using var factory = CreateFactory();
        var client = await AdminClientAsync(factory, admin, adminPassword, ct);

        var response = await client.PostAsJsonAsync(
            $"/api/identity/users/{Guid.NewGuid():D}/enable", new EnableUserRequest(99), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(response, ct)).ShouldBe(UserErrors.NotFound.Code);
    }
}
