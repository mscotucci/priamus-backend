using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.GetCurrentUser;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.GetCurrentUser;

/// <summary>
/// Integration tests for <c>GET /api/identity/me</c>: the signed-in user as the database knows it now.
/// </summary>
public sealed class GetCurrentUserEndpointTests : IdentityIntegrationTestBase
{
    private const string Password = "Cur3nt!Pass1";

    public GetCurrentUserEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class StubDriverLookup(Guid? driverId) : IDriverLookup
    {
        public Task<Guid?> GetDriverIdByUserIdAsync(UserId userId, CancellationToken cancellationToken)
            => Task.FromResult(driverId);
    }

    private WebApplicationFactory<Program> CreateFactory(Guid? driverId = null)
        => ApiFactory.Create(ConnectionString, services =>
        {
            services.RemoveAll<IDriverLookup>();
            services.AddSingleton<IDriverLookup>(new StubDriverLookup(driverId));
        });

    private async Task<User> SeedUserAsync(UserRole role, string username, CancellationToken ct)
    {
        var stub = TestDataBuilders.AUser().WithRole(role).WithUsername(username).Build();
        var hash = new PasswordHasher<User>().HashPassword(stub, Password);

        var user = User.Create(
            stub.Id,
            Username.Create(stub.Username.Value),
            PasswordHash.Create(hash),
            role,
            new SystemClock(),
            TestDataBuilders.ActorId);

        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private static async Task<HttpClient> SignInAsync(
        WebApplicationFactory<Program> factory, User user, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/identity/auth/login", new LoginRequest(user.Username.Value, Password), ct);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.BackOffice)]
    [InlineData(UserRole.Driver)]
    public async Task GetMe_WithValidToken_Returns200WithIdUsernameRoleAndDriverId(UserRole role)
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await SeedUserAsync(role, $"me.{role}".ToLowerInvariant(), ct);

        await using var factory = CreateFactory();
        var client = await SignInAsync(factory, user, ct);

        var response = await client.GetAsync("/api/identity/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCurrentUserResponse>(TestJson.Options, ct);
        body.ShouldNotBeNull();
        body.Id.ShouldBe(user.Id.Value);
        body.Username.ShouldBe(user.Username.Value);
        body.Role.ShouldBe(role);

        var raw = await client.GetFromJsonAsync<JsonElement>("/api/identity/me", ct);
        raw.GetProperty("role").GetString().ShouldBe(role.ToString(),
            "The role must travel by name, not as a number.");
        raw.TryGetProperty("driverId", out _).ShouldBeTrue("driverId must be present in the body.");
    }

    [Fact]
    public async Task GetMe_ForDriverLinkedToDriverRecord_ReturnsThatDriverId()
    {
        var ct = TestContext.Current.CancellationToken;
        var linkedDriverId = Guid.NewGuid();
        var user = await SeedUserAsync(UserRole.Driver, "linked.driver", ct);

        await using var factory = CreateFactory(linkedDriverId);
        var client = await SignInAsync(factory, user, ct);

        var body = await client.GetFromJsonAsync<GetCurrentUserResponse>("/api/identity/me", TestJson.Options, ct);

        body.ShouldNotBeNull();
        body.DriverId.ShouldBe(linkedDriverId);
    }

    [Theory]
    [InlineData(UserRole.Driver)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.BackOffice)]
    public async Task GetMe_ForDriverWithoutLinkAndForOtherRoles_ReturnsNullDriverId(UserRole role)
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await SeedUserAsync(role, $"nolink.{role}".ToLowerInvariant(), ct);

        // The stub answers an id for the roles that must never ask the lookup, and null for the
        // Driver with no link: a non-null driverId would show a non-Driver consulting it.
        await using var factory = CreateFactory(role == UserRole.Driver ? null : Guid.NewGuid());
        var client = await SignInAsync(factory, user, ct);

        var raw = await client.GetFromJsonAsync<JsonElement>("/api/identity/me", ct);

        raw.GetProperty("driverId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetMe_ForUserDisabledAfterSigningIn_Returns401WithAccountDisabledAndItalianDetail()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await SeedUserAsync(UserRole.BackOffice, "disabled.later", ct);

        await using var factory = CreateFactory();
        var client = await SignInAsync(factory, user, ct);

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tracked = await db.Users.FirstAsync(u => u.Id == user.Id, ct);
            tracked.Disable(new SystemClock(), TestDataBuilders.ActorId);
            await db.SaveChangesAsync(ct);
        }

        var response = await client.GetAsync("/api/identity/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("errorCode").GetString().ShouldBe(UserErrors.AccountDisabled.Code);
        body.GetProperty("errorType").GetString().ShouldBe(UserErrors.AccountDisabled.Type.ToString());
        body.GetProperty("detail").GetString()
            .ShouldBe("L'account è disabilitato. Contattare un amministratore.");
    }

    [Fact]
    public async Task GetMe_ForUserRemovedAfterSigningIn_Returns401WithInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await SeedUserAsync(UserRole.Admin, "removed.later", ct);

        await using var factory = CreateFactory();
        var client = await SignInAsync(factory, user, ct);

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            // refresh_tokens has no foreign key to users, so the row can go regardless of the token
            // issued by the sign-in above.
            await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync(ct);
        }

        var response = await client.GetAsync("/api/identity/me", ct);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, UserErrors.InvalidCredentials, ct);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/identity/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
