using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Operations.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Contracts;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Operations.IntegrationTests.CrossSlice;

/// <summary>
/// Rules that hold for every endpoint of the driver app, proved as a sweep over the endpoints the host
/// actually routes under <c>/api/operations/driver</c>: each one declares exactly the <c>Driver</c> policy
/// and none is anonymous, the OpenAPI document describes exactly the routed set, an anonymous caller gets
/// 401, a BackOffice, an Admin and a Driver whose session names no driver get 403, a Driver with a driver
/// is let in, and no response schema has a property named after a price. Each slice that adds an
/// endpoint is covered from then on.
/// </summary>
public sealed class OperationsEndpointSweepTests : OperationsIntegrationTestBase
{
    private const string Prefix = "/api/operations/driver/";
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    /// <summary>Words that must not appear in any property of a response (drivers never see pricing).</summary>
    private static readonly string[] ForbiddenWords =
        ["price", "rate", "amount", "total", "minimum", "fee", "night", "band"];

    public OperationsEndpointSweepTests(PostgresFixture fixture) : base(fixture) { }

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

    private static string UrlOf(string route) => Regex.Replace(route, @"\{[^}]+\}", Guid.Empty.ToString());

    [Fact]
    public async Task EveryRoutedDriverOperation_DeclaresTheDriverPolicyAndIsNeverAnonymous()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var operations = RoutedOperations(factory.Services);
        var violations = operations
            .Where(o => o.AllowsAnonymous
                || o.Policies.Length != 1
                || o.Policies[0] != AuthorizationPolicies.Driver)
            .Select(o => $"{o.Method} {o.Route} -> [{string.Join(", ", o.Policies)}] anonymous={o.AllowsAnonymous}")
            .ToList();

        // Assert
        operations.Select(o => $"{o.Method} {o.Route}").ShouldContain("GET /api/operations/driver/locations");
        operations.Select(o => $"{o.Method} {o.Route}").ShouldContain("DELETE /api/operations/driver/trips/{tripId}");
        operations.Select(o => $"{o.Method} {o.Route}").ShouldContain("GET /api/operations/driver/trips/{tripId}");
        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenApiDocument_DescribesExactlyTheRoutedDriverOperations()
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
    public async Task OpenApiDocument_ListsEveryDriverOperationUnderTheDriverTag()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);
        using var openApi = JsonDocument.Parse(document);
        var operations = openApi.RootElement.GetProperty("paths").EnumerateObject()
            .Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal))
            .SelectMany(p => p.Value.EnumerateObject()
                .Where(m => HttpMethods.Contains(m.Name.ToUpperInvariant()))
                .Select(m => m.Value))
            .ToList();

        // Assert
        operations.ShouldNotBeEmpty();
        operations.ShouldAllBe(o => o.GetProperty("tags").EnumerateArray().Any(t => t.GetString() == "Operations.Driver"));
        operations.Select(o => o.GetProperty("operationId").GetString()).ShouldContain("ListTripLocations");
    }

    [Fact]
    public async Task EveryRoutedDriverOperation_AnonymousCaller_Returns401()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);

        // Act
        var outcomes = await UnexpectedStatusesAsync(factory, null, HttpStatusCode.Unauthorized);

        // Assert
        outcomes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("BackOffice")]
    [InlineData("Admin")]
    [InlineData("Driver")]
    public async Task EveryRoutedDriverOperation_CallerWithoutADriver_Returns403(string role)
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);

        // Act
        var outcomes = await UnexpectedStatusesAsync(factory, TestCaller.WithRole(role), HttpStatusCode.Forbidden);

        // Assert
        outcomes.ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryRoutedDriverOperation_DriverWithADriver_IsLetIn()
    {
        // Arrange
        await using var factory = ApiFactory.Create(
            ConnectionString,
            services => services.AddSingleton<ITripRegistry>(new FakeTripRegistry()),
            useTestAuthentication: true);
        var client = TestCaller.DriverWith(Guid.NewGuid()).ApplyTo(factory.CreateClient());

        // Act
        var refused = new List<string>();
        foreach (var (method, route, _, _) in RoutedOperations(factory.Services))
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), UrlOf(route)), Ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                refused.Add($"{method} {route} -> {(int)response.StatusCode}");
            }
        }

        // Assert
        refused.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoDriverResponseSchema_HasAPropertyNamedAfterAPriceTheRateTheAmountTheMinimumOrTheNight()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);
        using var openApi = JsonDocument.Parse(document);
        var schemas = openApi.RootElement.GetProperty("components").GetProperty("schemas");
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var propertyNames = new List<string>();
        foreach (var path in openApi.RootElement.GetProperty("paths").EnumerateObject()
                     .Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("responses", out var responses))
                {
                    continue;
                }

                foreach (var schema in responses.EnumerateObject()
                             .Select(r => r.Value)
                             .Where(r => r.TryGetProperty("content", out _))
                             .SelectMany(r => r.GetProperty("content").EnumerateObject())
                             .Where(c => c.Value.TryGetProperty("schema", out _))
                             .Select(c => c.Value.GetProperty("schema")))
                {
                    Collect(schema, schemas, visited, propertyNames);
                }
            }
        }

        var violations = propertyNames
            .Where(n => ForbiddenWords.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Assert
        propertyNames.ShouldContain("municipality");
        violations.ShouldBeEmpty();
    }

    /// <summary>Gathers the property names of a schema and of every schema it reaches, following references.</summary>
    private static void Collect(JsonElement schema, JsonElement schemas, HashSet<string> visited, List<string> names)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!.Split('/')[^1];
            if (visited.Add(name))
            {
                Collect(schemas.GetProperty(name), schemas, visited, names);
            }

            return;
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                names.Add(property.Name);
                Collect(property.Value, schemas, visited, names);
            }
        }

        foreach (var keyword in new[] { "items", "additionalProperties" })
        {
            if (schema.TryGetProperty(keyword, out var inner) && inner.ValueKind == JsonValueKind.Object)
            {
                Collect(inner, schemas, visited, names);
            }
        }

        foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (schema.TryGetProperty(keyword, out var inner) && inner.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in inner.EnumerateArray())
                {
                    Collect(item, schemas, visited, names);
                }
            }
        }
    }

    /// <summary>
    /// Sends every routed operation as <paramref name="caller"/> (anonymous when <c>null</c>) and lists
    /// those that did not answer <paramref name="expected"/>.
    /// </summary>
    private static async Task<List<string>> UnexpectedStatusesAsync(
        WebApplicationFactory<Program> factory,
        TestCaller? caller,
        HttpStatusCode expected)
    {
        var client = factory.CreateClient();
        caller?.ApplyTo(client);

        var operations = RoutedOperations(factory.Services);
        operations.ShouldNotBeEmpty();

        var outcomes = new List<string>();
        foreach (var (method, route, _, _) in operations)
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), UrlOf(route)), Ct);
            if (response.StatusCode != expected)
            {
                outcomes.Add($"{method} {route} -> {(int)response.StatusCode}");
            }
        }

        return outcomes;
    }
}
