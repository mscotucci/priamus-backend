using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Application.Services;

/// <summary>
/// Identity's implementation of <see cref="IUserLookup"/> (ADR 0013): reads user accounts for
/// other modules, without tracking.
/// </summary>
internal sealed class UserLookup : IUserLookup
{
    private readonly IdentityDbContext _db;

    public UserLookup(IdentityDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, UserAccount>> GetAccountsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserAccount>();
        }

        var ids = userIds.Select(id => new UserId(id)).ToList();

        var accounts = await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(ToAccount)
            .ToListAsync(cancellationToken);

        return accounts.ToDictionary(a => a.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAccount>> ListDriverAccountsAsync(CancellationToken cancellationToken)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Driver)
            .Select(ToAccount)
            .ToListAsync(cancellationToken);
    }

    private static readonly Expression<Func<User, UserAccount>> ToAccount = u => new UserAccount(
        u.Id.Value,
        u.Username.Value,
        u.Role == UserRole.Driver,
        u.Status == UserStatus.Disabled);
}
