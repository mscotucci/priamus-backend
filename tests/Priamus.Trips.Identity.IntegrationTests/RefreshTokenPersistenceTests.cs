using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests;

/// <summary>
/// Persistence round-trip tests for the <see cref="RefreshToken"/> aggregate.
/// Per ADR 0009, all tests interact with <see cref="IdentityDbContext"/> directly.
/// Each test phase uses a separate DI scope.
/// </summary>
public sealed class RefreshTokenPersistenceTests : IdentityIntegrationTestBase
{
    public RefreshTokenPersistenceTests(PostgresFixture fixture) : base(fixture) { }

    // ── Basic round-trip ─────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_Rotation_WritesAuditLog()
    {
        var ct = TestContext.Current.CancellationToken;

        var userId = UserId.New();
        var clock = new SystemClock();
        var lifetime = TimeSpan.FromDays(7);

        var originalHash = new RefreshTokenHash(new string('b', 64));
        var newHash = new RefreshTokenHash(new string('c', 64));

        var originalToken = RefreshToken.Issue(
            RefreshTokenId.New(), userId, originalHash, clock, lifetime);

        // Save the original token
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(originalToken);
            await db.SaveChangesAsync(ct);
        }

        RefreshTokenId newTokenId;

        // Rotate the token in a second scope
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var loaded = await db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Id == originalToken.Id, ct);
            loaded.ShouldNotBeNull();

            var replacement = loaded.Rotate(RefreshTokenId.New(), newHash, clock, lifetime);
            newTokenId = replacement.Id;

            db.RefreshTokens.Add(replacement);
            await db.SaveChangesAsync(ct);
        }

        // Assert state and audit log
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var oldLoaded = await db.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.Id == originalToken.Id, ct);
            oldLoaded.ShouldNotBeNull();
            oldLoaded.RevokedAt.ShouldNotBeNull();
            oldLoaded.ReplacedByTokenId.ShouldBe(newTokenId);

            var newLoaded = await db.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.Id == newTokenId, ct);
            newLoaded.ShouldNotBeNull();
            newLoaded.RevokedAt.ShouldBeNull();
            newLoaded.ReplacedByTokenId.ShouldBeNull();

            var auditRows = await db.AuditLog
                .AsNoTracking()
                .Where(e => e.SubjectId == userId.Value)
                .OrderBy(e => e.OccurredAt)
                .ToListAsync(ct);

            auditRows.Count.ShouldBe(3);
            auditRows[0].EventType.ShouldBe(AuditEventTypes.RefreshTokenIssued);

            var secondSaveEvents = auditRows.Skip(1).Select(e => e.EventType).ToHashSet();
            secondSaveEvents.ShouldContain(AuditEventTypes.RefreshTokenIssued);
            secondSaveEvents.ShouldContain(AuditEventTypes.RefreshTokenRotated);
        }
    }

    [Fact]
    public async Task RefreshToken_RoundTrip_PreservesAllFields()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var tokenId = RefreshTokenId.New();
        var userId = UserId.New();
        var hash = TestDataBuilders.ValidRefreshTokenHash('d');
        var lifetime = TimeSpan.FromDays(14);
        var clock = new SystemClock();

        var token = RefreshToken.Issue(tokenId, userId, hash, clock, lifetime);

        // Act — save
        using (var arrangeScope = CreateScope())
        {
            var db = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync(ct);
        }

        // Assert — reload and check all fields
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var reloaded = await db.RefreshTokens.AsNoTracking().FirstAsync(rt => rt.Id == tokenId, ct);

            reloaded.Id.ShouldBe(tokenId);
            reloaded.UserId.ShouldBe(userId);
            reloaded.TokenHash.Value.ShouldBe(hash.Value);
            reloaded.RevokedAt.ShouldBeNull();
            reloaded.ReplacedByTokenId.ShouldBeNull();
            reloaded.IsRotated.ShouldBeFalse();
            reloaded.ExpiresAt.ShouldBe(token.ExpiresAt, TimeSpan.FromSeconds(1));
            reloaded.CreatedAt.ShouldBe(token.CreatedAt, TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task RefreshToken_WithRevocationChain_NullableFieldsRoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — issue original and new token
        var userId = UserId.New();
        var clock = new SystemClock();
        var lifetime = TimeSpan.FromDays(7);

        var originalId = RefreshTokenId.New();
        var originalToken = RefreshToken.Issue(originalId, userId,
            TestDataBuilders.ValidRefreshTokenHash('e'), clock, lifetime);

        // Save original
        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(originalToken);
            await db.SaveChangesAsync(ct);
        }

        // Rotate — creates replacement
        RefreshTokenId replacementId;
        using (var s2 = CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == originalId, ct);
            var replacement = loaded.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('f'), clock, lifetime);
            replacementId = replacement.Id;
            db.RefreshTokens.Add(replacement);
            await db.SaveChangesAsync(ct);
        }

        // Assert — original has non-null RevokedAt and ReplacedByTokenId; replacement has both null
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var original = await db.RefreshTokens.AsNoTracking().FirstAsync(rt => rt.Id == originalId, ct);
            original.RevokedAt.ShouldNotBeNull();
            original.ReplacedByTokenId.ShouldNotBeNull();
            original.ReplacedByTokenId.ShouldBe(replacementId);

            var replacement = await db.RefreshTokens.AsNoTracking().FirstAsync(rt => rt.Id == replacementId, ct);
            replacement.RevokedAt.ShouldBeNull();
            replacement.ReplacedByTokenId.ShouldBeNull();
        }
    }

    // ── Version ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_AfterPersistence_IncrementsVersionInDatabase()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var tokenId = RefreshTokenId.New();
        var token = TestDataBuilders.ARefreshToken().WithId(tokenId).Build();

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync(ct);
        }

        // Verify initial version
        using (var check = CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var initial = await db.RefreshTokens.AsNoTracking().FirstAsync(rt => rt.Id == tokenId, ct);
            initial.Version.ShouldBe(0);
        }

        // Act — revoke
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == tokenId, ct);
            loaded.Revoke(new SystemClock());
            await db.SaveChangesAsync(ct);
        }

        // Assert — version bumped to 1
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var updated = await db.RefreshTokens.AsNoTracking().FirstAsync(rt => rt.Id == tokenId, ct);
            updated.Version.ShouldBe(1);
        }
    }

    // ── Unique constraint on token_hash ───────────────────────────────────────

    [Fact]
    public async Task RefreshToken_DuplicateTokenHash_ThrowsDbUpdateException()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — both tokens share the same hash
        var sharedHash = TestDataBuilders.ValidRefreshTokenHash('g');
        var userId = UserId.New();

        var token1 = RefreshToken.Issue(RefreshTokenId.New(), userId, sharedHash, new SystemClock(), TimeSpan.FromDays(7));
        var token2 = RefreshToken.Issue(RefreshTokenId.New(), userId, sharedHash, new SystemClock(), TimeSpan.FromDays(7));

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(token1);
            await db.SaveChangesAsync(ct);
        }

        // Act + Assert
        using (var s2 = CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(token2);

            var act = async () => await db.SaveChangesAsync(ct);

            await act.ShouldThrowAsync<DbUpdateException>();
        }
    }

    // ── Audit log for Revoke ──────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_Revoke_WritesRefreshTokenRevokedAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var userId = UserId.New();
        var tokenId = RefreshTokenId.New();

        var token = RefreshToken.Issue(tokenId, userId,
            TestDataBuilders.ValidRefreshTokenHash('h'), new SystemClock(), TimeSpan.FromDays(7));

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync(ct);
        }

        // Act — revoke
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == tokenId, ct);
            loaded.Revoke(new SystemClock());
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var revokeRows = await db.AuditLog
                .AsNoTracking()
                .Where(e => e.SubjectId == userId.Value
                         && e.EventType == AuditEventTypes.RefreshTokenRevoked)
                .ToListAsync(ct);

            revokeRows.Count.ShouldBe(1);
            revokeRows[0].ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);
            revokeRows[0].SubjectId.ShouldBe(userId.Value);
        }
    }
}
