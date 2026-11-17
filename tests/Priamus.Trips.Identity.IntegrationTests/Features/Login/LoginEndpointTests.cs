using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.Login;

/// <summary>
/// Endpoint integration tests for <c>POST /api/identity/auth/login</c>.
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> wired to the per-test PostgreSQL
/// database produced by <see cref="PostgresFixture"/> / <see cref="IdentityIntegrationTestBase"/>.
/// </summary>
public sealed class LoginEndpointTests : IdentityIntegrationTestBase
{
    // JWT signing key that satisfies JwtOptions [MinLength(32)] validation.

    public LoginEndpointTests(PostgresFixture fixture) : base(fixture) { }

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
    /// Seeds a user with a known password into the test database and returns both the
    /// persisted user and the plain-text password used to hash it.
    /// </summary>
    private async Task<(User User, string PlainPassword)> SeedUserAsync(
        UserRole role,
        string plainPassword,
        CancellationToken ct)
    {
        var passwordHasher = new PasswordHasher<User>();

        // Build a temporary stub user to get the password hashed.
        var stub = TestDataBuilders.AUser().WithRole(role).Build();
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

    // ── Test 1: Valid credentials → 200 + access token + refresh token ────────

    [Fact]
    public async Task PostLogin_ValidCredentials_Returns200WithBothTokens()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, password) = await SeedUserAsync(UserRole.BackOffice, "Sup3rSecret!", ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new LoginRequest(user.Username.Value, password);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();
        body.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.Length.ShouldBe(43); // base64url, no padding, 32 random bytes
        body.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);
        body.RefreshTokenExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    // A login leaves exactly one audit row, the hand-written logged_in row, and none for the issued token.
    [Fact]
    public async Task PostLogin_ValidCredentials_WritesOnlyTheLoggedInAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, password) = await SeedUserAsync(UserRole.BackOffice, "Sup3rSecret!", ct);
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login", new LoginRequest(user.Username.Value, password), ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var eventTypes = await db.AuditLog.AsNoTracking()
            .Where(a => a.SubjectId == user.Id.Value && a.EventType != AuditEventTypes.UserCreated)
            .Select(a => a.EventType)
            .ToListAsync(ct);
        eventTypes.ShouldBe([AuditEventTypes.UserLoggedIn]);
    }

    // ── Test 2: Wrong password → 401 + InvalidCredentials + LoginFailed audit row

    [Fact]
    public async Task PostLogin_WrongPassword_Returns401WithInvalidCredentialsAndAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, _) = await SeedUserAsync(UserRole.BackOffice, "correct-pwd", ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new LoginRequest(user.Username.Value, "wrong-password");

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", request, ct);

        // Assert — HTTP layer
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        // ProblemDetails extensions are serialized at the root of the JSON object.
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(UserErrors.InvalidCredentials.Code);

        // Assert — audit row written for known-user bad-password case
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.SubjectId == user.Id.Value
                         && a.EventType == AuditEventTypes.LoginFailed)
                .ToListAsync(ct);

            auditRows.ShouldNotBeEmpty();
        }
    }

    // ── Test 3: Unknown username → 401 + InvalidCredentials, NO audit row ─────

    [Fact]
    public async Task PostLogin_UnknownUsername_Returns401WithInvalidCredentialsAndNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — the database is clean (no users with this username).
        // Use a deterministic username that cannot collide with other tests' users
        // because each test runs against its own isolated database clone.
        const string unknownUsername = "this-user-does-not-exist";

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new LoginRequest(unknownUsername, "any-password");

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", request, ct);

        // Assert — HTTP layer: same 401 as wrong password (no username enumeration)
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        // ProblemDetails extensions are serialized at the root of the JSON object.
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(UserErrors.InvalidCredentials.Code);

        // Assert — NO audit row (no subject_id to anchor the row)
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rowCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.LoginFailed, ct);

            // The database is isolated per test, so zero rows of this type is the
            // only valid outcome for the unknown-username path.
            rowCount.ShouldBe(0);
        }
    }

    // ── Test 4: Driver with linked DriverId → JWT carries driver_id claim ─────

    [Fact]
    public async Task PostLogin_DriverUserWithLinkedDriverId_JwtContainsDriverIdClaim()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, password) = await SeedUserAsync(UserRole.Driver, "driver-pwd", ct);

        var expectedDriverId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var driverLookup = Substitute.For<IDriverLookup>();
        driverLookup
            .GetDriverIdByUserIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(expectedDriverId);

        await using var factory = CreateFactory(services =>
        {
            // Replace the IDriverLookup singleton registered in Program.cs.
            services.AddSingleton(driverLookup);
        });

        var client = factory.CreateClient();
        var request = new LoginRequest(user.Username.Value, password);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", request, ct);

        // Assert — HTTP layer
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();

        // Decode the JWT and verify the driver_id claim is present with the expected value.
        var jwtHandler = new JwtSecurityTokenHandler();
        jwtHandler.CanReadToken(body.AccessToken).ShouldBeTrue();
        var token = jwtHandler.ReadJwtToken(body.AccessToken);

        var driverIdClaim = token.Claims.FirstOrDefault(c => c.Type == "driver_id");
        driverIdClaim.ShouldNotBeNull("Expected 'driver_id' claim to be present in the JWT");
        Guid.Parse(driverIdClaim.Value).ShouldBe(expectedDriverId);
    }

    // ── Test 5: Driver without linked DriverId → JWT has no driver_id claim ───

    [Fact]
    public async Task PostLogin_DriverUserWithNoLinkedDriverId_JwtLacksDriverIdClaim()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, password) = await SeedUserAsync(UserRole.Driver, "driver-pwd-2", ct);

        var driverLookup = Substitute.For<IDriverLookup>();
        driverLookup
            .GetDriverIdByUserIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        await using var factory = CreateFactory(services =>
        {
            services.AddSingleton(driverLookup);
        });

        var client = factory.CreateClient();
        var request = new LoginRequest(user.Username.Value, password);

        // Act
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", request, ct);

        // Assert — HTTP layer
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();

        // Decode the JWT and verify the driver_id claim is absent.
        var jwtHandler = new JwtSecurityTokenHandler();
        jwtHandler.CanReadToken(body.AccessToken).ShouldBeTrue();
        var token = jwtHandler.ReadJwtToken(body.AccessToken);

        var driverIdClaim = token.Claims.FirstOrDefault(c => c.Type == "driver_id");
        driverIdClaim.ShouldBeNull("Expected 'driver_id' claim to be absent when lookup returns null");
    }
}
