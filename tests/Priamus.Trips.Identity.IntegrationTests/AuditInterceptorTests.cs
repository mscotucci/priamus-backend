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
            // The payload still carries the role as its integer value, although the column now
            // stores the member name.
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

    // ── Refresh tokens: no audit rows ────────────────────────────────────────

    /// <summary>
    /// Issuing, rotating and revoking a refresh token (and a repeated revoke) write
    /// no audit row at all; the handlers' own rows are the only trace of a session.
    /// </summary>
    [Fact]
    public async Task IssueRotateRevoke_WriteNoAuditRows()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var userId = UserId.New();
        var firstId = RefreshTokenId.New();
        var secondId = RefreshTokenId.New();

        // Act — issue
        using (var s1 = CreateScope())
        {
            var db = s1.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.RefreshTokens.Add(RefreshToken.Issue(firstId, userId,
                TestDataBuilders.ValidRefreshTokenHash('i'), new SystemClock(), TimeSpan.FromDays(7)));
            await db.SaveChangesAsync(ct);
        }

        // Act — rotate
        using (var s2 = CreateScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == firstId, ct);
            db.RefreshTokens.Add(loaded.Rotate(secondId, TestDataBuilders.ValidRefreshTokenHash('j'),
                new SystemClock(), TimeSpan.FromDays(7)));
            await db.SaveChangesAsync(ct);
        }

        // Act — revoke, then revoke again (idempotent)
        for (var i = 0; i < 2; i++)
        {
            using var s3 = CreateScope();
            var db = s3.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var loaded = await db.RefreshTokens.FirstAsync(rt => rt.Id == secondId, ct);
            loaded.Revoke(new SystemClock());
            await db.SaveChangesAsync(ct);
        }

        // Assert — no audit row for this user, and both tokens are really persisted
        using (var assertScope = CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.AuditLog.AsNoTracking().CountAsync(e => e.SubjectId == userId.Value, ct)).ShouldBe(0);
            (await db.RefreshTokens.AsNoTracking().CountAsync(rt => rt.UserId == userId, ct)).ShouldBe(2);
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
