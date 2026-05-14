using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
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
/// Integration tests for <see cref="RefreshHandler"/>.
/// </summary>
public sealed class RefreshHandlerTests : IdentityIntegrationTestBase
{
    public RefreshHandlerTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateTime FixedNow =
        new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    private RefreshHandler BuildHandler(
        IdentityDbContext db,
        IRefreshTokenHasher? refreshTokenHasher = null,
        IJwtTokenIssuer? jwtIssuer = null,
        IDriverLookup? driverLookup = null,
        IClock? clock = null)
    {
        refreshTokenHasher ??= new Sha256RefreshTokenHasher();
        jwtIssuer ??= Substitute.For<IJwtTokenIssuer>();
        driverLookup ??= Substitute.For<IDriverLookup>();
        clock ??= new FixedClockStub(FixedNow);

        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "test-signing-key-that-is-at-least-32-chars",
            AccessTokenLifetime = TimeSpan.FromHours(1),
            RefreshTokenLifetime = TimeSpan.FromHours(12),
        });

        return new RefreshHandler(
            db, refreshTokenHasher, jwtIssuer, driverLookup,
            clock, jwtOptions, NullLogger<RefreshHandler>.Instance);
    }

    private static async Task<(User user, string rawToken, string tokenHash)> SeedUserAndTokenAsync(
        IdentityDbContext db,
        CancellationToken ct,
        UserRole role = UserRole.BackOffice)
    {
        var user = TestDataBuilders.AUser().WithRole(role).Build();
        db.Users.Add(user);

        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);
        var token = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            new FixedClockStub(new DateTime(2026, 5, 13, 9, 0, 0, DateTimeKind.Utc)),
            TimeSpan.FromHours(12));
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);

        return (user, rawToken, tokenHash);
    }

    private static string GenerateRawToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task HandleAsync_ValidToken_ReturnsNewTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var (user, rawToken, _) = await SeedUserAndTokenAsync(db, ct);

        var jwtIssuer = Substitute.For<IJwtTokenIssuer>();
        jwtIssuer.IssueAccessToken(Arg.Any<UserId>(), Arg.Any<UserRole>(), Arg.Any<Guid?>())
            .Returns("new-access-token");

        var handler = BuildHandler(db, jwtIssuer: jwtIssuer);
        var request = new RefreshRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("new-access-token");
        result.Value.RefreshToken.ShouldNotBe(rawToken); // rotated
        result.Value.RefreshToken.Length.ShouldBe(43);
    }

    [Fact]
    public async Task HandleAsync_TokenNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var handler = BuildHandler(db);
        var request = new RefreshRequest(GenerateRawToken());

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(RefreshTokenErrors.NotFound);
    }

    [Fact]
    public async Task HandleAsync_ExpiredToken_ReturnsExpired()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = TestDataBuilders.AUser().Build();
        db.Users.Add(user);

        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);
        // Issue token that already expired (clock in the past)
        var pastClock = new FixedClockStub(new DateTime(2026, 5, 12, 9, 0, 0, DateTimeKind.Utc));
        var token = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            pastClock,
            TimeSpan.FromSeconds(1)); // expires 1 second after 2026-05-12T09:00:00Z
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);

        // Handler uses FixedNow = 2026-05-13T10:00:00Z, token expired way before that.
        var handler = BuildHandler(db);
        var request = new RefreshRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(RefreshTokenErrors.Expired);
    }

    [Fact]
    public async Task HandleAsync_RevokedToken_ReturnsNoLongerValid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = TestDataBuilders.AUser().Build();
        db.Users.Add(user);

        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);
        var issueClock = new FixedClockStub(new DateTime(2026, 5, 13, 8, 0, 0, DateTimeKind.Utc));
        var token = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            issueClock,
            TimeSpan.FromHours(12));
        token.Revoke(issueClock);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);

        var handler = BuildHandler(db);
        var request = new RefreshRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(RefreshTokenErrors.NoLongerValid);
    }

    [Fact]
    public async Task HandleAsync_DisabledUser_ReturnsAccountDisabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = TestDataBuilders.AUser().WithRole(UserRole.Driver).Build();
        user.Disable(new SystemClock(), TestDataBuilders.ActorId);
        db.Users.Add(user);

        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);
        var token = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            new FixedClockStub(new DateTime(2026, 5, 13, 8, 0, 0, DateTimeKind.Utc)),
            TimeSpan.FromHours(12));
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);

        var handler = BuildHandler(db);
        var request = new RefreshRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.AccountDisabled);
    }

    [Fact]
    public async Task HandleAsync_DriverRole_CallsDriverLookupAtRefresh()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var (user, rawToken, _) = await SeedUserAndTokenAsync(db, ct, UserRole.Driver);

        var expectedDriverId = Guid.NewGuid();
        var driverLookup = Substitute.For<IDriverLookup>();
        driverLookup.GetDriverIdByUserIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(expectedDriverId);

        var jwtIssuer = Substitute.For<IJwtTokenIssuer>();
        jwtIssuer.IssueAccessToken(Arg.Any<UserId>(), Arg.Any<UserRole>(), Arg.Any<Guid?>())
            .Returns("driver-refreshed-token");

        var handler = BuildHandler(db, driverLookup: driverLookup, jwtIssuer: jwtIssuer);
        var request = new RefreshRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        await driverLookup.Received(1)
            .GetDriverIdByUserIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
        jwtIssuer.Received(1).IssueAccessToken(
            Arg.Any<UserId>(), UserRole.Driver, expectedDriverId);
    }
}
