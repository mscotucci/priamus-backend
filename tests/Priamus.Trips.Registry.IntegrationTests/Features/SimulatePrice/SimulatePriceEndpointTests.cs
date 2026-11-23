using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Priamus.Trips.IntegrationTesting;
using Priamus.Trips.Registry.Domain.Pricing;
using Priamus.Trips.Registry.IntegrationTests.Infrastructure;
using Priamus.Trips.SharedKernel.Time;
using Shouldly;
using Xunit;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.ContractTestSupport;
using static Priamus.Trips.Registry.IntegrationTests.Infrastructure.VehicleTestSupport;

namespace Priamus.Trips.Registry.IntegrationTests.Features.SimulatePrice;

/// <summary>
/// Endpoint integration tests for <c>POST /api/registry/contracts/simulation</c>, against the per-test
/// PostgreSQL database and the test authentication scheme.
/// </summary>
public sealed class SimulatePriceEndpointTests : RegistryIntegrationTestBase
{
    public SimulatePriceEndpointTests(PostgresFixture fixture) : base(fixture) { }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> CreateFactory() =>
        ApiFactory.Create(ConnectionString, useTestAuthentication: true);

    /// <summary>A valid body for a flat-rate contract without the night surcharge; tests override single members.</summary>
    private static Dictionary<string, object?> Body(string pricingMode, string startTime = "10:00")
    {
        var body = new Dictionary<string, object?>
        {
            ["billingLevel"] = "FlatRate",
            ["pricingMode"] = pricingMode,
            ["nightSurcharge"] = false,
            ["dayPrice"] = 60,
            ["minimumRule"] = "None",
            ["startTime"] = startTime,
        };

        switch (pricingMode)
        {
            case "ByHour":
                body["endTime"] = "12:20";
                break;
            case "ByWeight":
                body["kilograms"] = 12300;
                break;
            case "ByDistance":
                body["kilometres"] = 100;
                break;
        }

        return body;
    }

    private static void WithNightSurcharge(Dictionary<string, object?> body, decimal night, decimal fee)
    {
        body["nightSurcharge"] = true;
        body["nightPrice"] = night;
        body["nightCallFee"] = fee;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/api/registry/contracts/simulation", body, TestJson.Options, Ct);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = CreateDbContext();
        return await db.AuditLog.CountAsync(Ct);
    }

    [Fact]
    public async Task ByHour_DayTrip_ReturnsTheAmountAndItsSteps()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), Body("ByHour")));

        // Assert: 140 minutes at 60 an hour.
        result.GetProperty("total").GetDecimal().ShouldBe(140m);
        result.GetProperty("band").GetString().ShouldBe("Day");
        result.GetProperty("rateSource").GetString().ShouldBe("Contract");
        result.GetProperty("quantity").GetInt32().ShouldBe(140);
        result.GetProperty("unitPrice").GetDecimal().ShouldBe(60m);
        result.GetProperty("baseAmount").GetDecimal().ShouldBe(140m);
        result.GetProperty("minimumRule").GetString().ShouldBe("None");
        result.GetProperty("minimumApplied").GetBoolean().ShouldBeFalse();
        result.GetProperty("nightCallFee").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("nightBand").GetProperty("start").GetString().ShouldBe("22:00");
        result.GetProperty("nightBand").GetProperty("end").GetString().ShouldBe("06:00");
    }

    [Fact]
    public async Task ByHour_NightTripOverMidnight_ComputesThreeHoursAtTheNightPriceWithTheCallFee()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByHour", "23:00");
        body["endTime"] = "02:00";
        WithNightSurcharge(body, night: 90, fee: 5);

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert: 180 minutes at 90 an hour, plus the call fee.
        result.GetProperty("band").GetString().ShouldBe("Night");
        result.GetProperty("quantity").GetInt32().ShouldBe(180);
        result.GetProperty("unitPrice").GetDecimal().ShouldBe(90m);
        result.GetProperty("baseAmount").GetDecimal().ShouldBe(270m);
        result.GetProperty("nightCallFee").GetDecimal().ShouldBe(5m);
        result.GetProperty("total").GetDecimal().ShouldBe(275m);
    }

    [Fact]
    public async Task ByWeight_NightTrip_ReturnsTheRoundedAmountWithTheCallFee()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByWeight", "05:59");
        body["dayPrice"] = 10;
        WithNightSurcharge(body, night: 12, fee: 5);

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert: 12 300 kg at 12 a ton, plus the call fee.
        result.GetProperty("band").GetString().ShouldBe("Night");
        result.GetProperty("quantity").GetInt32().ShouldBe(12300);
        result.GetProperty("baseAmount").GetDecimal().ShouldBe(147.6m);
        result.GetProperty("total").GetDecimal().ShouldBe(152.6m);
    }

    [Fact]
    public async Task ByDistance_FixedMinimum_RaisesTheBaseAmountAndFlagsIt()
    {
        // Arrange: 100 km at 1.5 is 150, below the minimum of 200; 06:00 is a day start.
        await using var factory = CreateFactory();
        var body = Body("ByDistance", "06:00");
        WithNightSurcharge(body, night: 2, fee: 5);
        body["dayPrice"] = 1.5m;
        body["minimumRule"] = "FixedMinimum";
        body["minimumAmount"] = 200;

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert
        result.GetProperty("band").GetString().ShouldBe("Day");
        result.GetProperty("baseAmount").GetDecimal().ShouldBe(150m);
        result.GetProperty("minimumRule").GetString().ShouldBe("FixedMinimum");
        result.GetProperty("minimumAmount").GetDecimal().ShouldBe(200m);
        result.GetProperty("amountAfterMinimum").GetDecimal().ShouldBe(200m);
        result.GetProperty("minimumApplied").GetBoolean().ShouldBeTrue();
        result.GetProperty("nightCallFee").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("total").GetDecimal().ShouldBe(200m);
    }

    [Fact]
    public async Task ByTrip_StartBeforeTheBand_IsADayTripAtTheDayPrice()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByTrip", "21:59");
        body["dayPrice"] = 150;
        WithNightSurcharge(body, night: 180, fee: 10);

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert
        result.GetProperty("band").GetString().ShouldBe("Day");
        result.GetProperty("quantity").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("total").GetDecimal().ShouldBe(150m);
    }

    [Fact]
    public async Task VehicleWithARateOfItsOwn_IsPricedWithThatRate()
    {
        // Arrange
        await using var factory = CreateFactory();
        var vehicleId = Guid.NewGuid();
        var body = Body("ByHour");
        body["endTime"] = "12:00";
        body["vehicleRates"] = new[] { VehicleRateItem(vehicleId, """{"dayPrice":80,"minimumRule":"None"}""") };
        body["vehicleId"] = vehicleId;

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert: 120 minutes at the vehicle's 80 an hour.
        result.GetProperty("rateSource").GetString().ShouldBe("Vehicle");
        result.GetProperty("vehicleId").GetGuid().ShouldBe(vehicleId);
        result.GetProperty("unitPrice").GetDecimal().ShouldBe(80m);
        result.GetProperty("total").GetDecimal().ShouldBe(160m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VehicleWithoutARateOfItsOwn_IsPricedWithTheContractRate(bool vehicleSent)
    {
        // Arrange: the vehicle sent is not among the vehicle rates, or no vehicle is sent.
        await using var factory = CreateFactory();
        var body = Body("ByHour");
        body["endTime"] = "12:00";
        body["vehicleRates"] = new[] { VehicleRateItem(Guid.NewGuid(), """{"dayPrice":80,"minimumRule":"None"}""") };
        body["vehicleId"] = vehicleSent ? Guid.NewGuid() : null;

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert: 120 minutes at the contract's 60 an hour.
        result.GetProperty("rateSource").GetString().ShouldBe("Contract");
        result.GetProperty("vehicleId").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("total").GetDecimal().ShouldBe(120m);
    }

    [Fact]
    public async Task ChangedBand_IsUsedByTheNextSimulation()
    {
        // Arrange: 21:00 is a day start in 22:00-06:00 and a night start in 20:00-05:00.
        await using var factory = CreateFactory();
        var client = BackOfficeClient(factory);
        var body = Body("ByTrip", "21:00");
        WithNightSurcharge(body, night: 90, fee: 5);
        var before = await ReadAsync(await PostAsync(client, body));

        await using (var db = CreateDbContext())
        {
            var setting = await db.NightBandSettings.SingleAsync(Ct);
            setting.Update(
                NightBand.Create(new TimeOnly(20, 0), new TimeOnly(5, 0)).Value,
                new SystemClock(),
                FakeCurrentUser.ActorId);
            await db.SaveChangesAsync(Ct);
        }

        // Act
        var after = await ReadAsync(await PostAsync(client, body));

        // Assert
        before.GetProperty("band").GetString().ShouldBe("Day");
        before.GetProperty("total").GetDecimal().ShouldBe(60m);
        after.GetProperty("band").GetString().ShouldBe("Night");
        after.GetProperty("total").GetDecimal().ShouldBe(95m);
        after.GetProperty("nightBand").GetProperty("start").GetString().ShouldBe("20:00");
        after.GetProperty("nightBand").GetProperty("end").GetString().ShouldBe("05:00");
    }

    [Fact]
    public async Task Simulation_WritesNothing()
    {
        // Arrange
        await using var factory = CreateFactory();
        var auditBefore = await AuditCountAsync();

        // Act
        var response = await PostAsync(BackOfficeClient(factory), Body("ByHour"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuditCountAsync()).ShouldBe(auditBefore);
        await using var db = CreateDbContext();
        (await db.NightBandSettings.AsNoTracking().SingleAsync(Ct)).Version.ShouldBe(0);
        (await db.Contracts.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Admin_CanSimulate()
    {
        // Arrange
        await using var factory = CreateFactory();

        // Act
        var response = await PostAsync(TestCaller.WithRole("Admin").ApplyTo(factory.CreateClient()), Body("ByTrip"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Driver_Returns403()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());

        // Act
        var response = await PostAsync(client, Body("ByHour"));

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TotalOverTheMaximum_Returns422WithTheItalianMessage()
    {
        // Arrange: two kilometres at the largest day price.
        await using var factory = CreateFactory();
        var body = Body("ByDistance");
        body["kilometres"] = 2;
        body["dayPrice"] = 9999999999.99m;

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        (await DetailAsync(response)).ShouldBe("Importo troppo alto.");
        await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, PricingErrors.AmountTooLarge, Ct);
    }

    [Theory]
    [InlineData("ByHour", "endTime", null, "EndTime", "Valore obbligatorio.")]
    [InlineData("ByHour", "endTime", "10:00", "EndTime", "La fine deve essere diversa dall'inizio.")]
    [InlineData("ByHour", "endTime", "sera", "EndTime", "Inserisci un orario.")]
    [InlineData("ByHour", "startTime", "9", "StartTime", "Inserisci un orario.")]
    [InlineData("ByHour", "startTime", null, "StartTime", "Valore obbligatorio.")]
    [InlineData("ByHour", "kilograms", 5, "Kilograms", "Questo valore non si applica al contratto.")]
    [InlineData("ByHour", "kilometres", 5, "Kilometres", "Questo valore non si applica al contratto.")]
    [InlineData("ByHour", "pricingMode", null, "PricingMode", "Valore obbligatorio.")]
    [InlineData("ByHour", "billingLevel", null, "BillingLevel", "Valore obbligatorio.")]
    [InlineData("ByHour", "pickupLocationId", "7d0e8f0e-9b3a-4c53-9a39-3f6a1d2b4c11", "PickupLocationId", "Questo valore non si applica al contratto.")]
    [InlineData("ByHour", "deliveryLocationId", "7d0e8f0e-9b3a-4c53-9a39-3f6a1d2b4c11", "DeliveryLocationId", "Questo valore non si applica al contratto.")]
    [InlineData("ByHour", "dayPrice", null, "DayPrice", "Valore obbligatorio.")]
    [InlineData("ByWeight", "kilograms", null, "Kilograms", "Valore obbligatorio.")]
    [InlineData("ByWeight", "kilograms", 0, "Kilograms", "Inserisci un numero intero maggiore di zero.")]
    [InlineData("ByWeight", "kilograms", -3, "Kilograms", "Inserisci un numero intero maggiore di zero.")]
    [InlineData("ByWeight", "kilograms", 12.5, "Kilograms", "Inserisci un numero intero maggiore di zero.")]
    [InlineData("ByWeight", "kilograms", 100000, "Kilograms", "Al massimo 99.999 kg.")]
    [InlineData("ByWeight", "endTime", "10:00", "EndTime", "Questo valore non si applica al contratto.")]
    [InlineData("ByWeight", "kilometres", 5, "Kilometres", "Questo valore non si applica al contratto.")]
    [InlineData("ByDistance", "kilometres", null, "Kilometres", "Valore obbligatorio.")]
    [InlineData("ByDistance", "kilometres", 0, "Kilometres", "Inserisci un numero intero maggiore di zero.")]
    [InlineData("ByDistance", "kilometres", 2.5, "Kilometres", "Inserisci un numero intero maggiore di zero.")]
    [InlineData("ByDistance", "kilometres", 10000, "Kilometres", "Al massimo 9.999 km.")]
    [InlineData("ByDistance", "kilograms", 5, "Kilograms", "Questo valore non si applica al contratto.")]
    [InlineData("ByTrip", "kilometres", 5, "Kilometres", "Questo valore non si applica al contratto.")]
    [InlineData("ByTrip", "kilograms", 5, "Kilograms", "Questo valore non si applica al contratto.")]
    [InlineData("ByTrip", "endTime", "10:00", "EndTime", "Questo valore non si applica al contratto.")]
    public async Task InvalidMember_Returns400UnderThatMemberInItalian(
        string mode, string field, object? value, string expectedKey, string expectedMessage)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body(mode);
        if (value is null)
        {
            body.Remove(field);
        }
        else
        {
            body[field] = value;
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = document.RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).ShouldBe([expectedKey]);
        errors.GetProperty(expectedKey)[0].GetString().ShouldBe(expectedMessage);
    }

    [Fact]
    public async Task NightPriceBelowDayPrice_Returns400UnderNightPrice()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByHour");
        WithNightSurcharge(body, night: 50, fee: 5);

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["NightPrice"]);
    }

    [Fact]
    public async Task VehicleRateWithoutVehicle_Returns400UnderItsVehicleId()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByHour");
        body["vehicleRates"] = new[] { VehicleRateItem(null) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VehicleRates[0].VehicleId"]);
    }

    [Fact]
    public async Task VehicleRateWithoutNightPriceOnASurchargeContract_Returns400UnderItsNightPrice()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByHour");
        WithNightSurcharge(body, night: 70, fee: 5);
        body["vehicleRates"] = new[] { VehicleRateItem(Guid.NewGuid(), """{"dayPrice":80,"minimumRule":"None"}""") };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = document.RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).ShouldContain("VehicleRates[0].NightPrice");
        errors.GetProperty("VehicleRates[0].NightPrice")[0].GetString().ShouldBe("Valore obbligatorio.");
    }

    [Fact]
    public async Task ByTripWithATieredMinimum_Returns400UnderMinimumRule()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByTrip");
        body["minimumRule"] = "TieredOverage";

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var errors = document.RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).ShouldBe(["MinimumRule"]);
        errors.GetProperty("MinimumRule")[0].GetString().ShouldBe("Questo valore non si applica al contratto.");
    }

    private static readonly Guid LocationA = Guid.NewGuid();
    private static readonly Guid LocationB = Guid.NewGuid();
    private static readonly Guid LocationC = Guid.NewGuid();

    /// <summary>A valid body for a location contract by hour without the night surcharge, holding the given location rates.</summary>
    private static Dictionary<string, object?> LocationBodyFor(
        string billingLevel, params Dictionary<string, object?>[] locationRates)
    {
        var body = Body("ByHour");
        body["billingLevel"] = billingLevel;
        body.Remove("dayPrice");
        body.Remove("minimumRule");
        body["locationRates"] = locationRates;
        return body;
    }

    private static string Camel(string member) => char.ToLowerInvariant(member[0]) + member[1..];

    [Theory]
    [InlineData("PerPickupLocation", "Location", 80, 160)]
    [InlineData("PerDeliveryLocation", "Location", 90, 180)]
    [InlineData("PerLocationPair", "LocationPair", 100, 200)]
    public async Task LocationLevel_IsPricedWithTheRateOfTheTripLocationsAndNamesItsSource(
        string level, string source, int dayPrice, int total)
    {
        // Arrange: two rates; the trip is 120 minutes and names the second one's locations.
        await using var factory = CreateFactory();
        var body = LocationBodyFor(
            level,
            LocationRateFor(level, LocationA, LocationA, """{"dayPrice":1,"minimumRule":"None"}"""),
            LocationRateFor(level, LocationB, LocationC, $$"""{"dayPrice":{{dayPrice}},"minimumRule":"None"}"""));
        body["endTime"] = "12:00";
        body["pickupLocationId"] = level == "PerDeliveryLocation" ? null : LocationB;
        body["deliveryLocationId"] = level == "PerPickupLocation" ? null : LocationC;

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert
        result.GetProperty("rateSource").GetString().ShouldBe(source);
        result.GetProperty("vehicleId").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("unitPrice").GetDecimal().ShouldBe(dayPrice);
        result.GetProperty("total").GetDecimal().ShouldBe(total);
    }

    [Theory]
    [InlineData("PerPickupLocation", "PickupLocationId")]
    [InlineData("PerDeliveryLocation", "DeliveryLocationId")]
    [InlineData("PerLocationPair", "DeliveryLocationId")]
    public async Task LocationOrPairWithoutARate_Returns400OnTheMemberWithTheNoRateText(string level, string expectedKey)
    {
        // Arrange: the trip names a location, or a pair in the reverse order, that no written rate has.
        await using var factory = CreateFactory();
        var body = LocationBodyFor(level, LocationRateFor(level, LocationA, LocationB));
        body["pickupLocationId"] = level == "PerDeliveryLocation" ? null : LocationB;
        body["deliveryLocationId"] = level == "PerPickupLocation" ? null : LocationA;

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([expectedKey]);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        document.RootElement.GetProperty("errors").GetProperty(expectedKey)[0].GetString()
            .ShouldBe("Non c'è una tariffa per i luoghi del viaggio.");
    }

    [Theory]
    [InlineData("PerPickupLocation", "PickupLocationId")]
    [InlineData("PerDeliveryLocation", "DeliveryLocationId")]
    [InlineData("PerLocationPair", "PickupLocationId")]
    [InlineData("PerLocationPair", "DeliveryLocationId")]
    public async Task LocationTheLevelUses_WhenMissing_Returns400RequiredOnTheMember(string level, string member)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = LocationBodyFor(level, LocationRateFor(level, LocationA, LocationB));
        body["pickupLocationId"] = level == "PerDeliveryLocation" ? null : LocationA;
        body["deliveryLocationId"] = level == "PerPickupLocation" ? null : LocationB;
        body.Remove(Camel(member));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([member]);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        document.RootElement.GetProperty("errors").GetProperty(member)[0].GetString().ShouldBe("Valore obbligatorio.");
    }

    [Theory]
    [InlineData("PerPickupLocation", "DeliveryLocationId")]
    [InlineData("PerDeliveryLocation", "PickupLocationId")]
    public async Task LocationTheLevelDoesNotUse_Returns400NotApplicableOnTheMember(string level, string member)
    {
        // Arrange: the trip names the level's location and, besides, the one the level does not use.
        await using var factory = CreateFactory();
        var body = LocationBodyFor(level, LocationRateFor(level, LocationA, LocationA));
        body["pickupLocationId"] = LocationA;
        body["deliveryLocationId"] = LocationA;

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([member]);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        document.RootElement.GetProperty("errors").GetProperty(member)[0].GetString()
            .ShouldBe("Questo valore non si applica al contratto.");
    }

    /// <summary>A location body by hour (120 minutes) with rates on A and B, the trip on B, and the given vehicle rates.</summary>
    private static Dictionary<string, object?> LocationBodyWithVehicleRates(
        string level, Guid? tripVehicle, params Dictionary<string, object?>[] vehicleRates)
    {
        var body = LocationBodyFor(
            level,
            LocationRateFor(level, LocationA, LocationA, """{"dayPrice":1,"minimumRule":"None"}"""),
            LocationRateFor(level, LocationB, LocationB, """{"dayPrice":50,"minimumRule":"None"}"""));
        body["endTime"] = "12:00";
        body["pickupLocationId"] = level == "PerDeliveryLocation" ? null : LocationB;
        body["deliveryLocationId"] = level == "PerPickupLocation" ? null : LocationB;
        body["vehicleRates"] = vehicleRates;
        body["vehicleId"] = tripVehicle;
        return body;
    }

    [Theory]
    [InlineData("PerPickupLocation")]
    [InlineData("PerDeliveryLocation")]
    [InlineData("PerLocationPair")]
    public async Task VehicleWithARateOnTheTripLocation_IsPricedWithThatRateAndNamesTheVehicle(string level)
    {
        // Arrange
        await using var factory = CreateFactory();
        var vehicleId = Guid.NewGuid();
        var body = LocationBodyWithVehicleRates(
            level,
            vehicleId,
            VehicleRateFor(level, vehicleId, LocationB, LocationB, """{"dayPrice":80,"minimumRule":"None"}"""));

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert: 120 minutes at the vehicle's 80 an hour, not at the location's 50.
        result.GetProperty("rateSource").GetString().ShouldBe("Vehicle");
        result.GetProperty("vehicleId").GetGuid().ShouldBe(vehicleId);
        result.GetProperty("unitPrice").GetDecimal().ShouldBe(80m);
        result.GetProperty("total").GetDecimal().ShouldBe(160m);
    }

    [Theory]
    [InlineData("PerPickupLocation", "Location", false)]
    [InlineData("PerPickupLocation", "Location", true)]
    [InlineData("PerDeliveryLocation", "Location", false)]
    [InlineData("PerDeliveryLocation", "Location", true)]
    [InlineData("PerLocationPair", "LocationPair", false)]
    [InlineData("PerLocationPair", "LocationPair", true)]
    public async Task VehicleWithoutARateOnTheTripLocationOrNoVehicle_IsPricedWithTheLocationRate(
        string level, string source, bool vehicleSent)
    {
        // Arrange: the vehicle has a rate on A only, the trip is on B.
        await using var factory = CreateFactory();
        var vehicleId = Guid.NewGuid();
        var body = LocationBodyWithVehicleRates(
            level,
            vehicleSent ? vehicleId : null,
            VehicleRateFor(level, vehicleId, LocationA, LocationA, """{"dayPrice":80,"minimumRule":"None"}"""));

        // Act
        var result = await ReadAsync(await PostAsync(BackOfficeClient(factory), body));

        // Assert: 120 minutes at the location's 50 an hour.
        result.GetProperty("rateSource").GetString().ShouldBe(source);
        result.GetProperty("vehicleId").ValueKind.ShouldBe(JsonValueKind.Null);
        result.GetProperty("total").GetDecimal().ShouldBe(100m);
    }

    [Theory]
    [InlineData("PerPickupLocation", "VehicleRates[1].PickupLocationId")]
    [InlineData("PerDeliveryLocation", "VehicleRates[1].DeliveryLocationId")]
    [InlineData("PerLocationPair", "VehicleRates[1].DeliveryLocationId")]
    public async Task TwoVehicleRatesOfTheVehicleOnTheSameLocation_Returns400UnderTheSecondOne(string level, string expectedKey)
    {
        // Arrange
        await using var factory = CreateFactory();
        var vehicleId = Guid.NewGuid();
        var body = LocationBodyWithVehicleRates(
            level,
            vehicleId,
            VehicleRateFor(level, vehicleId, LocationB, LocationB),
            VehicleRateFor(level, vehicleId, LocationB, LocationB, """{"dayPrice":90,"minimumRule":"None"}"""));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([expectedKey]);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        document.RootElement.GetProperty("errors").GetProperty(expectedKey)[0].GetString()
            .ShouldBe("Il mezzo ha già una tariffa propria per questo luogo o questa coppia.");
    }

    [Theory]
    [InlineData("PerPickupLocation", "VehicleRates[0].PickupLocationId")]
    [InlineData("PerDeliveryLocation", "VehicleRates[0].DeliveryLocationId")]
    [InlineData("PerLocationPair", "VehicleRates[0].DeliveryLocationId")]
    public async Task VehicleRateOnALocationWithoutALocationRate_Returns400UnderItsLocation(string level, string expectedKey)
    {
        // Arrange: the contract has rates on A and B only.
        await using var factory = CreateFactory();
        var vehicleId = Guid.NewGuid();
        var body = LocationBodyWithVehicleRates(
            level, vehicleId, VehicleRateFor(level, vehicleId, LocationC, LocationC));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([expectedKey]);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("errors");
        errors.GetProperty(expectedKey)[0].GetString().ShouldBe("Il contratto non ha una tariffa per questo luogo o questa coppia.");
    }

    [Fact]
    public async Task VehicleRateWithoutLocationsOnALocationLevel_Returns400UnderItsLocation()
    {
        // Arrange
        await using var factory = CreateFactory();
        var vehicleId = Guid.NewGuid();
        var body = LocationBodyWithVehicleRates(
            "PerPickupLocation", vehicleId, VehicleRateFor("FlatRate", vehicleId, null, null));

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["VehicleRates[0].PickupLocationId"]);
    }

    [Fact]
    public async Task LocationLevelWithoutWrittenRates_Returns400OnLocationRatesOnly()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = LocationBodyFor("PerPickupLocation");
        body["pickupLocationId"] = LocationA;

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["LocationRates"]);
    }

    [Fact]
    public async Task FlatRateWithLocationRates_Returns400NotApplicableOnLocationRates()
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = Body("ByHour");
        body["locationRates"] = new[] { LocationRateFor("PerPickupLocation", LocationA, null) };

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe(["LocationRates"]);
    }

    [Theory]
    [MemberData(nameof(InvalidLocationRates), MemberType = typeof(ContractTestSupport))]
    public async Task BrokenLocationRate_Returns400UnderItsMemberKey(
        string pricingMode, bool nightSurcharge, string rate, string keys)
    {
        // Arrange
        await using var factory = CreateFactory();
        var body = LocationBodyFor("PerPickupLocation", LocationRateFor("PerPickupLocation", LocationA, null, rate));
        body["pricingMode"] = pricingMode;
        body["nightSurcharge"] = nightSurcharge;
        body["pickupLocationId"] = LocationA;
        if (pricingMode != "ByHour")
        {
            body.Remove("endTime");
        }

        // Act
        var response = await PostAsync(BackOfficeClient(factory), body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorKeysAsync(response)).ShouldBe([.. LocationRateKeys(keys).Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task LocationSimulation_AsDriver_Returns403()
    {
        // Arrange
        await using var factory = CreateFactory();
        var client = TestCaller.WithRole("Driver").ApplyTo(factory.CreateClient());
        var body = LocationBodyFor("PerPickupLocation", LocationRateFor("PerPickupLocation", LocationA, null));
        body["pickupLocationId"] = LocationA;

        // Act
        var response = await PostAsync(client, body);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
