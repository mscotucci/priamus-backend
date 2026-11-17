using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Vehicles;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.Registry.Resources;
using Priamus.Trips.SharedKernel.Results;
using Shouldly;
using Xunit;

namespace Priamus.Trips.Registry.IntegrationTests.CrossSlice;

/// <summary>
/// The Italian texts of the module: the localization step of problem details replaces the English
/// description by the .resx entry of the error code and fills <c>{0}</c> from
/// <see cref="ErrorMessageArguments"/>.
/// </summary>
public sealed class RegistryMessagesTests : RegistryIntegrationTestBase
{
    public RegistryMessagesTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<string?> DetailAsync(Error error, params object[] arguments)
    {
        await using var factory = ApiFactory.Create(ConnectionString);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ErrorMessageArguments>().Set(arguments);

        var problem = new ProblemDetails { Detail = error.Description };
        problem.Extensions["errorCode"] = error.Code;
        var context = new ProblemDetailsContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            ProblemDetails = problem,
        };

        factory.Services.GetRequiredService<IOptions<ProblemDetailsOptions>>().Value.CustomizeProblemDetails!(context);
        return problem.Detail;
    }

    [Fact]
    public async Task LicensePlateTaken_AnswersInItalianWithThePlate()
    {
        var detail = await DetailAsync(VehicleErrors.LicensePlateTaken, "AB456CD");

        detail.ShouldBe("La targa AB456CD è già registrata su un altro mezzo.");
    }

    [Fact]
    public async Task LicensePlateTakenByDecommissioned_AnswersInItalianWithThePlate()
    {
        var detail = await DetailAsync(VehicleErrors.LicensePlateTakenByDecommissioned, "AB456CD");

        detail.ShouldBe(
            "La targa AB456CD è già registrata su un mezzo dismesso. Per rimetterlo in servizio, riattivalo dalla lista.");
    }

    [Fact]
    public async Task ConcurrentUpdate_AnswersInItalian()
    {
        var detail = await DetailAsync(VehicleErrors.ConcurrentUpdate);

        detail.ShouldBe("Il mezzo è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
    }

    [Fact]
    public async Task EveryVehicleError_HasAnItalianText()
    {
        var errors = new[]
        {
            VehicleErrors.LicensePlateInvalid,
            VehicleErrors.DescriptionInvalid,
            VehicleErrors.RequiresTrailerNotAllowed,
            VehicleErrors.NotFound,
            VehicleErrors.LicensePlateTaken,
            VehicleErrors.LicensePlateTakenByDecommissioned,
            VehicleErrors.ConcurrentUpdate,
        };

        foreach (var error in errors)
        {
            var detail = await DetailAsync(error, "AB456CD");
            detail.ShouldNotBe(error.Description, error.Code);
        }
    }

    [Fact]
    public async Task ValidationMessages_AreItalian()
    {
        await using var factory = ApiFactory.Create(ConnectionString);
        var localizer = factory.Services.GetRequiredService<IStringLocalizer<RegistryMessages>>();

        localizer["Validation.Required"].Value.ShouldBe("Valore obbligatorio.");
        localizer["Validation.MaxLength"].Value.ShouldBe("Massimo {1} caratteri.");
        localizer["Validation.LicensePlate"].ResourceNotFound.ShouldBeFalse();
        localizer["Validation.RequiresTrailerNotAllowed"].ResourceNotFound.ShouldBeFalse();
    }
}
