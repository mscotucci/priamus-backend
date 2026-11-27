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
/// Rules that hold for every endpoint of the back office, proved as a sweep over the endpoints the host
/// actually routes under <c>/api/operations/backoffice</c>: each one declares exactly the
/// <c>BackOfficeOrAdmin</c> policy and none is anonymous, the OpenAPI document describes exactly the routed
/// set under the back-office tag, an anonymous caller gets 401, a Driver gets 403, and a BackOffice and an
/// Admin are let in. Each slice that adds an endpoint is covered from then on.
/// </summary>
public sealed class OperationsBackOfficeSweepTests : OperationsIntegrationTestBase
{
    private const string Prefix = "/api/operations/backoffice/";
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public OperationsBackOfficeSweepTests(PostgresFixture fixture) : base(fixture) { }

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
    public async Task EveryRoutedBackOfficeOperation_DeclaresTheBackOfficeOrAdminPolicyAndIsNeverAnonymous()
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
        operations.Select(o => $"{o.Method} {o.Route}").ShouldContain("GET /api/operations/backoffice/trips");
        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenApiDocument_ListsEveryBackOfficeOperationUnderTheBackOfficeTagExactlyAsRouted()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);
        var routed = RoutedOperations(factory.Services)
            .Select(o => $"{o.Method} {o.Route}")
            .OrderBy(o => o, StringComparer.Ordinal);

        // Act
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", Ct);
        using var openApi = JsonDocument.Parse(document);
        var operations = openApi.RootElement.GetProperty("paths").EnumerateObject()
            .Where(p => p.Name.StartsWith(Prefix, StringComparison.Ordinal))
            .SelectMany(p => p.Value.EnumerateObject()
                .Where(m => HttpMethods.Contains(m.Name.ToUpperInvariant()))
                .Select(m => (Key: $"{m.Name.ToUpperInvariant()} {p.Name}", Operation: m.Value)))
            .ToList();

        // Assert
        operations.Select(o => o.Key).OrderBy(o => o, StringComparer.Ordinal).ShouldBe(routed);
        operations.ShouldAllBe(o => o.Operation.GetProperty("tags").EnumerateArray()
            .Any(t => t.GetString() == "Operations.BackOffice"));
        operations.Select(o => o.Operation.GetProperty("operationId").GetString()).ShouldContain("ListBackOfficeTrips");
    }

    [Fact]
    public async Task EveryRoutedBackOfficeOperation_AnonymousCaller_Returns401()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);

        // Act
        var outcomes = await UnexpectedStatusesAsync(factory, null, HttpStatusCode.Unauthorized);

        // Assert
        outcomes.ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryRoutedBackOfficeOperation_Driver_Returns403()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString, useTestAuthentication: true);

        // Act
        var outcomes = await UnexpectedStatusesAsync(
            factory, TestCaller.DriverWith(Guid.NewGuid()), HttpStatusCode.Forbidden);

        // Assert
        outcomes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("BackOffice")]
    [InlineData("Admin")]
    public async Task EveryRoutedBackOfficeOperation_BackOfficeAndAdmin_AreLetIn(string role)
    {
        // Arrange
        await using var factory = ApiFactory.Create(
            ConnectionString,
            services => services.AddSingleton<ITripRegistry>(new FakeTripRegistry()),
            useTestAuthentication: true);
        var client = TestCaller.WithRole(role).ApplyTo(factory.CreateClient());

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
