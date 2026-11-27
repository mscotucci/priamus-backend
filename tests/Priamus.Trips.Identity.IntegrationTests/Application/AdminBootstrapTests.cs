using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Application.Services;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Identity.Persistence.DependencyInjection;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Application;

/// <summary>
/// The bootstrap administrator against real PostgreSQL: through the host's startup where sign-in
/// is part of the proof, and at the level of the service where two scopes must race.
/// </summary>
public sealed class AdminBootstrapTests : IdentityIntegrationTestBase
{
    private const string AdminUsername = "first.admin";
    private const string AdminPassword = "Str0ng-bootstrap-pass!";

    private static readonly DateTime Now = new(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc);

    public AdminBootstrapTests(PostgresFixture fixture) : base(fixture) { }

    // ── Host startup ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Startup_OnEmptyUsersWithValidConfiguration_CreatesTheAdminWhoCanSignIn()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = HostWithBootstrap(AdminUsername, AdminPassword);

        // Act
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/identity/auth/login", new LoginRequest(AdminUsername, AdminPassword), ct);

        // Assert
        await using var db = CreateDbContext();
        var user = await db.Users.SingleAsync(ct);
        user.Username.Value.ShouldBe(AdminUsername);
        user.Role.ShouldBe(UserRole.Admin);
        user.Status.ShouldBe(UserStatus.Active);
        user.CreatedBy.ShouldBe(user.Id);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Startup_CreatingTheAdmin_WritesUserCreatedWithoutActorAndAdminBootstrappedByTheAdmin()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = HostWithBootstrap(AdminUsername, AdminPassword);

        // Act
        using var client = factory.CreateClient();

        // Assert
        await using var db = CreateDbContext();
        var user = await db.Users.SingleAsync(ct);
        var rows = await db.AuditLog.Where(a => a.SubjectId == user.Id.Value).ToListAsync(ct);
        rows.Count.ShouldBe(2);
        rows.Single(r => r.EventType == AuditEventTypes.UserCreated).ActorId.ShouldBeNull();
        rows.Single(r => r.EventType == AuditEventTypes.AdminBootstrapped).ActorId.ShouldBe(user.Id.Value);
    }

    [Theory]
    [InlineData(AdminUsername, "short1!", "password rule")]
    [InlineData(AdminUsername, "NoDigitsHere!!", "password rule")]
    [InlineData(AdminUsername, "NoSpecial12345", "password rule")]
    [InlineData("ab", AdminPassword, "Username")]
    [InlineData("-leading-dash", AdminPassword, "Username")]
    [InlineData("white space", AdminPassword, "Username")]
    public async Task Startup_OnEmptyUsersWithInvalidConfiguration_FailsNamingTheRuleAndCreatesNoUser(
        string username, string password, string expectedInMessage)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var factory = HostWithBootstrap(username, password);

        // Act
        var act = () => factory.CreateClient();

        // Assert
        var exception = Should.Throw<InvalidOperationException>(act);
        exception.Message.ShouldContain(expectedInMessage);
        exception.Message.ShouldNotContain(password);
        await using var db = CreateDbContext();
        (await db.Users.CountAsync(ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(AdminUsername, null)]
    [InlineData(null, AdminPassword)]
    [InlineData("", AdminPassword)]
    public async Task Startup_OnEmptyUsersWithoutCompleteConfiguration_StartsAndLogsAWarning(
        string? username, string? password)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        await using var factory = HostWithBootstrap(username, password, logs);

        // Act
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("nobody can sign in"));
        await using var db = CreateDbContext();
        (await db.Users.CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Startup_OnNonEmptyUsers_ChangesNothingWhateverTheConfiguration()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var existing = TestDataBuilders.AUser().WithUsername("existing.user").WithRole(UserRole.Driver).Build();
        await using (var seedDb = CreateDbContext())
        {
            seedDb.Users.Add(existing);
            await seedDb.SaveChangesAsync(ct);
        }

        var logs = new CapturingLoggerProvider();
        await using var factory = HostWithBootstrap(AdminUsername, AdminPassword, logs);

        // Act
        using var client = factory.CreateClient();

        // Assert
        await using var db = CreateDbContext();
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        var only = users.ShouldHaveSingleItem();
        only.Id.ShouldBe(existing.Id);
        only.Username.Value.ShouldBe("existing.user");
        only.Role.ShouldBe(UserRole.Driver);
        only.Version.ShouldBe(existing.Version);
        (await db.AuditLog.CountAsync(a => a.EventType == AuditEventTypes.AdminBootstrapped, ct)).ShouldBe(0);
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Message.Contains("is ignored"));
    }

    // ── Service level ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TwoBootstrapsOnAnEmptyTable_LeaveExactlyOneAdminAndNeitherThrows()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildServiceProvider();
        await using var scopeA = provider.CreateAsyncScope();
        await using var scopeB = provider.CreateAsyncScope();
        var serviceA = scopeA.ServiceProvider.GetRequiredService<AdminBootstrapService>();
        var serviceB = scopeB.ServiceProvider.GetRequiredService<AdminBootstrapService>();

        // Act
        var act = () => Task.WhenAll(serviceA.RunAsync(ct), serviceB.RunAsync(ct));

        // Assert
        await Should.NotThrowAsync(act);
        await using var db = CreateDbContext();
        var admin = await db.Users.SingleAsync(ct);
        admin.Role.ShouldBe(UserRole.Admin);
        (await db.AuditLog.CountAsync(a => a.EventType == AuditEventTypes.AdminBootstrapped, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_WhenAnotherInstanceSavesTheSameUsernameFirst_DoesNotThrowAndLeavesOneUser()
    {
        // Arrange: the interceptor inserts the user through a separate context just before the
        // service saves, so the service's own save hits uq_users_username.
        var ct = TestContext.Current.CancellationToken;
        var competing = TestDataBuilders.AUser().WithUsername(AdminUsername).WithRole(UserRole.Admin).Build();
        var interceptor = new CompetingInsertInterceptor(CreateDbContext, competing);
        await using var provider = BuildServiceProvider(interceptor);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AdminBootstrapService>();

        // Act
        var act = () => service.RunAsync(ct);

        // Assert
        await Should.NotThrowAsync(act);
        interceptor.Fired.ShouldBeTrue();
        await using var db = CreateDbContext();
        var only = await db.Users.SingleAsync(ct);
        only.Username.Value.ShouldBe(AdminUsername);
        only.Id.ShouldBe(competing.Id);
        (await db.AuditLog.CountAsync(a => a.EventType == AuditEventTypes.AdminBootstrapped, ct)).ShouldBe(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> HostWithBootstrap(
        string? username, string? password, CapturingLoggerProvider? logs = null)
    {
        return ApiFactory.Create(ConnectionString, services =>
            {
                if (logs is not null)
                {
                    services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs]));
                }
            })
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Identity:BootstrapAdmin:Username", username ?? string.Empty);
                builder.UseSetting("Identity:BootstrapAdmin:Password", password ?? string.Empty);
            });
    }

    /// <summary>
    /// A container with an unauthenticated current user, as at startup when no request is in progress.
    /// </summary>
    private ServiceProvider BuildServiceProvider(SaveChangesInterceptor? extraInterceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser, NoRequestCurrentUser>();
        services.AddSingleton<IClock>(new FixedClockStub(Now));
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddIdentityPersistence(ConnectionString);
        if (extraInterceptor is not null)
        {
            services.ConfigureDbContext<IdentityDbContext>(options => options.AddInterceptors(extraInterceptor));
        }

        services.AddOptions<BootstrapAdminOptions>().Configure(o =>
        {
            o.Username = AdminUsername;
            o.Password = AdminPassword;
        });
        services.AddScoped<AdminBootstrapService>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Before the first save of the context it is attached to, inserts a competing user through
    /// another context, forcing the unique-violation path of the service's own save.
    /// </summary>
    private sealed class CompetingInsertInterceptor(Func<IdentityDbContext> createDbContext, User competing)
        : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var other = createDbContext();
                other.Users.Add(competing);
                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class NoRequestCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public UserId UserId => throw new InvalidOperationException("No request is in progress.");

        public IReadOnlyCollection<string> Roles => [];

        public Guid? DriverId => null;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries)
                {
                    owner._entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
