using Priamus.Trips.Identity.Domain.Users.Events;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Domain.Users;

/// <summary>
/// Authenticated principal of the system. Owns the login identifier (<see cref="Username"/>),
/// the opaque password hash, the functional <see cref="UserRole"/>, and the lifecycle
/// <see cref="UserStatus"/>.
/// </summary>
/// <remarks>
/// <para>
/// Password verification is NOT a domain concern — it lives in the application layer
/// where <c>IPasswordHasher&lt;User&gt;</c> is injected. The aggregate only exposes the
/// <see cref="PasswordHash"/> value object.
/// </para>
/// <para>
/// <see cref="RefreshToken"/> is a separate aggregate (ADR 0005); cross-aggregate
/// references are bare typed IDs with no navigation properties.
/// </para>
/// <para>
/// Per ADR 0008, the User↔Driver link is owned by the Registry module
/// (<c>Driver.UserId</c>), NOT by Identity. The JWT <c>driver_id</c> claim is populated
/// at login time by the Identity Login slice via an <c>IDriverLookup</c> abstraction
/// exposed in <c>Registry.Contracts</c>. The <see cref="UserRole.Driver"/> value here
/// only states "this principal is a driver" — it does not say which driver.
/// </para>
/// </remarks>
public sealed class User : AggregateRoot
{
    /// <summary>EF only.</summary>
    private User()
    {
        // EF only. Members are populated by EF Core via property setters/backing fields.
        Username = null!;
        PasswordHash = null!;
    }

    private User(
        UserId id,
        Username username,
        PasswordHash passwordHash,
        UserRole role,
        UserStatus status,
        DateTime createdAt,
        UserId createdBy)
    {
        Id = id;
        Username = username;
        PasswordHash = passwordHash;
        Role = role;
        Status = status;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        LastModifiedAt = createdAt;
        LastModifiedBy = createdBy;
    }

    /// <summary>Strongly-typed identifier.</summary>
    public UserId Id { get; private set; }

    /// <summary>Normalized login identifier (see <see cref="ValueObjects.Username"/>).</summary>
    public Username Username { get; private set; }

    /// <summary>Opaque password hash (see <see cref="ValueObjects.PasswordHash"/>).</summary>
    public PasswordHash PasswordHash { get; private set; }

    /// <summary>Functional role.</summary>
    public UserRole Role { get; private set; }

    /// <summary>Lifecycle state.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>UTC instant at which the user was created.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Identifier of the actor (admin or back-office user) that created this user.</summary>
    public UserId CreatedBy { get; private set; }

    /// <summary>UTC instant at which the user was last modified.</summary>
    public DateTime LastModifiedAt { get; private set; }

    /// <summary>Identifier of the actor that last modified this user.</summary>
    public UserId LastModifiedBy { get; private set; }

    /// <summary>
    /// Creates a new user and stamps audit fields.
    /// </summary>
    /// <param name="id">Identifier of the new user.</param>
    /// <param name="username">Normalized username value object.</param>
    /// <param name="passwordHash">Opaque hash computed by the application-layer hasher.</param>
    /// <param name="role">Functional role.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="createdBy">Actor performing the creation.</param>
    public static User Create(
        UserId id,
        Username username,
        PasswordHash passwordHash,
        UserRole role,
        IClock clock,
        UserId createdBy)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var user = new User(id, username, passwordHash, role, UserStatus.Active, now, createdBy);
        user.RaiseEvent(new UserCreated(Guid.NewGuid(), now, id, role));
        return user;
    }

    /// <summary>
    /// Rotates the password hash. Allowed only on an <see cref="UserStatus.Active"/> user.
    /// </summary>
    /// <param name="newHash">The new opaque hash produced by the application-layer hasher.</param>
    /// <param name="clock">Time source for audit and event timestamping.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    /// <exception cref="DomainException">
    /// Thrown with <see cref="UserErrors.CannotChangePasswordOnDisabledUser"/> when
    /// the user is disabled.
    /// </exception>
    public void ChangePassword(PasswordHash newHash, IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(newHash);
        ArgumentNullException.ThrowIfNull(clock);

        // Identity domain brief: disabled users cannot rotate their password.
        EnsureActive();

        PasswordHash = newHash;

        var now = clock.UtcNow;
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
        RaiseEvent(new UserPasswordChanged(Guid.NewGuid(), now, Id));
    }

    /// <summary>
    /// Disables the user. Idempotent: a no-op on already-disabled users does not
    /// raise an event and does not bump <c>Version</c>.
    /// </summary>
    /// <param name="clock">Time source for audit and event timestamping.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Disable(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is UserStatus.Disabled)
        {
            return;
        }

        Status = UserStatus.Disabled;

        var now = clock.UtcNow;
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
        RaiseEvent(new UserDisabled(Guid.NewGuid(), now, Id));
    }

    /// <summary>
    /// Enables the user. Idempotent: a no-op on already-active users does not raise
    /// an event and does not bump <c>Version</c>.
    /// </summary>
    /// <param name="clock">Time source for audit and event timestamping.</param>
    /// <param name="modifiedBy">Actor performing the change.</param>
    public void Enable(IClock clock, UserId modifiedBy)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is UserStatus.Active)
        {
            return;
        }

        Status = UserStatus.Active;

        var now = clock.UtcNow;
        LastModifiedAt = now;
        LastModifiedBy = modifiedBy;
        IncrementVersion();
        RaiseEvent(new UserEnabled(Guid.NewGuid(), now, Id));
    }

    /// <summary>
    /// Ensures the user is active.
    /// </summary>
    /// <exception cref="DomainException"></exception>
    private void EnsureActive()
    {
        if (Status is UserStatus.Disabled)
        {
            throw new DomainException(UserErrors.CannotChangePasswordOnDisabledUser);
        }
    }
}
