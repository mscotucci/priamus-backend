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
Whether a customer can be chosen for new trips and contracts: `Active` (Attivo) or `Deactivated` (Disattivato). A deactivated customer stays in the registry for the trips and contracts that name it; its contracts can be read but not added or changed until it is reactivated, while its own data stay editable.
_Avoid_: deleted, archived, decommissioned (that is a vehicle's)

**Deactivate** / **Reactivate** (Disattivare / Riattivare):
Moving a customer from active to deactivated and back. Neither changes its data, its contracts or any trip already recorded.
_Avoid_: delete, close, disable

### Contracts

**Contract** (Contratto):
The billing rules agreed with one customer for a period: how detailed its rates are, how a trip's amount is measured, which documents a trip must carry, whether night trips cost more. A customer may have many contracts over time, never two whose validity overlaps.
_Avoid_: agreement, deal, contratto attivo (a contract's status is read from its validity)

**Validity** (Validità):
The days a contract applies to: a start date and, when known, an end date, both included. Days, not instants.
_Avoid_: duration, term, effective dates

**Open contract** (Contratto aperto):
A contract with no end date. A customer has at most one, and it is the latest. Creating a later contract closes it on the day before the new one starts.
_Avoid_: active contract, current contract (those say whether today falls in it)

**Change from a date** (Modifica da una data):
Changing the terms of a contract in force from a given day: the contract keeps its terms and ends on the day before, and a new contract of the same customer with the changed terms starts on that day and ends where the contract ended. The way prices change.
_Avoid_: renewal, new version, split

**Correction** (Correzione):
Changing a contract in place because its data were wrong from the start: the change holds for its whole validity, days already passed included.
_Avoid_: amendment, update (alone)

**Contract status** (Stato del contratto):
Where today falls against a contract's validity, read from the calendar and never set by hand: `Current` (In vigore) when today is inside it, `Upcoming` (Futuro) when it starts later, `Expired` (Scaduto) when it has ended.
_Avoid_: active / inactive (those are a customer's or a vehicle's status), open (that is about the end date)

**Night surcharge** (Maggiorazione notturna):
Whether a contract charges night trips more. When it does, each of its rates has a night price and a night call fee besides the day price; when it does not, a rate has the day price only.
_Avoid_: night rate (that is one price of a rate), night band (that is when night is)

**Billing level** (Livello di fatturazione):
How finely a contract's rates are set, and so which locations a trip must name: `FlatRate` (Forfettario, one rate for the customer), `PerPickupLocation` (Per luogo di partenza), `PerDeliveryLocation` (Per luogo di consegna), `PerLocationPair` (Per coppia di luoghi).
_Avoid_: rate level, granularity

**Pricing mode** (Modalità di pagamento):
What a trip's amount is measured by, and so what the driver enters: `ByHour` (A ore), `ByWeight` (A peso), `ByDistance` (A km), `ByTrip` (A viaggio).
_Avoid_: payment method (it is not how the customer pays), rate type

**Rate** (Tariffa):
The prices a contract charges a trip, in the unit of its pricing mode: a day price and, when the contract has the night surcharge, a night price and a night call fee, with its minimum rule. A flat-rate contract has one rate; the other billing levels have one per location or pair of locations. A rate applies for the whole validity of its contract and has no validity of its own: prices that change from a date make a new contract from that date.
_Avoid_: price list, tariff, rate validity

**Required documents** (Documenti obbligatori):
The documents a contract asks for: on every trip a delivery note (DDT), a delivery slip (bolla) or a waste form (FIR), and at the end of the driver's day a daily summary. Any of them, or none.
_Avoid_: attachments (an attachment is the file a driver uploads)

### Drivers

**Driver** (Autista):
A person who drives the company's vehicles and records trips, known to people by first and last name. A record of the registry, distinct from the user account it signs in with; a driver may have no account.
_Avoid_: employee, operator, user (that is the account)

**Driver account** (Account dell'autista):
The user account, with the role Autista, linked to a driver: the one through which that driver signs in. A driver has at most one, and an account belongs to at most one driver.
_Avoid_: driver user, login (alone)

**Driver status** (Stato):
Whether a driver can work in the system: `Active` (Attivo) or `Deactivated` (Disattivato). A deactivated driver stays in the registry for the trips that name them.
_Avoid_: dismissed, fired, disabled (that is an account's)

## Relationships

- A trip records its own vehicle composition when it is registered (Operations); changing a vehicle's type or its requires-trailer property later does not change it.
- Depends on **Identity** for the current user and the authorization policies.
