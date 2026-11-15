using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
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
/// Integration tests for <see cref="RevokeHandler"/>.
/// </summary>
public sealed class RevokeHandlerTests : IdentityIntegrationTestBase
{
    public RevokeHandlerTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateTime FixedNow =
        new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    private RevokeHandler BuildHandler(
        IdentityDbContext db,
        ICurrentUser? currentUser = null,
        IRefreshTokenHasher? refreshTokenHasher = null,
        IClock? clock = null)
    {
        currentUser ??= Substitute.For<ICurrentUser>();
        refreshTokenHasher ??= new Sha256RefreshTokenHasher();
        clock ??= new FixedClockStub(FixedNow);

        return new RevokeHandler(
            db, refreshTokenHasher, currentUser, clock,
            NullLogger<RevokeHandler>.Instance);
    }

    private static string GenerateRawToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static async Task<(UserId userId, string rawToken)> SeedTokenAsync(
        IdentityDbContext db,
        CancellationToken ct)
    {
        var user = TestDataBuilders.AUser().Build();
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

        return (user.Id, rawToken);
    }

    [Fact]
    public async Task HandleAsync_OwnerRevokesToken_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var (userId, rawToken) = await SeedTokenAsync(db, ct);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var handler = BuildHandler(db, currentUser: currentUser);
        var request = new RevokeRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        // Verify audit row was written
        await using var verifyScope = CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditRows = verifyDb.AuditLog
            .Where(a => a.SubjectId == userId.Value && a.EventType == AuditEventTypes.TokenRevokedByOwner)
            .ToList();
        auditRows.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TokenNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId.New());

        var handler = BuildHandler(db, currentUser: currentUser);
        var request = new RevokeRequest(GenerateRawToken());

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(RefreshTokenErrors.NotFound);
    }

    [Fact]
    public async Task HandleAsync_DifferentOwner_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var (_, rawToken) = await SeedTokenAsync(db, ct);

        // Authenticate as a DIFFERENT user
        var otherUserId = UserId.New();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(otherUserId);

        var handler = BuildHandler(db, currentUser: currentUser);
        var request = new RevokeRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(SharedKernel.Results.ErrorType.Forbidden);
    }

    [Fact]
    public async Task HandleAsync_AlreadyRotatedToken_ReturnsCannotRevokeRotatedToken()
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

        // Rotate the token (makes it no longer directly revocable)
        var newHash = hasher.Hash(GenerateRawToken());
        var newToken = token.Rotate(
            RefreshTokenId.New(),
            RefreshTokenHash.Create(newHash),
            issueClock,
            TimeSpan.FromHours(12));

        db.RefreshTokens.Add(token);
        db.RefreshTokens.Add(newToken);
        await db.SaveChangesAsync(ct);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(user.Id);

        var handler = BuildHandler(db, currentUser: currentUser);
        var request = new RevokeRequest(rawToken);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert: the domain error reaches the caller unchanged
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(RefreshTokenErrors.CannotRevokeRotatedToken);
    }
}
