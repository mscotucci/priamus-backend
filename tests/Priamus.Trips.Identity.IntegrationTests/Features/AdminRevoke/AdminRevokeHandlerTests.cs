using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Features.AdminRevoke;
using Priamus.Trips.Identity.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.AdminRevoke;

/// <summary>
/// Integration tests for <see cref="AdminRevokeHandler"/>.
/// </summary>
public sealed class AdminRevokeHandlerTests : IdentityIntegrationTestBase
{
    public AdminRevokeHandlerTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateTime FixedNow =
        new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    private AdminRevokeHandler BuildHandler(
        IdentityDbContext db,
        ICurrentUser? currentUser = null,
        IClock? clock = null)
    {
        currentUser ??= Substitute.For<ICurrentUser>();
        clock ??= new FixedClockStub(FixedNow);

        return new AdminRevokeHandler(
            db, currentUser, clock,
            NullLogger<AdminRevokeHandler>.Instance);
    }

    private static async Task<(RefreshTokenId tokenId, UserId userId)> SeedTokenAsync(
        IdentityDbContext db,
        CancellationToken ct)
    {
        var user = TestDataBuilders.AUser().Build();
        db.Users.Add(user);

        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);
        var tokenId = RefreshTokenId.New();
        var token = RefreshToken.Issue(
            tokenId,
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            new FixedClockStub(new DateTime(2026, 5, 13, 8, 0, 0, DateTimeKind.Utc)),
            TimeSpan.FromHours(12));
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);

        return (tokenId, user.Id);
    }

    private static string GenerateRawToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task HandleAsync_ValidToken_Succeeds_AndWritesAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var (tokenId, userId) = await SeedTokenAsync(db, ct);

        var adminId = UserId.New();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(adminId);

        var handler = BuildHandler(db, currentUser: currentUser);

        // Act
        var result = await handler.HandleAsync(tokenId.Value, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        // Verify audit row was written
        await using var verifyScope = CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditRows = verifyDb.AuditLog
            .Where(a => a.SubjectId == userId.Value && a.EventType == AuditEventTypes.TokenRevokedByAdmin)
            .ToList();
        auditRows.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_TokenNotFound_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var adminId = UserId.New();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(adminId);

        var handler = BuildHandler(db, currentUser: currentUser);

        // Act: non-existent token ID
        var result = await handler.HandleAsync(Guid.NewGuid(), ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(RefreshTokenErrors.NotFound);
    }

    [Fact]
    public async Task HandleAsync_AnotherUsersToken_StillSucceeds()
    {
        // AdminRevoke does NOT enforce ownership — any admin can revoke any token.
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var (tokenId, _) = await SeedTokenAsync(db, ct);

        // Act as a completely different user (admin)
        var adminId = UserId.New();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(adminId);

        var handler = BuildHandler(db, currentUser: currentUser);
        var result = await handler.HandleAsync(tokenId.Value, ct);

        // Assert: should succeed — no ownership enforcement
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_AlreadyRotatedToken_ReturnsDomainError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var user = TestDataBuilders.AUser().Build();
        db.Users.Add(user);

        var hasher = new Sha256RefreshTokenHasher();
        var rawToken = GenerateRawToken();
        var tokenHash = hasher.Hash(rawToken);
        var tokenId = RefreshTokenId.New();
        var issueClock = new FixedClockStub(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 8, 0, 0, DateTimeKind.Utc));
        var token = RefreshToken.Issue(
            tokenId,
            user.Id,
            RefreshTokenHash.Create(tokenHash),
            issueClock,
            TimeSpan.FromHours(12));

        // Rotate the token
        var newHash = hasher.Hash(GenerateRawToken());
        var newToken = token.Rotate(
            RefreshTokenId.New(),
            RefreshTokenHash.Create(newHash),
            issueClock,
            TimeSpan.FromHours(12));
        db.RefreshTokens.Add(token);
        db.RefreshTokens.Add(newToken);
        await db.SaveChangesAsync(ct);

        var adminId = UserId.New();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(adminId);

        var handler = BuildHandler(db, currentUser: currentUser);
        var result = await handler.HandleAsync(tokenId.Value, ct);

        // Assert: Conflict error because refresh token was already rotated and the Revoke method emits a RefreshTokenErrors.CannotRevokeRotatedToken(Conflict)
        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }
}
