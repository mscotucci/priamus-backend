using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Application.Services;
using Priamus.Trips.Identity.Contracts;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Application;

/// <summary>
/// <see cref="UserLookup"/> against real PostgreSQL: the account facts Registry reads (ADR 0013).
/// </summary>
public sealed class UserLookupTests : IdentityIntegrationTestBase
{
    public UserLookupTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetAccounts_FoundAndMissingIds_ReturnsOnlyTheFoundOnes()
    {
        var ct = TestContext.Current.CancellationToken;
        var alice = TestDataBuilders.AUser().WithUsername("alice.driver").WithRole(UserRole.Driver).Build();
        var bob = TestDataBuilders.AUser().WithUsername("bob.driver").WithRole(UserRole.Driver).Build();
        await SeedAsync(ct, alice, bob);
        var missing = Guid.NewGuid();

        var accounts = await GetAccountsAsync([alice.Id.Value, missing], ct);

        accounts.Count.ShouldBe(1);
        accounts.ContainsKey(missing).ShouldBeFalse();
        accounts[alice.Id.Value].Username.ShouldBe("alice.driver");
    }

    [Fact]
    public async Task GetAccounts_MapsRoleAndStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var activeDriver = TestDataBuilders.AUser().WithUsername("active.driver").WithRole(UserRole.Driver).Build();
        var disabledDriver = TestDataBuilders.AUser().WithUsername("disabled.driver").WithRole(UserRole.Driver).Build();
        disabledDriver.Disable(new SystemClock(), TestDataBuilders.ActorId);
        var backOffice = TestDataBuilders.AUser().WithUsername("office.user").WithRole(UserRole.BackOffice).Build();
        await SeedAsync(ct, activeDriver, disabledDriver, backOffice);

        var accounts = await GetAccountsAsync(
            [activeDriver.Id.Value, disabledDriver.Id.Value, backOffice.Id.Value], ct);

        accounts[activeDriver.Id.Value].ShouldBe(
            new UserAccount(activeDriver.Id.Value, "active.driver", IsDriver: true, IsDisabled: false));
        accounts[disabledDriver.Id.Value].ShouldBe(
            new UserAccount(disabledDriver.Id.Value, "disabled.driver", IsDriver: true, IsDisabled: true));
        accounts[backOffice.Id.Value].ShouldBe(
            new UserAccount(backOffice.Id.Value, "office.user", IsDriver: false, IsDisabled: false));
    }

    [Fact]
    public async Task GetAccounts_EmptyInput_ReturnsEmptyDictionary()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct, TestDataBuilders.AUser().Build());

        var accounts = await GetAccountsAsync([], ct);

        accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListDriverAccounts_ReturnsEnabledAndDisabledDriversOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var enabled = TestDataBuilders.AUser().WithUsername("enabled.driver").WithRole(UserRole.Driver).Build();
        var disabled = TestDataBuilders.AUser().WithUsername("disabled.driver").WithRole(UserRole.Driver).Build();
        disabled.Disable(new SystemClock(), TestDataBuilders.ActorId);
        var admin = TestDataBuilders.AUser().WithUsername("the.admin").WithRole(UserRole.Admin).Build();
        var backOffice = TestDataBuilders.AUser().WithUsername("the.office").WithRole(UserRole.BackOffice).Build();
        await SeedAsync(ct, enabled, disabled, admin, backOffice);

        await using var scope = CreateAsyncScope();
        var lookup = new UserLookup(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
        var accounts = await lookup.ListDriverAccountsAsync(ct);

        accounts.OrderBy(a => a.Username).ToList().ShouldBe(
        [
            new UserAccount(disabled.Id.Value, "disabled.driver", IsDriver: true, IsDisabled: true),
            new UserAccount(enabled.Id.Value, "enabled.driver", IsDriver: true, IsDisabled: false),
        ]);
    }

    private async Task<IReadOnlyDictionary<Guid, UserAccount>> GetAccountsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        await using var scope = CreateAsyncScope();
        var lookup = new UserLookup(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
        return await lookup.GetAccountsAsync(ids, ct);
    }

    private async Task SeedAsync(CancellationToken ct, params User[] users)
    {
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.AddRange(users);
        await db.SaveChangesAsync(ct);
    }
}
