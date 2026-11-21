# Appendix: Pricing Engine

The pricing engine is the highest-risk component of the system (PRD §10.2). This appendix describes the engine as built, the rules it applies and the tests that guard it. ADR 0003 records why it is a pure function, and that decision holds. The type names and the result shape the ADR sketches (`IPricingEngine` in `Registry.Contracts`, `RateMatrix`, `TripParameters`, `TimeBandConfiguration`, `Money`, a human-readable `Explanation` string) are superseded by what this appendix describes.

## Scope

The engine lives in module `Registry`, in `src/Modules/Registry/Priamus.Trips.Registry/Domain/Pricing/`, and is internal to the module. Exposing it to Operations through `Registry.Contracts` is the job of the trips slice. Nothing here saves a trip or a snapshot.

It works on the types the contract slices already have (`Rate`, `VehicleRate`, `MinimumRule`, in `Domain/Contracts/ValueObjects/`) with euro `decimal` amounts. It does not use a `RateMatrix`, a `Money` type or a second `MinimumRule`.

Files:
- `NightBand.cs`: the night band value object.
- `TripMeasure.cs`: what of a trip the engine measures, one case per pricing mode.
- `PricingTerms.cs`: the terms of a flat-rate contract.
- `PriceCalculation.cs`: the structured result, with the `TimeBand` and `RateSource` enums.
- `PricingErrors.cs`: the errors of the night band, the measures and the engine.
- `PricingEngine.cs`: the calculation, `PricingEngine.Calculate(terms, vehicle, trip, band)`.

Tests, in `tests/Priamus.Trips.Registry.UnitTests/Domain/Pricing/`: `NightBandTests.cs`, `TripMeasureTests.cs`, `PricingEngineTests.cs`.

## Types

- `NightBand`: `Start` and `End` as whole minutes (seconds and fractions are dropped). `Create` refuses a band whose end equals its start. `NightBand.Default` is 22:00–06:00. `IsNight(start)` is `Start <= t < End` for a band inside the day and `t >= Start || t < End` for a band that wraps midnight. The start is included and the end excluded.
- `PricingTerms(Mode, NightSurcharge, Rate, VehicleRates)`: the terms of a flat-rate contract, saved or as written (the simulation prices an unsaved contract).
- `TripMeasure`, sealed, one case per `PricingMode`, every case carrying the start time:
  - `ByHour(start, end)` holds the exact `Minutes`. An end earlier than the start means the next day (23:00–02:00 is 3 h); an end equal to the start is refused, so a duration is 1 to 1439 minutes.
  - `ByWeight(kilograms, start)`: whole kilograms, 1 to 99,999.
  - `ByDistance(kilometres, start)`: whole kilometres, 1 to 9,999.
  - `ByTrip(start)`.
- `PriceCalculation`: no sentence, only data the caller can write in its own words: `Total`, `Band` (`TimeBand.Day | Night`), `NightSurcharge`, the `NightBand` used, `RateSource` (`Contract | Vehicle`) with `VehicleId`, `Quantity` (minutes, kilograms or kilometres, `null` by trip), `UnitPrice`, `BaseAmount`, `Minimum`, `AmountAfterMinimum`, `MinimumApplied` (the minimum changed the amount) and `NightCallFee` (`null` when none is added).
- `PricingEngine.Calculate` returns `Result<PriceCalculation>`. The only failure of the engine is `PricingErrors.AmountTooLarge` (Domain, 422).

## Algorithm

The engine is a pure function: no state, no I/O, no clock, `decimal` only. In order:

1. **Rate.** The vehicle rate of `vehicle` in `terms.VehicleRates` if there is one, otherwise `terms.Rate`. Rate, minimum and call fee all come from the same rate.
2. **Band.** Night when `band.IsNight(start)`. The classification depends on the start time alone (PRD §5.8): a trip that starts at 21:59 and ends at 02:00 is a day trip.
3. **Unit price.** The night price when the trip is night and the contract has the night surcharge, otherwise the day price. A night trip without the surcharge stays classified as night, with the day price and no call fee.
4. **Base amount.**
   - By hour: `round(minutes × price / 60)`. The minutes are exact, never rounded to a step.
   - By weight: `round(kilograms × price / 1000)`. The price is per ton.
   - By distance: `round(kilometres × price)`.
   - By trip: the price.
5. **Minimum.**
   - None: the base amount.
   - Fixed minimum M: `max(base, M)`.
   - Tiered overage (M, threshold X, overage price Y): `M + round(max(0, q − X') × Y')`, whatever the day or night price. By hour q is the minutes, X' = X × 60 and Y' = Y / 60. By weight q is the kilograms, X' = X × 1000 and Y' = Y / 1000. By distance q is the kilometres. The overage is rounded once.
6. **Night call fee.** Added when the trip is night and the contract has the surcharge (it may be 0).
7. **Limit.** A total above `Rate.MaxAmount` (9,999,999,999.99) is `AmountTooLarge`.

`round` is `decimal.Round(x, 2, MidpointRounding.AwayFromZero)`; amounts are positive, so it is half up. Multiplications come before divisions so no precision is lost.

## Where it departs from PRD §5.7

PRD §5.7 adds the night call fee before the minimum. The engine adds it after: the minimum sees the base amount alone and the call fee is added to its result. With 1 h at 42 €/h, a minimum of 100 € and a call fee of 35 €, the PRD order gives 100 € and the engine gives 135 €. A tiered overage ignores the day or night price altogether, so on 3 h with 50 € up to 4 h then 12 €/h and a call of 35 €, a day trip costs 50 € and a night trip 85 €. The reason is that the call fee pays for the call, not for the time, so a minimum that swallows it would make the call free for short trips. None of the numeric examples of PRD §12.1 to §12.6 combines a night trip with a minimum, so none of them changes.

Weight is in whole kilograms priced per ton, not decimal tons. The night price must not be lower than the day price: this is checked when a rate is saved (`Rate.Create`, error `Registry.Contract.NightPriceBelowDayPrice`), not by the engine, and a stored rate is rebuilt without that check. A night trip on a contract with the surcharge always has a night price and a call fee, since a rate without them is refused when saved, so there is no fallback to the day price. The result carries data, not an explanation string or audit steps; the caller writes the lines.

## Invariants

- Pure function, `decimal` only: no `double`, no `float`, no `DateTime.UtcNow`.
- The mode of the trip must equal `terms.Mode`; otherwise `Calculate` throws `ArgumentException` (a programming error, not a `Result`).
- Terms that lack a night price or call fee they need, or a trip by trip with a tiered overage, throw `InvalidOperationException`: saving a contract already forbids both.
- The night band is a system setting passed in by the caller; the engine never reads it.
- A snapshot, once the trips slice has one, keeps the amount and the band it was computed with; later changes of rate or band do not alter it.

## Required tests

Every numeric example of PRD §12.1 to §12.6 has a test that asserts the exact amount (one theory, `Calculate_PrdExample_ReturnsTheStatedAmount`). Besides, `PricingEngineTests` covers:
- exact minutes (2 h 20 min at 30 €/h is 70.00 €) and rounding to the cent, with a half cent rounding up;
- weight per ton (12,300 kg at 18.50 € per ton is 227.55 €) and distance;
- the fixed minimum below, at and above the base amount, and the call fee added after it;
- the tiered overage by hour (threshold compared in minutes), by weight (in kilograms) and by distance, rounded once, with the call fee added after it;
- classification by the start alone, at the boundaries of the band;
- day trip, night trip with the surcharge, with a zero call fee, and without the surcharge;
- the vehicle rate used for every step, a vehicle without a rate of its own, and no vehicle;
- a total over the maximum refused and a total equal to it accepted;
- a trip of another mode, terms without a night price or call fee, and a trip by trip with a tiered overage all throwing.

`NightBandTests` covers the default band, the boundaries 22:00 and 05:59 as night and 06:00 and 21:59 as day, a band inside the day, the refusal of an equal start and end, and the dropping of seconds. `TripMeasureTests` covers the duration (the next day, the equal start and end refused), the quantity limits and the by-trip start.

## Coverage target

100% line coverage on the types in `Domain/Pricing/`. Anything below 100% is a defect. The line coverage is not measured by the test runner today (`coverlet.collector` does not run under Microsoft.Testing.Platform), so it is argued by reading the tests against the code.

## Forbidden patterns in pricing code

- `double` or `float` anywhere.
- `DateTime.UtcNow` (the engine is pure).
- Silent fallbacks: a missing value the engine needs is an exception, not a default.
- Skipping a PRD §12 example test case.
- "Approximately equal" assertions: numeric tests are exact.
- Optimizations that reduce readability.
