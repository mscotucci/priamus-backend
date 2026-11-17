# Registry

The context that holds the company's master data: the fleet, and later customers, drivers, locations, contracts and rates, with the pricing engine. Other contexts read from it. Seeded from PRD §5.2 and §6.6; started at the grilling of 2026-11-16 with the vehicle registry.

## Language

### Fleet

**Vehicle** (Mezzo):
A vehicle of the company's own fleet, identified by its license plate. Every vehicle has exactly one type and one status.
_Avoid_: truck, asset, unit

**License plate** (Targa):
The registration of a vehicle. Unique among all vehicles, decommissioned ones included, whatever their type. Kept in a normal form: uppercase letters and digits only, spaces and hyphens removed, 2 to 10 characters; no national format is checked, since trailers and foreign vehicles differ.
_Avoid_: plate number, registration, targa (in code)

**Description** (Descrizione / modello):
Free text that tells people which vehicle it is (make, model, body).
_Avoid_: name, model (alone)

**Vehicle type** (Tipologia):
What role a vehicle can play in a trip, one of three: `Tractor` (Motrice), `Trailer` (Rimorchio), `StandaloneVehicle` (Autonomo).
_Avoid_: category (categories of vehicles are out of scope), kind

**Tractor** (Motrice):
A vehicle that can lead a trip on its own or pulling a trailer.
_Avoid_: truck, cab, head

**Trailer** (Rimorchio):
A towed vehicle. It never leads a trip; it is only ever the trailer of a tractor.
_Avoid_: semi-trailer (a kind of trailer, not a separate type)

**Standalone vehicle** (Mezzo autonomo):
A vehicle that never takes a trailer (vacuum truck, mobile crane, van). It leads a trip alone.
_Avoid_: special vehicle, autonomous

**Requires trailer** (Richiede rimorchio):
A property of a tractor only: a trip led by it must name a trailer. A tractor without it may run with or without one.
_Avoid_: trailer mandatory, coupled

**Vehicle status** (Stato):
Whether a vehicle can be chosen for new trips: `Active` (Attivo) or `Decommissioned` (Dismesso). A decommissioned vehicle stays in the registry for the trips that name it.
_Avoid_: deleted, disabled, archived

**Decommission** / **Reactivate** (Dismettere / Riattivare):
Moving a vehicle from active to decommissioned and back. Neither changes any trip already recorded.
_Avoid_: delete, retire, restore

## Relationships

- A trip records its own vehicle composition when it is registered (Operations); changing a vehicle's type or its requires-trailer property later does not change it.
- Depends on **Identity** for the current user and the authorization policies.
