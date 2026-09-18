using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;

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

        // ── Username.Unwrap() — query-side identity over the varchar column ───
        // Translates UsernameQueryExtensions.Unwrap(Username) to the underlying
        // varchar column so that downstream Contains/StartsWith/EndsWith compose
        // as ordinary SQL LIKE. EF Core's value-converter mapping treats Username
        // as an atomic VO; this DbFunction is the documented escape hatch.
        //
        // The parameter is declared with the same store type as the username column
        // (varchar(64)) so the provider can map the Username VO through its
        // ValueConverter when emitting SQL. The translation is identity — args[0]
        // is already the converted varchar expression at the SQL layer.
        modelBuilder
            .HasDbFunction(typeof(UsernameQueryExtensions)
                .GetMethod(nameof(UsernameQueryExtensions.Unwrap), [typeof(Username)])!)
            .HasTranslation(args => new SqlFunctionExpression(
                functionName: string.Empty,
                arguments: args,
                nullable: false,
                argumentsPropagateNullability: new[] { false },
                type: typeof(string),
                typeMapping: null))
            .HasParameter("username")
                .HasStoreType("varchar(64)");

        base.OnModelCreating(modelBuilder);
    }
}
