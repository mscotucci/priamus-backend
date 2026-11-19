using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Customers;
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
    public async Task VatNumberTaken_AnswersInItalianWithVatNumberAndHolder()
    {
        var detail = await DetailAsync(CustomerErrors.VatNumberTaken, "01455280683", "Comune di Penne");

        detail.ShouldBe("La partita IVA 01455280683 è già registrata sul cliente Comune di Penne.");
    }

    [Fact]
    public async Task VatNumberTakenByDeactivated_AnswersInItalianWithVatNumberAndHolder()
    {
        var detail = await DetailAsync(CustomerErrors.VatNumberTakenByDeactivated, "01455280683", "Comune di Penne");

        detail.ShouldBe(
            "La partita IVA 01455280683 è già registrata sul cliente Comune di Penne (disattivato). Per riprenderlo, riattivalo dalla lista.");
    }

    [Fact]
    public async Task VatNumberTakenByAnother_AnswersInItalianWithTheVatNumberOnly()
    {
        var detail = await DetailAsync(CustomerErrors.VatNumberTakenByAnother, "01455280683");

        detail.ShouldBe("La partita IVA 01455280683 è già registrata su un altro cliente.");
    }

    [Fact]
    public async Task CustomerConcurrentUpdate_AnswersInItalian()
    {
        var detail = await DetailAsync(CustomerErrors.ConcurrentUpdate);

        detail.ShouldBe("Il cliente è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
    }

    [Fact]
    public async Task EveryCustomerError_HasAnItalianText()
    {
        var errors = new[]
        {
            CustomerErrors.CompanyNameInvalid,
            CustomerErrors.AddressInvalid,
            CustomerErrors.VatNumberInvalid,
            CustomerErrors.TaxCodeInvalid,
            CustomerErrors.NotFound,
            CustomerErrors.VatNumberTaken,
            CustomerErrors.VatNumberTakenByDeactivated,
            CustomerErrors.VatNumberTakenByAnother,
            CustomerErrors.ConcurrentUpdate,
        };

        foreach (var error in errors)
        {
            var detail = await DetailAsync(error, "01455280683", "Comune di Penne");
            detail.ShouldNotBe(error.Description, error.Code);
        }
    }

    [Fact]
    public async Task ContractOverlap_AnswersInItalianWithTheOtherValidity()
    {
        var detail = await DetailAsync(ContractErrors.Overlap, "dal 01/04/2026 al 31/12/2026");

        detail.ShouldBe("La validità si sovrappone al contratto dal 01/04/2026 al 31/12/2026.");
    }

    [Fact]
    public async Task EveryContractError_HasAnItalianText()
    {
        var errors = new[]
        {
            ContractErrors.ValidityEndBeforeStart,
            ContractErrors.NotFound,
            ContractErrors.Overlap,
            ContractErrors.OverlapWithAnother,
            ContractErrors.CustomerDeactivated,
            ContractErrors.ConcurrentUpdate,
            ContractErrors.CustomerDeactivatedReadOnly,
            ContractErrors.DayPriceNotPositive,
            ContractErrors.NightPriceNotPositive,
            ContractErrors.NightCallFeeNegative,
            ContractErrors.MinimumAmountNotPositive,
            ContractErrors.MinimumThresholdNotPositive,
            ContractErrors.MinimumOveragePriceNotPositive,
            ContractErrors.AmountTooPrecise,
            ContractErrors.AmountTooLarge,
            ContractErrors.RateRequired,
            ContractErrors.RateNotApplicable,
            ContractErrors.NightRateMissing,
            ContractErrors.NightRateNotApplicable,
            ContractErrors.MinimumNotApplicableByTrip,
        };

        foreach (var error in errors)
        {
            var detail = await DetailAsync(error, "dal 01/04/2026 al 31/12/2026");
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
        localizer["Validation.PositiveAmount"].Value.ShouldBe("Inserisci un importo maggiore di zero.");
        localizer["Validation.NonNegativeAmount"].Value.ShouldBe("L'importo non può essere negativo.");
        localizer["Validation.AmountDecimals"].Value.ShouldBe("Usa al massimo due decimali.");
        localizer["Validation.AmountTooLarge"].Value.ShouldBe("Importo troppo alto.");
        localizer["Registry.Contract.AmountTooLarge"].Value.ShouldBe("Importo troppo alto.");
        localizer["Registry.Contract.ValueNotApplicable"].Value.ShouldBe("Questo valore non si applica al contratto.");
        localizer["Registry.Contract.ChangeFromOutsideValidity"].Value.ShouldBe(
            "La data deve essere successiva all'inizio e dentro la validità del contratto.");
        localizer["Validation.LicensePlate"].ResourceNotFound.ShouldBeFalse();
        localizer["Validation.RequiresTrailerNotAllowed"].ResourceNotFound.ShouldBeFalse();
        localizer["Validation.VatNumber"].ResourceNotFound.ShouldBeFalse();
        localizer["Validation.TaxCode"].ResourceNotFound.ShouldBeFalse();
    }
}
