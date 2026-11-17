using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.CrossSlice;

/// <summary>
/// Rules that hold for every Identity endpoint, proved as a sweep over the endpoints the host
/// actually routes: each one declares exactly one nominal policy (login and refresh are the only
/// anonymous ones), the OpenAPI document describes exactly the routed set with camelCase query
/// names, and an anonymous caller gets 401 everywhere a policy is required.
/// </summary>
public sealed class IdentityEndpointSweepTests : IdentityIntegrationTestBase
{
    private const string Prefix = "/api/identity/";
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    private static readonly string[] AnonymousRoutes =
    [
        "/api/identity/auth/login",
        "/api/identity/auth/refresh",
    ];

    private static readonly string[] KnownPolicies =
    [
        AuthorizationPolicies.Authenticated,
        AuthorizationPolicies.Admin,
        AuthorizationPolicies.BackOfficeOrAdmin,
    ];

    public IdentityEndpointSweepTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task EveryRoutedIdentityOperation_DeclaresOneNominalPolicyOrIsAKnownAnonymousRoute()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var operations = RoutedOperations(factory.Services);
        var violations = operations
            .Where(o => AnonymousRoutes.Contains(o.Route)
                ? !o.AllowsAnonymous || o.Policies.Length != 0
                : o.AllowsAnonymous || o.Policies.Length != 1 || !KnownPolicies.Contains(o.Policies[0]))
            .Select(o => $"{o.Method} {o.Route} -> [{string.Join(", ", o.Policies)}] anonymous={o.AllowsAnonymous}")
            .ToList();

        // Assert
        operations.ShouldNotBeEmpty();
        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryRoutedIdentityOperation_OutsideTheModulePrefix_DoesNotExist()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var outside = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => Normalise(e.RoutePattern.RawText ?? string.Empty))
            .Where(r => r.StartsWith("/api/", StringComparison.Ordinal)
                && !r.StartsWith(Prefix, StringComparison.Ordinal)
                && !r.StartsWith("/api/registry/", StringComparison.Ordinal))
            .ToList();

        // Assert: Identity and Registry are the modules with endpoints today; a new module adds its own prefix here.
        outside.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenApiDocument_DescribesExactlyTheRoutedIdentityOperations()
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
    public async Task OpenApiDocument_QueryParameterNames_AreCamelCase()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);

        // Act
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);
        using var openApi = JsonDocument.Parse(document);
        var queryNames = openApi.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(m => m.Value.TryGetProperty("parameters", out _))
            .SelectMany(m => m.Value.GetProperty("parameters").EnumerateArray())
            .Where(p => p.GetProperty("in").GetString() == "query")
            .Select(p => p.GetProperty("name").GetString()!)
            .ToList();

        // Assert
        queryNames.ShouldContain("pageSize");
        queryNames.Where(n => char.IsUpper(n[0])).ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryProtectedIdentityOperation_AnonymousCaller_Returns401()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);
        var client = factory.CreateClient();
        var operations = RoutedOperations(factory.Services).Where(o => !o.AllowsAnonymous).ToList();

        // Act
        var outcomes = new List<string>();
        foreach (var (method, route, _, _) in operations)
        {
            var url = Regex.Replace(route, @"\{[^}]+\}", Guid.Empty.ToString());
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                outcomes.Add($"{method} {route} -> {(int)response.StatusCode}");
            }
        }

        // Assert
        operations.ShouldNotBeEmpty();
        outcomes.ShouldBeEmpty();
    }
}
