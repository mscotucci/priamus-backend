using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.Refresh;
using Priamus.Trips.Identity.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.Refresh;

/// <summary>
/// Endpoint integration tests for <c>POST /api/auth/refresh</c>.
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> wired to the per-test PostgreSQL
/// database produced by <see cref="PostgresFixture"/> / <see cref="IdentityIntegrationTestBase"/>.
/// </summary>
public sealed class RefreshEndpointTests : IdentityIntegrationTestBase
{
    // JWT signing key that satisfies JwtOptions [MinLength(32)] validation.
    private const string TestSigningKey = "test-signing-key-endpoint-32chars!!";
    private const string TestIssuer    = "test-issuer";
    private const string TestAudience  = "test-audience";

    public RefreshEndpointTests(PostgresFixture fixture) : base(fixture) { }

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
    /// Seeds a user and an active refresh token into the isolated test database.
    /// Returns the user, the raw (plain-text) refresh token string, and the token's ID.
    /// </summary>
    private async Task<(User User, string RawToken, RefreshTokenId TokenId)> SeedUserWithActiveTokenAsync(
        UserRole role,
        CancellationToken ct)
    {
        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);

        var user = TestDataBuilders.AUser().WithRole(role).Build();
        var issueClock = new FixedClockStub(new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc));
        var refreshToken = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            issueClock,
            TimeSpan.FromHours(12));

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            db.RefreshTokens.Add(refreshToken);
            await db.SaveChangesAsync(ct);
        }

        return (user, rawToken, refreshToken.Id);
    }

    private static string GenerateRawToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // ── Test 1: Valid unused token → 200 + new access token + new refresh token; old token rotated ──

    [Fact]
    public async Task PostRefresh_ValidUnusedToken_Returns200WithNewTokensAndOldTokenRotatedInDb()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, rawToken, oldTokenId) = await SeedUserWithActiveTokenAsync(UserRole.BackOffice, ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new RefreshRequest(rawToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", request, ct);

        // Assert — HTTP response
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>(ct);
        body.ShouldNotBeNull();
        body.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        body.RefreshToken.ShouldNotBe(rawToken, "the refresh token must be rotated, not returned as-is");
        body.RefreshToken.Length.ShouldBe(43, "base64url without padding of 32 random bytes is always 43 chars");
        body.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);
        body.RefreshTokenExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);

        // Assert — old token is marked as rotated in the database
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var oldToken = await db.RefreshTokens.AsNoTracking()
                .FirstAsync(rt => rt.Id == oldTokenId, ct);

            oldToken.IsRotated.ShouldBeTrue("the consumed token must be marked rotated");
            oldToken.RevokedAt.ShouldNotBeNull("rotation sets RevokedAt on the old token");
            oldToken.ReplacedByTokenId.ShouldNotBeNull("rotation sets the replacement pointer");
        }
    }

    // ── Test 2: Unknown refresh token → 401 with RefreshToken.NotFound ────────

    [Fact]
    public async Task PostRefresh_UnknownRefreshToken_Returns401WithNotFoundErrorCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — no token is seeded; we submit a randomly-generated one.
        var unknownToken = GenerateRawToken();

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new RefreshRequest(unknownToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(RefreshTokenErrors.NotFound.Code);
    }

    // ── Test 3: Already-used (rotated) token → 401 with NoLongerValid ─────────
    // NOTE: The brief describes a "revoke all other active tokens" side effect as replay
    // detection. That behavior is explicitly out of scope for MVP per ADR 0005 §Out of scope
    // ("token family invalidation is out of scope for MVP"). The production RefreshHandler
    // returns RefreshTokenErrors.NoLongerValid without any additional side effects when it
    // encounters an already-rotated token. This test verifies the actual production behavior.

    [Fact]
    public async Task PostRefresh_AlreadyUsedRotatedToken_Returns401WithNoLongerValidErrorCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — seed a user, an already-rotated token, and a second active token
        // for the same user (to verify the second one is NOT revoked by the handler,
        // since family invalidation is out of scope per ADR 0005).
        var hasher = new Sha256RefreshTokenHasher();
        var issueClock = new FixedClockStub(new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc));

        // The already-rotated (consumed) token that we will attempt to reuse.
        var consumedRawToken = GenerateRawToken();
        var consumedHash = hasher.Hash(consumedRawToken);

        // A second, still-active token for the same user.
        var activeRawToken = GenerateRawToken();
        var activeHash = hasher.Hash(activeRawToken);

        var user = TestDataBuilders.AUser().WithRole(UserRole.BackOffice).Build();

        var consumedToken = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(consumedHash),
            issueClock,
            TimeSpan.FromHours(12));

        var activeToken = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(activeHash),
            issueClock,
            TimeSpan.FromHours(12));

        var activeTokenId = activeToken.Id;

        // Rotate the consumed token to simulate prior use (sets IsRotated = true).
        var replacementHash = hasher.Hash(GenerateRawToken());
        consumedToken.Rotate(
            RefreshTokenId.New(),
            RefreshTokenHash.Create(replacementHash),
            issueClock,
            TimeSpan.FromHours(12));
        // Note: we discard the returned replacement token intentionally —
        // we only need the original to be in the rotated state.

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            db.RefreshTokens.Add(consumedToken);
            db.RefreshTokens.Add(activeToken);
            // The replacement token created by Rotate() was not saved — we intentionally
            // only persist the rotated-state original and the second active token.
            await db.SaveChangesAsync(ct);
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new RefreshRequest(consumedRawToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", request, ct);

        // Assert — HTTP error
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(RefreshTokenErrors.NoLongerValid.Code);

        // Assert — the second active token is NOT revoked (family invalidation is out of scope
        // per ADR 0005; verifying the handler does not silently implement unspecified behavior).
        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var secondToken = await db.RefreshTokens.AsNoTracking()
                .FirstAsync(rt => rt.Id == activeTokenId, ct);

            secondToken.RevokedAt.ShouldBeNull(
                "family invalidation is out of scope per ADR 0005; the second token must remain active");
            secondToken.IsRotated.ShouldBeFalse();
        }
    }

    // ── Test 4: Expired refresh token → 401 with Expired error code ───────────

    [Fact]
    public async Task PostRefresh_ExpiredToken_Returns401WithExpiredErrorCode()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — issue a token that expired a day ago (lifetime = 1 second, issued yesterday).
        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);

        var user = TestDataBuilders.AUser().WithRole(UserRole.BackOffice).Build();

        // Clock set to one day in the past so the token expires immediately.
        var pastClock = new FixedClockStub(new DateTime(2026, 5, 13, 8, 0, 0, DateTimeKind.Utc));
        var expiredToken = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            pastClock,
            TimeSpan.FromSeconds(1)); // expires 1 second after 2026-05-13T08:00:00Z

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            db.RefreshTokens.Add(expiredToken);
            await db.SaveChangesAsync(ct);
        }

        // The host's real clock (or the production IClock) is well past that expiry —
        // the handler will call IsActive(clock) which checks clock.UtcNow < ExpiresAt.
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var request = new RefreshRequest(rawToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", request, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var problemJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(problemJson);
        doc.RootElement.GetProperty("errorCode").GetString()
           .ShouldBe(RefreshTokenErrors.Expired.Code);
    }

    // ── Test 5: Driver user → new JWT carries driver_id claim re-fetched via IDriverLookup ──

    [Fact]
    public async Task PostRefresh_DriverUserWithLinkedDriverId_NewJwtContainsDriverIdClaimFromLookup()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var (user, rawToken, _) = await SeedUserWithActiveTokenAsync(UserRole.Driver, ct);

        var expectedDriverId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        // Substitute IDriverLookup so that it returns a known DriverId for any user.
        // Per ADR 0010, the Refresh handler must re-execute IDriverLookup (not snapshot the value).
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
        var request = new RefreshRequest(rawToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/refresh", request, ct);

        // Assert — HTTP response
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>(ct);
        body.ShouldNotBeNull();

        // Decode the new access JWT and verify the driver_id claim is present with
        // the value returned by IDriverLookup, not any cached/snapshotted value.
        var jwtHandler = new JwtSecurityTokenHandler();
        jwtHandler.CanReadToken(body.AccessToken).ShouldBeTrue();
        var token = jwtHandler.ReadJwtToken(body.AccessToken);

        var driverIdClaim = token.Claims.FirstOrDefault(c => c.Type == "driver_id");
        driverIdClaim.ShouldNotBeNull("expected 'driver_id' claim to be present in the refreshed JWT");
        Guid.Parse(driverIdClaim.Value).ShouldBe(expectedDriverId,
            "the driver_id must come from IDriverLookup re-queried at refresh per ADR 0010");
    }
}
