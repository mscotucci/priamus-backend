using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.GetContract;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/customers/{customerId}/contracts/{contractId}</c>,
/// against the per-test PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class GetContractEndpointTests : RegistryIntegrationTestBase
{
    private static readonly DateTime Midday = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    public GetContractEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Url(Guid customerId, Guid contractId) =>
        $"/api/registry/customers/{customerId}/contracts/{contractId}";

    private async Task<Customer> SeedCustomerAsync(string vatNumber = VatNumberA)
    {
        await using var db = CreateDbContext();
        return await CustomerTestSupport.SeedCustomerAsync(db, vatNumber: vatNumber, taxCode: null);
    }

    private async Task<Contract> SeedAsync(CustomerId customerId)
    {
        await using var db = CreateDbContext();
        return await SeedContractAsync(db, customerId, new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task ReturnsTheContractWithItsStatusAndTerms()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedAsync(customer.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("id").GetGuid().ShouldBe(contract.Id.Value);
        body.GetProperty("customerId").GetGuid().ShouldBe(customer.Id.Value);
        body.GetProperty("validFrom").GetString().ShouldBe("2026-04-01");
        body.GetProperty("validTo").GetString().ShouldBe("2026-12-31");
        body.GetProperty("status").GetString().ShouldBe("Current");
        body.GetProperty("billingLevel").GetString().ShouldBe("PerLocationPair");
        body.GetProperty("pricingMode").GetString().ShouldBe("ByTrip");
        body.GetProperty("requiredDocuments").EnumerateArray().Select(d => d.GetString()).ShouldBe(["DeliveryNote", "DailySummary"]);
        body.GetProperty("nightSurcharge").GetBoolean().ShouldBeTrue();
        body.GetProperty("version").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task UnknownContract_Returns404ContractNotFound()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, Guid.NewGuid()), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
    }

    [Fact]
    public async Task ContractOfAnotherCustomer_Returns404ContractNotFound()
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var other = await SeedCustomerAsync(VatNumberB);
        var contract = await SeedAsync(other.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, ContractErrors.NotFound, Ct);
    }

    [Fact]
    public async Task UnknownCustomer_Returns404CustomerNotFound()
    {
        // Arrange
        await using var factory = CreateFactory(ConnectionString, Midday);

        // Act
        var response = await BackOfficeClient(factory).GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()), Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoReadsAContract(string role, HttpStatusCode expected)
    {
        // Arrange
        var customer = await SeedCustomerAsync();
        var contract = await SeedAsync(customer.Id);
        await using var factory = CreateFactory(ConnectionString, Midday);
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync(Url(customer.Id.Value, contract.Id.Value), Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
    }
}
