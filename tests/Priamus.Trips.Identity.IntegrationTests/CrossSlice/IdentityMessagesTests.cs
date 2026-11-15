using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Features.Login;
using Priamus.Trips.Identity.IntegrationTests.Infrastructure;
using Priamus.Trips.Identity.Resources;
using Priamus.Trips.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Identity.IntegrationTests.CrossSlice;

/// <summary>
/// The text a user reads comes from <c>IdentityMessages.resx</c>, keyed by <c>Error.Code</c>:
/// the problem <c>detail</c> is Italian while <c>errorCode</c> and the error description stay as
/// declared, and request-validation messages come from the same file.
/// </summary>
public sealed class IdentityMessagesTests : IdentityIntegrationTestBase
{
    public IdentityMessagesTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PostLogin_WithUnknownUser_AnswersTheItalianDetailOfTheErrorCode()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login", new LoginRequest("nobody", "Wr0ng!Pass"), Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errorCode").GetString().ShouldBe(UserErrors.InvalidCredentials.Code);
        body.GetProperty("detail").GetString().ShouldBe("Nome utente o password non validi.");
    }

    [Fact]
    public async Task PostLogin_WithMissingPassword_AnswersTheItalianValidationMessage()
    {
        // Arrange
        await using var factory = ApiFactory.Create(ConnectionString);
        var client = factory.CreateClient();
        var content = new StringContent("""{"username":"someone"}""", System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/api/identity/auth/login", content, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("errors").GetProperty("Password")[0].GetString().ShouldBe(IdentityMessages.Validation_Required);
    }

    [Fact]
    public void EveryUserFacingErrorCode_HasAnItalianEntry()
    {
        // Arrange: the errors a user cannot cause keep the English description on purpose.
        Error[] internalOnly =
        [
            UserErrors.PasswordHashInvalid,
            RefreshTokenErrors.HashInvalid,
            RefreshTokenErrors.LifetimeInvalid,
        ];
        var errors = new[] { typeof(UserErrors), typeof(RefreshTokenErrors) }
            .SelectMany(t => t.GetFields())
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => (Error)f.GetValue(null)!)
            .Except(internalOnly)
            .ToList();

        // Act
        var missing = errors
            .Where(e => IdentityMessages.ResourceManager.GetString(e.Code) is null)
            .Select(e => e.Code)
            .ToList();

        // Assert
        errors.ShouldNotBeEmpty();
        missing.ShouldBeEmpty();
    }
}
