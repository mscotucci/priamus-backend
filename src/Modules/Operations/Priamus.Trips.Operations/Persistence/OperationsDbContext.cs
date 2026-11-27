using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Operations.Domain.Trips;

namespace Priamus.Trips.Operations.Persistence;

/// <summary>
/// EF Core <see cref="DbContext"/> for the Operations module. Owns the <c>operations</c>
/// PostgreSQL schema and all migrations under <c>Persistence/Migrations/</c>.
/// </summary>
public sealed class OperationsDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="OperationsDbContext"/>.
    /// </summary>
    /// <param name="options">Options built by the DI registration.</param>
    public OperationsDbContext(DbContextOptions<OperationsDbContext> options)
        : base(options)
    {
    }

    private const string Schema = "operations";
    private const string TripNumberSequence = "trip_number";
    private const string NextTripNumberSql = "SELECT nextval('" + Schema + "." + TripNumberSequence + "') AS \"Value\"";

    /// <summary>The trips the drivers added.</summary>
    public DbSet<Trip> Trips => Set<Trip>();

    /// <summary>Takes the next trip number from the sequence; a number taken is never given again.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<long> NextTripNumberAsync(CancellationToken cancellationToken) =>
        await Database
            .SqlQueryRaw<long>(NextTripNumberSql)
            .SingleAsync(cancellationToken);

    /// <summary>
    /// Saves the changes of an act that updates a trip. When the trip was changed by a competing act since it
    /// was read (<see cref="DbUpdateConcurrencyException"/>), the tracked changes are discarded and the answer
    /// is <c>false</c>: the handler reads the trip again and answers from what it finds, once. On that second
    /// try (<paramref name="isRetry"/>) the exception is not caught.
    /// </summary>
    /// <param name="isRetry">Whether this is the save after a trip read again.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns><c>true</c> when saved; <c>false</c> when the trip changed meanwhile and nothing was saved.</returns>
    public async Task<bool> TrySaveTripAsync(bool isRetry, CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException) when (!isRetry)
        {
            ChangeTracker.Clear();
            return false;
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasSequence<long>(TripNumberSequence, Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        base.OnModelCreating(modelBuilder);
    }
}
