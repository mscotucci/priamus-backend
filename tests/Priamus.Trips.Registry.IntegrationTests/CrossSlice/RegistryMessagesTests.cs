using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Priamus.Trips.Registry.Application.Services;
using Priamus.Trips.Registry.Domain.Contracts;
using Priamus.Trips.Registry.Domain.Customers;
using Priamus.Trips.Registry.Domain.Drivers;
using Priamus.Trips.Registry.Domain.Locations;
using Priamus.Trips.Registry.Domain.NightBands;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.Domain.Trips;
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
    public async Task NightBandErrors_AnswerInItalian()
    {
        (await DetailAsync(PricingErrors.NightBandEndEqualsStart)).ShouldBe("La fine deve essere diversa dall'inizio.");
        (await DetailAsync(NightBandErrors.ConcurrentUpdate)).ShouldBe(
            "La fascia notturna è stata modificata da un altro utente nel frattempo. Controlla i dati aggiornati e riprova.");
    }

    [Fact]
    public async Task PricingErrors_AnswerInItalian()
    {
        (await DetailAsync(PricingErrors.TripEndEqualsStart)).ShouldBe("La fine deve essere diversa dall'inizio.");
        (await DetailAsync(PricingErrors.QuantityNotPositive)).ShouldBe("Inserisci un numero intero maggiore di zero.");
        (await DetailAsync(PricingErrors.KilogramsTooLarge)).ShouldBe("Al massimo 99.999 kg.");
        (await DetailAsync(PricingErrors.KilometresTooLarge)).ShouldBe("Al massimo 9.999 km.");
        (await DetailAsync(PricingErrors.AmountTooLarge)).ShouldBe("Importo troppo alto.");
        (await DetailAsync(PricingErrors.NoRateForLocation)).ShouldBe("Non c'è una tariffa per i luoghi del viaggio.");
        (await DetailAsync(PricingErrors.MeasureMissing)).ShouldBe("Mancano gli orari o le misure del viaggio.");
    }

    [Fact]
    public async Task TripErrors_AnswerInItalian_WithoutNamingAnAmount()
    {
        (await DetailAsync(TripErrors.AnswersChanged)).ShouldBe(
            "Qualcosa è cambiato mentre compilavi. Controlla le risposte e riprova.");
        (await DetailAsync(TripErrors.NotPriceable)).ShouldBe(
            "Non è stato possibile terminare il viaggio. Chiama la segreteria.");
        (await DetailAsync(TripErrors.MeasureChanged)).ShouldBe(
            "Il contratto è cambiato mentre terminavi il viaggio. Controlla i dati e riprova.");
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
    public async Task EveryLocationError_AnswersInItalianWithTheLocationAsStored()
    {
        var expected = new (Error Error, string Text)[]
        {
            (LocationErrors.NameInvalid, "Il nome è obbligatorio e può avere al massimo 100 caratteri."),
            (LocationErrors.MunicipalityInvalid, "Il comune è obbligatorio e può avere al massimo 100 caratteri."),
            (LocationErrors.AddressInvalid, "L'indirizzo può avere al massimo 200 caratteri."),
            (LocationErrors.NotFound, "Luogo non trovato."),
            (LocationErrors.Taken, "Il luogo Magazzino  centrale, Spoltore è già in anagrafica."),
            (LocationErrors.TakenByDeactivated,
                "Il luogo Magazzino  centrale, Spoltore è già in anagrafica (disattivato). Per riprenderlo, riattivalo dalla lista."),
            (LocationErrors.ConcurrentUpdate,
                "Il luogo è stato modificato da un altro utente nel frattempo. Controlla i dati aggiornati e riprova."),
        };

        foreach (var (error, text) in expected)
        {
            var detail = await DetailAsync(error, "Magazzino  centrale", "Spoltore");
            detail.ShouldBe(text, error.Code);
        }
    }

    [Fact]
    public async Task AccountTaken_AnswersInItalianWithAccountAndHolder()
    {
        var detail = await DetailAsync(DriverErrors.AccountTaken, "luca.difebo", "Luca Di Febo");

        detail.ShouldBe("L'account luca.difebo è già collegato all'autista Luca Di Febo.");
    }

    [Fact]
    public async Task AccountTakenByDeactivated_AnswersInItalianWithAccountAndHolder()
    {
        var detail = await DetailAsync(DriverErrors.AccountTakenByDeactivated, "luca.difebo", "Luca Di Febo");

        detail.ShouldBe("L'account luca.difebo è già collegato all'autista Luca Di Febo (disattivato).");
    }

    [Fact]
    public async Task AccountUnavailable_AnswersInItalian()
    {
        var detail = await DetailAsync(DriverErrors.AccountUnavailable);

        detail.ShouldBe("L'account scelto non è un account autista abilitato: scegline un altro.");
    }

    [Fact]
    public async Task DriverConcurrentUpdate_AnswersInItalian()
    {
        var detail = await DetailAsync(DriverErrors.ConcurrentUpdate);

        detail.ShouldBe("L'autista è stato modificato da un altro utente. Ricarica la lista e riprova.");
    }

    [Fact]
    public async Task EveryDriverError_HasAnItalianText()
    {
        var expected = new (Error Error, string Text)[]
        {
            (DriverErrors.FirstNameInvalid, "Il nome è obbligatorio e può avere al massimo 60 caratteri."),
            (DriverErrors.LastNameInvalid, "Il cognome è obbligatorio e può avere al massimo 60 caratteri."),
            (DriverErrors.PhoneInvalid, "Il telefono può avere al massimo 20 caratteri: cifre e spazi, con un + iniziale facoltativo."),
            (DriverErrors.NotFound, "Autista non trovato."),
            (DriverErrors.AccountTaken, "L'account luca.difebo è già collegato all'autista Luca Di Febo."),
            (DriverErrors.AccountTakenByDeactivated, "L'account luca.difebo è già collegato all'autista Luca Di Febo (disattivato)."),
            (DriverErrors.AccountUnavailable, "L'account scelto non è un account autista abilitato: scegline un altro."),
            (DriverErrors.ConcurrentUpdate, "L'autista è stato modificato da un altro utente. Ricarica la lista e riprova."),
        };

        foreach (var (error, text) in expected)
        {
            var detail = await DetailAsync(error, "luca.difebo", "Luca Di Febo");
            detail.ShouldBe(text, error.Code);
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
            ContractErrors.NightPriceBelowDayPrice,
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
            ContractErrors.VehicleRateLocationsMismatch,
            ContractErrors.VehicleRateDuplicate,
            ContractErrors.VehicleRateWithoutLocationRate,
            ContractErrors.VehicleRateUnknownVehicle,
            ContractErrors.VehicleRateOnTrailer,
            ContractErrors.VehicleRateDecommissioned,
            ContractErrors.LocationRateNotApplicable,
            ContractErrors.LocationRateLocationsMismatch,
            ContractErrors.LocationRateDuplicate,
            ContractErrors.LocationRateUnknownLocation,
            ContractErrors.LocationRateDeactivatedLocation,
            ContractErrors.LocationRatesRequired,
        };

        foreach (var error in errors)
        {
            var detail = await DetailAsync(error, "dal 01/04/2026 al 31/12/2026");
            detail.ShouldNotBe(error.Description, error.Code);
        }
    }

    [Fact]
    public async Task VehicleRateErrors_AnswerInItalianWithTheNameAndNeverUseTheInternalTerm()
    {
        var expected = new (Error Error, string[] Arguments, string Text)[]
        {
            (ContractErrors.VehicleRateLocationsMismatch, [], "Questo valore non si applica al contratto."),
            (ContractErrors.VehicleRateDuplicate, [], "Il mezzo ha già una tariffa propria in questo contratto."),
            (ContractErrors.VehicleRateWithoutLocationRate, [], "Il contratto non ha una tariffa per questo luogo o questa coppia."),
            (ContractErrors.VehicleRateUnknownVehicle, ["0b6f1c1e"], "Il mezzo 0b6f1c1e non esiste."),
            (ContractErrors.VehicleRateOnTrailer, ["AB456CD"], "Il mezzo AB456CD è un rimorchio: non può avere una tariffa propria."),
            (ContractErrors.VehicleRateDecommissioned, ["AB456CD"], "Il mezzo AB456CD è dismesso: non si può aggiungere una sua tariffa."),
        };

        foreach (var (error, arguments, text) in expected)
        {
            var detail = await DetailAsync(error, arguments);
            detail.ShouldBe(text, error.Code);
            detail!.ShouldNotContain("override", Case.Insensitive, error.Code);
            error.Description.ShouldNotContain("override", Case.Insensitive, error.Code);
        }
    }

    [Fact]
    public async Task HasVehicleRates_AnswersInItalianWithTheCustomerAndNeverUsesTheInternalTerm()
    {
        var detail = await DetailAsync(VehicleErrors.HasVehicleRates, "Ecoservizi Adriatica S.r.l.");

        detail.ShouldBe(
            "Il mezzo ha una tariffa propria nel contratto di Ecoservizi Adriatica S.r.l.: toglila dal contratto prima di farlo diventare un rimorchio.");
        detail!.ShouldNotContain("override", Case.Insensitive);
        VehicleErrors.HasVehicleRates.Description.ShouldNotContain("override", Case.Insensitive);
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
