# Appendix: Pricing Engine

The pricing engine is the highest-risk component of the system (PRD §10.2). This appendix details the algorithm, types, invariants, and required test coverage.

## Scope

Lives in module `Registry`. Exposed via `IPricingEngine` interface in `Registry.Contracts`.

Files under control:
- `src/Modules/Registry/Priamus.Trips.Registry/Domain/Pricing/PricingEngine.cs`
- `Domain/Pricing/RateMatrix.cs`
- `Domain/Pricing/TimeBand.cs` (sealed: Daytime | Nighttime)
- `Domain/Pricing/TimeBandConfiguration.cs`
- `Domain/Pricing/MinimumRule.cs` (sealed: None | FixedMinimum | TieredOverage)
- `Domain/Pricing/CalculatedAmount.cs`
- `Domain/Pricing/RateSnapshot.cs`
- `Domain/Pricing/PricingErrors.cs`
- `Application/Abstractions/IPricingEngine.cs`
- `Priamus.Trips.Registry.Contracts/Pricing/IPricingEngine.cs`

Tests:
- `tests/Priamus.Trips.Registry.UnitTests/Domain/Pricing/`

## Algorithm (normative — PRD §5.7 and §5.9)

Input:
- `matrix: RateMatrix` — already resolved (standard or vehicle override) before reaching the engine
- `parameters: TripParameters` — sealed type discriminating ByHour, ByWeight, ByDistance, ByTrip
- `timeBandConfig: TimeBandConfiguration` — the night-band definition

Output: `CalculatedAmount` with `FinalAmount`, `AppliedTimeBand`, `Steps`, `Explanation`.

Steps:

1. **Classify time band**:
   ```
   timeBand = timeBandConfig.Classify(parameters.Period.StartTime)
   ```
   Classification depends on `StartTime` only (PRD §5.8).

2. **Resolve unit rate**:
   ```
   unitRate = timeBand.GetRateFrom(matrix)
   // Daytime → matrix.DaytimeRate (always)
   // Nighttime → matrix.NighttimeRate if present, else fallback to DaytimeRate
   ```

3. **Compute base amount**:
   ```csharp
   var baseAmount = parameters switch
   {
       ByHourParameters p     => p.Hours * unitRate,
       ByWeightParameters p   => p.Tons.Value * unitRate,
       ByDistanceParameters p => p.Kilometers.Value * unitRate,
       ByTripParameters _     => unitRate,  // flat
       _ => throw new UnreachableException(...)
   };
   ```

4. **Add night call fee**:
   ```
   nightCallFee = timeBand.GetNightCallFeeFrom(matrix)
   // Daytime → Money.Zero
   // Nighttime → matrix.NightCallFee if present, else Money.Zero
   amountWithCallFee = baseAmount + nightCallFee
   ```

5. **Apply minimum rule**:
   ```
   finalAmount = matrix.MinimumRule.Apply(amountWithCallFee, parameters.QuantitativeParameter)
   ```

6. **Build `CalculatedAmount`** with steps + explanation.

## Key invariants

### 1. Pure function
No state, no I/O, no time access, no random. `PricingEngine` has no fields, no constructor dependencies:

```csharp
public sealed class PricingEngine : IPricingEngine
{
    public CalculatedAmount Calculate(
        RateMatrix matrix,
        TripParameters parameters,
        TimeBandConfiguration timeBandConfig)
    {
        // pure computation only
    }
}
```

### 2. Decimal precision only
Money/quantities are `Money`/`Tons`/`Kilometers`/`Hours`. No `double`, no `float`.

### 3. Mode consistency
`RateMatrix.Mode` and `TripParameters.Mode` must match. Engine throws `DomainException` otherwise.

### 4. ByTrip forbids minimum rules (PRD §5.6)
`RateMatrix` constructor rejects `ByTrip` + `FixedMinimum`/`TieredOverage`.

### 5. Night rate and night call fee come together
`RateMatrix` constructor rejects: one of `NighttimeRate`/`NightCallFee` present without the other.

### 6. Nighttime rate >= Daytime rate
Semantic guard: a night premium is not a discount.

### 7. Snapshot immutability (PRD §5.3, §5.9)
Once a `RateSnapshot` is generated, it is immutable. Rate changes do not retroactively alter it.

### 8. Time band classification by StartTime only
`TimeBandConfiguration.Classify(DateTime startTime)` takes only the start.

### 9. Night fallback to daytime when matrix is daytime-only
If a trip is classified `Nighttime` but the matrix has no nighttime rates, the engine uses daytime rate and zero call fee. The classification remains `Nighttime` for traceability; only the economic effect is neutralized.

## Sealed type structure

### TimeBand

```csharp
public abstract record TimeBand
{
    public abstract Money GetRateFrom(RateMatrix matrix);
    public abstract Money GetNightCallFeeFrom(RateMatrix matrix);
    public abstract string Name { get; }

    public sealed record Daytime : TimeBand
    {
        public override Money GetRateFrom(RateMatrix matrix) => matrix.DaytimeRate;
        public override Money GetNightCallFeeFrom(RateMatrix matrix) => Money.Zero("EUR");
        public override string Name => "Daytime";
    }

    public sealed record Nighttime : TimeBand
    {
        public override Money GetRateFrom(RateMatrix matrix) =>
            matrix.NighttimeRate ?? matrix.DaytimeRate;
        public override Money GetNightCallFeeFrom(RateMatrix matrix) =>
            matrix.NightCallFee ?? Money.Zero("EUR");
        public override string Name => "Nighttime";
    }
}
```

### MinimumRule

```csharp
public abstract record MinimumRule
{
    public abstract Money Apply(Money baseAmount, decimal quantitativeParameter);
    public abstract string Description { get; }

    public sealed record None : MinimumRule
    {
        public override Money Apply(Money baseAmount, decimal _) => baseAmount;
        public override string Description => "No minimum";
    }

    public sealed record FixedMinimum(Money Threshold) : MinimumRule
    {
        public override Money Apply(Money baseAmount, decimal _) =>
            baseAmount.Amount >= Threshold.Amount ? baseAmount : Threshold;
        public override string Description => $"Fixed minimum {Threshold}";
    }

    public sealed record TieredOverage(
        Money FixedAmount, decimal Threshold, Money OveragePrice) : MinimumRule
    {
        public override Money Apply(Money _, decimal quantitativeParameter)
        {
            var overage = Math.Max(0, quantitativeParameter - Threshold);
            return FixedAmount + (OveragePrice * overage);
        }
        public override string Description =>
            $"Tiered: {FixedAmount} up to {Threshold} units, then {OveragePrice} per unit overage";
    }
}
```

`TieredOverage.Apply` IGNORES `baseAmount` — the formula is `FixedAmount + overage × OveragePrice`, not "minimum between baseAmount and tiered formula".

### TimeBandConfiguration

```csharp
public sealed record TimeBandConfiguration(TimeOnly NightStart, TimeOnly NightEnd)
{
    public TimeBand Classify(DateTime startTime)
    {
        var start = TimeOnly.FromDateTime(startTime);
        var isInNight = NightStart < NightEnd
            ? start >= NightStart && start < NightEnd      // same-day range
            : start >= NightStart || start < NightEnd;     // wraps midnight (e.g. 22:00-06:00)

        return isInNight ? new TimeBand.Nighttime() : new TimeBand.Daytime();
    }
}
```

## Mandatory test cases (PRD §12 Appendix)

Every numeric example in PRD §12 MUST have a corresponding test asserting the exact expected result. Required test method names and expected results:

| PRD ref | Test method | Expected |
|---|---|---|
| §12.1 | `Calculate_ByHour_2Hours_BelowFixedMinimum_Returns30Eur` | 30.00 € |
| §12.1 | `Calculate_ByHour_5Hours_AboveFixedMinimum_Returns50Eur` | 50.00 € |
| §12.2 | `Calculate_TieredOverage_3Hours_BelowThreshold_Returns50Eur` | 50.00 € |
| §12.2 | `Calculate_TieredOverage_4Hours_AtThreshold_Returns50Eur` | 50.00 € |
| §12.2 | `Calculate_TieredOverage_6Hours_AboveThreshold_Returns74Eur` | 74.00 € |
| §12.3 A | `Calculate_PerLocationPair_Daytime_StandardVehicle_Returns90Eur` | 90.00 € |
| §12.3 B | `Calculate_PerLocationPair_Nighttime_StandardVehicle_Returns161Eur` | 161.00 € |
| §12.3 C | `Calculate_PerLocationPair_Daytime_VehicleOverride_Returns165Eur` | 165.00 € |
| §12.3 D | `Calculate_PerLocationPair_Nighttime_VehicleOverride_Returns266Eur` | 266.00 € |
| §12.4 | `Calculate_FlatRate_ByHour_Daytime_3Hours_Returns60Eur` | 60.00 € |
| §12.4 | `Calculate_FlatRate_ByHour_Nighttime_3Hours_Returns115Eur` | 115.00 € |
| §12.5 | `Calculate_FlatRate_ByDistance_Daytime_80Km_Returns96Eur` | 96.00 € |
| §12.5 | `Calculate_FlatRate_ByDistance_Nighttime_80Km_Returns158Eur` | 158.00 € |
| §12.6 | `Calculate_FlatRate_ByTrip_Daytime_Returns150Eur` | 150.00 € |
| §12.6 | `Calculate_FlatRate_ByTrip_Nighttime_Returns240Eur` | 240.00 € |
| §12.7 | `Snapshot_AfterRetroactiveRateChange_PreservesOriginalAmount` | unchanged |

Additionally, boundary tests:
- Time band exactly at 22:00 (in/out), exactly at 06:00 (in/out)
- Same-day vs midnight-wrapping night range
- TieredOverage at exactly the threshold (no overage)
- FixedMinimum when base amount exactly equals threshold
- Zero quantity rejected upstream (verify engine receives validated inputs)
- ByTrip with minimum rule → constructor of RateMatrix throws
- Mismatched mode between matrix and parameters → engine throws

And invariant verification tests:
- `RateMatrix_WithNighttimeRateButNoNightCallFee_ThrowsDomainException`
- `RateMatrix_WithNightCallFeeButNoNighttimeRate_ThrowsDomainException`
- `RateMatrix_WithByTripModeAndFixedMinimum_ThrowsDomainException`
- `RateMatrix_WithNighttimeRateLowerThanDaytimeRate_ThrowsDomainException`

## Coverage target

100% line coverage on:
- `PricingEngine`
- `RateMatrix`
- `TimeBand` (Daytime + Nighttime)
- `MinimumRule` (None + FixedMinimum + TieredOverage)
- `TimeBandConfiguration`

Anything below 100% is a defect.

## Explanation string format

`CalculatedAmount.Explanation` is human-readable for back-office display:

```
{quantity} {unit} × {unitRate} = {baseAmount}{nightCallFeePart}{minimumPart} = {finalAmount}
```

Examples:
- `3.00 hours × 30.00 EUR = 90.00 EUR (daytime) = 90.00 EUR`
- `3.00 hours × 42.00 EUR + 35.00 EUR night call fee = 161.00 EUR (nighttime) = 161.00 EUR`
- `2.00 hours × 10.00 EUR = 20.00 EUR (below 30.00 EUR minimum) = 30.00 EUR`
- `6.00 hours: 50.00 EUR fixed + (6.00 - 4.00) × 12.00 EUR overage = 74.00 EUR (daytime) = 74.00 EUR`
- `Trip flat rate 200.00 EUR + 40.00 EUR night call fee = 240.00 EUR (nighttime) = 240.00 EUR`

## Calculation steps audit trail

`CalculatedAmount.Steps` is a sequence of `CalculationStep` records for traceability:

```csharp
public abstract record CalculationStep(int Order, string Description);

public sealed record TimeBandClassificationStep(int Order, DateTime StartTime, TimeBand Result, string Description) : CalculationStep(Order, Description);
public sealed record UnitRateResolutionStep(int Order, TimeBand Band, Money Rate, string Description) : CalculationStep(Order, Description);
public sealed record BaseAmountStep(int Order, decimal Quantity, Money UnitRate, Money Result, string Description) : CalculationStep(Order, Description);
public sealed record NightCallFeeStep(int Order, Money Fee, Money TotalAfter, string Description) : CalculationStep(Order, Description);
public sealed record MinimumRuleStep(int Order, MinimumRule Rule, Money Before, Money After, string Description) : CalculationStep(Order, Description);
```

Every calculation emits the full sequence, including no-op steps (e.g. `MinimumRuleStep` for `None`).

## Forbidden patterns in pricing code

- `double` or `float` anywhere
- `DateTime.UtcNow` (engine is pure)
- Silent fallbacks without an explicit step in the audit trail
- Skipping a PRD §12 example test case
- "Approximately equal" assertions (numeric tests are exact)
- Performance optimizations that reduce readability
