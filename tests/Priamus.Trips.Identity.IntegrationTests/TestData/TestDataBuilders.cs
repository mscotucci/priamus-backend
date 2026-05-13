using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.IntegrationTests.TestData;

/// <summary>
/// Centralized test-data builders for Identity integration tests.
/// Integration tests use SystemClock-based time because their scope is persistence
/// correctness — time determinism is not required for round-trip assertions.
/// </summary>
internal static class TestDataBuilders
{
    // ── Known deterministic IDs ──────────────────────────────────────────────

    /// <summary>Actor id that matches FakeCurrentUser.ActorId for audit assertions.</summary>
    public static readonly UserId ActorId = FakeCurrentUser.ActorId;

    // ── Value-object helpers ─────────────────────────────────────────────────

    public static PasswordHash ValidPasswordHash(char fill = 'a') =>
        PasswordHash.Create(new string(fill, 64));

    public static RefreshTokenHash ValidRefreshTokenHash(char fill = 'b') =>
        RefreshTokenHash.Create(new string(fill, 64));

    // ── User builder ─────────────────────────────────────────────────────────

    public sealed class UserBuilder
    {
        private UserId _id = UserId.New();
        private string _username = "integtest" + Guid.NewGuid().ToString("N")[..8];
        private PasswordHash _passwordHash = PasswordHash.Create(new string('a', 64));
        private UserRole _role = UserRole.Driver;
        private UserId _createdBy = FakeCurrentUser.ActorId;

        public UserBuilder WithId(UserId id) { _id = id; return this; }
        public UserBuilder WithUsername(string username) { _username = username; return this; }
        public UserBuilder WithPasswordHash(string hash) { _passwordHash = PasswordHash.Create(hash); return this; }
        public UserBuilder WithRole(UserRole role) { _role = role; return this; }
        public UserBuilder WithCreatedBy(UserId createdBy) { _createdBy = createdBy; return this; }

        public User Build() =>
            User.Create(_id, Username.Create(_username), _passwordHash, _role, new SystemClock(), _createdBy);
    }

    public static UserBuilder AUser() => new();

    // ── RefreshToken builder ─────────────────────────────────────────────────

    public sealed class RefreshTokenBuilder
    {
        private RefreshTokenId _id = RefreshTokenId.New();
        private UserId _userId = UserId.New();
        private RefreshTokenHash _hash = RefreshTokenHash.Create(new string('b', 64));
        private TimeSpan _lifetime = TimeSpan.FromDays(7);

        public RefreshTokenBuilder WithId(RefreshTokenId id) { _id = id; return this; }
        public RefreshTokenBuilder WithUserId(UserId userId) { _userId = userId; return this; }
        public RefreshTokenBuilder WithHash(char fill) { _hash = RefreshTokenHash.Create(new string(fill, 64)); return this; }
        public RefreshTokenBuilder WithHash(string raw) { _hash = RefreshTokenHash.Create(raw); return this; }
        public RefreshTokenBuilder WithLifetime(TimeSpan lifetime) { _lifetime = lifetime; return this; }

        public RefreshToken Build() =>
            RefreshToken.Issue(_id, _userId, _hash, new SystemClock(), _lifetime);
    }

    public static RefreshTokenBuilder ARefreshToken() => new();
}
