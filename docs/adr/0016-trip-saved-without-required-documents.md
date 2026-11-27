# 0016 — A trip is saved without its required documents

**Context.** The PRD contradicts itself. RF-09, RF-10 and RF-10bis block the save of a trip when the delivery note, the delivery slip or the waste form its contract requires is not attached; step 10 of §7.1 says the same. RF-34 lists "trip saved without a required delivery note / slip / waste form" among the anomalies the system raises by itself, RF-36 resolves it with a later upload, and metric M-05 counts the trips saved without their required attachments.

**Decision.** RF-34 holds. A trip is saved whatever documents are attached; a required document that is missing is an anomaly for the back office, resolved by uploading it later or by accepting it with a reason. RF-09, RF-10, RF-10bis and the attachment check of §7.1 step 10 are not implemented.

**Why.** A driver records a trip in the cab, often without the paper at hand or without signal enough for a photo. Blocking the save would stop the driver's work, or push them to record the trip late or not at all, and the trip is the billing record. Decided by Michele on 2026-11-21: "non possiamo limitare l'operatività di un autista o creargli dei problemi durante il suo lavoro".

**Consequences.** Registering a trip and attaching its documents are separate use cases: a trip exists before its attachments. A trip keeps the documents its contract required when it was registered, so the anomaly does not depend on how the contract is corrected later (decided by Michele on 2026-11-23).

**Considered options.** Blocking the save (RF-09, RF-10, RF-10bis); blocking it with a way for the driver to force it.
