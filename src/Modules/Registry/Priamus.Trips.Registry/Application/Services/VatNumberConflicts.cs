using Microsoft.EntityFrameworkCore;
using Npgsql;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// Tells whether a VAT number is already registered on another customer, and which error says so
/// (PRD §4, RF-15). Shared by the create and edit slices, which both check upfront and again after
/// a unique-index violation.
/// </summary>
internal static class VatNumberConflicts
{
    /// <summary>Name of the unique index on the normalised VAT number.</summary>
    public const string UniqueIndexName = "uq_customers_vat_number";

    /// <summary>
    /// Looks for another customer holding <paramref name="vatNumber"/>. When found, stores the VAT
    /// number and the holder's company name in <paramref name="arguments"/> for the Italian message
    /// and returns the error for an active or a deactivated holder; otherwise returns <c>null</c>.
    /// </summary>
    /// <param name="db">The Registry context.</param>
    /// <param name="vatNumber">The VAT number in normal form.</param>
    /// <param name="excluding">The customer being edited, which may keep its own VAT number; <c>null</c> on create.</param>
    /// <param name="arguments">Receives the VAT number and the holder's company name for the message.</param>
    /// <param name="cancellationToken">Propagated to the query.</param>
    public static async Task<Error?> FindAsync(
        RegistryDbContext db,
        VatNumber vatNumber,
        CustomerId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        var holders = db.Customers.AsNoTracking().Where(c => c.VatNumber == vatNumber);
        if (excluding is { } own)
        {
            holders = holders.Where(c => c.Id != own);
        }

        var holder = await holders
            .Select(c => new { c.Status, c.CompanyName })
            .FirstOrDefaultAsync(cancellationToken);

        if (holder is null)
        {
            return null;
        }

        arguments.Set(vatNumber.Value, holder.CompanyName);
        return holder.Status is CustomerStatus.Deactivated
            ? CustomerErrors.VatNumberTakenByDeactivated
            : CustomerErrors.VatNumberTaken;
    }

    /// <summary>Tells whether <paramref name="exception"/> is a violation of the VAT number's unique index.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx
        && pgEx.ConstraintName == UniqueIndexName;

    /// <summary>
    /// The error to raise after losing a race on the unique index: looks the competitor up again to
    /// tell an active holder from a deactivated one, and falls back to
    /// <see cref="CustomerErrors.VatNumberTakenByAnother"/>, which names only the VAT number, when it
    /// can no longer be found.
    /// </summary>
    public static async Task<Error> ErrorAfterLostRaceAsync(
        RegistryDbContext db,
        VatNumber vatNumber,
        CustomerId? excluding,
        ErrorMessageArguments arguments,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(db, vatNumber, excluding, arguments, cancellationToken) is { } raced)
        {
            return raced;
        }

        arguments.Set(vatNumber.Value);
        return CustomerErrors.VatNumberTakenByAnother;
    }
}
