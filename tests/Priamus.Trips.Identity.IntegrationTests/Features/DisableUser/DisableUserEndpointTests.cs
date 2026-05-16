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
using Priamus.Trips.Identity.Features.DisableUser;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.DisableUser;

/// <summary>
/// Integration tests for <c>POST /api/users/{id:guid}/disable</c> (endpoint slice) and
/// the <see cref="DisableUserHandler"/> (handler slice).
///
/// Each test method that needs a real HTTP layer uses a <see cref="WebApplicationFactory{TEntryPoint}"/>;
/// handler-level tests interact with the handler directly through a real
/// <see cref="IdentityDbContext"/> backed by the per-test Postgres database.
/// </summary>
public sealed class DisableUserEndpointTests : IdentityIntegrationTestBase
{
    private const string TestSigningKey = "test-signing-key-endpoint-32chars!!";
    private const string TestIssuer    = "test-issuer";
    private const string TestAudience  = "test-audience";

    private static readonly DateTime FixedNow =
        new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    public DisableUserEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ── Factory helpers ───────────────────────────────────────────────────────

    private WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Identity", ConnectionString);
                builder.UseSetting("Jwt:Issuer",     TestIssuer);
                builder.UseSetting("Jwt:Audience",   TestAudience);
                builder.UseSetting("Jwt:SigningKey",  TestSigningKey);
                builder.UseSetting("Jwt:AccessTokenLifetime",  "01:00:00");
                builder.UseSetting("Jwt:RefreshTokenLifetime", "12:00:00");

                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }

    /// <summary>
    /// Seeds a user with the given <paramref name="role"/> and a BCrypt hash of
    /// <paramref name="plainPassword"/> into the isolated test database.
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
            // Clear domain events so the seeding itself does not create audit rows
            // that would interfere with audit-count assertions.
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

    /// <summary>
    /// Calls <c>POST /api/auth/login</c> and returns the <see cref="LoginResponse"/>.
    /// Asserts 200 OK so that test failures are surfaced clearly.
    /// </summary>
    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken ct)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(username, password), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK,
            $"Login must succeed for user '{username}' to obtain a JWT for this test.");
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();
        return body;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handler-level integration tests (real DB, no HTTP layer)
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Handler Test 1: Happy path ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WithExistingEnabledUserAndDistinctActor_ReturnsSuccessAndDisablesUser()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: scope #1 — seed a distinct (non-actor) user
        var targetUserId = UserId.New();
        await using (var arrangeScope = CreateAsyncScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = TestDataBuilders.AUser().WithId(targetUserId).Build();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Act: scope #2 — invoke the handler
        await using (var actScope = CreateAsyncScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var handler = new DisableUserHandler(
                db,
                new FakeCurrentUser(), // actor != targetUserId
                new FixedClockStub(FixedNow),
                NullLogger<DisableUserHandler>.Instance);

            var result = await handler.HandleAsync(targetUserId.Value, ct);

            // Assert inside Act scope: Result is success
            result.IsSuccess.ShouldBeTrue(
                "The handler must return success when the target user exists and is active.");
        }

        // Assert: scope #3 — verify persisted state
        await using (var assertScope = CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
            user.ShouldNotBeNull();
            user.Status.ShouldBe(UserStatus.Disabled,
                "User.Status must be Disabled after a successful disable operation.");

            // Verify audit log row
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.SubjectId == targetUserId.Value
                         && a.EventType == AuditEventTypes.UserDisabled)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1,
                "Exactly one UserDisabled audit row must be written on the first disable.");

            var auditRow = auditRows[0];
            auditRow.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value,
                "actorId must equal the current user's UserId.");
            auditRow.SubjectId.ShouldBe(targetUserId.Value,
                "subjectId must equal the disabled user's Id.");
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
        var handler = new DisableUserHandler(
            db,
            new FakeCurrentUser(),
            new FixedClockStub(FixedNow),
            NullLogger<DisableUserHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(unknownId, ct);

        // Assert
        result.IsFailure.ShouldBeTrue(
            "The handler must return failure when no user matches the given id.");
        result.Error.ShouldBe(UserErrors.NotFound,
            "The error must be UserErrors.NotFound for an unknown user id.");
    }

    // ── Handler Test 3: Idempotent — already disabled ─────────────────────────

    [Fact]
    public async Task HandleAsync_WhenUserAlreadyDisabled_ReturnsSuccessAndWritesNoNewAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: scope #1 — seed a user that is already disabled.
        // The seeding explicitly calls User.Disable then ClearDomainEvents before
        // SaveChangesAsync so that no audit row is written during setup.
        var targetUserId = UserId.New();

        await using (var arrangeScope = CreateAsyncScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = TestDataBuilders.AUser().WithId(targetUserId).Build();
            user.Disable(new SystemClock(), TestDataBuilders.ActorId);
            user.ClearDomainEvents(); // suppress audit row from seeding
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Confirm zero audit rows for this subject before the Act phase.
        await using (var preActScope = CreateAsyncScope())
        {
            var db = preActScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var preActCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUserId.Value
                              && a.EventType == AuditEventTypes.UserDisabled, ct);
            preActCount.ShouldBe(0,
                "Precondition: no audit rows must exist for this subject before the idempotent call.");
        }

        // Act: scope #2 — invoke the handler on an already-disabled user
        Result actResult;
        await using (var actScope = CreateAsyncScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var handler = new DisableUserHandler(
                db,
                new FakeCurrentUser(),
                new FixedClockStub(FixedNow),
                NullLogger<DisableUserHandler>.Instance);

            actResult = await handler.HandleAsync(targetUserId.Value, ct);
        }

        // Assert: scope #3
        actResult.IsSuccess.ShouldBeTrue(
            "The handler must return success even when the user is already disabled (idempotent).");

        await using (var assertScope = CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var postActCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUserId.Value
                              && a.EventType == AuditEventTypes.UserDisabled, ct);

            postActCount.ShouldBe(0,
                "No new audit row must be written when Disable is called on an already-disabled user " +
                "(idempotent no-op: domain method raises no event, AuditInterceptor writes no row).");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Endpoint integration tests (full HTTP layer via WebApplicationFactory)
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Endpoint Test 1: 204 happy path ──────────────────────────────────────

    [Fact]
    public async Task PostDisable_WithAdminJwtAndExistingEnabledUser_Returns204AndPersistsDisabledStatus()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) =
            await SeedUserAsync(UserRole.Admin, "Adm1n!Pass1", ct: ct);
        var (targetUser, _) =
            await SeedUserAsync(UserRole.Driver, "Driv3r!Pass1", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Act
        var response = await client.PostAsync(
            $"/api/users/{targetUser.Id.Value:D}/disable", content: null, ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent,
            "A successful disable must return 204 No Content.");

        // Assert — DB state in a fresh scope
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var persisted = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == targetUser.Id, ct);
            persisted.ShouldNotBeNull();
            persisted.Status.ShouldBe(UserStatus.Disabled,
                "User.Status must be Disabled after a successful disable.");

            // Assert — exactly one new audit row for UserDisabled
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.SubjectId == targetUser.Id.Value
                         && a.EventType == AuditEventTypes.UserDisabled)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1,
                "Exactly one UserDisabled audit row must be written on a real status transition.");

            var auditRow = auditRows[0];
            auditRow.SubjectId.ShouldBe(targetUser.Id.Value);
            // actor_id comes from the JWT claim resolved by the real ICurrentUser implementation.
            auditRow.ActorId.ShouldNotBeNull(
                "actor_id must be populated from the authenticated admin's JWT claim.");
            auditRow.ActorId!.Value.ShouldBe(adminUser.Id.Value,
                "actor_id must equal the admin who sent the request.");
        }
    }

    // ── Endpoint Test 2: 204 idempotent — no new audit row ───────────────────

    [Fact]
    public async Task PostDisable_WhenUserAlreadyDisabled_Returns204AndWritesNoNewAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed an admin and a target that is already disabled (no audit row for it)
        var (adminUser, adminPassword) =
            await SeedUserAsync(UserRole.Admin, "Adm1n!Pass2", ct: ct);
        var (targetUser, _) =
            await SeedUserAsync(UserRole.Driver, "Driv3r!Pass2", disabled: true, ct: ct);

        // Verify precondition: no UserDisabled audit row exists for the target.
        await using (var preScope = CreateAsyncScope())
        {
            var db = preScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var preCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUser.Id.Value
                              && a.EventType == AuditEventTypes.UserDisabled, ct);
            preCount.ShouldBe(0,
                "Precondition: the seeded disabled user must have no UserDisabled audit row.");
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Act
        var response = await client.PostAsync(
            $"/api/users/{targetUser.Id.Value:D}/disable", content: null, ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent,
            "Disabling an already-disabled user must still return 204 (idempotent).");

        // Assert — no new audit row written
        await using (var assertScope = CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var postCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.SubjectId == targetUser.Id.Value
                              && a.EventType == AuditEventTypes.UserDisabled, ct);

            postCount.ShouldBe(0,
                "No UserDisabled audit row must be written on an idempotent no-op disable. " +
                "This is a regression-sensitive guarantee: if User.Disable emits an event " +
                "on already-disabled users, this count will be 1 instead of 0.");
        }
    }

    // ── Endpoint Test 3: 409 self-disable ─────────────────────────────────────

    [Fact]
    public async Task PostDisable_WhenAdminTriesToDisableOwnAccount_Returns409WithCannotDisableSelfCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed the admin; the disable target IS the admin themselves
        var (adminUser, adminPassword) =
            await SeedUserAsync(UserRole.Admin, "Adm1n!Pass3", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Act — disable with the admin's own id
        var response = await client.PostAsync(
            $"/api/users/{adminUser.Id.Value:D}/disable", content: null, ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict,
            "Self-disable must return 409 Conflict.");

        // Assert — ProblemDetails carries the correct errorCode
        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
            .ShouldBe(UserErrors.CannotDisableSelf.Code,
                "The ProblemDetails errorCode must be UserErrors.CannotDisableSelf.Code.");
    }

    // ── Endpoint Test 4: 404 unknown id ──────────────────────────────────────

    [Fact]
    public async Task PostDisable_WhenUserIdDoesNotExist_Returns404WithNotFoundCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) =
            await SeedUserAsync(UserRole.Admin, "Adm1n!Pass4", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.PostAsync(
            $"/api/users/{nonExistentId:D}/disable", content: null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound,
            "A disable request for an unknown user id must return 404 Not Found.");

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
            .ShouldBe(UserErrors.NotFound.Code,
                "The ProblemDetails errorCode must be UserErrors.NotFound.Code.");
    }

    // ── Endpoint Test 5: 401 unauthenticated ─────────────────────────────────

    [Fact]
    public async Task PostDisable_WithNoAuthorizationHeader_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var anyId = Guid.NewGuid();

        // Act — no Authorization header
        var response = await client.PostAsync(
            $"/api/users/{anyId:D}/disable", content: null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "Requests without a JWT must be rejected with 401.");
    }

    // ── Endpoint Test 6: 403 Driver JWT ──────────────────────────────────────

    [Fact]
    public async Task PostDisable_WithDriverJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: Driver role does not satisfy the AdminOnly policy
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
            $"/api/users/{anyId:D}/disable", content: null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "A Driver JWT must not satisfy the AdminOnly policy.");
    }

    // ── Endpoint Test 7: 403 BackOffice JWT ──────────────────────────────────

    [Fact]
    public async Task PostDisable_WithBackOfficeJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: BackOffice role does not satisfy the AdminOnly policy
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
            $"/api/users/{anyId:D}/disable", content: null, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "A BackOffice JWT must not satisfy the AdminOnly policy.");
    }
}
