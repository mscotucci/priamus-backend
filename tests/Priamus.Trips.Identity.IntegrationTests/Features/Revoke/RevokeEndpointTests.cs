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
using Priamus.Trips.Identity.Features.Refresh;
using Priamus.Trips.Identity.Features.Revoke;
using Priamus.Trips.Identity.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.Revoke;

/// <summary>
/// Endpoint integration tests for <c>POST /api/auth/revoke</c>.
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> wired to the per-test PostgreSQL
/// database produced by <see cref="PostgresFixture"/> / <see cref="IdentityIntegrationTestBase"/>.
/// </summary>
public sealed class RevokeEndpointTests : IdentityIntegrationTestBase
{
    // JWT signing key that satisfies JwtOptions [MinLength(32)] validation.
    private const string TestSigningKey = "test-signing-key-endpoint-32chars!!";
    private const string TestIssuer    = "test-issuer";
    private const string TestAudience  = "test-audience";

    public RevokeEndpointTests(PostgresFixture fixture) : base(fixture) { }

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
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Identity", ConnectionString);
                builder.UseSetting("Jwt:Issuer",     TestIssuer);
                builder.UseSetting("Jwt:Audience",   TestAudience);
                builder.UseSetting("Jwt:SigningKey",  TestSigningKey);
                builder.UseSetting("Jwt:AccessTokenLifetime",  "01:00:00");
                builder.UseSetting("Jwt:RefreshTokenLifetime", "12:00:00");
            });
    }

    /// <summary>
    /// Seeds a user with a known password into the isolated test database and returns the
    /// persisted <see cref="User"/> and the plain-text password used to produce its hash.
    /// </summary>
    private async Task<(User User, string PlainPassword)> SeedUserAsync(
        string plainPassword,
        CancellationToken ct)
    {
        var passwordHasher = new PasswordHasher<User>();
        var stub = TestDataBuilders.AUser().WithRole(UserRole.BackOffice).Build();
        var hashString = passwordHasher.HashPassword(stub, plainPassword);

        var user = User.Create(
            stub.Id,
            Username.Create(stub.Username.Value),
            PasswordHash.Create(hashString),
            UserRole.BackOffice,
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
    /// Calls <c>POST /api/auth/login</c> with the given credentials and returns the
    /// <see cref="LoginResponse"/>. Asserts that the response is 200 OK.
    /// </summary>
    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken ct)
    {
        var loginRequest = new LoginRequest(username, password);
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", loginRequest, ct);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();
        return body;
    }

    /// <summary>
    /// Seeds a raw refresh token for <paramref name="userId"/> directly in the DB
    /// and returns the raw token string that can be submitted to <c>/api/auth/revoke</c>.
    /// </summary>
    private async Task<string> SeedRawRefreshTokenForUserAsync(
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

        return rawToken;
    }

    // ── Test 1: Authenticated user revokes own active token → 204, RevokedAt set, audit row ──

    [Fact]
    public async Task PostRevoke_AuthenticatedOwnerRevokesOwnActiveToken_Returns204AndPersistsRevocationAndAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, password) = await SeedUserAsync("Revoke-Test-1-Pwd!", ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Login to obtain an access token and the refresh token to be revoked.
        var loginBody = await LoginAsync(client, user.Username.Value, password, ct);
        var accessToken  = loginBody.AccessToken;
        var rawRefreshToken = loginBody.RefreshToken;

        // Confirm the refresh token was persisted so we can look it up later.
        RefreshTokenId? seededTokenId;
        var hasher = new Sha256RefreshTokenHasher();
        var hash = hasher.Hash(rawRefreshToken);
        var hashVo = new RefreshTokenHash(hash);

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var token = await db.RefreshTokens.AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.TokenHash == hashVo, ct);
            token.ShouldNotBeNull("The refresh token issued at login must exist in the DB.");
            seededTokenId = token.Id;
        }

        var revokeRequest = new RevokeRequest(rawRefreshToken);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/revoke")
        {
            Content = JsonContent.Create(revokeRequest),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — HTTP status
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Assert — token has RevokedAt set in DB
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var revokedToken = await db.RefreshTokens.AsNoTracking()
                .FirstAsync(rt => rt.Id == seededTokenId, ct);

            revokedToken.RevokedAt.ShouldNotBeNull(
                "RevokedAt must be set after a successful owner-initiated revocation.");
            revokedToken.IsRotated.ShouldBeFalse(
                "The token was explicitly revoked, not rotated; ReplacedByTokenId must remain null.");
        }

        // Assert — one TokenRevokedByOwner audit row exists with actorId == subjectId == userId
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditRows = await db.AuditLog.AsNoTracking()
                .Where(a => a.EventType == AuditEventTypes.TokenRevokedByOwner
                         && a.SubjectId == user.Id.Value)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(1,
                "Exactly one TokenRevokedByOwner audit row must be written per successful revocation.");

            var auditRow = auditRows[0];
            auditRow.ActorId.ShouldBe(user.Id.Value,
                "actorId must equal the authenticated caller's UserId.");
            auditRow.SubjectId.ShouldBe(user.Id.Value,
                "subjectId must equal the token owner's UserId.");

            // Structural payload check: the tokenId field must be present.
            using var payloadDoc = JsonDocument.Parse(auditRow.Payload);
            payloadDoc.RootElement.TryGetProperty("tokenId", out var tokenIdElement)
                .ShouldBeTrue("Audit payload must contain the 'tokenId' field.");
            tokenIdElement.GetString().ShouldBe(seededTokenId!.Value.Value.ToString());
        }
    }

    // ── Test 2: Token does not exist → 404 with RefreshToken.NotFound, no audit row ──

    [Fact]
    public async Task PostRevoke_TokenDoesNotExist_Returns404WithNotFoundErrorCodeAndNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, password) = await SeedUserAsync("Revoke-Test-2-Pwd!", ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, user.Username.Value, password, ct);
        var accessToken = loginBody.AccessToken;

        // A randomly-fabricated raw token that has never been stored.
        var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var nonExistentToken = Convert.ToBase64String(randomBytes).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');

        var revokeRequest = new RevokeRequest(nonExistentToken);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/revoke")
        {
            Content = JsonContent.Create(revokeRequest),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — HTTP status and error code
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(RefreshTokenErrors.NotFound.Code);

        // Assert — no TokenRevokedByOwner audit row written (handler returns early before audit step)
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var auditCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.TokenRevokedByOwner
                              && a.SubjectId == user.Id.Value, ct);

            auditCount.ShouldBe(0,
                "No TokenRevokedByOwner audit row must be written when the token is not found.");
        }
    }

    // ── Test 3: Token owned by a different user → 403 with RefreshToken.NotOwner, no audit row ──
    // Seed two users (A and B). User A logs in and attempts to revoke user B's token.

    [Fact]
    public async Task PostRevoke_TokenOwnedByDifferentUser_Returns403WithNotOwnerErrorCodeAndNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — seed user A and user B
        var (userA, passwordA) = await SeedUserAsync("Revoke-Test-3A-Pwd!", ct);
        var (userB, _)         = await SeedUserAsync("Revoke-Test-3B-Pwd!", ct);

        // Seed a refresh token owned by user B directly in the DB.
        var userBRawToken = await SeedRawRefreshTokenForUserAsync(userB.Id, ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Login as user A to obtain an access token.
        var loginBody = await LoginAsync(client, userA.Username.Value, passwordA, ct);
        var userAAccessToken = loginBody.AccessToken;

        // User A attempts to revoke user B's token.
        var revokeRequest = new RevokeRequest(userBRawToken);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/revoke")
        {
            Content = JsonContent.Create(revokeRequest),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAAccessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — HTTP status and error code
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        // The handler returns Error.Forbidden("RefreshToken.NotOwner", ...) inline.
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe("RefreshToken.NotOwner");

        // Assert — no TokenRevokedByOwner audit row written for either user
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var auditCountA = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.TokenRevokedByOwner
                              && a.SubjectId == userA.Id.Value, ct);
            var auditCountB = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.TokenRevokedByOwner
                              && a.SubjectId == userB.Id.Value, ct);

            auditCountA.ShouldBe(0,
                "No TokenRevokedByOwner audit row must be written when the ownership check fails.");
            auditCountB.ShouldBe(0,
                "No TokenRevokedByOwner audit row must be written for the token owner when ownership check fails.");
        }
    }

    // ── Test 4: Token already rotated → 422 with RefreshToken.CannotRevokeRotatedToken, no audit row ──
    //
    // NOTE — deviation from the brief:
    // The brief says "token already revoked". In the production domain, RefreshToken.Revoke()
    // is *idempotent* for tokens that have been explicitly revoked but NOT rotated — it returns
    // without error or event. The only state that causes a domain error from Revoke() is when
    // the token has been *rotated* (ReplacedByTokenId is set), which throws
    // RefreshTokenErrors.CannotRevokeRotatedToken. This test therefore uses a rotated token
    // (achieved by calling /api/auth/refresh to consume the token before revoking it) and
    // verifies the 422 domain error with code "RefreshToken.CannotRevokeRotatedToken".
    // See discrepancy report in the test file summary.

    [Fact]
    public async Task PostRevoke_TokenAlreadyRotated_Returns422WithCannotRevokeRotatedTokenErrorCodeAndNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — login to obtain access token and the refresh token to be consumed.
        var (user, password) = await SeedUserAsync("Revoke-Test-4-Pwd!", ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, user.Username.Value, password, ct);
        var accessToken      = loginBody.AccessToken;
        var originalRawToken = loginBody.RefreshToken;

        // Rotate the original refresh token by calling /api/auth/refresh.
        // This makes the original token's IsRotated = true in the database.
        var refreshRequest = new RefreshRequest(originalRawToken);
        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", refreshRequest, ct);
        refreshResponse.StatusCode.ShouldBe(HttpStatusCode.OK,
            "The refresh call must succeed to rotate the original token.");

        // Now attempt to revoke the *original* (now-rotated) token using the
        // access token obtained during login (still valid for 1 hour).
        var revokeRequest = new RevokeRequest(originalRawToken);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/revoke")
        {
            Content = JsonContent.Create(revokeRequest),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — HTTP status: Domain error → 422 Unprocessable Entity
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(RefreshTokenErrors.CannotRevokeRotatedToken.Code);

        // Assert — no TokenRevokedByOwner audit row written for this attempt
        // (the handler catches the DomainException and returns before the audit step).
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            // Count only rows produced after the rotation — there should be none for
            // TokenRevokedByOwner regardless of when they were produced, since the
            // revoke call never completes successfully.
            var auditCount = await db.AuditLog.AsNoTracking()
                .CountAsync(a => a.EventType == AuditEventTypes.TokenRevokedByOwner
                              && a.SubjectId == user.Id.Value, ct);

            auditCount.ShouldBe(0,
                "No TokenRevokedByOwner audit row must be written when revoking a rotated token fails.");
        }
    }

    // ── Test 5: Anonymous request (no JWT) → 401, no DB writes, no audit rows ──

    [Fact]
    public async Task PostRevoke_AnonymousRequest_Returns401AndNoAuditRowsWritten()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — the database is clean; no users or tokens seeded.
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // A plausible-looking raw token; the authorization middleware must reject
        // before the handler is invoked, so the token value is irrelevant.
        var randomBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(randomBytes).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');

        var revokeRequest = new RevokeRequest(rawToken);
        // No Authorization header — anonymous request.
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/revoke")
        {
            Content = JsonContent.Create(revokeRequest),
        };

        // Act
        var response = await client.SendAsync(httpRequest, ct);

        // Assert — authorization middleware short-circuits at 401
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Assert — no audit rows of any type were written (handler never invoked)
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var totalAuditCount = await db.AuditLog.AsNoTracking().CountAsync(ct);

            totalAuditCount.ShouldBe(0,
                "The authorization middleware must short-circuit before the handler; " +
                "no audit rows must be written for an anonymous revoke attempt.");
        }
    }
}
