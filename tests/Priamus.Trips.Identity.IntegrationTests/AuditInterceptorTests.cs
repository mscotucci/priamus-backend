using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests;

/// <summary>
/// Tests that every domain event produces the correct <see cref="AuditLogEntry"/> row,
/// and that idempotent no-ops produce ZERO rows.
/// Per ADR 0009, tests interact with <see cref="IdentityDbContext"/> directly.
/// </summary>
public sealed class AuditInterceptorTests : IdentityIntegrationTestBase
{
    public AuditInterceptorTests(PostgresFixture fixture) : base(fixture) { }

    // ── UserCreated ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UserCreated_ProducesAuditRowWithCorrectShape()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).WithRole(UserRole.Driver).Build();

        // Act
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var entry = await db.AuditLog.AsNoTracking()
                .SingleOrDefaultAsync(e => e.SubjectId == userId.Value
                                        && e.EventType == AuditEventTypes.UserCreated, ct);

            entry.ShouldNotBeNull();
            entry.EventType.ShouldBe(AuditEventTypes.UserCreated);
            entry.SubjectId.ShouldBe(userId.Value);
            entry.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);

            using var doc = JsonDocument.Parse(entry.Payload);
            doc.RootElement.GetProperty("userId").GetProperty("value").GetGuid().ShouldBe(userId.Value);
            doc.RootElement.GetProperty("role").GetInt32().ShouldBe((int)UserRole.Driver);
        }
    }

    // ── UserPasswordChanged ──────────────────────────────────────────────────

    [Fact]
    public async Task UserPasswordChanged_ProducesAuditRowWithCorrectShape()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — create and save a user
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Act — change password in new scope
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.ChangePassword(PasswordHash.Create(new string('z', 64)), new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var entry = await db.AuditLog.AsNoTracking()
                .SingleOrDefaultAsync(e => e.SubjectId == userId.Value
                                        && e.EventType == AuditEventTypes.UserPasswordChanged, ct);

            entry.ShouldNotBeNull();
            entry.EventType.ShouldBe(AuditEventTypes.UserPasswordChanged);
            entry.SubjectId.ShouldBe(userId.Value);
            entry.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);

            using var doc = JsonDocument.Parse(entry.Payload);
            doc.RootElement.GetProperty("userId").GetProperty("value").GetGuid().ShouldBe(userId.Value);
        }
    }

    // ── UserDisabled ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UserDisabled_ProducesAuditRowWithCorrectShape()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Act
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.Disable(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var entry = await db.AuditLog.AsNoTracking()
                .SingleOrDefaultAsync(e => e.SubjectId == userId.Value
                                        && e.EventType == AuditEventTypes.UserDisabled, ct);

            entry.ShouldNotBeNull();
            entry.EventType.ShouldBe(AuditEventTypes.UserDisabled);
            entry.SubjectId.ShouldBe(userId.Value);
            entry.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);

            using var doc = JsonDocument.Parse(entry.Payload);
            doc.RootElement.GetProperty("userId").GetProperty("value").GetGuid().ShouldBe(userId.Value);
        }
    }

    // ── UserEnabled ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UserEnabled_ProducesAuditRowWithCorrectShape()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — create, save, disable
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        using (var s2 = CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.Disable(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Act — enable
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.Enable(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var entry = await db.AuditLog.AsNoTracking()
                .SingleOrDefaultAsync(e => e.SubjectId == userId.Value
                                        && e.EventType == AuditEventTypes.UserEnabled, ct);

            entry.ShouldNotBeNull();
            entry.EventType.ShouldBe(AuditEventTypes.UserEnabled);
            entry.SubjectId.ShouldBe(userId.Value);
            entry.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);

            using var doc = JsonDocument.Parse(entry.Payload);
            doc.RootElement.GetProperty("userId").GetProperty("value").GetGuid().ShouldBe(userId.Value);
        }
    }

    // ── RefreshTokenIssued ───────────────────────────────────────────────────

    [Fact]
    public async Task RefreshTokenIssued_ProducesAuditRowWithCorrectShape()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var userId = UserId.New();
        var tokenId = RefreshTokenId.New();
        var token = RefreshToken.Issue(tokenId, userId,
            TestDataBuilders.ValidRefreshTokenHash('i'), new SystemClock(), TimeSpan.FromDays(7));

        // Act
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var entry = await db.AuditLog.AsNoTracking()
                .SingleOrDefaultAsync(e => e.SubjectId == userId.Value
                                        && e.EventType == AuditEventTypes.RefreshTokenIssued, ct);

            entry.ShouldNotBeNull();
            entry.EventType.ShouldBe(AuditEventTypes.RefreshTokenIssued);
            entry.SubjectId.ShouldBe(userId.Value);
            entry.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);

            using var doc = JsonDocument.Parse(entry.Payload);
            doc.RootElement.GetProperty("userId").GetProperty("value").GetGuid().ShouldBe(userId.Value);
            doc.RootElement.GetProperty("refreshTokenId").GetProperty("value").GetGuid().ShouldBe(tokenId.Value);
        }
    }

    // ── RefreshTokenRotated ──────────────────────────────────────────────────

    [Fact]
    public async Task RefreshTokenRotated_ProducesAuditRowWithCorrectShape()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — issue original token
        var userId = UserId.New();
        var oldTokenId = RefreshTokenId.New();
        var originalToken = RefreshToken.Issue(oldTokenId, userId,
            TestDataBuilders.ValidRefreshTokenHash('j'), new SystemClock(), TimeSpan.FromDays(7));

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(originalToken);
            await db.SaveChangesAsync(ct);
        }

        // Act — rotate
        RefreshTokenId newTokenId;
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == oldTokenId, ct);
            var replacement = loaded.Rotate(RefreshTokenId.New(), TestDataBuilders.ValidRefreshTokenHash('k'),
                new SystemClock(), TimeSpan.FromDays(7));
            newTokenId = replacement.Id;
            db.RefreshTokens.Add(replacement);
            await db.SaveChangesAsync(ct);
        }

        // Assert
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var entry = await db.AuditLog.AsNoTracking()
                .SingleOrDefaultAsync(e => e.SubjectId == userId.Value
                                        && e.EventType == AuditEventTypes.RefreshTokenRotated, ct);

            entry.ShouldNotBeNull();
            entry.EventType.ShouldBe(AuditEventTypes.RefreshTokenRotated);
            entry.SubjectId.ShouldBe(userId.Value);
            entry.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);

            using var doc = JsonDocument.Parse(entry.Payload);
            doc.RootElement.GetProperty("userId").GetProperty("value").GetGuid().ShouldBe(userId.Value);
            doc.RootElement.GetProperty("oldRefreshTokenId").GetProperty("value").GetGuid().ShouldBe(oldTokenId.Value);
            doc.RootElement.GetProperty("newRefreshTokenId").GetProperty("value").GetGuid().ShouldBe(newTokenId.Value);
        }
    }

    // ── RefreshTokenRevoked ──────────────────────────────────────────────────

    [Fact]
    public async Task RefreshTokenRevoked_ProducesAuditRowWithCorrectShape()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var userId = UserId.New();
        var tokenId = RefreshTokenId.New();
        var token = RefreshToken.Issue(tokenId, userId,
            TestDataBuilders.ValidRefreshTokenHash('l'), new SystemClock(), TimeSpan.FromDays(7));

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

            var entry = await db.AuditLog.AsNoTracking()
                .SingleOrDefaultAsync(e => e.SubjectId == userId.Value
                                        && e.EventType == AuditEventTypes.RefreshTokenRevoked, ct);

            entry.ShouldNotBeNull();
            entry.EventType.ShouldBe(AuditEventTypes.RefreshTokenRevoked);
            entry.SubjectId.ShouldBe(userId.Value);
            entry.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value);

            using var doc = JsonDocument.Parse(entry.Payload);
            doc.RootElement.GetProperty("userId").GetProperty("value").GetGuid().ShouldBe(userId.Value);
            doc.RootElement.GetProperty("refreshTokenId").GetProperty("value").GetGuid().ShouldBe(tokenId.Value);
        }
    }

    // ── Idempotency: no audit row on no-op ───────────────────────────────────

    /// <summary>
    /// Regression guard: Revoke on an already-revoked (but not rotated) token must
    /// NOT produce a new audit row. This is the correctly-implemented idempotency
    /// path in RefreshToken.Revoke.
    /// </summary>
    [Fact]
    public async Task Revoke_OnAlreadyRevokedToken_ProducesZeroNewAuditRows()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — issue and revoke the token
        var userId = UserId.New();
        var tokenId = RefreshTokenId.New();
        var token = RefreshToken.Issue(tokenId, userId,
            TestDataBuilders.ValidRefreshTokenHash('m'), new SystemClock(), TimeSpan.FromDays(7));

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync(ct);
        }

        using (var s2 = CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == tokenId, ct);
            loaded.Revoke(new SystemClock());
            await db.SaveChangesAsync(ct);
        }

        // Record count before second revoke attempt
        int auditCountAfterFirstRevoke;
        using (var check = CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            auditCountAfterFirstRevoke = await db.AuditLog.AsNoTracking()
                .CountAsync(e => e.SubjectId == userId.Value && e.EventType == AuditEventTypes.RefreshTokenRevoked, ct);
        }
        auditCountAfterFirstRevoke.ShouldBe(1);

        // Act — attempt second revoke (idempotent: no domain event raised, no audit row)
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == tokenId, ct);
            loaded.Revoke(new SystemClock()); // no-op: RevokedAt already set, early return
            await db.SaveChangesAsync(ct);
        }

        // Assert — still only 1 revoke audit row
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var count = await db.AuditLog.AsNoTracking()
                .CountAsync(e => e.SubjectId == userId.Value && e.EventType == AuditEventTypes.RefreshTokenRevoked, ct);
            count.ShouldBe(1);
        }
    }

    /// <summary>
    /// Regression guard: Disable on an already-disabled user must NOT produce a new audit row.
    /// NOTE: This test exposes a production bug. As of the current implementation, Disable()
    /// does NOT have an early return guard for the already-disabled case, so it always raises
    /// an event and this test will FAIL until the production code is fixed.
    /// </summary>
    [Fact]
    public async Task Disable_OnAlreadyDisabledUser_ProducesZeroNewAuditRows()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — create and disable the user
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        using (var s2 = CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.Disable(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Record count after first disable
        int countAfterFirstDisable;
        using (var check = CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            countAfterFirstDisable = await db.AuditLog.AsNoTracking()
                .CountAsync(e => e.SubjectId == userId.Value && e.EventType == AuditEventTypes.UserDisabled, ct);
        }
        countAfterFirstDisable.ShouldBe(1);

        // Act — second Disable (should be idempotent)
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.Disable(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Assert — still only 1 disable audit row
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var count = await db.AuditLog.AsNoTracking()
                .CountAsync(e => e.SubjectId == userId.Value && e.EventType == AuditEventTypes.UserDisabled, ct);
            count.ShouldBe(1);
        }
    }

    /// <summary>
    /// Regression guard: Enable on an already-active user must NOT produce a new audit row.
    /// NOTE: This test exposes a production bug. Same root cause as Disable idempotency.
    /// This test will FAIL until the production code is fixed.
    /// </summary>
    [Fact]
    public async Task Enable_OnAlreadyActiveUser_ProducesZeroNewAuditRows()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange — user is Active by default after Create
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();

        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Act — Enable on an already-active user
        using (var actScope = CreateScope())
        {
            var db = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.Users.FirstAsync(u => u.Id == userId, ct);
            loaded.Enable(new SystemClock(), FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(ct);
        }

        // Assert — zero UserEnabled audit rows produced
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var count = await db.AuditLog.AsNoTracking()
                .CountAsync(e => e.SubjectId == userId.Value && e.EventType == AuditEventTypes.UserEnabled, ct);
            count.ShouldBe(0);
        }
    }
}
