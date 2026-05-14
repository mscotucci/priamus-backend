# 0011 — Unit of work: the handler is the commit point

## Status

Accepted (2026-05)

## Context

During the Identity authentication slices milestone, two design questions arose in rapid succession and exposed the same underlying issue from different angles.

First, `docs/appendices/slices-and-handlers.md` referenced a `TransactionFilter` type that did not exist. A first attempt at remediation proposed placing such a filter in `SharedKernel.Http`. A second attempt narrowed it to module-local (an `IdentityTransactionFilter`). Both attempts shared the same premise: that a centralized component above the handler should orchestrate the database transaction and the call to `SaveChangesAsync`.

Second, the Refresh handler needed to catch `DbUpdateConcurrencyException` and map it to a domain error (`RefreshTokenErrors.ConcurrentRefresh`). With a filter that called `SaveChangesAsync` on behalf of the handler, the exception would emerge from the filter, not the handler — moving exception mapping above the handler and forcing the filter to know module-specific error vocabulary. Awkward.

The underlying premise — that a transactional middleware is needed — was never examined. `SaveChangesAsync` in EF Core, called without an enclosing explicit transaction, opens its own implicit transaction, executes the accumulated state changes, and commits or rolls back atomically based on SQL success. For a handler that calls `SaveChangesAsync` exactly once, the implicit transaction is sufficient. Every authentication handler in the milestone has this shape. The transactional filter was protecting nothing the implicit transaction wasn't already protecting.

Once the premise was named, the conclusion followed: the filter is not needed, and the lifecycle of a request — including its commit — belongs to the handler.

## Decision

**The handler is the single commit point for the operation it executes. There is no transactional middleware, no Unit-of-Work abstraction, no filter that calls `SaveChangesAsync` on the handler's behalf.**

Concretely:

- Handlers invoke `await dbContext.SaveChangesAsync(ct)` explicitly at the point in the handler body where persistence is required.
- For the typical handler — a single logical operation that produces one `SaveChangesAsync` call — no explicit transaction is opened. EF Core's implicit transaction around that single call provides atomicity.
- Handlers that genuinely require atomicity across multiple `SaveChangesAsync` calls, or atomicity between EF operations and raw SQL, open an explicit transaction locally:

    ```csharp
    await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
    // ... operations ...
    await tx.CommitAsync(ct);
    ```

    This pattern is acceptable when needed. It is local to the handler that needs it. It is never centralized.

- Mapping infrastructure exceptions to domain errors (e.g. `DbUpdateConcurrencyException → Result.Failure(...)`) is done by the handler via a try/catch scoped narrowly around the `SaveChangesAsync` call that can produce them.

### Conventions for the `Result.Failure` path

- An handler that returns `Result.Failure` and intends no persistence simply does not call `SaveChangesAsync`. The pending changes in the change tracker are discarded when the request scope ends.
- An handler that returns `Result.Failure` but intends a deliberate side effect (typically an audit row — e.g. recording `LoginFailed` before returning `User.InvalidCredentials`) calls `SaveChangesAsync` explicitly before the `Failure` return. The side effect persists.
- An handler that returns `Result.Success` calls `SaveChangesAsync` as the final persistence action before constructing the response.

### Underlying principle

Do not abstract **operational** behavior (commit, transactional scoping, infrastructure-exception mapping) above the handler unless every slice needs the same behavior in the same way. Slices vary in what they persist, when, and how they handle failures: some read-only, some single-commit, some multi-step with explicit transactions, some with deliberate side effects on the failure path. Centralizing operational behavior either forces uniformity that doesn't exist, or hides the variation behind a parameterized abstraction whose configuration burden equals or exceeds the original duplication.

This is the same reasoning that drives ADR 0009 ("no repository pattern") applied one level up: the vertical slice owns the full lifecycle of its operation, from request to persistence to response.

**Scope of the principle.** It applies to operational concerns of request handling. It does **not** prescribe how the domain layer organizes itself — aggregates, value objects, domain services, and module-internal abstractions remain legitimate and necessary. The principle constrains _the path between an endpoint and its persistence_, not the structure of the domain it serves.

## Consequences

**Positive:**

- The handler has full visibility and control over the commit. Exception-to-error mapping happens where the semantics are known (the handler knows what `DbUpdateConcurrencyException` means for _its_ operation, in a way no generic filter could).
- The codebase has one fewer layer of indirection. Reading an endpoint route to its handler tells the full story; there is no hidden middleware altering behavior at request boundaries.
- The "Failure with side effect" pattern (audit row on failed login, etc.) becomes natural: the handler calls `SaveChangesAsync` when it decides to persist, full stop. No special accommodation in any framework code.
- Unit tests against handlers exercise the real persistence flow including `SaveChangesAsync`-derived exceptions, with no need to simulate or mock a transactional wrapper.

**Negative:**

- Handlers must remember to call `SaveChangesAsync`. A handler that mutates state and forgets the call will silently lose those mutations. Mitigation: handler-level tests with a real DbContext (Testcontainers, per `appendices/testing.md`) catch this immediately — the post-condition assertion on persisted state fails.
- The convention "operational behavior stays in the handler" must be defended in code review. New contributors familiar with classical n-tier patterns will likely propose `UnitOfWork`, `TransactionFilter`, `CommandPipeline`, or similar. This ADR is the reference to cite when redirecting those proposals.

**Neutral:**

- Handlers that need multi-step atomicity write three lines of explicit transaction code locally. This is more verbose than a centralized filter that wraps everything, but the verbosity is opt-in and appears only where genuinely needed. The default case (single `SaveChangesAsync`) is one line.

## Alternatives considered

**Transactional filter in `SharedKernel.Http` resolving `DbContext` generically.** Rejected. The filter would either resolve any `DbContext` from scope (fragile and type-unsafe) or be parameterized by `DbContext` type at the registration site (the same as module-local, dressed differently). Neither variant addresses a real need.

**Transactional filter module-local (`IdentityTransactionFilter` and equivalents per module).** Rejected. The filter's only behavior in the typical case is to call `SaveChangesAsync` after the handler returns — a single line of code moved one level up, in exchange for moving exception mapping out of the handler. The trade is unfavorable: the handler loses control over the commit point and the ability to map persistence exceptions in context, while the filter gains responsibility for behavior that should be slice-specific.

**Unit-of-Work interface (`IUnitOfWork`) implemented by each module's `DbContext`.** Rejected. The abstraction would exist to enable a centralized commit, which this ADR concludes is not needed. Introducing an interface to solve a problem we don't have, in service of an architecture we don't want, is the speculation pattern ADR 0009 already rejected at the persistence layer.

**Closed-door policy on future reintroduction.** This ADR closes the question of transactional middleware definitively for the current architecture. Reintroducing one would require a successor ADR that supersedes this one, justified by concrete and recurring evidence — not by speculation about scalability or "best practices."

## Implementation notes

- `appendices/slices-and-handlers.md` is updated alongside this ADR: every reference to `TransactionFilter` is removed, and the handler-as-commit-point convention with its two failure-path sub-cases is documented in its place. ADR 0011 is cited for the rationale.
- No code is required to enforce this decision. The absence of a filter is itself the implementation. Handler unit tests verify the convention empirically.
- When a handler genuinely requires multi-`SaveChangesAsync` atomicity, the explicit-transaction snippet in the Decision section is the canonical form. Architecture-guardian audits should flag the absence of explicit transactions only when correctness depends on multi-step atomicity — never as a default requirement.
