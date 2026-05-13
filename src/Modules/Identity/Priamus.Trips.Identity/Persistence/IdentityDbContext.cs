using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.Users;

namespace Priamus.Trips.Identity.Persistence;

/// <summary>
/// EF Core <see cref="DbContext"/> for the Identity module. Owns the <c>identity</c>
/// PostgreSQL schema and all migrations under <c>Persistence/Migrations/</c>.
/// </summary>
/// <remarks>
/// The <see cref="Interceptors.AuditInterceptor"/> is injected into the DbContext options
/// by the DI registration (see <c>IdentityPersistenceModuleExtensions</c>), so this class
/// does not take <see cref="Priamus.Trips.SharedKernel.Security.ICurrentUser"/> directly.
/// </remarks>
public sealed class IdentityDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="IdentityDbContext"/>.
    /// </summary>
    /// <param name="options">Options built by the DI registration, including the <see cref="Interceptors.AuditInterceptor"/>.</param>
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    /// <summary>User aggregate roots.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Refresh-token aggregate roots.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Audit log entries written synchronously by the <see cref="Interceptors.AuditInterceptor"/>.</summary>
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
