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
The prices a contract charges a trip, in the unit of its pricing mode: a day price and, when the contract has the night surcharge, a night price and a night call fee, with its minimum rule. A flat-rate contract has one rate; the other billing levels have at least one, one per location (**Location rate**) or per pair of locations (**Pair rate**). A rate applies for the whole validity of its contract and has no validity of its own: prices that change from a date make a new contract from that date.
_Avoid_: price list, tariff, rate validity

**Vehicle rate** (Tariffa del mezzo):
A complete rate of its own that a contract gives one vehicle (a tractor or a standalone vehicle): day price, night price and night call fee when the contract has the night surcharge, and its own minimum rule. A trip led by that vehicle is priced with it instead of the contract's rate. In a contract priced per location or per pair a vehicle rate applies on some of the locations or pairs the contract has a rate for, not on the whole contract: a vehicle may have several vehicle rates with different prices, never two on the same location or pair, and may have none on some locations; a trip led by that vehicle on a location it has a vehicle rate for is priced with it instead of the location's rate. Like the contract's rate, it lives as long as its contract and has no validity of its own.
_Avoid_: override (the PRD's word, not used by the business), special rate

**Location rate** (Tariffa del luogo):
A complete rate that a contract priced per pickup location or per delivery location gives one location: day price, night price and night call fee when the contract has the night surcharge, and its own minimum rule. A trip is priced with the rate of its pickup or delivery location, according to the billing level. A location without a rate is not usable for that customer. Like the contract's rate, it lives as long as its contract and has no validity of its own.
_Avoid_: location price, location tariff

**Pair rate** (Tariffa della coppia):
The same, for a contract priced per pair of locations: it belongs to an ordered pair (pickup location, delivery location), so A → B and B → A are two rates, and the same location may be both. A trip is priced with the rate of its pair.
_Avoid_: route rate, itinerary rate

**Night band** (Fascia notturna):
The hours of the day in which a trip that starts is a night trip, one for the whole system (22:00–06:00 unless the administrator changes it). A change holds for every amount computed from then on, never for amounts already computed.
_Avoid_: night shift, night time, night surcharge (that is a contract's choice to charge night trips more)

**Night trip** / **Day trip** (Viaggio notturno / diurno):
A trip whose start time falls inside / outside the night band. Its end time and duration never count. A night trip on a contract without the night surcharge is still a night trip; it just costs the day price.
_Avoid_: night service, overnight trip

**Simulation** (Prova della configurazione):
Computing the amount of a sample trip on a contract as written, before or without saving it, with the same calculation that prices real trips.
_Avoid_: estimate, preview, test calculation

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

### Locations

**Location** (Luogo):
A place a trip starts from or delivers to: a warehouse, a building site, a landfill, a plant. One registry for the whole company: a location belongs to no customer, and any location can be the pickup or the delivery location of a trip. The locations a customer's trips use are those its contract has rates for: a **Location rate** for a pickup or delivery location, a **Pair rate** for a pair. People know it as "<name>, <municipality>".
_Avoid_: site, address (that is one of its fields), destination, customer location, general location

**Location name** (Nome del luogo):
What people call the place ("Magazzino centrale", "Cantiere A14"). Together with the municipality it is unique among all locations, deactivated ones included.
_Avoid_: description, label

**Municipality** (Comune):
The town the location is in. Required, so that two places with the same name are told apart.
_Avoid_: city, town, comune (in code)

**Location status** (Stato):
Whether a location can be chosen for new rates and trips: `Active` (Attivo) or `Deactivated` (Disattivato). A deactivated location stays in the registry for the rates and trips that name it.
_Avoid_: deleted, archived, decommissioned (that is a vehicle's)

## Relationships

- A trip records its own vehicle composition when it is registered (Operations); changing a vehicle's type or its requires-trailer property later does not change it.
- Depends on **Identity** for the current user and the authorization policies.
