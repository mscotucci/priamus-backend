using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.AdminRevoke;

/// <summary>
/// Endpoint integration tests for <c>POST /api/identity/auth/admin/tokens/{tokenId:guid}/revoke</c>.
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> wired to the per-test PostgreSQL
/// database produced by <see cref="PostgresFixture"/> / <see cref="IdentityIntegrationTestBase"/>.
/// </summary>
public sealed class AdminRevokeEndpointTests : IdentityIntegrationTestBase
{
    // JWT signing key that satisfies JwtOptions [MinLength(32)] validation.

    public AdminRevokeEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="WebApplicationFactory{TEntryPoint}"/> whose DI container has:
    /// <list type="bullet">
    ///   <item>the <see cref="IdentityDbContext"/> pointed at this test's isolated database,</item>
    ///   <item>JWT options overridden to deterministic test values.</item>
    /// </list>
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return ApiFactory.Create(ConnectionString);
    }

    /// <summary>
    /// Seeds a user with the specified <paramref name="role"/> and <paramref name="plainPassword"/>
    /// into the isolated test database. Returns the persisted <see cref="User"/> and the
    /// plain-text password used to produce its hash.
    /// </summary>
    private async Task<(User User, string PlainPassword)> SeedUserAsync(
        UserRole role,
        string plainPassword,
        CancellationToken ct)
    {
        var passwordHasher = new PasswordHasher<User>();
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
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();
        return body;
    }

    /// <summary>
    /// Seeds an active refresh token directly in the DB for the given user and returns
    /// its <see cref="RefreshTokenId"/>.
    /// </summary>
    private async Task<RefreshTokenId> SeedActiveRefreshTokenAsync(
        UserId userId,
        CancellationToken ct)
    {
        var hasher = new Sha256RefreshTokenHasher();
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = hasher.Hash(rawToken);

        var issueClock = new FixedClockStub(new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc));
        var refreshToken = RefreshToken.Issue(
            RefreshTokenId.New(),
            userId,
            RefreshTokenHash.Create(tokenHash),
            issueClock,
            TimeSpan.FromHours(12));

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(refreshToken);
            await db.SaveChangesAsync(ct);
        }

        return refreshToken.Id;
    }

    /// <summary>
    /// Seeds an already-rotated refresh token directly in the DB for the given user and
    /// returns its <see cref="RefreshTokenId"/>.
    /// </summary>
    private async Task<RefreshTokenId> SeedRotatedRefreshTokenAsync(
        UserId userId,
        CancellationToken ct)
    {
        var hasher = new Sha256RefreshTokenHasher();
        var issueClock = new FixedClockStub(new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc));

        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);
        var originalToken = RefreshToken.Issue(
            RefreshTokenId.New(),
            userId,
            RefreshTokenHash.Create(tokenHash),
            issueClock,
            TimeSpan.FromHours(12));

        var newHash = hasher.Hash(GenerateRawToken());
        var replacementToken = originalToken.Rotate(
            RefreshTokenId.New(),
            RefreshTokenHash.Create(newHash),
            issueClock,
            TimeSpan.FromHours(12));

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(originalToken);
            db.RefreshTokens.Add(replacementToken);
            await db.SaveChangesAsync(ct);
        }

        return originalToken.Id;
    }

    private static string GenerateRawToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // ── Test 1: BackOffice user revokes a token belonging to a different (Driver) user
    //           → 204; RevokedAt set in DB; one TokenRevokedByAdmin audit row with
    //             actorId == adminUserId, subjectId == tokenOwnerUserId, payload contains tokenId.

    [Fact]
    public async Task PostAdminRevoke_BackOfficeUserRevokesAnotherUsersToken_Returns204AndPersistsRevocationAndAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — seed a BackOffice admin and a Driver (the token owner); the two IDs are distinct.
        var (adminUser, adminPassword)   = await SeedUserAsync(UserRole.BackOffice, "AdminRevoke-Test-1-Admin!", ct);
        var (driverUser, _)              = await SeedUserAsync(UserRole.Driver,     "AdminRevoke-Test-1-Driver!", ct);

        adminUser.Id.Value.ShouldNotBe(driverUser.Id.Value,
            "The admin and driver users must have distinct IDs to validate the audit assertions.");

        var tokenId = await SeedActiveRefreshTokenAsync(driverUser.Id, ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Login as the BackOffice admin to obtain an access token.
        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        var adminAccessToken = loginBody.AccessToken;

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/identity/auth/admin/tokens/{tokenId.Value}/revoke");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAccessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Assert — RevokedAt set in DB
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var revokedToken = await db.RefreshTokens.AsNoTracking()
                .FirstAsync(rt => rt.Id == tokenId, ct);

            revokedToken.RevokedAt.ShouldNotBeNull(
                "RevokedAt must be set after a successful admin-initiated revocation.");
            revokedToken.IsRotated.ShouldBeFalse(
                "The token was explicitly revoked, not rotated; ReplacedByTokenId must remain null.");
        }

        // Assert — one TokenRevokedByAdmin audit row; actorId == admin; subjectId == driver; payload has tokenId
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.EventType == AuditEventTypes.TokenRevokedByAdmin
                         && a.SubjectId == driverUser.Id.Value)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1,
                "Exactly one TokenRevokedByAdmin audit row must be written per successful admin revocation.");

            var auditRow = auditRows[0];
            auditRow.ActorId.ShouldBe(adminUser.Id.Value,
                "actorId must equal the authenticated admin caller's UserId.");
            auditRow.SubjectId.ShouldBe(driverUser.Id.Value,
                "subjectId must equal the token owner's UserId (the Driver), not the admin's.");

            // Structural payload check: the tokenId field must be present.
            using var payloadDoc = JsonDocument.Parse(auditRow.Payload);
            payloadDoc.RootElement.TryGetProperty("tokenId", out var tokenIdElement)
                .ShouldBeTrue("Audit payload must contain the 'tokenId' field.");
            tokenIdElement.GetString().ShouldBe(tokenId.Value.ToString());
        }
    }

    // ── Test 2: Authenticated BackOffice user submits a tokenId that does not exist
    //           → 404 with RefreshToken.NotFound; no audit row written.

    [Fact]
    public async Task PostAdminRevoke_TokenDoesNotExist_Returns404WithNotFoundErrorCodeAndNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.BackOffice, "AdminRevoke-Test-2-Pwd!", ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        var adminAccessToken = loginBody.AccessToken;

        // A token ID that was never stored in the database.
        var nonExistentTokenId = Guid.NewGuid();

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/identity/auth/admin/tokens/{nonExistentTokenId}/revoke");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAccessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — HTTP status and error code
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(RefreshTokenErrors.NotFound.Code);

        // Assert — no TokenRevokedByAdmin audit row written (handler returns before audit step)
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.TokenRevokedByAdmin, ct);

            auditCount.ShouldBe(0,
                "No TokenRevokedByAdmin audit row must be written when the token is not found.");
        }
    }

    // ── Test 3: Authenticated BackOffice user submits a tokenId of an already-rotated token
    //           → 409 with the exact domain error code emitted by RefreshToken.Revoke() for
    //             rotated tokens; no audit row written.
    
    [Fact]
    public async Task PostAdminRevoke_AlreadyRotatedToken_Returns409WithCannotRevokeRotatedTokenErrorCodeAndNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (adminUser, adminPassword) = await SeedUserAsync(UserRole.BackOffice, "AdminRevoke-Test-3-Pwd!", ct);
        var (tokenOwner, _)            = await SeedUserAsync(UserRole.Driver,     "AdminRevoke-Test-3-Driver!", ct);

        // Seed an already-rotated token for the driver.
        var rotatedTokenId = await SeedRotatedRefreshTokenAsync(tokenOwner.Id, ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        var adminAccessToken = loginBody.AccessToken;

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/identity/auth/admin/tokens/{rotatedTokenId.Value}/revoke");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAccessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — HTTP status: Error.Conflict
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(RefreshTokenErrors.CannotRevokeRotatedToken.Code);

        // Assert — no TokenRevokedByAdmin audit row written
        // (the handler catches the DomainException and returns before the audit step)
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.TokenRevokedByAdmin
                              && a.SubjectId == tokenOwner.Id.Value, ct);

            auditCount.ShouldBe(0,
                "No TokenRevokedByAdmin audit row must be written when revoking a rotated token fails.");
        }
    }

    // ── Test 4: Authenticated user with role Driver calls the endpoint
    //           → 403; authorization middleware short-circuits the BackOfficeOrAdmin policy;
    //             no handler invocation; no DB writes; no audit rows.

    [Fact]
    public async Task PostAdminRevoke_DriverRoleUser_Returns403AndNoAuditRowsWritten()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — seed a Driver user (does not satisfy BackOfficeOrAdmin policy).
        var (driverUser, driverPassword) = await SeedUserAsync(UserRole.Driver, "AdminRevoke-Test-4-Pwd!", ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Login as the Driver to obtain an access token.
        var loginBody = await LoginAsync(client, driverUser.Username.Value, driverPassword, ct);
        var driverAccessToken = loginBody.AccessToken;

        // Use a plausible token ID; the authorization middleware must short-circuit
        // before the handler is invoked, so the token existence is irrelevant.
        var anyTokenId = Guid.NewGuid();

        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/identity/auth/admin/tokens/{anyTokenId}/revoke");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", driverAccessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — authorization policy rejects the Driver role with 403
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Assert — no audit rows of any type written (handler never invoked).
        // The only rows that could have been written are the LoginSucceeded row from the
        // login call above. Filter for TokenRevokedByAdmin specifically.
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.TokenRevokedByAdmin, ct);

            auditCount.ShouldBe(0,
                "The BackOfficeOrAdmin policy must short-circuit before the handler; " +
                "no TokenRevokedByAdmin audit row must be written for a Driver-role request.");
        }
    }

    // ── Test 5: Anonymous request (no JWT) → 401; authorization middleware short-circuits;
    //           no handler invocation; no DB writes; no audit rows.

    [Fact]
    public async Task PostAdminRevoke_AnonymousRequest_Returns401AndNoAuditRowsWritten()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — no users seeded; no Authorization header provided.
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Use a plausible token ID; the authentication middleware must short-circuit
        // before the handler is invoked.
        var anyTokenId = Guid.NewGuid();

        // No Authorization header — anonymous request.
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/identity/auth/admin/tokens/{anyTokenId}/revoke");

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — authentication middleware short-circuits at 401
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Assert — no audit rows of any type written (handler never invoked)
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var totalAuditCount = await db.AuditLog.AsNoTracking().CountAsync(ct);

            totalAuditCount.ShouldBe(0,
                "The authentication middleware must short-circuit before the handler; " +
                "no audit rows must be written for an anonymous admin-revoke attempt.");
        }
    }
}
