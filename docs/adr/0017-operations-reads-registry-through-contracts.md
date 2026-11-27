# 0017 — Operations reads the Registry through one interface in Registry.Contracts

**Context.** The driver app registers trips, which belong to Operations, but a trip is made of Registry facts: the driver's name, the active vehicles, the customers with a contract in force on a day, the locations, what a contract asks and, in the end, the price. Operations may not reference the Registry project, and the Registry must not gain endpoints a Driver can call: its sweep test proves a Driver gets 403 everywhere.

**Decision.** The driver app talks to Operations only: every endpoint of the driver app is `/api/operations/driver/...`, with the policy `Driver`. Operations reads the Registry through one interface, `ITripRegistry`, in `Registry.Contracts`, implemented inside the Registry. Its types are made of `Guid`, `DateOnly`, strings and enums of their own, and carry no term of a contract: Operations is told what to ask (which locations, which measure), never the billing level, the pricing mode, a rate or a price.

**Why.** A module boundary is a contract, so the interface is built before its first caller, unlike a shared loader, which ADR 0012 extracts from working slices. One interface keeps what Operations may know of the Registry in one place, and keeps prices out of every response a driver can reach.

**Consequences.** Operations references `Registry.Contracts` and `Identity.Contracts`, never `Registry`. The choices a driver is offered, the checks of a trip's answers and its pricing are Registry code behind `ITripRegistry`; Operations keeps what was priced. A new read of the Registry for a trip is a new member of the interface, not a new reference.

**Considered options.**
- Operations references the Registry project: breaks the module boundary and exposes Registry types, with their contract terms, to Operations.
- Driver endpoints in the Registry: the Registry would answer to a Driver, against its sweep test and the rule that a module's endpoints serve its own area.
- Operations keeps its own copy of vehicles, customers and locations: two sources of truth for master data.
