using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.Events;
using Priamus.Trips.Identity.UnitTests.TestData;
using Priamus.Trips.SharedKernel.Security;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.UnitTests.Features.EnableUser;

/// <summary>
/// Unit tests for the <c>EnableUser</c> slice covering the domain-level paths that
/// do not require a running database.
///
/// Paths tested here (pure domain, no I/O required):
/// - Happy path: a disabled User transitions to Active and emits exactly one
///   <see cref="UserEnabled"/> domain event.
/// - Idempotency: a second <c>Enable</c> on an already-active User does not
///   raise a new <see cref="UserEnabled"/> event and does not bump <c>Version</c>.
/// - Calling <c>Enable</c> on a freshly-created (Active) user keeps Status
///   unchanged and raises no event — this is the aggregate invariant that the
///   handler's idempotent no-op relies on.
///
/// Paths that require a real database (covered in the integration project):
/// - Handler happy path (user found, disabled → Active, audit row written).
/// - Not found (no matching row → <see cref="UserErrors.NotFound"/>).
/// - Idempotent DB round-trip (already-enabled → 204, no new audit row).
/// </summary>
public sealed class EnableUserHandlerTests
{
    // ── Test 1: Domain-level happy path: Enable raises UserEnabled event ──────

    [Fact]
    public void Enable_WhenCalledOnDisabledUser_RaisesUserEnabledEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        var clock = TestDataBuilders.DefaultClock();

        // Put the user into the Disabled state first, then clear events so we only
        // observe events raised by the Enable call under test.
        user.Disable(clock, TestDataBuilders.AdminId);
        user.ClearDomainEvents();

        // Act
        user.Enable(clock, TestDataBuilders.AdminId);

        // Assert
        user.Status.ShouldBe(UserStatus.Active,
            "Enable on a disabled user must transition Status to Active.");
        user.DomainEvents.Count.ShouldBe(1,
            "Enable on a disabled user must raise exactly one UserEnabled domain event.");
        var evt = user.DomainEvents[0].ShouldBeOfType<UserEnabled>();
        evt.UserId.ShouldBe(userId,
            "The UserEnabled event must carry the id of the user that was enabled.");
        evt.OccurredAt.ShouldBe(clock.UtcNow,
            "The UserEnabled event must be timestamped with the clock's current instant.");
    }

    // ── Test 2: Domain-level idempotency of User.Enable ───────────────────────
    //
    // Verifies the regression-sensitive guarantee that the User aggregate emits no
    // UserEnabled event on a second Enable call. This is the invariant that the
    // handler's idempotent no-op behaviour (no audit row on re-enable) depends on.

    [Fact]
    public void Enable_WhenCalledTwiceOnSameUser_DoesNotRaiseSecondEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        var clock = TestDataBuilders.DefaultClock();

        // Transition to Disabled then back to Active for the first Enable.
        user.Disable(clock, TestDataBuilders.AdminId);
        user.Enable(clock, TestDataBuilders.AdminId);
        var versionAfterFirstEnable = user.Version;
        user.ClearDomainEvents();

        // Act — second Enable on an already-active user
        user.Enable(clock, TestDataBuilders.AdminId);

        // Assert — idempotent: no new event, no version bump
        user.DomainEvents.Count.ShouldBe(0,
            "A second Enable on an already-active user must not raise a new UserEnabled event.");
        user.Version.ShouldBe(versionAfterFirstEnable,
            "Version must not increment on an idempotent no-op Enable.");
    }

    // ── Test 3: Enable on an Active user (factory default) is a no-op ─────────

    [Fact]
    public void Enable_WhenCalledOnActiveUser_KeepsStatusActiveAndRaisesNoEvent()
    {
        // Arrange
        var userId = UserId.New();
        var user = TestDataBuilders.AUser().WithId(userId).Build();
        var clock = TestDataBuilders.DefaultClock();
        // Clear the UserCreated event raised by Build() to isolate the Enable call.
        user.ClearDomainEvents();

        // Act
        user.Enable(clock, TestDataBuilders.AdminId);

        // Assert
        user.Status.ShouldBe(UserStatus.Active,
            "Enable on an already-active user must leave Status unchanged.");
        user.DomainEvents.Count.ShouldBe(0,
            "Enable on an already-active user must not raise any domain event.");
    }
}
