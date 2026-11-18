using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.CustomerTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.GetCustomer;

/// <summary>
/// Endpoint integration tests for <c>GET /api/registry/customers/{id}</c>, against the per-test
/// PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class GetCustomerEndpointTests : RegistryIntegrationTestBase
{
    public GetCustomerEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    [Fact]
    public async Task ExistingCustomer_ReturnsEveryFieldOfTheCustomerPage()
    {
        // Arrange
        Customer customer;
        await using (var db = CreateDbContext())
        {
            customer = await SeedCustomerAsync(db, deactivated: true);
        }

        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync($"/api/registry/customers/{customer.Id.Value}", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("id").GetGuid().ShouldBe(customer.Id.Value);
        body.GetProperty("companyName").GetString().ShouldBe("Comune di Penne");
        body.GetProperty("vatNumber").GetString().ShouldBe(VatNumberA);
        body.GetProperty("taxCode").GetString().ShouldBe("MRCGLI72D45G438K");
        body.GetProperty("address").GetString().ShouldBe("Via Roma 1, Penne");
        body.GetProperty("status").GetString().ShouldBe("Deactivated");
        body.GetProperty("version").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task UnknownCustomer_Returns404CustomerNotFound()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await BackOfficeClient(factory).GetAsync($"/api/registry/customers/{Guid.NewGuid()}", Ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, CustomerErrors.NotFound, Ct);
    }

    [Theory]
    [InlineData("BackOffice", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Role_DecidesWhoReadsACustomer(string role, HttpStatusCode expected)
    {
        // Arrange
        Customer customer;
        await using (var db = CreateDbContext())
        {
            customer = await SeedCustomerAsync(db);
        }

        await using var factory = CreateFactory();
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

        // Act
        var response = await client.GetAsync($"/api/registry/customers/{customer.Id.Value}", Ct);

        // Assert
        response.StatusCode.ShouldBe(expected);
    }
}
