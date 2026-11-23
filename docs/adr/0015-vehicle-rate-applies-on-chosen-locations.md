# 0015 — A vehicle rate of a location contract applies on chosen locations

**Context.** A contract priced per pickup location, per delivery location or per pair of locations holds a rate for each location or pair. Some vehicles have prices of their own, which until now only a flat-rate contract could hold; the back office needs the same on a location contract, where the price of a trip depends on the place.

**Decision.** In a contract priced per location or per pair, a vehicle rate applies on chosen locations (or pairs) of the contract, not on the whole contract. A vehicle may have several vehicle rates with different prices, never two on the same location or pair, and none on some locations. A vehicle rate names the location or pair it applies on, as a location rate does; a vehicle that applies on several locations holds one vehicle rate for each, with equal prices. A flat-rate contract keeps one vehicle rate per vehicle, with no locations. When a trip's vehicle has a vehicle rate on the trip's location or pair, the trip is priced with it; otherwise with the location's rate.

**Why.** The price of a trip depends on the place (especially "a viaggio" and "a peso"), so a vehicle's rate has to be settable per place. One vehicle rate per place keeps the aggregate, the table and the audit flat, the engine's lookup one predicate, and "never two on the same place" one unique key. Decided by Michele on 2026-11-23.

**Considered options.**
- One vehicle rate per vehicle for the whole contract: cannot price a vehicle differently by location, which is wrong for "a viaggio" and "a peso".
- Both levels, a vehicle rate for the whole contract and one per location: three steps in the engine and two places to read an amount.
