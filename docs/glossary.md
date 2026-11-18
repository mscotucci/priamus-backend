# Glossary: Domain Terms (Italian PRD → English code)

Authoritative translation table for the Priamus Trips project. Use these terms exactly across backend, frontend, database, and documentation. If a term is missing, STOP and ask before translating; do not invent.

This file lives in the backend repository and is the source of truth for the project. The frontend repository should consume the same translations (mirror this file or import it) to keep the layers aligned — mismatched terms between backend and frontend are a major source of integration bugs.

## Aggregates and entities

| Italian | English |
|---|---|
| Viaggio | Trip |
| Autista | Driver |
| Cliente | Customer |
| Contratto | Contract |
| Tariffa | Rate |
| Mezzo | Vehicle |
| Motrice | Tractor |
| Rimorchio | Trailer |
| Autonomo (mezzo) | StandaloneVehicle |
| Luogo | Location |
| Allegato | Attachment |
| Anomalia | Issue |
| Riepilogo fine giornata | DailySummary |

## Domain concepts

| Italian | English |
|---|---|
| Composizione mezzo | VehicleComposition |
| Tratta | Route |
| Periodo viaggio | TripPeriod |
| Modalità di pagamento | PricingMode |
| Fascia oraria | TimeBand |
| Diurna | Daytime |
| Notturna | Nighttime |
| Chiamata notturna | NightCallFee |
| Minimo fatturabile | MinimumCharge |
| Scaglione con eccesso | TieredOverage |
| Regola minimo | MinimumRule |
| Snapshot tariffa | RateSnapshot |
| Matrice tariffaria | RateMatrix |
| Override mezzo | VehicleOverride |
| Importo calcolato | CalculatedAmount |
| Motore di calcolo | PricingEngine |
| Configurazione fasce | TimeBandConfiguration |
| Numero di riferimento viaggio | TripReferenceNumber |
| Contesto registrazione viaggio | TripRegistrationContext |
| Finestra di edit | EditWindow |
| Giornata operativa | OperatingDay |
| Cut-off riepilogo | SummaryCutoff |
| Livello di fatturazione | BillingLevel |
| Maggiorazione notturna | NightSurcharge |
| Validità (contratto) | Validity |
| Stato contratto: in vigore / futuro / scaduto | ContractStatus (`Current`, `Upcoming`, `Expired`) |

## Vehicles

| Italian | English |
|---|---|
| Targa | LicensePlate |
| Descrizione / modello (mezzo) | Description |
| Tipologia mezzo | VehicleType (`Tractor`, `Trailer`, `StandaloneVehicle`) |
| Richiede rimorchio | RequiresTrailer |
| Stato mezzo: attivo / dismesso | VehicleStatus (`Active`, `Decommissioned`) |
| Dismissione / dismettere | Decommission |
| Riattivazione / riattivare | Reactivate |

## Pricing modes (sealed type variants)

| Italian | English |
|---|---|
| A ore | ByHour |
| A peso | ByWeight |
| A km | ByDistance |
| A viaggio | ByTrip |

## Billing levels (sealed type variants)

| Italian | English |
|---|---|
| Forfettario | FlatRate |
| Per luogo di consegna | PerDeliveryLocation |
| Per luogo di partenza | PerPickupLocation |
| Per coppia di luoghi | PerLocationPair |

## Documents

| Italian | English |
|---|---|
| DDT (Documento di Trasporto) | DeliveryNote |
| Bolla | DeliverySlip |
| Formulario rifiuti (FIR) | WasteForm |

## Quantities

| Italian | English |
|---|---|
| Tonnellate | Tons |
| Chilometri | Kilometers |
| Ore | Hours |

## Roles and modules

| Italian | English |
|---|---|
| Segreteria | BackOffice |
| Anagrafiche (modulo) | Registry |
| Operativo (modulo) | Operations |

## Naming convention for new terms

If you encounter an Italian domain term not in this table:
1. STOP. Do not translate.
2. Ask the user for the canonical translation.
3. After the decision, add the term to this table before writing the code.
