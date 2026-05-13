using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.Events;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Domain.Users;

public sealed class UserTests
{
    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidArguments_ReturnsActiveUser()
    {
        // Arrange
        var id = UserId.New();
        var username = TestDataBuilders.ValidUsername("bob99");
        var hash = TestDataBuilders.ValidPasswordHash();
        var clock = TestDataBuilders.DefaultClock();
        var createdBy = TestDataBuilders.AdminId;

        // Act
        var user = User.Create(id, username, hash, UserRole.Driver, clock, createdBy);

        // Assert
        user.Id.ShouldBe(id);
        user.Username.ShouldBe(username);
        user.PasswordHash.ShouldBe(hash);
        user.Role.ShouldBe(UserRole.Driver);
        user.Status.ShouldBe(UserStatus.Active);
        user.CreatedBy.ShouldBe(createdBy);
        user.LastModifiedBy.ShouldBe(createdBy);
        user.CreatedAt.ShouldBe(clock.UtcNow);
        user.LastModifiedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Create_WithValidArguments_RaisesUserCreatedEvent()
    {
        // Arrange
        var id = UserId.New();
        var clock = TestDataBuilders.DefaultClock();

        // Act
        var user = User.Create(id, TestDataBuilders.ValidUsername(), TestDataBuilders.ValidPasswordHash(),
            UserRole.BackOffice, clock, TestDataBuilders.AdminId);

        // Assert
        user.DomainEvents.Count.ShouldBe(1);
        var evt = user.DomainEvents[0].ShouldBeOfType<UserCreated>();
        evt.UserId.ShouldBe(id);
        evt.Role.ShouldBe(UserRole.BackOffice);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Create_WithVersionZero_HasDefaultVersion()
    {
        // Arrange / Act
        var user = TestDataBuilders.AUser().Build();

        // Assert
        user.Version.ShouldBe(0);
    }

    [Fact]
    public void Create_WithNullUsername_ThrowsArgumentNullException()
    {
        // Arrange
        var id = UserId.New();
        var clock = TestDataBuilders.DefaultClock();

        // Act
        var act = () => User.Create(id, null!, TestDataBuilders.ValidPasswordHash(),
            UserRole.Driver, clock, TestDataBuilders.AdminId);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithNullPasswordHash_ThrowsArgumentNullException()
    {
        // Arrange
        var id = UserId.New();
        var clock = TestDataBuilders.DefaultClock();

        // Act
        var act = () => User.Create(id, TestDataBuilders.ValidUsername(), null!,
            UserRole.Driver, clock, TestDataBuilders.AdminId);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange
        var id = UserId.New();

        // Act
        var act = () => User.Create(id, TestDataBuilders.ValidUsername(), TestDataBuilders.ValidPasswordHash(),
            UserRole.Driver, null!, TestDataBuilders.AdminId);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Theory]
    [InlineData(UserRole.Driver)]
    [InlineData(UserRole.BackOffice)]
    [InlineData(UserRole.Admin)]
    public void Create_WithAnyRole_SetsRoleCorrectly(UserRole role)
    {
        // Arrange
        var clock = TestDataBuilders.DefaultClock();

        // Act
        var user = User.Create(UserId.New(), TestDataBuilders.ValidUsername(), TestDataBuilders.ValidPasswordHash(),
            role, clock, TestDataBuilders.AdminId);

        // Assert
        user.Role.ShouldBe(role);
    }

    // ── ChangePassword ───────────────────────────────────────────────────────

    [Fact]
    public void ChangePassword_OnActiveUser_UpdatesPasswordHash()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var newHash = TestDataBuilders.ValidPasswordHash('z');
        var clock = TestDataBuilders.DefaultClock();
        var modifier = TestDataBuilders.AdminId;

        // Act
        user.ChangePassword(newHash, clock, modifier);

        // Assert
        user.PasswordHash.ShouldBe(newHash);
        user.LastModifiedBy.ShouldBe(modifier);
        user.LastModifiedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void ChangePassword_OnActiveUser_IncrementsVersion()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var initialVersion = user.Version;

        // Act
        user.ChangePassword(TestDataBuilders.ValidPasswordHash('z'), TestDataBuilders.DefaultClock(), TestDataBuilders.AdminId);

        // Assert
        user.Version.ShouldBe(initialVersion + 1);
    }

    [Fact]
    public void ChangePassword_OnActiveUser_RaisesUserPasswordChangedEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        user.ClearDomainEvents();
        var clock = TestDataBuilders.DefaultClock();

        // Act
        user.ChangePassword(TestDataBuilders.ValidPasswordHash('z'), clock, TestDataBuilders.AdminId);

        // Assert
        user.DomainEvents.Count.ShouldBe(1);
        var evt = user.DomainEvents[0].ShouldBeOfType<UserPasswordChanged>();
        evt.UserId.ShouldBe(userId);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void ChangePassword_OnDisabledUser_ThrowsDomainException()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var clock = TestDataBuilders.DefaultClock();
        user.Disable(clock, TestDataBuilders.AdminId);
        user.ClearDomainEvents();

        // Act
        var act = () => user.ChangePassword(TestDataBuilders.ValidPasswordHash('z'), clock, TestDataBuilders.AdminId);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.CannotChangePasswordOnDisabledUser.Code);
    }

    [Fact]
    public void ChangePassword_WithNullHash_ThrowsArgumentNullException()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();

        // Act
        var act = () => user.ChangePassword(null!, TestDataBuilders.DefaultClock(), TestDataBuilders.AdminId);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void ChangePassword_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();

        // Act
        var act = () => user.ChangePassword(TestDataBuilders.ValidPasswordHash('z'), null!, TestDataBuilders.AdminId);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    // ── Disable ──────────────────────────────────────────────────────────────

    [Fact]
    public void Disable_OnActiveUser_SetsStatusToDisabled()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var clock = TestDataBuilders.DefaultClock();

        // Act
        user.Disable(clock, TestDataBuilders.AdminId);

        // Assert
        user.Status.ShouldBe(UserStatus.Disabled);
    }

    [Fact]
    public void Disable_OnActiveUser_IncrementsVersion()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var initialVersion = user.Version;

        // Act
        user.Disable(TestDataBuilders.DefaultClock(), TestDataBuilders.AdminId);

        // Assert
        user.Version.ShouldBe(initialVersion + 1);
    }

    [Fact]
    public void Disable_OnActiveUser_RaisesUserDisabledEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        user.ClearDomainEvents();
        var clock = TestDataBuilders.DefaultClock();

        // Act
        user.Disable(clock, TestDataBuilders.AdminId);

        // Assert
        user.DomainEvents.Count.ShouldBe(1);
        var evt = user.DomainEvents[0].ShouldBeOfType<UserDisabled>();
        evt.UserId.ShouldBe(userId);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    /// <summary>
    /// Regression guard: Disable on an already-disabled user must be idempotent —
    /// no event raised, no version bump.
    /// NOTE: This test exposes a production bug. As of the current implementation,
    /// Disable() calls EnsureActive(false) which is always a no-op (never returns
    /// early), so the status mutation, IncrementVersion(), and RaiseEvent() execute
    /// unconditionally. The test will FAIL until the production guard is added.
    /// </summary>
    [Fact]
    public void Disable_OnAlreadyDisabledUser_IsIdempotent_NoEventRaised()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var clock = TestDataBuilders.DefaultClock();
        user.Disable(clock, TestDataBuilders.AdminId);
        var versionAfterFirstDisable = user.Version;
        user.ClearDomainEvents();

        // Act — second Disable on an already-disabled user
        user.Disable(clock, TestDataBuilders.AdminId);

        // Assert — idempotent: no new event, no version bump
        user.DomainEvents.Count.ShouldBe(0);
        user.Version.ShouldBe(versionAfterFirstDisable);
    }

    [Fact]
    public void Disable_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();

        // Act
        var act = () => user.Disable(null!, TestDataBuilders.AdminId);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    // ── Enable ───────────────────────────────────────────────────────────────

    [Fact]
    public void Enable_OnDisabledUser_SetsStatusToActive()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var clock = TestDataBuilders.DefaultClock();
        user.Disable(clock, TestDataBuilders.AdminId);
        user.ClearDomainEvents();

        // Act
        user.Enable(clock, TestDataBuilders.AdminId);

        // Assert
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void Enable_OnDisabledUser_IncrementsVersion()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var clock = TestDataBuilders.DefaultClock();
        user.Disable(clock, TestDataBuilders.AdminId);
        var versionAfterDisable = user.Version;
        user.ClearDomainEvents();

        // Act
        user.Enable(clock, TestDataBuilders.AdminId);

        // Assert
        user.Version.ShouldBe(versionAfterDisable + 1);
    }

    [Fact]
    public void Enable_OnDisabledUser_RaisesUserEnabledEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        var clock = TestDataBuilders.DefaultClock();
        user.Disable(clock, TestDataBuilders.AdminId);
        user.ClearDomainEvents();

        // Act
        user.Enable(clock, TestDataBuilders.AdminId);

        // Assert
        user.DomainEvents.Count.ShouldBe(1);
        var evt = user.DomainEvents[0].ShouldBeOfType<UserEnabled>();
        evt.UserId.ShouldBe(userId);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    /// <summary>
    /// Regression guard: Enable on an already-active user must be idempotent —
    /// no event raised, no version bump.
    /// NOTE: This test exposes a production bug. As of the current implementation,
    /// Enable() calls EnsureActive(false) which is always a no-op (never returns
    /// early), so IncrementVersion() and RaiseEvent() execute unconditionally even
    /// when the user is already active. The test will FAIL until fixed.
    /// </summary>
    [Fact]
    public void Enable_OnAlreadyActiveUser_IsIdempotent_NoEventRaised()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build(); // already Active by construction
        var clock = TestDataBuilders.DefaultClock();
        user.ClearDomainEvents();
        var versionBeforeEnable = user.Version;

        // Act — Enable on an already-active user
        user.Enable(clock, TestDataBuilders.AdminId);

        // Assert — idempotent: no new event, no version bump
        user.DomainEvents.Count.ShouldBe(0);
        user.Version.ShouldBe(versionBeforeEnable);
    }

    [Fact]
    public void Enable_WithNullClock_ThrowsArgumentNullException()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();

        // Act
        var act = () => user.Enable(null!, TestDataBuilders.AdminId);

        // Assert
        act.ShouldThrow<ArgumentNullException>();
    }

    // ── Disable → Enable → ChangePassword lifecycle round-trip ───────────────

    [Fact]
    public void LifecycleRoundTrip_DisableThenEnableThenChangePassword_SucceedsAndAccumulatesVersionBumps()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var clock = TestDataBuilders.DefaultClock();
        var initialVersion = user.Version;

        // Act
        user.Disable(clock, TestDataBuilders.AdminId);
        user.Enable(clock, TestDataBuilders.AdminId);
        user.ChangePassword(TestDataBuilders.ValidPasswordHash('z'), clock, TestDataBuilders.AdminId);

        // Assert
        user.Status.ShouldBe(UserStatus.Active);
        user.Version.ShouldBe(initialVersion + 3);
    }

    [Fact]
    public void AfterDisable_ChangePasswordThrowsDomainException()
    {
        // Arrange
        var user = TestDataBuilders.AUser().Build();
        var clock = TestDataBuilders.DefaultClock();
        user.Disable(clock, TestDataBuilders.AdminId);

        // Act
        var act = () => user.ChangePassword(TestDataBuilders.ValidPasswordHash('z'), clock, TestDataBuilders.AdminId);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(UserErrors.CannotChangePasswordOnDisabledUser.Code);
    }
}
