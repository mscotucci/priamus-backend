using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.CreateUser;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.CreateUser;

/// <summary>
/// Endpoint integration tests for <c>POST /api/identity/users</c>.
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> wired to the per-test PostgreSQL
/// database produced by <see cref="PostgresFixture"/> / <see cref="IdentityIntegrationTestBase"/>.
/// </summary>
public sealed class CreateUserEndpointTests : IdentityIntegrationTestBase
{
    // JWT signing key that satisfies JwtOptions [MinLength(32)] validation.

    public CreateUserEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="WebApplicationFactory{TEntryPoint}"/> whose DI container has:
    /// <list type="bullet">
    ///   <item>the <see cref="IdentityDbContext"/> pointed at this test's isolated database,</item>
    ///   <item>JWT options overridden to deterministic test values,</item>
    ///   <item>any additional DI overrides supplied via <paramref name="configureServices"/>.</item>
    /// </list>
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return ApiFactory.Create(ConnectionString, configureServices);
    }

    /// <summary>
    /// Seeds a user with the specified <paramref name="role"/> and <paramref name="plainPassword"/>
    /// into the isolated test database. Returns the persisted <see cref="User"/> and the
    /// plain-text password used to produce its hash.
    /// </summary>
    private async Task<(User User, string PlainPassword)> SeedUserAsync(
        UserRole role,
        string plainPassword,
        string? username = null,
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

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        return (user, plainPassword);
    }

    /// <summary>
    /// Calls <c>POST /api/identity/auth/login</c> with the given credentials and returns the
    /// <see cref="LoginResponse"/>. Asserts that the response is 200 OK.
    /// </summary>
    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken ct)
    {
        var loginRequest = new LoginRequest(username, password);
        var loginResponse = await client.PostAsJsonAsync("/api/identity/auth/login", loginRequest, ct);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK,
            $"Login must succeed for user '{username}' to obtain a JWT for the test.");
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();
        return body;
    }

    // ── Test 1: 201 happy path ────────────────────────────────────────────────

    [Fact]
    public async Task PostUsers_WithAdminJwtAndValidPayload_Returns201AndPersistsUser()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var request = new CreateUserRequest("brandnewuser", "Str0ng!Pass", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Assert — response body
        var body = await response.Content.ReadFromJsonAsync<CreateUserResponse>(TestJson.Options, ct);
        body.ShouldNotBeNull();
        body.Id.ShouldNotBe(Guid.Empty);
        body.Username.ShouldBe("brandnewuser");
        body.Role.ShouldBe(UserRole.Driver);
        body.Status.ShouldBe(UserStatus.Active);

        // Assert — Location header
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldBe($"/api/identity/users/{body.Id}");

        // Assert — persisted in DB
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var persisted = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == new UserId(body.Id), ct);

            persisted.ShouldNotBeNull();
            persisted.Username.Value.ShouldBe("brandnewuser");
            persisted.PasswordHash.Value.ShouldNotBeNullOrWhiteSpace();
            persisted.Status.ShouldBe(UserStatus.Active);
        }
    }

    // ── Test 2: Audit row written on successful POST ───────────────────────────

    [Fact]
    public async Task PostUsers_OnSuccess_WritesAuditRowWithCorrectEventTypeAndFields()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass2", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var request = new CreateUserRequest("auditloguser", "Str0ng!Pass", UserRole.BackOffice);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<CreateUserResponse>(TestJson.Options, ct);
        body.ShouldNotBeNull();
        var newUserId = body.Id;

        // Assert — audit row in a fresh DI scope
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.SubjectId == newUserId
                         && a.EventType == AuditEventTypes.UserCreated)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1,
                "Exactly one UserCreated audit row must be written per successful user creation.");

            var auditRow = auditRows[0];
            auditRow.ActorId.ShouldBe(adminUser.Id.Value,
                "actorId must equal the authenticated admin caller's UserId.");
            auditRow.SubjectId.ShouldBe(newUserId,
                "subjectId must equal the newly created user's Id.");

            // Payload structural check: 'role' field must be present.
            // Note: UserCreated event does NOT carry 'username' — the event shape contains
            // only EventId, OccurredAt, UserId, and Role. This is a known brief/event-shape
            // mismatch; the orchestrator must decide whether to add Username to the event.
            // The Role enum is serialized as a JSON Number by default System.Text.Json options,
            // so the assertion reads the integer value and compares against (int)UserRole.X.
            using var payloadDoc = JsonDocument.Parse(auditRow.Payload);
            payloadDoc.RootElement.TryGetProperty("role", out var roleElement)
                .ShouldBeTrue("Audit payload must contain the 'role' field.");
            roleElement.GetInt32().ShouldBe((int)UserRole.BackOffice);
        }
    }

    // ── Test 3: 401 — no Authorization header ────────────────────────────────

    [Fact]
    public async Task PostUsers_WithNoAuthorizationHeader_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new CreateUserRequest("someuser", "Str0ng!Pass", UserRole.Driver);

        // Act — no Authorization header set
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ── Test 4: 403 — Driver JWT ──────────────────────────────────────────────

    [Fact]
    public async Task PostUsers_WithDriverJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed a Driver user; Driver role does not satisfy Admin policy
        var (driverUser, driverPassword) = await SeedUserAsync(UserRole.Driver, "Driv3r!Pass", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, driverUser.Username.Value, driverPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var request = new CreateUserRequest("newuser999", "Str0ng!Pass", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ── Test 5: 403 — BackOffice JWT ─────────────────────────────────────────

    [Fact]
    public async Task PostUsers_WithBackOfficeJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed a BackOffice user; BackOffice role does not satisfy Admin policy
        var (backOfficeUser, backOfficePassword) =
            await SeedUserAsync(UserRole.BackOffice, "B4ckOffice!Pass", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, backOfficeUser.Username.Value, backOfficePassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var request = new CreateUserRequest("newuser998", "Str0ng!Pass", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ── Test 6: 409 — username already taken (upfront check path) ────────────

    [Fact]
    public async Task PostUsers_WhenUsernameAlreadyTaken_Returns409WithUsernameAlreadyTakenCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        const string duplicateUsername = "duplicateuser";
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass3", ct: ct);
        // Seed the user whose username will collide
        await SeedUserAsync(UserRole.Driver, "SomePass1!", username: duplicateUsername, ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var request = new CreateUserRequest(duplicateUsername, "Str0ng!Pass", UserRole.BackOffice);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(UserErrors.UsernameAlreadyTaken.Code);
    }

    // ── Test 7: 409 — race-condition path (concurrent calls, unique index violation)

    [Fact]
    public async Task PostUsers_WhenTwoConcurrentRequestsUseTheSameUsername_ExactlyOneSucceedsAndOneReturns409()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass4", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        var adminJwt = loginBody.AccessToken;

        const string racedUsername = "racedendpointuser";

        async Task<HttpResponseMessage> PostAsync()
        {
            // Each concurrent call creates a separate HttpRequestMessage to avoid shared-state issues.
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/identity/users");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            req.Content = JsonContent.Create(
                new CreateUserRequest(racedUsername, "Str0ng!Pass", UserRole.Driver),
                options: TestJson.Options);
            return await client.SendAsync(req, ct);
        }

        // Act: fire both concurrently
        var responses = await Task.WhenAll(PostAsync(), PostAsync());

        // Assert: exactly one 201, one 409
        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => s).ToList();
        statuses.ShouldContain(HttpStatusCode.Created,
            "Exactly one of the concurrent requests must succeed with 201.");
        statuses.ShouldContain(HttpStatusCode.Conflict,
            "Exactly one of the concurrent requests must fail with 409.");

        // Verify the 409 carries the correct errorCode
        var conflictResponse = responses.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var problemJson = await conflictResponse.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(UserErrors.UsernameAlreadyTaken.Code);

        // Verify exactly one user row was persisted
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rowCount = await db.Users.AsNoTracking()
                .CountAsync(u => u.Username == Username.Create(racedUsername), ct);
            rowCount.ShouldBe(1, "Only one user row must exist despite the concurrent requests.");
        }
    }

    // ── Test 8: 422 — password too short ────────────────────────────────────

    [Fact]
    public async Task PostUsers_WithPasswordTooShort_Returns400Or422()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass5", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Password with only 5 chars — below StrongPassword.MinLength (8)
        var request = new CreateUserRequest("validuser01", "Ab1!", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert: [StrongPassword] via [ValidatableType] produces a 400 Bad Request
        // (Microsoft.Extensions.Validation source generator) or 422 if mapped differently.
        // The important assertion is that the request is rejected — not that it succeeds.
        var statusCode = (int)response.StatusCode;
        statusCode.ShouldBeOneOf(
            new[] { StatusCodes.Status400BadRequest, StatusCodes.Status422UnprocessableEntity },
            "A password below MinLength must be rejected by the [StrongPassword] attribute.");
    }

    // ── Test 9: 422/400 — password too long ─────────────────────────────────

    [Fact]
    public async Task PostUsers_WithPasswordTooLong_Returns400Or422()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass6", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // 129-char password — above StrongPassword.MaxLength (128)
        var tooLongPassword = "Abc1!" + new string('x', 124);
        var request = new CreateUserRequest("validuser02", tooLongPassword, UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        var statusCode = (int)response.StatusCode;
        statusCode.ShouldBeOneOf(
            new[] { StatusCodes.Status400BadRequest, StatusCodes.Status422UnprocessableEntity },
            "A password above MaxLength must be rejected by the [StrongPassword] attribute.");
    }

    // ── Test 10: 422/400 — password has no letter ────────────────────────────

    [Fact]
    public async Task PostUsers_WithPasswordHavingNoLetter_Returns400Or422()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass7", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // No letters, only digits and special chars
        var request = new CreateUserRequest("validuser03", "12345678!", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        var statusCode = (int)response.StatusCode;
        statusCode.ShouldBeOneOf(
            new[] { StatusCodes.Status400BadRequest, StatusCodes.Status422UnprocessableEntity },
            "A password with no letter must be rejected by the [StrongPassword] attribute.");
    }

    // ── Test 11: 422/400 — password has no digit ─────────────────────────────

    [Fact]
    public async Task PostUsers_WithPasswordHavingNoDigit_Returns400Or422()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass8", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // No digits, only letters and special chars
        var request = new CreateUserRequest("validuser04", "Abcdefg!", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        var statusCode = (int)response.StatusCode;
        statusCode.ShouldBeOneOf(
            new[] { StatusCodes.Status400BadRequest, StatusCodes.Status422UnprocessableEntity },
            "A password with no digit must be rejected by the [StrongPassword] attribute.");
    }

    // ── Test 12: 422/400 — password has no special character ─────────────────

    [Fact]
    public async Task PostUsers_WithPasswordHavingNoSpecialChar_Returns400Or422()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!Pass9", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // No special characters, only letters and digits
        var request = new CreateUserRequest("validuser05", "Abcde123", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        var statusCode = (int)response.StatusCode;
        statusCode.ShouldBeOneOf(
            new[] { StatusCodes.Status400BadRequest, StatusCodes.Status422UnprocessableEntity },
            "A password with no special char must be rejected by the [StrongPassword] attribute.");
    }

    // ── Test 13: 422 — invalid username shape (starts with '.') ─────────────

    [Fact]
    public async Task PostUsers_WithInvalidUsernameShape_Returns422WithUsernameInvalidCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!PsA", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Leading '.' violates Username invariant — handler returns Error.Domain → 422
        var request = new CreateUserRequest(".invalidstart", "Str0ng!Pass", UserRole.Driver);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/users", request, TestJson.Options, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(UserErrors.UsernameInvalid.Code);
    }

    // ── Test 14: 400 — required field (Role) null, omitted, or sent as an integer ──
    //
    // Role is declared nullable so that an omitted or null value binds to null and
    // [Required] answers 400, instead of binding to the enum's zero value. Enums travel
    // by name only: the host rejects an integer.

    [Theory]
    [InlineData("""{"username":"validuser06","password":"Str0ng!Pass","role":null}""")]
    [InlineData("""{"username":"validuser06","password":"Str0ng!Pass"}""")]
    [InlineData("""{"username":"validuser06","password":"Str0ng!Pass","role":0}""")]
    [InlineData("""{"username":"validuser06","password":"Str0ng!Pass","role":"NotARole"}""")]
    public async Task PostUsers_WithRoleNullOmittedIntegerOrUnknown_Returns400(string incompleteJson)
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.Admin, "Adm1n!PsB", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        var content = new StringContent(
            incompleteJson,
            System.Text.Encoding.UTF8,
            "application/json");

        // Act
        var response = await client.PostAsync("/api/identity/users", content, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}

// Needed to reference StatusCodes constants without a using statement for the HTTP namespace
file static class StatusCodes
{
    public const int Status400BadRequest = 400;
    public const int Status422UnprocessableEntity = 422;
}
