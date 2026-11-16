using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.CreateUser;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Application.Services;

/// <summary>
/// Creates the first administrator at startup from <see cref="BootstrapAdminOptions"/> when the
/// users table is empty. It never touches an existing user.
/// </summary>
/// <remarks>
/// Two instances starting together on an empty table both pass the emptiness check; the loser of
/// the unique constraint <c>uq_users_username</c> treats the administrator as already created.
/// No HTTP request is in progress at startup, so the automatic <c>UserCreated</c> audit row has no
/// actor; the <see cref="AuditEventTypes.AdminBootstrapped"/> row written here names the new
/// administrator as its own actor.
/// </remarks>
internal sealed class AdminBootstrapService
{
    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IClock _clock;
    private readonly BootstrapAdminOptions _options;
    private readonly ILogger<AdminBootstrapService> _logger;

    public AdminBootstrapService(
        IdentityDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock,
        IOptions<BootstrapAdminOptions> options,
        ILogger<AdminBootstrapService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs the bootstrap.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The users table is empty and the configured user name or password breaks its rule.
    /// </exception>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var configured = !string.IsNullOrWhiteSpace(_options.Username)
            && !string.IsNullOrWhiteSpace(_options.Password);

        if (await _db.Users.AnyAsync(cancellationToken))
        {
            if (configured)
            {
                _logger.LogInformation(
                    "Users already exist: the configured Identity:BootstrapAdmin is ignored");
            }

            return;
        }

        if (!configured)
        {
            _logger.LogWarning(
                "No user exists and Identity:BootstrapAdmin is absent or incomplete: nobody can sign in");
            return;
        }

        Username username;
        try
        {
            username = Username.Create(_options.Username);
        }
        catch (DomainException ex)
        {
            throw new InvalidOperationException(
                $"Identity:BootstrapAdmin:Username is invalid: it must be {Username.MinLength}-{Username.MaxLength} "
                + "characters of a-z, 0-9, '.', '_' or '-', starting with a letter or a digit.",
                ex);
        }

        var password = _options.Password!;
        var passwordResults = new List<ValidationResult>();
        var passwordValid = Validator.TryValidateValue(
            password,
            new ValidationContext(_options) { MemberName = nameof(BootstrapAdminOptions.Password) },
            passwordResults,
            [new StrongPasswordAttribute()]);
        if (!passwordValid)
        {
            throw new InvalidOperationException(
                "Identity:BootstrapAdmin:Password breaks the password rule: it must be "
                + $"{StrongPasswordAttribute.MinLength}-{StrongPasswordAttribute.MaxLength} characters with at "
                + "least one letter, one digit and one special character.");
        }

        var passwordHash = PasswordHash.Create(_passwordHasher.HashPassword(null!, password));
        var userId = UserId.New();
        var user = User.Create(userId, username, passwordHash, UserRole.Admin, _clock, userId);

        _db.Users.Add(user);
        _db.AuditLog.Add(AuditLogEntry.ForNonDomainEvent(
            AuditEventTypes.AdminBootstrapped,
            user.CreatedAt,
            actorId: userId.Value,
            subjectId: userId.Value,
            payload: new { username = username.Value }));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" } pgEx
                  && pgEx.ConstraintName == "uq_users_username")
        {
            _logger.LogInformation(
                "Another instance created the bootstrap administrator first: nothing to do");
            return;
        }

        _logger.LogInformation(
            "Bootstrap administrator {Username} created with id {UserId}",
            username.Value, userId.Value);
    }
}
