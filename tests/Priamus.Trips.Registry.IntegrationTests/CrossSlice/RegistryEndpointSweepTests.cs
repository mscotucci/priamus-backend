using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.CrossSlice;

/// <summary>
/// Rules that hold for every Registry endpoint, proved as a sweep over the endpoints the host
/// actually routes: each one declares exactly the <c>BackOfficeOrAdmin</c> policy and none is
/// anonymous, the OpenAPI document describes exactly the routed set with camelCase query names,
/// an anonymous caller gets 401 and a <c>Driver</c> gets 403. Each vertical slice that adds an
/// endpoint is covered from then on.
/// </summary>
public sealed class RegistryEndpointSweepTests : RegistryIntegrationTestBase
{
    private const string Prefix = "/api/registry/";
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public RegistryEndpointSweepTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Route templates lose their constraints in OpenAPI (<c>{id:guid}</c> becomes <c>{id}</c>).</summary>
    private static string Normalise(string route) =>
        "/" + Regex.Replace(route.TrimStart('/'), @"\{([^}:]+)[^}]*\}", "{$1}");

    private static List<(string Method, string Route, string?[] Policies, bool AllowsAnonymous)> RoutedOperations(
        IServiceProvider services)
    {
        var operations = new List<(string, string, string?[], bool)>();
        foreach (var endpoint in services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var route = Normalise(endpoint.RoutePattern.RawText ?? string.Empty);
            if (!route.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToArray();
            var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            foreach (var method in methods)
            {
                operations.Add((method.ToUpperInvariant(), route, policies, allowsAnonymous));
            }
        }

        return operations;
    }

    [Fact]
    public async Task EveryRoutedRegistryOperation_DeclaresTheBackOfficeOrAdminPolicyAndIsNeverAnonymous()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var operations = RoutedOperations(factory.Services);
        var violations = operations
            .Where(o => o.AllowsAnonymous
                || o.Policies.Length != 1
                || o.Policies[0] != AuthorizationPolicies.BackOfficeOrAdmin)
            .Select(o => $"{o.Method} {o.Route} -> [{string.Join(", ", o.Policies)}] anonymous={o.AllowsAnonymous}")
            .ToList();

        // Assert
        operations.ShouldNotBeEmpty();
        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenApiDocument_DescribesExactlyTheRoutedRegistryOperations()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);
        var routed = RoutedOperations(factory.Services)
            .Select(o => $"{o.Method} {o.Route}")
            .OrderBy(o => o, StringComparer.Ordinal);

        // Act
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);
        using var openApi = JsonDocument.Parse(document);
        var described = openApi.RootElement.GetProperty("paths").EnumerateObject()
            .Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal))
            .SelectMany(p => p.Value.EnumerateObject()
                .Where(m => HttpMethods.Contains(m.Name.ToUpperInvariant()))
                .Select(m => $"{m.Name.ToUpperInvariant()} {p.Name}"))
            .OrderBy(o => o, StringComparer.Ordinal);

        // Assert
        described.ShouldBe(routed);
    }

    [Fact]
    public async Task OpenApiDocument_RegistryQueryParameterNames_AreCamelCase()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);
        using var openApi = JsonDocument.Parse(document);
        var queryNames = openApi.RootElement.GetProperty("paths").EnumerateObject()
            .Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal))
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(m => m.Value.TryGetProperty("parameters", out _))
            .SelectMany(m => m.Value.GetProperty("parameters").EnumerateArray())
            .Where(p => p.GetProperty("in").GetString() == "query")
            .Select(p => p.GetProperty("name").GetString()!)
            .ToList();

        // Assert
        queryNames.Where(n => char.IsUpper(n[0])).ShouldBeEmpty();
        queryNames.ShouldContain("pageSize");
    }

    [Fact]
    public async Task NoRoutedRegistryOperationDeletesAVehicle()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var deletes = RoutedOperations(factory.Services)
            .Where(o => o.Method == "DELETE")
            .Select(o => $"{o.Method} {o.Route}")
            .ToList();

        // Assert
        deletes.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoRoutedRegistryOperationDeletesACustomer()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var customerOperations = RoutedOperations(factory.Services)
            .Where(o => o.Route.StartsWith(Prefix + "customers", StringComparison.Ordinal))
            .Select(o => $"{o.Method} {o.Route}")
            .ToList();

        // Assert
        customerOperations.ShouldContain("POST /api/registry/customers/{id}/deactivate");
        customerOperations.ShouldContain("POST /api/registry/customers/{id}/reactivate");
        customerOperations.Where(o => o.StartsWith("DELETE ", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task NoRoutedRegistryOperationDeletesADriver()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var driverOperations = RoutedOperations(factory.Services)
            .Where(o => o.Route.StartsWith(Prefix + "drivers", StringComparison.Ordinal))
            .Select(o => $"{o.Method} {o.Route}")
            .ToList();

        // Assert
        driverOperations.ShouldContain("POST /api/registry/drivers/{id}/deactivate");
        driverOperations.ShouldContain("POST /api/registry/drivers/{id}/reactivate");
        driverOperations.Where(o => o.StartsWith("DELETE ", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryRoutedRegistryOperation_AnonymousCaller_Returns401()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);
        var client = factory.CreateClient();

        // Act
        var outcomes = await UnexpectedStatusesAsync(factory.Services, client, HttpStatusCode.Unauthorized);

        // Assert
        outcomes.ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryRoutedRegistryOperation_DriverCaller_Returns403()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());

        // Act
        var outcomes = await UnexpectedStatusesAsync(factory.Services, client, HttpStatusCode.Forbidden);

        // Assert
        outcomes.ShouldBeEmpty();
    }

    /// <summary>Sends every routed Registry operation and lists those that did not answer <paramref name="expected"/>.</summary>
    private static async Task<List<string>> UnexpectedStatusesAsync(
        IServiceProvider services, HttpClient client, HttpStatusCode expected)
    {
        var outcomes = new List<string>();
        foreach (var (method, route, _, _) in RoutedOperations(services))
        {
            var url = Regex.Replace(route, @"\{[^}]+\}", Guid.Empty.ToString());
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);
            if (response.StatusCode != expected)
            {
                outcomes.Add($"{method} {route} -> {(int)response.StatusCode}");
            }
        }

        return outcomes;
    }
}
