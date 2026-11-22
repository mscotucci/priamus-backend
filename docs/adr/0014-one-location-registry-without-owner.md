# 0014 — One location registry, without an owner customer

**Context.** PRD §5.1 describes a location as "associated with a customer or general", and RF-23 asks for the list of locations of each customer whose billing level needs them. Real places (a landfill, a plant, a warehouse) often serve several customers.

**Decision.** Locations are one registry for the whole company. A location belongs to no customer and stores no list of customers. The locations a customer's trips use are those its contract has rates for (per-location billing levels); a flat-rate customer's trips may name any active location.

**Why.** An owner per location would duplicate a shared place once per customer, and an explicit customer list would be state to keep in step with the rates, which already say which locations a per-location contract can price. A location with no rate cannot be priced for that customer anyway. Decided by Michele on 2026-11-21.

**Considered options.** A location owned by one customer or general (the PRD's reading); a location with an editable list of associated customers.
