using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.IntegrationTests.TestData;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.Features.Login;

/// <summary>
/// Integration tests for <see cref="LoginHandler"/>.
/// Uses a real <see cref="IdentityDbContext"/> (Testcontainers PostgreSQL) and
/// NSubstitute for application-level dependencies.
/// </summary>
public sealed class LoginHandlerTests : IdentityIntegrationTestBase
{
    public LoginHandlerTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateTime FixedNow =
        new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    private LoginHandler BuildHandler(
        IdentityDbContext db,
        IPasswordHasher<User>? passwordHasher = null,
        IRefreshTokenHasher? refreshTokenHasher = null,
        IJwtTokenIssuer? jwtIssuer = null,
        IDriverLookup? driverLookup = null,
        IClock? clock = null)
    {
        passwordHasher ??= new PasswordHasher<User>();
        refreshTokenHasher ??= new Sha256RefreshTokenHasher();
        jwtIssuer ??= Substitute.For<IJwtTokenIssuer>();
        driverLookup ??= Substitute.For<IDriverLookup>();
        clock ??= new FixedClockStub(FixedNow);

        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "test-signing-key-that-is-at-least-32-chars",
            AccessTokenLifetime = TimeSpan.FromHours(1),
            RefreshTokenLifetime = TimeSpan.FromHours(12),
        });

        return new LoginHandler(
            db, passwordHasher, refreshTokenHasher, jwtIssuer,
            driverLookup, clock, jwtOptions,
            NullLogger<LoginHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_ReturnsTokens()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Arrange: create a user with a known password hash
        var passwordHasher = new PasswordHasher<User>();
        var user = TestDataBuilders.AUser()
            .WithRole(UserRole.BackOffice)
            .Build();

        // Re-create user with real password hash
        var plainPassword = "Sup3rSecret!";
        var hashString = passwordHasher.HashPassword(user, plainPassword);
        var userWithHash = User.Create(
            user.Id,
            Username.Create(user.Username.Value),
            PasswordHash.Create(hashString),
            UserRole.BackOffice,
            new SystemClock(),
            TestDataBuilders.ActorId);
        db.Users.Add(userWithHash);
        await db.SaveChangesAsync(ct);

        var jwtIssuer = Substitute.For<IJwtTokenIssuer>();
        jwtIssuer.IssueAccessToken(Arg.Any<UserId>(), Arg.Any<UserRole>(), Arg.Any<Guid?>())
            .Returns("fake-access-token");

        var handler = BuildHandler(db, passwordHasher: passwordHasher, jwtIssuer: jwtIssuer);
        var request = new LoginRequest(userWithHash.Username.Value, plainPassword);

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("fake-access-token");
        result.Value.RefreshToken.ShouldNotBeNullOrEmpty();
        result.Value.RefreshToken.Length.ShouldBe(43);
    }

    [Fact]
    public async Task HandleAsync_UnknownUsername_ReturnsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var handler = BuildHandler(db);
        var request = new LoginRequest("nonexistent-user-xyz", "anypassword");

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidCredentials);
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_ReturnsInvalidCredentials_AndWritesAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Arrange
        var passwordHasher = new PasswordHasher<User>();
        var user = TestDataBuilders.AUser().WithRole(UserRole.BackOffice).Build();
        var hashString = passwordHasher.HashPassword(user, "correct-password");
        var userWithHash = User.Create(
            user.Id,
            Username.Create(user.Username.Value),
            PasswordHash.Create(hashString),
            UserRole.BackOffice,
            new SystemClock(),
            TestDataBuilders.ActorId);
        db.Users.Add(userWithHash);
        await db.SaveChangesAsync(ct);

        var handler = BuildHandler(db, passwordHasher: passwordHasher);
        var request = new LoginRequest(userWithHash.Username.Value, "wrong-password");

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.InvalidCredentials);

        // Audit row for LoginFailed should have been written
        await using var verifyScope = CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditRows = verifyDb.AuditLog
            .Where(a => a.SubjectId == user.Id.Value && a.EventType == AuditEventTypes.LoginFailed)
            .ToList();
        auditRows.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_DisabledUser_ReturnsAccountDisabled_AndWritesAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Arrange: create user then disable them
        var passwordHasher = new PasswordHasher<User>();
        var user = TestDataBuilders.AUser().WithRole(UserRole.Driver).Build();
        var hashString = passwordHasher.HashPassword(user, "correct-password");
        var userWithHash = User.Create(
            user.Id,
            Username.Create(user.Username.Value),
            PasswordHash.Create(hashString),
            UserRole.Driver,
            new SystemClock(),
            TestDataBuilders.ActorId);
        userWithHash.Disable(new SystemClock(), TestDataBuilders.ActorId);
        db.Users.Add(userWithHash);
        await db.SaveChangesAsync(ct);

        var handler = BuildHandler(db, passwordHasher: passwordHasher);
        var request = new LoginRequest(userWithHash.Username.Value, "correct-password");

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.AccountDisabled);

        // Audit row for LoginFailed (AccountDisabled) should have been written
        await using var verifyScope = CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var auditRows = verifyDb.AuditLog
            .Where(a => a.SubjectId == user.Id.Value && a.EventType == AuditEventTypes.LoginFailed)
            .ToList();
        auditRows.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_DriverWithLinkedId_EmitsDriverIdClaim()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Arrange
        var passwordHasher = new PasswordHasher<User>();
        var user = TestDataBuilders.AUser().WithRole(UserRole.Driver).Build();
        var hashString = passwordHasher.HashPassword(user, "pwd");
        var userWithHash = User.Create(
            user.Id,
            Username.Create(user.Username.Value),
            PasswordHash.Create(hashString),
            UserRole.Driver,
            new SystemClock(),
            TestDataBuilders.ActorId);
        db.Users.Add(userWithHash);
        await db.SaveChangesAsync(ct);

        var expectedDriverId = Guid.NewGuid();
        var driverLookup = Substitute.For<IDriverLookup>();
        driverLookup.GetDriverIdByUserIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(expectedDriverId);

        var jwtIssuer = Substitute.For<IJwtTokenIssuer>();
        jwtIssuer.IssueAccessToken(Arg.Any<UserId>(), Arg.Any<UserRole>(), Arg.Any<Guid?>())
            .Returns("driver-access-token");

        var handler = BuildHandler(db, passwordHasher: passwordHasher,
            driverLookup: driverLookup, jwtIssuer: jwtIssuer);
        var request = new LoginRequest(userWithHash.Username.Value, "pwd");

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        jwtIssuer.Received(1).IssueAccessToken(
            Arg.Is<UserId>(id => id.Value == user.Id.Value),
            UserRole.Driver,
            expectedDriverId);
    }

    [Fact]
    public async Task HandleAsync_DriverWithNoLinkedId_IssuesTokenWithoutDriverId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Arrange
        var passwordHasher = new PasswordHasher<User>();
        var user = TestDataBuilders.AUser().WithRole(UserRole.Driver).Build();
        var hashString = passwordHasher.HashPassword(user, "pwd2");
        var userWithHash = User.Create(
            user.Id,
            Username.Create(user.Username.Value),
            PasswordHash.Create(hashString),
            UserRole.Driver,
            new SystemClock(),
            TestDataBuilders.ActorId);
        db.Users.Add(userWithHash);
        await db.SaveChangesAsync(ct);

        var driverLookup = Substitute.For<IDriverLookup>();
        driverLookup.GetDriverIdByUserIdAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var jwtIssuer = Substitute.For<IJwtTokenIssuer>();
        jwtIssuer.IssueAccessToken(Arg.Any<UserId>(), Arg.Any<UserRole>(), Arg.Any<Guid?>())
            .Returns("driver-no-driver-id-token");

        var handler = BuildHandler(db, passwordHasher: passwordHasher,
            driverLookup: driverLookup, jwtIssuer: jwtIssuer);
        var request = new LoginRequest(userWithHash.Username.Value, "pwd2");

        // Act
        var result = await handler.HandleAsync(request, ct);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        jwtIssuer.Received(1).IssueAccessToken(
            Arg.Any<UserId>(),
            UserRole.Driver,
            null);
    }
}
