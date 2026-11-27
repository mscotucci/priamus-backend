# Operations

The context that records the work done: the trips drivers add, start and end, later their attachments, the daily summaries and the anomalies. It reads master data and prices from Registry and never changes them. Seeded from PRD §5.2, §5.4, §5.5 and §7.1; started at the grilling of 2026-11-23 with the trip added by the driver.

## Language

**Trip** (Viaggio):
One job a driver does for one customer with one vehicle, recorded by that driver. It is priced once, when it is ended, with the contract in force for the customer on the trip's day.
_Avoid_: journey, delivery, run, ride

**Planned trip** (Viaggio da fare):
A trip added and not started yet: vehicle, customer and locations, with no day and no time. A driver may have many.
_Avoid_: draft, scheduled trip (it has no day), template

**Trip in progress** (Viaggio in corso):
A trip started and not ended yet. A driver has one at most. It has no amount.
_Avoid_: open trip, active trip, current trip

**Ended trip** (Viaggio concluso):
A trip with its start and its end. It has its amount and its trip number, and never changes.
_Avoid_: closed trip, completed trip

**Add a trip** (Inserire un viaggio):
The driver's act of recording a trip to do; it may be started in the same act.
_Avoid_: register (the act no longer exists), create, plan

**Start a trip** / **End a trip** (Iniziare / Terminare un viaggio):
The two acts that give a trip its start and its end: now, stamped by the system, or at a time the driver types, never in the future. A trip is ended whatever documents are attached (ADR 0016).
_Avoid_: open, close, depart, arrive, submit

**Trip number** (Numero del viaggio):
The whole number people call an ended trip by ("Viaggio n. 1248"), given when the trip is ended. It grows by one over all ended trips and never starts again.
_Avoid_: reference number, code, id

**Trip day** (Giorno del viaggio):
The calendar day, in Italy, on which the trip started; a planned trip has none. Today or a past day, never a future one. It picks the contract: the one whose validity holds that day.
_Avoid_: execution date (in texts for people), creation date (that is when it was added)

**Leading vehicle** (Mezzo):
The tractor or standalone vehicle the trip was done with. It is the vehicle a vehicle rate looks at.
_Avoid_: primary vehicle (in texts for people), truck

**Trailer** (Rimorchio):
The trailer coupled to the tractor during the trip: always named when the tractor requires one, named if coupled when it does not, never with a standalone vehicle. It never changes the amount.
_Avoid_: second vehicle

**Pickup location** / **Delivery location** (Luogo di partenza / Luogo di consegna):
Where the trip started from and where it delivered. The contract's billing level says which of the two the trip must name; that one is a location the contract has a rate for. The other is optional and may be any active location.
_Avoid_: origin, destination, address

**Measure** (Misura del viaggio):
What the trip is measured by, according to the contract's pricing mode: start and end time by hour, kilograms by weight, kilometres by distance, nothing by trip. Every trip has a start time, which alone says whether it is a night trip.
_Avoid_: parameters (in texts for people), quantity (it is one part of it)

**Repeatable trip** (Viaggio da ripetere):
What a past trip of a driver offers to do again: its customer, locations, leading vehicle and trailer, without day or measure. Two trips with the same four are the same repeatable trip. It is read from the trips, never stored.
_Avoid_: template, favourite (a favourite would be chosen and stored), route

## Relationships

- A trip is planned, then in progress, then ended; which one is read from its start and its end.
- An ended trip keeps for good what it was priced with (rate, night band, amount) and the documents its contract required; later changes of master data never change it.
- A trip names its driver, customer, contract, vehicles and locations by id; they live in **Registry**.
- A driver sees and works only on their own trips, and never a price, a rate or an amount.
- The back office reads every trip of every driver, with its amount and the calculation it was saved with, and changes none.
- Depends on **Identity** for the current user, whose session names the driver.
