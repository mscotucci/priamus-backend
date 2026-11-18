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

### Customers

**Customer** (Cliente):
A company or public body Priamus carries goods for, and bills. Identified for people by its company name and, when it has one, by its VAT number. Its billing rules come from its contracts.
_Avoid_: client, account, company (alone)

**Company name** (Ragione sociale):
The name of the customer as it appears on its documents. Two customers may share it.
_Avoid_: name (alone), business name, denominazione

**VAT number** (Partita IVA):
The Italian VAT registration of the customer: 11 digits with a check digit, kept without spaces or the "IT" prefix. Unique among all customers, deactivated ones included. A foreign customer has none.
_Avoid_: P.IVA (in code), tax ID

**Tax code** (Codice fiscale):
The Italian tax identifier of the customer: 16 letters and digits for a person, 11 digits for a company or public body, often equal to its VAT number. Not unique.
_Avoid_: fiscal code, CF (in code)

**Customer status** (Stato):
Whether a customer can be chosen for new trips and contracts: `Active` (Attivo) or `Deactivated` (Disattivato). A deactivated customer stays in the registry for the trips and contracts that name it.
_Avoid_: deleted, archived, decommissioned (that is a vehicle's)

**Deactivate** / **Reactivate** (Disattivare / Riattivare):
Moving a customer from active to deactivated and back. Neither changes its data, its contracts or any trip already recorded.
_Avoid_: delete, close, disable

## Relationships

- A trip records its own vehicle composition when it is registered (Operations); changing a vehicle's type or its requires-trailer property later does not change it.
- Depends on **Identity** for the current user and the authorization policies.
