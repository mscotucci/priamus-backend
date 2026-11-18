using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Contracts.ValueObjects;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Persistence;
using Priamus.Trips.SharedKernel.Time;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.Infrastructure;

/// <summary>Helpers shared by the contract endpoint tests.</summary>
public static class ContractTestSupport
{
    /// <summary>A clock that always reads the same instant.</summary>
    public sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;

        public DateTimeOffset UtcNowOffset => new(now, TimeSpan.Zero);
    }

    /// <summary>
    /// A host whose clock reads <paramref name="utcNow"/>, so the status of a contract, computed from
    /// today in Italian time, is deterministic.
    /// </summary>
    public static WebApplicationFactory<Program> CreateFactory(string connectionString, DateTime utcNow) =>
        ApiFactory.Create(
            connectionString,
            services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FixedClock(utcNow));
            },
            useTestAuthentication: true);

    /// <summary>Saves a contract straight through the context.</summary>
    public static async Task<Contract> SeedContractAsync(
        RegistryDbContext db,
        CustomerId customerId,
        DateOnly validFrom,
        DateOnly? validTo,
        BillingLevel billingLevel = BillingLevel.PerLocationPair,
        PricingMode pricingMode = PricingMode.ByTrip,
        RequiredDocument[]? requiredDocuments = null,
        bool nightSurcharge = true)
    {
        var contract = Contract.Create(
            ContractId.New(),
            customerId,
            Validity.Create(validFrom, validTo).Value,
            billingLevel,
            pricingMode,
            requiredDocuments ?? [RequiredDocument.DailySummary, RequiredDocument.DeliveryNote],
            nightSurcharge,
            new SystemClock(),
            FakeCurrentUser.ActorId);

        db.Contracts.Add(contract);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return contract;
    }

    /// <summary>
    /// Runs one statement through a separate connection just before the first save reaches the
    /// database, so the handler's upfront checks have already passed.
    /// </summary>
    public abstract class BeforeFirstSaveInterceptor(string connectionString) : SaveChangesInterceptor
    {
        private bool _fired;

        protected abstract void Configure(NpgsqlCommand command);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_fired)
            {
                return result;
            }

            _fired = true;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            Configure(command);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }
    }

    /// <summary>Inserts an open contract of the customer from 2026-06-01 before the first save.</summary>
    public sealed class CompetingContractInterceptor(string connectionString, Guid customerId)
        : BeforeFirstSaveInterceptor(connectionString)
    {
        protected override void Configure(NpgsqlCommand command)
        {
            command.CommandText =
                """
                INSERT INTO registry.contracts
                    (id, customer_id, valid_from, valid_to, billing_level, pricing_mode, required_documents,
                     night_surcharge, created_at, created_by, last_modified_at, last_modified_by, version)
                VALUES
                    (@id, @customer, '2026-06-01', NULL, 'FlatRate', 'ByHour', '{}',
                     false, now(), @actor, now(), @actor, 0)
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("customer", customerId);
            command.Parameters.AddWithValue("actor", FakeCurrentUser.ActorId.Value);
        }
    }

    /// <summary>Bumps the version of a contract before the first save, as another user's edit would.</summary>
    public sealed class BumpVersionInterceptor(string connectionString, Guid contractId)
        : BeforeFirstSaveInterceptor(connectionString)
    {
        protected override void Configure(NpgsqlCommand command)
        {
            command.CommandText = "UPDATE registry.contracts SET version = version + 1 WHERE id = @id";
            command.Parameters.AddWithValue("id", contractId);
        }
    }
}
