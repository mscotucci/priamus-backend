using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.UnitTests.TestData;

/// <summary>
/// Centralized test-data builders for Identity unit tests.
/// </summary>
internal static class TestDataBuilders
{
    // ── Well-known deterministic IDs ─────────────────────────────────────────

    public static readonly UserId AdminId = new(new Guid("a0000000-0000-0000-0000-000000000001"));
    public static readonly UserId DriverUserId = new(new Guid("b0000000-0000-0000-0000-000000000002"));

    // ── Default clock ────────────────────────────────────────────────────────

    /// <summary>Returns a fresh FakeClock fixed at 2026-05-12T10:00:00Z.</summary>
    public static FakeClock DefaultClock() =>
        new(DateTime.Parse("2026-05-12T10:00:00Z").ToUniversalTime());

    // ── Valid value-object factories ─────────────────────────────────────────

    /// <summary>Creates a valid username (already normalized).</summary>
    public static Username ValidUsername(string value = "alice42") =>
        Username.Create(value);

    /// <summary>Creates a valid password hash (64 lowercase hex chars).</summary>
    public static PasswordHash ValidPasswordHash(char fill = 'a') =>
        PasswordHash.Create(new string(fill, 64));

    /// <summary>Creates a valid refresh-token hash (exactly 64 chars).</summary>
    public static RefreshTokenHash ValidRefreshTokenHash(char fill = 'b') =>
        RefreshTokenHash.Create(new string(fill, 64));

    // ── User builders ────────────────────────────────────────────────────────

    public sealed class UserBuilder
    {
        private UserId _id = UserId.New();
        private Username _username = Username.Create("defaultuser");
        private PasswordHash _passwordHash = PasswordHash.Create(new string('a', 64));
        private UserRole _role = UserRole.Driver;
        private IClock _clock = DefaultClock();
        private UserId _createdBy = AdminId;

        public UserBuilder WithId(UserId id) { _id = id; return this; }
        public UserBuilder WithUsername(string raw) { _username = Username.Create(raw); return this; }
        public UserBuilder WithPasswordHash(string hash) { _passwordHash = PasswordHash.Create(hash); return this; }
        public UserBuilder WithRole(UserRole role) { _role = role; return this; }
        public UserBuilder WithClock(IClock clock) { _clock = clock; return this; }
        public UserBuilder WithCreatedBy(UserId createdBy) { _createdBy = createdBy; return this; }

        public User Build() =>
            User.Create(_id, _username, _passwordHash, _role, _clock, _createdBy);
    }

    /// <summary>Returns a builder with sensible defaults for a Driver user.</summary>
    public static UserBuilder AUser() => new();

    // ── RefreshToken builders ────────────────────────────────────────────────

    public sealed class RefreshTokenBuilder
    {
        private RefreshTokenId _id = RefreshTokenId.New();
        private UserId _userId = DriverUserId;
        private RefreshTokenHash _hash = RefreshTokenHash.Create(new string('b', 64));
        private IClock _clock = DefaultClock();
        private TimeSpan _lifetime = TimeSpan.FromDays(7);

        public RefreshTokenBuilder WithId(RefreshTokenId id) { _id = id; return this; }
        public RefreshTokenBuilder WithUserId(UserId userId) { _userId = userId; return this; }
        public RefreshTokenBuilder WithHash(char fill) { _hash = RefreshTokenHash.Create(new string(fill, 64)); return this; }
        public RefreshTokenBuilder WithHash(string raw) { _hash = RefreshTokenHash.Create(raw); return this; }
        public RefreshTokenBuilder WithClock(IClock clock) { _clock = clock; return this; }
        public RefreshTokenBuilder WithLifetime(TimeSpan lifetime) { _lifetime = lifetime; return this; }

        public RefreshToken Build() =>
            RefreshToken.Issue(_id, _userId, _hash, _clock, _lifetime);
    }

    /// <summary>Returns a builder with sensible defaults for a fresh RefreshToken.</summary>
    public static RefreshTokenBuilder ARefreshToken() => new();
}
