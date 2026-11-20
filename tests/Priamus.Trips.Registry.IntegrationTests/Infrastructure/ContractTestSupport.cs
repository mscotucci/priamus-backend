using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
        bool nightSurcharge = true,
        Rate? rate = null,
        IReadOnlyList<VehicleRate>? vehicleRates = null)
    {
        var contract = Contract.Create(
            ContractId.New(),
            customerId,
            Validity.Create(validFrom, validTo).Value,
            billingLevel,
            pricingMode,
            requiredDocuments ?? [RequiredDocument.DailySummary, RequiredDocument.DeliveryNote],
            nightSurcharge,
            rate,
            vehicleRates ?? [],
            new SystemClock(),
            FakeCurrentUser.ActorId).Value;

        db.Contracts.Add(contract);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return contract;
    }

    /// <summary>
    /// Sets the billing terms and the rate members of a contract request body; <paramref name="rate"/>
    /// is a JSON object of the rate members (<c>{}</c> for none).
    /// </summary>
    public static void SetRate(
        Dictionary<string, object?> body, string billingLevel, string pricingMode, bool nightSurcharge, string rate)
    {
        body["billingLevel"] = billingLevel;
        body["pricingMode"] = pricingMode;
        body["nightSurcharge"] = nightSurcharge;
        foreach (var member in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rate)!)
        {
            body[member.Key] = member.Value;
        }
    }

    /// <summary>Rate members each broken rule: billing level, pricing mode, night surcharge, members, error keys.</summary>
    public static TheoryData<string, string, bool, string, string> InvalidRates => new()
    {
        { "FlatRate", "ByHour", false, """{"minimumRule":"None"}""", "DayPrice" },
        { "FlatRate", "ByHour", false, """{"dayPrice":0,"minimumRule":"None"}""", "DayPrice" },
        { "FlatRate", "ByHour", false, """{"dayPrice":-5,"minimumRule":"None"}""", "DayPrice" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45.123,"minimumRule":"None"}""", "DayPrice" },
        { "FlatRate", "ByHour", true, """{"dayPrice":45,"minimumRule":"None"}""", "NightCallFee,NightPrice" },
        { "FlatRate", "ByHour", true, """{"dayPrice":45,"nightPrice":0,"nightCallFee":5,"minimumRule":"None"}""", "NightPrice" },
        { "FlatRate", "ByHour", true, """{"dayPrice":45,"nightPrice":55.555,"nightCallFee":5,"minimumRule":"None"}""", "NightPrice" },
        { "FlatRate", "ByHour", true, """{"dayPrice":45,"nightPrice":55,"nightCallFee":-1,"minimumRule":"None"}""", "NightCallFee" },
        { "FlatRate", "ByHour", true, """{"dayPrice":45,"nightPrice":55,"nightCallFee":5.555,"minimumRule":"None"}""", "NightCallFee" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"nightPrice":55,"minimumRule":"None"}""", "NightPrice" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"nightCallFee":0,"minimumRule":"None"}""", "NightCallFee" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45}""", "MinimumRule" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"FixedMinimum"}""", "MinimumAmount" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"FixedMinimum","minimumAmount":0}""", "MinimumAmount" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"FixedMinimum","minimumAmount":100.001}""", "MinimumAmount" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"FixedMinimum","minimumAmount":100,"minimumThreshold":4}""", "MinimumThreshold" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"None","minimumAmount":100}""", "MinimumAmount" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"TieredOverage"}""", "MinimumAmount,MinimumOveragePrice,MinimumThreshold" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":0,"minimumOveragePrice":60}""", "MinimumThreshold" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":4,"minimumOveragePrice":60.555}""", "MinimumOveragePrice" },
        { "FlatRate", "ByTrip", false, """{"dayPrice":45,"minimumRule":"FixedMinimum","minimumAmount":100}""", "MinimumAmount,MinimumRule" },
        { "FlatRate", "ByTrip", false, """{"dayPrice":45,"minimumRule":"None","minimumAmount":100}""", "MinimumAmount" },
        { "PerLocationPair", "ByHour", false, """{"dayPrice":45}""", "DayPrice" },
        { "PerPickupLocation", "ByHour", true, """{"nightPrice":55,"nightCallFee":0}""", "NightCallFee,NightPrice" },
        { "PerDeliveryLocation", "ByHour", false, """{"minimumRule":"None"}""", "MinimumRule" },
        { "PerLocationPair", "ByHour", false, """{"minimumAmount":100,"minimumThreshold":4,"minimumOveragePrice":60}""", "MinimumAmount,MinimumOveragePrice,MinimumThreshold" },
        { "FlatRate", "ByHour", false, """{"dayPrice":10000000000,"minimumRule":"None"}""", "DayPrice" },
        { "FlatRate", "ByHour", true, """{"dayPrice":45,"nightPrice":10000000000,"nightCallFee":5,"minimumRule":"None"}""", "NightPrice" },
        { "FlatRate", "ByHour", true, """{"dayPrice":45,"nightPrice":55,"nightCallFee":10000000000,"minimumRule":"None"}""", "NightCallFee" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"FixedMinimum","minimumAmount":10000000000}""", "MinimumAmount" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":10000000000,"minimumOveragePrice":60}""", "MinimumThreshold" },
        { "FlatRate", "ByHour", false, """{"dayPrice":45,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":4,"minimumOveragePrice":10000000000}""", "MinimumOveragePrice" },
    };

    /// <summary>Rate members each accepted: billing level, pricing mode, night surcharge, members.</summary>
    public static TheoryData<string, string, bool, string> ValidRates => new()
    {
        { "FlatRate", "ByHour", false, """{"dayPrice":45.5,"minimumRule":"None"}""" },
        { "FlatRate", "ByDistance", true, """{"dayPrice":45,"nightPrice":55.25,"nightCallFee":0,"minimumRule":"FixedMinimum","minimumAmount":120}""" },
        { "FlatRate", "ByWeight", true, """{"dayPrice":45,"nightPrice":55,"nightCallFee":5,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":4.5,"minimumOveragePrice":60}""" },
        { "FlatRate", "ByTrip", false, """{"dayPrice":150,"minimumRule":"None"}""" },
        { "FlatRate", "ByTrip", false, """{"dayPrice":150}""" },
        { "FlatRate", "ByHour", true, """{"dayPrice":9999999999.99,"nightPrice":9999999999.99,"nightCallFee":9999999999.99,"minimumRule":"TieredOverage","minimumAmount":9999999999.99,"minimumThreshold":9999999999.99,"minimumOveragePrice":9999999999.99}""" },
        { "PerLocationPair", "ByHour", true, "{}" },
    };

    /// <summary>The members of a vehicle rate that is only a day price and no minimum.</summary>
    public const string DayOnlyRate = """{"dayPrice":70,"minimumRule":"None"}""";

    /// <summary>Vehicle rate members each accepted on a flat-rate contract: pricing mode, night surcharge, members.</summary>
    public static TheoryData<string, bool, string> ValidVehicleRates => new()
    {
        { "ByHour", false, """{"dayPrice":70.5,"minimumRule":"None"}""" },
        { "ByDistance", true, """{"dayPrice":70,"nightPrice":85.25,"nightCallFee":0,"minimumRule":"FixedMinimum","minimumAmount":150}""" },
        { "ByWeight", true, """{"dayPrice":70,"nightPrice":85,"nightCallFee":6,"minimumRule":"TieredOverage","minimumAmount":300,"minimumThreshold":4.5,"minimumOveragePrice":60}""" },
        { "ByTrip", false, """{"dayPrice":150}""" },
    };

    /// <summary>One item of the <c>vehicleRates</c> member of a contract request body.</summary>
    public static Dictionary<string, object?> VehicleRateItem(Guid? vehicleId, string rate = DayOnlyRate)
    {
        var item = new Dictionary<string, object?> { ["vehicleId"] = vehicleId };
        foreach (var member in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rate)!)
        {
            item[member.Key] = member.Value;
        }

        return item;
    }

    /// <summary>
    /// Writes a contract request body whose own rate is valid for the billing terms and whose single
    /// vehicle rate has <paramref name="vehicleRate"/> as members, so that only the vehicle rate can
    /// be wrong.
    /// </summary>
    public static void SetRateWithVehicleRate(
        Dictionary<string, object?> body, string billingLevel, string pricingMode, bool nightSurcharge, string vehicleRate)
    {
        foreach (var member in new[]
        {
            "dayPrice", "nightPrice", "nightCallFee", "minimumRule", "minimumAmount", "minimumThreshold", "minimumOveragePrice",
        })
        {
            body.Remove(member);
        }

        var contractRate = billingLevel != "FlatRate" ? "{}"
            : nightSurcharge ? """{"dayPrice":45,"nightPrice":55,"nightCallFee":5,"minimumRule":"None"}"""
            : """{"dayPrice":45,"minimumRule":"None"}""";
        SetRate(body, billingLevel, pricingMode, nightSurcharge, contractRate);
        body["vehicleRates"] = new[] { VehicleRateItem(Guid.NewGuid(), vehicleRate) };
    }

    /// <summary>
    /// The 400 keys a broken vehicle rate of <see cref="InvalidRates"/> is reported under: its member
    /// keys under the first item, or the list as a whole when the contract is not flat rate.
    /// </summary>
    public static string[] VehicleRateKeys(string billingLevel, string keys) =>
        billingLevel == "FlatRate" ? [.. keys.Split(',').Select(k => $"VehicleRates[0].{k}")] : ["VehicleRates"];

    /// <summary>
    /// Saves a flat-rate contract that has no rate, as one stored before rates existed: the domain
    /// no longer builds one, so it is seeded as another billing level and switched in the table.
    /// </summary>
    public static async Task<Contract> SeedFlatRateWithoutRateAsync(
        RegistryDbContext db,
        CustomerId customerId,
        DateOnly validFrom,
        DateOnly? validTo)
    {
        var contract = await SeedContractAsync(
            db, customerId, validFrom, validTo, BillingLevel.PerLocationPair, PricingMode.ByHour, nightSurcharge: false);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE registry.contracts SET billing_level = 'FlatRate' WHERE id = {contract.Id.Value}",
            TestContext.Current.CancellationToken);
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
