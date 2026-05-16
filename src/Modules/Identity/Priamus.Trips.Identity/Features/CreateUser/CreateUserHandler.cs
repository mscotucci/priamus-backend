using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Features.CreateUser;

/// <summary>
/// Handles <c>POST /api/users</c>: creates a new user account.
/// </summary>
/// <remarks>
/// Race-condition safety net: a uniqueness check is performed upfront, but if two
/// concurrent requests slip through, the unique index constraint (<c>uq_users_username</c>)
/// is caught on <see cref="DbUpdateException"/> and translated to
/// <see cref="UserErrors.UsernameAlreadyTaken"/>.
/// </remarks>
public sealed class CreateUserHandler
{
    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateUserHandler> _logger;

    public CreateUserHandler(
        IdentityDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock,
        ICurrentUser currentUser,
        ILogger<CreateUserHandler> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new user with the supplied credentials and role.
    /// </summary>
    public async Task<Result<CreateUserResponse>> HandleAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        // ── 1. Normalize and validate username via domain value object ────────
        Username username;
        try
        {
            username = Username.Create(request.Username);
        }
        catch (DomainException ex)
        {
            return Error.Domain(ex.ErrorCode, ex.Message);
        }

        // ── 2. Upfront uniqueness check ───────────────────────────────────────
        var usernameVo = username; // already normalized
        var exists = await _db.Users
            .AnyAsync(u => u.Username == usernameVo, cancellationToken);

        if (exists)
        {
            return UserErrors.UsernameAlreadyTaken;
        }

        // ── 3. Hash password via application-layer hasher ─────────────────────
        // A temporary placeholder User instance is required by IPasswordHasher<T>;
        // the hasher does not read any User properties for PBKDF2.
        var rawHash = _passwordHasher.HashPassword(null!, request.Password);

        PasswordHash passwordHash;
        try
        {
            passwordHash = PasswordHash.Create(rawHash);
        }
        catch (DomainException ex)
        {
            return Error.Domain(ex.ErrorCode, ex.Message);
        }

        // ── 4. Create the aggregate ───────────────────────────────────────────
        var actorId = _currentUser.UserId;
        var userId = UserId.New();

        User user;
        try
        {
            user = User.Create(userId, username, passwordHash, request.Role, _clock, actorId);
        }
        catch (DomainException ex)
        {
            return Error.Domain(ex.ErrorCode, ex.Message);
        }

        _db.Users.Add(user);

        // ── 5. Persist — catch race-condition uniqueness violation ────────────
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" } pgEx
                  && pgEx.ConstraintName == "uq_users_username")
        {
            return UserErrors.UsernameAlreadyTaken;
        }

        _logger.LogInformation(
            "User {UserId} created with username {Username} and role {Role} by actor {ActorId}",
            user.Id.Value, user.Username.Value, user.Role, actorId.Value);

        return new CreateUserResponse(
            Id: user.Id.Value,
            Username: user.Username.Value,
            Role: user.Role,
            Status: user.Status);
    }
}
