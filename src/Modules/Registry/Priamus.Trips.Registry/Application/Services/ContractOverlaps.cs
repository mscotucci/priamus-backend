using System.Globalization;
using Npgsql;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Application.Services;

/// <summary>
/// Tells whether a validity overlaps another contract of the same customer, and which error says
/// so (PRD §5.4, RF-18). Shared by the create and edit slices, which check upfront and map the
/// exclusion constraint after a lost race.
/// </summary>
internal static class ContractOverlaps
{
    /// <summary>Name of the exclusion constraint that keeps a customer's validities apart.</summary>
    public const string ExclusionConstraintName = "ex_contracts_customer_validity";

    /// <summary>
    /// Looks for a contract among <paramref name="others"/> that shares a day with
    /// <paramref name="validity"/>. When found, stores the other validity in
    /// <paramref name="arguments"/> for the Italian message and returns
    /// <see cref="ContractErrors.Overlap"/>; otherwise returns <c>null</c>.
    /// </summary>
    /// <param name="others">The customer's other contracts, as they will stand after the save.</param>
    /// <param name="validity">The validity being created or edited.</param>
    /// <param name="arguments">Receives the other validity, as text, for the message.</param>
    public static Error? Find(IEnumerable<Contract> others, Validity validity, ErrorMessageArguments arguments)
    {
        var conflicting = others.FirstOrDefault(c => c.Validity.Overlaps(validity));
        if (conflicting is null)
        {
            return null;
        }

        arguments.Set(Describe(conflicting.Validity));
        return ContractErrors.Overlap;
    }

    /// <summary>
    /// Tells whether <paramref name="exception"/> says the save lost a race on the customer's
    /// validities: a violation of the exclusion constraint, or the deadlock Postgres raises when two
    /// concurrent inserts wait on each other's index entry. The constraint is deferred, so its
    /// violation surfaces at commit as a bare <see cref="PostgresException"/>; one wrapped further
    /// down the exception chain is accepted too.
    /// <para>
    /// Any <c>40P01</c> deadlock in the chain is treated as a lost race too: a deadlock aborts the whole
    /// save, so nothing is written, and the statements of this save that can wait on each other are
    /// the customer's contract writes checked by the deferred exclusion constraint. The user gets the
    /// "another contract" conflict and can retry.
    /// </para>
    /// </summary>
    public static bool IsLostRace(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pgEx
                && ((pgEx.SqlState == PostgresErrorCodes.ExclusionViolation && pgEx.ConstraintName == ExclusionConstraintName)
                    || pgEx.SqlState == PostgresErrorCodes.DeadlockDetected))
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(Validity validity)
    {
        var from = validity.ValidFrom.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return validity.ValidTo is { } to
            ? $"dal {from} al {to.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}"
            : $"dal {from}, senza scadenza";
    }
}
