using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.ListUsers;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Paging;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.ListUsers;

/// <summary>
/// Integration tests for <c>GET /api/users</c>.
///
/// Each test owns an isolated PostgreSQL database (cloned from the migrated template)
/// via <see cref="IdentityIntegrationTestBase"/>. Seeds are inserted in a dedicated
/// Arrange scope; assertions use a separate Assert scope with AsNoTracking reads.
/// </summary>
public sealed class ListUsersEndpointTests : IdentityIntegrationTestBase
{
    private const string TestSigningKey = "test-signing-key-endpoint-32chars!!";
    private const string TestIssuer    = "test-issuer";
    private const string TestAudience  = "test-audience";

    public ListUsersEndpointTests(PostgresFixture fixture) : base(fixture) { }

    // ── Factory helpers ───────────────────────────────────────────────────────

    private WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Identity", ConnectionString);
                builder.UseSetting("Jwt:Issuer",     TestIssuer);
                builder.UseSetting("Jwt:Audience",   TestAudience);
                builder.UseSetting("Jwt:SigningKey",  TestSigningKey);
                builder.UseSetting("Jwt:AccessTokenLifetime",  "01:00:00");
                builder.UseSetting("Jwt:RefreshTokenLifetime", "12:00:00");

                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }

    /// <summary>
    /// Seeds a user into the isolated test database.
    /// The <paramref name="clock"/> parameter allows callers to provide a
    /// <see cref="FixedClockStub"/> with a deterministic instant so that
    /// sort-order assertions are stable.
    /// When <paramref name="disabled"/> is true the user is persisted in
    /// <see cref="UserStatus.Disabled"/> state with no audit rows (domain
    /// events are cleared before SaveChangesAsync).
    /// </summary>
    private async Task<User> SeedUserAsync(
        UserRole role,
        string plainPassword,
        string? username = null,
        bool disabled = false,
        DateTime? createdAt = null,
        CancellationToken ct = default)
    {
        var passwordHasher = new PasswordHasher<User>();

        var stub = username is not null
            ? TestDataBuilders.AUser().WithRole(role).WithUsername(username).Build()
            : TestDataBuilders.AUser().WithRole(role).Build();

        var hashString = passwordHasher.HashPassword(stub, plainPassword);

        var clock = createdAt.HasValue
            ? (Priamus.Trips.SharedKernel.Time.IClock)new FixedClockStub(createdAt.Value)
            : new Priamus.Trips.SharedKernel.Time.SystemClock();

        var user = User.Create(
            stub.Id,
            Username.Create(stub.Username.Value),
            PasswordHash.Create(hashString),
            role,
            clock,
            TestDataBuilders.ActorId);

        if (disabled)
        {
            user.Disable(clock, TestDataBuilders.ActorId);
            user.ClearDomainEvents();
        }

        await using (var scope = CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    private static async Task<LoginResponse> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken ct)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password), ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK,
            $"Login must succeed for user '{username}' to obtain a JWT for this test.");
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>(ct);
        body.ShouldNotBeNull();
        return body;
    }

    private static async Task<HttpClient> CreateAuthenticatedAdminClientAsync(
        WebApplicationFactory<Program> factory,
        User adminUser,
        string adminPassword,
        CancellationToken ct)
    {
        var client = factory.CreateClient();
        var loginBody = await LoginAsync(client, adminUser.Username.Value, adminPassword, ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);
        return client;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 1 — Happy path: empty filters, default sort (createdAt DESC)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithNoFilters_Returns200WithAllSeededUsersInCreatedAtDescOrder()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed 5 users with distinct, deterministic CreatedAt values (oldest first).
        var base1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!P1", createdAt: base1.AddMinutes(1), ct: ct);
        var user2     = await SeedUserAsync(UserRole.Driver, "Driv3r!P1", createdAt: base1.AddMinutes(2), ct: ct);
        var user3     = await SeedUserAsync(UserRole.Driver, "Driv3r!P2", createdAt: base1.AddMinutes(3), ct: ct);
        var user4     = await SeedUserAsync(UserRole.BackOffice, "B4ck!P1", createdAt: base1.AddMinutes(4), ct: ct);
        var user5     = await SeedUserAsync(UserRole.BackOffice, "B4ck!P2", createdAt: base1.AddMinutes(5), ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P1", ct);

        // Act
        var response = await client.GetAsync("/api/users", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();
        body.Data.Count.ShouldBe(5, "All 5 seeded users must be returned when no filters are applied.");
        body.TotalCount.ShouldBe(5, "TotalCount must equal the full unfiltered set.");

        // Default sort is createdAt DESC — user5 first, adminUser last.
        body.Data[0].Id.ShouldBe(user5.Id.Value);
        body.Data[1].Id.ShouldBe(user4.Id.Value);
        body.Data[2].Id.ShouldBe(user3.Id.Value);
        body.Data[3].Id.ShouldBe(user2.Id.Value);
        body.Data[4].Id.ShouldBe(adminUser.Id.Value);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 2 — Filter by Role
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithRoleFilterDriver_Returns200WithOnlyDrivers()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: 3 Drivers, 2 BackOffice, 1 Admin
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!P2", ct: ct);
        await SeedUserAsync(UserRole.Driver, "Driv3r!P3", ct: ct);
        await SeedUserAsync(UserRole.Driver, "Driv3r!P4", ct: ct);
        await SeedUserAsync(UserRole.Driver, "Driv3r!P5", ct: ct);
        await SeedUserAsync(UserRole.BackOffice, "B4ck!P3", ct: ct);
        await SeedUserAsync(UserRole.BackOffice, "B4ck!P4", ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P2", ct);

        // Act
        var response = await client.GetAsync("/api/users?role=Driver", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();
        body.Data.Count.ShouldBe(3, "Exactly 3 Driver users must be returned.");
        body.TotalCount.ShouldBe(3);
        body.Data.ShouldAllBe(u => u.Role == UserRole.Driver);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 3 — Filter by Status
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithStatusFilterDisabled_Returns200WithOnlyDisabledUsers()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: 3 Active users, 2 Disabled users
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!P3", ct: ct);
        await SeedUserAsync(UserRole.Driver, "Driv3r!P6", ct: ct);
        await SeedUserAsync(UserRole.BackOffice, "B4ck!P5", ct: ct);
        var disabled1 = await SeedUserAsync(UserRole.Driver, "Driv3r!P7", disabled: true, ct: ct);
        var disabled2 = await SeedUserAsync(UserRole.BackOffice, "B4ck!P6", disabled: true, ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P3", ct);

        // Act
        var response = await client.GetAsync("/api/users?status=Disabled", ct);

        // Assert
        var statusResponseBody = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"Status filter test body: {statusResponseBody}");

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();
        body.Data.Count.ShouldBe(2, "Exactly 2 Disabled users must be returned.");
        body.TotalCount.ShouldBe(2);
        body.Data.ShouldAllBe(u => u.Status == UserStatus.Disabled);
        body.Data.Select(u => u.Id).ShouldContain(disabled1.Id.Value);
        body.Data.Select(u => u.Id).ShouldContain(disabled2.Id.Value);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 4 — UsernameContains is case-insensitive
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithUsernameContainsFilterUppercase_Returns200WithCaseInsensitiveMatch()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: the Username VO normalizes to lowercase on creation.
        // Seeding with "aliceadmin" stores the value as "aliceadmin".
        // Searching with "ALICE" (uppercase needle) must still match.
        var adminUser  = await SeedUserAsync(UserRole.Admin, "Adm1n!P4", username: "aliceadmin", ct: ct);
        await SeedUserAsync(UserRole.Driver, "Driv3r!P8", username: "bobdriver", ct: ct);
        await SeedUserAsync(UserRole.BackOffice, "B4ck!P7", username: "carolback", ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P4", ct);

        // Act: uppercase needle "ALICE" must match "aliceadmin" stored in lowercase
        var response = await client.GetAsync("/api/users?usernameContains=ALICE", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();
        body.Data.Count.ShouldBe(1, "Exactly 1 user must match the case-insensitive filter 'ALICE'.");
        body.Data[0].Username.ShouldBe("aliceadmin",
            "The returned username must be 'aliceadmin' (normalized to lowercase).");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 5 — Combined filters compose with AND semantics
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithRoleAndStatusAndUsernameContainsFilters_Returns200WithAndIntersection()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: plant users that match subsets of the combined filter.
        // Only "john_driver" (Driver + Active + contains "jo") must be returned.
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!P5", username: "adminmaster", ct: ct);

        // Matches role=Driver AND usernameContains=jo AND status=Active  ← SHOULD MATCH
        var matchUser = await SeedUserAsync(UserRole.Driver, "Driv3r!P9", username: "john_driver", ct: ct);

        // Matches role=Driver AND usernameContains=jo but status=Disabled ← should NOT match
        await SeedUserAsync(UserRole.Driver, "Driv3r!PA", username: "joe_disabled", disabled: true, ct: ct);

        // Matches role=BackOffice AND usernameContains=jo ← should NOT match (wrong role)
        await SeedUserAsync(UserRole.BackOffice, "B4ck!P8", username: "joanne_back", ct: ct);

        // Matches role=Driver AND status=Active but NOT usernameContains=jo ← should NOT match
        await SeedUserAsync(UserRole.Driver, "Driv3r!PB", username: "alice_driver", ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P5", ct);

        // Act
        var response = await client.GetAsync(
            "/api/users?role=Driver&status=Active&usernameContains=jo", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();
        body.Data.Count.ShouldBe(1, "Only one user satisfies all three filters simultaneously.");
        body.Data[0].Id.ShouldBe(matchUser.Id.Value);
        body.Data[0].Role.ShouldBe(UserRole.Driver);
        body.Data[0].Status.ShouldBe(UserStatus.Active);
        body.Data[0].Username.ShouldContain("jo");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 6 — Sort whitelist: every column × every direction
    // ══════════════════════════════════════════════════════════════════════════

    // SortDirection values: Ascending = 0, Descending = 1.
    // The query-string param name is "sortDirection"; numeric values are used because
    // ASP.NET Core 10 minimal-API enum binding reliably parses numeric values from
    // query strings regardless of locale or casing.
    [Theory]
    [InlineData("username",       0, true)]
    [InlineData("username",       1, false)]
    [InlineData("role",           0, true)]
    [InlineData("role",           1, false)]
    [InlineData("status",         0, true)]
    [InlineData("status",         1, false)]
    [InlineData("createdAt",      0, true)]
    [InlineData("createdAt",      1, false)]
    [InlineData("lastModifiedAt", 0, true)]
    [InlineData("lastModifiedAt", 1, false)]
    public async Task GetUsers_WithWhitelistedSortColumn_Returns200WithCorrectlySortedItems(
        string sortColumn,
        int sortDirectionValue,
        bool ascending)
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed 3 users with deterministic, distinct timestamps and distinct
        // username/role/status values so every sort produces an unambiguous order.
        var base2 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        // User A: username "aaaa_user", Driver, Active, earliest timestamps
        var userA = await SeedUserAsync(
            UserRole.Driver, "Driv3r!PC",
            username: "aaaa_user",
            createdAt: base2.AddMinutes(1),
            ct: ct);

        // User B: username "mmmm_user", BackOffice, Active, middle timestamps
        var adminUser = await SeedUserAsync(
            UserRole.Admin, "Adm1n!P6",
            username: "mmmm_user",
            createdAt: base2.AddMinutes(2),
            ct: ct);

        // User C: username "zzzz_user", Admin, Disabled, latest timestamps
        var userC = await SeedUserAsync(
            UserRole.Admin, "Adm1n!P7",
            username: "zzzz_user",
            disabled: true,
            createdAt: base2.AddMinutes(3),
            ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P6", ct);

        // Act: include page=1&pageSize=20 explicitly because ASP.NET Core's [AsParameters]
        // binder requires non-nullable int parameters (Page, PageSize) to be present in
        // the query string when other non-nullable parameters (sortDirection) are also provided.
        var url = $"/api/users?sortColumn={sortColumn}&sortDirection={sortDirectionValue}&page=1&pageSize=20";
        var response = await client.GetAsync(url, ct);

        // Assert — HTTP status (read body for diagnostic output on failure)
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            $"GET {url} must return 200 OK. Body: {responseBody}");

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();
        body.Data.Count.ShouldBe(3, "All 3 seeded users must be returned.");

        switch (sortColumn.ToLowerInvariant())
        {
            case "username":
            {
                // Ascending:  aaaa_user < mmmm_user < zzzz_user
                // Descending: zzzz_user > mmmm_user > aaaa_user
                var usernames = body.Data.Select(u => u.Username).ToList();
                var sorted = ascending
                    ? usernames.OrderBy(n => n).ToList()
                    : usernames.OrderByDescending(n => n).ToList();
                usernames.ShouldBe(sorted,
                    $"username {(ascending ? "ASC" : "DESC")}: items must be in alphabetical order.");
                break;
            }

            case "role":
                // Driver=1, Admin=3 (two Admins — assert only the cross-role boundary)
                if (ascending)
                {
                    body.Data[0].Role.ShouldBe(UserRole.Driver,
                        "role ASC: Driver user (lowest enum value) must be first.");
                }
                else
                {
                    body.Data[2].Role.ShouldBe(UserRole.Driver,
                        "role DESC: Driver user (lowest enum value) must be last.");
                }
                break;

            case "status":
                // Active=1, Disabled=2 — one disabled (userC), two active
                if (ascending)
                {
                    body.Data[0].Status.ShouldBe(UserStatus.Active,
                        "status ASC: Active users must come before Disabled.");
                    body.Data[2].Status.ShouldBe(UserStatus.Disabled,
                        "status ASC: Disabled user must be last.");
                }
                else
                {
                    body.Data[0].Status.ShouldBe(UserStatus.Disabled,
                        "status DESC: Disabled user must be first.");
                    body.Data[2].Status.ShouldBe(UserStatus.Active,
                        "status DESC: Active users must come last.");
                }
                break;

            case "createdat":
                // base2+1m (userA) < base2+2m (adminUser) < base2+3m (userC)
                if (ascending)
                {
                    body.Data[0].Id.ShouldBe(userA.Id.Value, "createdAt ASC: earliest user first.");
                    body.Data[2].Id.ShouldBe(userC.Id.Value, "createdAt ASC: latest user last.");
                }
                else
                {
                    body.Data[0].Id.ShouldBe(userC.Id.Value, "createdAt DESC: latest user first.");
                    body.Data[2].Id.ShouldBe(userA.Id.Value, "createdAt DESC: earliest user last.");
                }
                break;

            case "lastmodifiedat":
                // All three users were just created and not modified, so LastModifiedAt == CreatedAt.
                // Ordering by lastModifiedAt therefore follows the same CreatedAt ordering.
                if (ascending)
                {
                    body.Data[0].Id.ShouldBe(userA.Id.Value, "lastModifiedAt ASC: earliest user first.");
                    body.Data[2].Id.ShouldBe(userC.Id.Value, "lastModifiedAt ASC: latest user last.");
                }
                else
                {
                    body.Data[0].Id.ShouldBe(userC.Id.Value, "lastModifiedAt DESC: latest user first.");
                    body.Data[2].Id.ShouldBe(userA.Id.Value, "lastModifiedAt DESC: earliest user last.");
                }
                break;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 7 — Unknown sort column falls back to createdAt DESC
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithUnknownSortColumn_Returns200WithCreatedAtDescFallback()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed 3 users with distinct, deterministic CreatedAt values.
        var base3 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!P8", createdAt: base3.AddMinutes(1), ct: ct);
        var userB     = await SeedUserAsync(UserRole.Driver, "Driv3r!PD", createdAt: base3.AddMinutes(2), ct: ct);
        var userC2    = await SeedUserAsync(UserRole.BackOffice, "B4ck!P9", createdAt: base3.AddMinutes(3), ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P8", ct);

        // Act: use an explicitly unknown column that is NOT in the whitelist.
        // The handler's wildcard arm must pin to OrderByDescending(u => u.CreatedAt)
        // regardless of the SortDirection supplied by the caller.
        // Supply SortDirection=0 (Ascending) explicitly — the fallback must ignore it and apply DESC.
        // Also supply page=1&pageSize=20 because ASP.NET Core [AsParameters] requires
        // non-nullable int params when other non-nullable params are present.
        var response = await client.GetAsync(
            "/api/users?sortColumn=passwordHash&sortDirection=0&page=1&pageSize=20", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();
        body.Data.Count.ShouldBe(3);

        // Expected order: userC2 (latest) → userB → adminUser (oldest) = createdAt DESC
        body.Data[0].Id.ShouldBe(userC2.Id.Value,
            "Unknown sort column must fall back to createdAt DESC: latest user first.");
        body.Data[1].Id.ShouldBe(userB.Id.Value,
            "Unknown sort column must fall back to createdAt DESC: middle user second.");
        body.Data[2].Id.ShouldBe(adminUser.Id.Value,
            "Unknown sort column must fall back to createdAt DESC: oldest user last.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 8 — Paging: TotalCount reflects the filtered total, not the page count
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithPagingAndFilter_Returns200WithCorrectPageAndFilteredTotalCount()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed 12 Driver users + 1 Admin (to authenticate).
        // Use usernameContains filter so the TotalCount is the filtered count (12 Drivers),
        // not the page count (5). Combined filter + paging is the mandatory scenario.
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!P9", username: "pagingadmin", ct: ct);

        var base4 = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 12; i++)
        {
            await SeedUserAsync(
                UserRole.Driver,
                $"Driv3r!{i:D2}",
                username: $"pagingdriver{i:D2}",
                createdAt: base4.AddMinutes(i),
                ct: ct);
        }

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!P9", ct);

        // Act: page=2, pageSize=5, filter=Driver, usernameContains=pagingdriver
        // Filtered set = 12 Driver users whose username contains "pagingdriver".
        // Default sort is createdAt DESC, so page 1 = users 12..8, page 2 = users 7..3.
        var response = await client.GetAsync(
            "/api/users?role=Driver&usernameContains=pagingdriver&page=2&pageSize=5", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<UserListItemDto>>(ct);
        body.ShouldNotBeNull();

        body.TotalCount.ShouldBe(12,
            "TotalCount must reflect the full filtered set (12 Driver users), not just the page.");
        body.Data.Count.ShouldBe(5,
            "Page 2 of 12 with pageSize=5 must return exactly 5 items.");
        body.Page.ShouldBe(2);
        body.PageSize.ShouldBe(5);
        body.TotalPages.ShouldBe(3,
            "ceil(12 / 5) = 3 total pages.");
        body.HasNextPage.ShouldBeTrue("There is a page 3 after page 2.");
        body.HasPreviousPage.ShouldBeTrue("There is a page 1 before page 2.");

        // Page 2 in createdAt DESC order: positions 6..10 (0-indexed: indices 5..9)
        // = pagingdriver07 (minute 7) down to pagingdriver03 (minute 3)
        body.Data.ShouldAllBe(u => u.Role == UserRole.Driver);
        body.Data.ShouldAllBe(u => u.Username.Contains("pagingdriver"));

        // Verify items are in createdAt DESC order within the page
        var createdAtValues = body.Data.Select(u => u.CreatedAt).ToList();
        var createdAtDescSorted = createdAtValues.OrderByDescending(d => d).ToList();
        createdAtValues.ShouldBe(createdAtDescSorted,
            "Items within the page must be ordered by createdAt DESC.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 9 — DTO shape leak guard: raw JSON must not expose sensitive fields
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_ResponseBodyAsRawString_DoesNotContainSensitiveFieldNames()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!PA", ct: ct);
        await SeedUserAsync(UserRole.Driver, "Driv3r!PE", ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!PA", ct);

        // Act: read the response body as a raw string to catch serialization drift
        // that would not be visible through a typed DTO deserialization.
        var response = await client.GetAsync("/api/users", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rawBody = await response.Content.ReadAsStringAsync(ct);

        // Assert: the serialized JSON must not contain any of these field names
        // (case-insensitive check — protects against casing variants).
        var lowerBody = rawBody.ToLowerInvariant();
        lowerBody.Contains("passwordhash").ShouldBeFalse(
            "The response JSON must never expose 'passwordHash'. " +
            "If this fails, the DTO serializes a field it should not.");
        lowerBody.Contains("createdby").ShouldBeFalse(
            "The response JSON must never expose 'createdBy'. " +
            "If this fails, the DTO serializes a field it should not.");
        lowerBody.Contains("lastmodifiedby").ShouldBeFalse(
            "The response JSON must never expose 'lastModifiedBy'. " +
            "If this fails, the DTO serializes a field it should not.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 10 — 401 unauthenticated
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithNoAuthorizationHeader_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Act — no Authorization header
        var response = await client.GetAsync("/api/users", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "Requests without a JWT must be rejected with 401.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 11 — 403 Driver JWT
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithDriverJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: Driver role does not satisfy the AdminOnly policy.
        var driverUser = await SeedUserAsync(UserRole.Driver, "Driv3r!PF", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, driverUser.Username.Value, "Driv3r!PF", ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Act
        var response = await client.GetAsync("/api/users", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "A Driver JWT must not satisfy the AdminOnly policy.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 12 — 403 BackOffice JWT
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithBackOfficeJwt_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: BackOffice role does not satisfy the AdminOnly policy.
        var backOfficeUser = await SeedUserAsync(UserRole.BackOffice, "B4ck!PA", ct: ct);

        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var loginBody = await LoginAsync(client, backOfficeUser.Username.Value, "B4ck!PA", ct);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);

        // Act
        var response = await client.GetAsync("/api/users", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "A BackOffice JWT must not satisfy the AdminOnly policy.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Test 13 — 400 on malformed query string (unparseable enum value)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetUsers_WithMalformedRoleQueryParameter_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        var adminUser = await SeedUserAsync(UserRole.Admin, "Adm1n!PB", ct: ct);

        await using var factory = CreateFactory();
        var client = await CreateAuthenticatedAdminClientAsync(factory, adminUser, "Adm1n!PB", ct);

        // Act: "NotARealRole" is not a valid UserRole enum value, so model binding fails.
        var response = await client.GetAsync("/api/users?role=NotARealRole", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest,
            "An unparseable enum value in the query string must produce 400 Bad Request.");
    }
}
