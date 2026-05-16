using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.CreateUser;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.CreateUser;

/// <summary>
/// Integration tests for <see cref="CreateUserHandler"/>.
/// Uses a real <see cref="IdentityDbContext"/> (Testcontainers PostgreSQL) and
/// NSubstitute for application-level dependencies (IPasswordHasher, ICurrentUser).
/// </summary>
public sealed class CreateUserHandlerTests : IdentityIntegrationTestBase
{
    public CreateUserHandlerTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateTime FixedNow =
        new(2026, 5, 16, 9, 0, 0, DateTimeKind.Utc);

    // ── Builder helper ────────────────────────────────────────────────────────

    private CreateUserHandler BuildHandler(
        IdentityDbContext db,
        IPasswordHasher<User>? passwordHasher = null,
        ICurrentUser? currentUser = null,
        IClock? clock = null)
    {
        passwordHasher ??= new PasswordHasher<User>();
        currentUser ??= new FakeCurrentUser();
        clock ??= new FixedClockStub(FixedNow);

        return new CreateUserHandler(
            db,
            passwordHasher,
            clock,
            currentUser,
            NullLogger<CreateUserHandler>.Instance);
    }

    // ── Test 1: Happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WithValidRequest_ReturnsSuccessAndPersistsUser()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = BuildHandler(db);
        var request = new CreateUserRequest("newuser42", "Str0ng!Pass", UserRole.Driver);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Username.ShouldBe("newuser42");
        result.Value.Role.ShouldBe(UserRole.Driver);
        result.Value.Status.ShouldBe(UserStatus.Active);
        result.Value.Id.ShouldNotBe(Guid.Empty);

        // Verify user was persisted
        await using var verifyScope = CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var persisted = await verifyDb.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == new UserId(result.Value.Id), ct);

        persisted.ShouldNotBeNull();
        persisted.Username.Value.ShouldBe("newuser42");
        persisted.Role.ShouldBe(UserRole.Driver);
        persisted.Status.ShouldBe(UserStatus.Active);
        persisted.PasswordHash.Value.ShouldNotBeNullOrWhiteSpace();
    }

    // ── Test 2: Username normalization ────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WithMixedCaseUsername_NormalizesToLowercase()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = BuildHandler(db);
        var request = new CreateUserRequest("  MixedCaseUser  ", "Str0ng!Pass", UserRole.BackOffice);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Username.ShouldBe("mixedcaseuser");
        result.Value.Role.ShouldBe(UserRole.BackOffice);
        result.Value.Status.ShouldBe(UserStatus.Active);
    }

    // ── Test 3: Invalid username (too short) → UsernameInvalid domain error ───

    [Fact]
    public async Task HandleAsync_WhenUsernameTooShort_ReturnsDomainError()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = BuildHandler(db);
        // "ab" is only 2 chars, which is below the Username.MinLength of 3
        var request = new CreateUserRequest("ab", "Str0ng!Pass", UserRole.Driver);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.UsernameInvalid);
    }

    // ── Test 4: Invalid username (starts with '.') → UsernameInvalid domain error

    [Fact]
    public async Task HandleAsync_WhenUsernameStartsWithDot_ReturnsDomainError()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = BuildHandler(db);
        // Leading '.' violates the Username invariant (must start with alphanumeric)
        var request = new CreateUserRequest(".invalid", "Str0ng!Pass", UserRole.Driver);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.UsernameInvalid);
    }

    // ── Test 5: Upfront uniqueness check → UsernameAlreadyTaken (pre-save path)

    [Fact]
    public async Task HandleAsync_WhenUsernameAlreadyExists_ReturnsUsernameAlreadyTaken()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed an existing user with the same username
        const string existingUsername = "existinguser";

        await using (var arrangeScope = CreateAsyncScope())
        {
            var arrangeDb = arrangeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var existingUser = TestDataBuilders.AUser().WithUsername(existingUsername).Build();
            arrangeDb.Users.Add(existingUser);
            await arrangeDb.SaveChangesAsync(ct);
        }

        // Act: attempt to create another user with the same username
        await using var actScope = CreateAsyncScope();
        var actDb = actScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = BuildHandler(actDb);
        var request = new CreateUserRequest(existingUsername, "Str0ng!Pass", UserRole.BackOffice);

        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.UsernameAlreadyTaken);
    }

    // ── Test 6: PasswordHash.Create throws when hasher returns empty string ────

    [Fact]
    public async Task HandleAsync_WhenPasswordHasherReturnsEmptyString_ReturnsDomainError()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: substitute the password hasher to return an empty string,
        // which causes PasswordHash.Create to throw DomainException(PasswordHashInvalid)
        var badHasher = Substitute.For<IPasswordHasher<User>>();
        badHasher.HashPassword(Arg.Any<User>()!, Arg.Any<string>())
            .Returns(string.Empty);

        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = BuildHandler(db, passwordHasher: badHasher);
        var request = new CreateUserRequest("validuser1", "Str0ng!Pass", UserRole.Driver);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.PasswordHashInvalid);
    }

    // ── Test 7: Audit row is written on successful user creation ─────────────

    [Fact]
    public async Task HandleAsync_OnSuccess_WritesAuditRowWithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler = BuildHandler(db);
        var request = new CreateUserRequest("auditableuser", "Str0ng!Pass", UserRole.Admin);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert — result is successful
        result.IsSuccess.ShouldBeTrue();
        var newUserId = result.Value.Id;

        // Assert — audit log row exists in a fresh scope
        await using var assertScope = CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditRows = await assertDb.AuditLog.AsNoTracking()
            .Where(a => a.SubjectId == newUserId
                     && a.EventType == AuditEventTypes.UserCreated)
            .ToListAsync(ct);

        auditRows.Count.ShouldBe(1,
            "Exactly one UserCreated audit row must be written on successful user creation.");

        var auditRow = auditRows[0];
        auditRow.ActorId.ShouldBe(FakeCurrentUser.ActorId.Value,
            "actorId must equal the current user's UserId (FakeCurrentUser.ActorId).");
        auditRow.SubjectId.ShouldBe(newUserId,
            "subjectId must equal the newly created user's Id.");

        // Payload must contain the role field (UserCreated event carries Role).
        // Note: UserCreated event does NOT carry username (see heads-up in phase brief).
        // The Role enum is serialized as a JSON Number by default System.Text.Json options,
        // so the assertion reads the integer value and compares against (int)UserRole.X.
        using var payloadDoc = System.Text.Json.JsonDocument.Parse(auditRow.Payload);
        payloadDoc.RootElement.TryGetProperty("role", out var roleElement)
            .ShouldBeTrue("Audit payload must contain the 'role' field.");
        roleElement.GetInt32().ShouldBe((int)UserRole.Admin);
    }

    // ── Test 8: Race-condition path (unique index violation → UsernameAlreadyTaken)

    [Fact]
    public async Task HandleAsync_WhenTwoConcurrentCallsUseTheSameUsername_ExactlyOneSucceedsAndOneReturnsAlreadyTaken()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: build two separate handler instances each with their own DbContext scope
        // to simulate concurrent requests hitting the same unique-index constraint.
        const string racedUsername = "raceduser1";
        const string password = "Str0ng!Pass";

        await using var scope1 = CreateAsyncScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler1 = BuildHandler(db1);

        await using var scope2 = CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var handler2 = BuildHandler(db2);

        var request1 = new CreateUserRequest(racedUsername, password, UserRole.Driver);
        var request2 = new CreateUserRequest(racedUsername, password, UserRole.BackOffice);

        // Act: fire both concurrently
        var task1 = handler1.HandleAsync(request1, ct);
        var task2 = handler2.HandleAsync(request2, ct);
        var results = await Task.WhenAll(task1, task2);

        // Assert: exactly one succeeds, the other returns UsernameAlreadyTaken
        var successes = results.Count(r => r.IsSuccess);
        var failures  = results.Count(r => r.IsFailure);

        successes.ShouldBe(1, "Exactly one of the concurrent requests must succeed.");
        failures.ShouldBe(1,  "Exactly one of the concurrent requests must fail with UsernameAlreadyTaken.");

        var failingResult = results.First(r => r.IsFailure);
        failingResult.Error.ShouldBe(UserErrors.UsernameAlreadyTaken);

        // Assert: exactly one user row in DB
        await using var verifyScope = CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var rowCount = await verifyDb.Users.AsNoTracking()
            .CountAsync(u => u.Username == Username.Create(racedUsername), ct);

        rowCount.ShouldBe(1, "Only one user row must be persisted despite the concurrent requests.");
    }
}
