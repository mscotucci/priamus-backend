using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Customers.ValueObjects;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Infrastructure;

/// <summary>
/// Helpers shared by the customer endpoint tests. The caller, error-detail and error-key helpers are
/// those of <see cref="VehicleTestSupport"/>.
/// </summary>
public static class CustomerTestSupport
{
    /// <summary>An 11-digit VAT number with a valid check digit.</summary>
    public const string VatNumberA = "01455280683";

    /// <summary>Another 11-digit VAT number with a valid check digit.</summary>
    public const string VatNumberB = "12345678903";

    /// <summary>A valid request body; tests override or remove single fields.</summary>
    public static Dictionary<string, object?> CustomerBody(
        string companyName = "Comune di Penne",
        string? vatNumber = VatNumberA,
        string? taxCode = "MRCGLI72D45G438K",
        string? address = "Via Roma 1, Penne") =>
        new()
        {
            ["companyName"] = companyName,
            ["vatNumber"] = vatNumber,
            ["taxCode"] = taxCode,
            ["address"] = address,
        };

    /// <summary>Saves a customer straight through the context.</summary>
    public static async Task<Customer> SeedCustomerAsync(
        RegistryDbContext db,
        string companyName = "Comune di Penne",
        string? vatNumber = VatNumberA,
        string? taxCode = "MRCGLI72D45G438K",
        string? address = "Via Roma 1, Penne",
        bool deactivated = false)
    {
        var clock = new SystemClock();
        var customer = Customer.Create(
            CustomerId.New(),
            companyName,
            vatNumber is null ? null : VatNumber.Create(vatNumber),
            taxCode is null ? null : TaxCode.Create(taxCode),
            address,
            clock,
            FakeCurrentUser.ActorId);
        if (deactivated)
        {
            customer.Deactivate(clock, FakeCurrentUser.ActorId);
        }

        db.Customers.Add(customer);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return customer;
    }

    /// <summary>
    /// A save interceptor that, once, inserts a customer with the given VAT number through a separate
    /// connection just before the first save reaches the database: the handler's upfront check has
    /// already passed, so only the unique index can stop the write.
    /// </summary>
    public sealed class CompetingVatInsertInterceptor : SaveChangesInterceptor
    {
        private readonly string _connectionString;
        private readonly string _vatNumber;
        private readonly string _status;
        private readonly bool _vanishAfterFailure;
        private readonly Guid _competitorId = Guid.NewGuid();
        private bool _fired;

        public CompetingVatInsertInterceptor(string connectionString, string vatNumber, string status, bool vanishAfterFailure = false)
        {
            _connectionString = connectionString;
            _vatNumber = vatNumber;
            _status = status;
            _vanishAfterFailure = vanishAfterFailure;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_fired)
            {
                return result;
            }

            _fired = true;
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO registry.customers
                    (id, company_name, vat_number, tax_code, address, status,
                     created_at, created_by, last_modified_at, last_modified_by, version)
                VALUES
                    (@id, 'Concorrente Srl', @vat, NULL, NULL, @status,
                     now(), @actor, now(), @actor, 0)
                """;
            command.Parameters.AddWithValue("id", _competitorId);
            command.Parameters.AddWithValue("vat", _vatNumber);
            command.Parameters.AddWithValue("status", _status);
            command.Parameters.AddWithValue("actor", FakeCurrentUser.ActorId.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }

        public override async Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!_vanishAfterFailure)
            {
                return;
            }

            // The competitor that won the race on the unique index is gone before the handler
            // looks it up again to name it.
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM registry.customers WHERE id = @id";
            command.Parameters.AddWithValue("id", _competitorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
