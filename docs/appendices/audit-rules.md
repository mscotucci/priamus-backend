# Appendix: Architecture Audit Rules

Detection rules for architecture conformity reviews. Each rule has a severity (BLOCKER / MAJOR / MINOR) and a detection method (typically a grep pattern).

## BLOCKER rules

### B1 — Banned libraries
```bash
grep -rE "using (MediatR|FluentValidation|AutoMapper|MassTransit)" src/
```

### B2 — Italian in production code
```bash
grep -rnE "\b(Viaggio|Autista|Cliente|Contratto|Tariffa|Mezzo|Motrice|Rimorchio|Anagrafica|Anomalia|Riepilogo|Allegato|Fattura|Segreteria)\b" src/ --include="*.cs"
```
Allowed exceptions: files under `Resources/it-IT.resx`.

### B3 — `DateTime.UtcNow` outside SystemClock
```bash
grep -rn "DateTime\.UtcNow\|DateTime\.Now\|DateTimeOffset\.Now" src/ --include="*.cs" | grep -v "SystemClock\.cs"
```

### B4 — Cross-module reference to implementation
Read each `.csproj` under `src/Modules/{X}/Priamus.Trips.{X}/`. Allowed `ProjectReference`:
- `Priamus.Trips.SharedKernel`
- `Priamus.Trips.{X}.Contracts` (own)
- `Priamus.Trips.{Y}.Contracts` (others)

FORBIDDEN: `Priamus.Trips.{Y}` (implementation of another module), `Priamus.Trips.Host`.

### B5 — Data annotations on domain entities
```bash
grep -rnE "\[Required\]|\[Key\]|\[MaxLength|\[StringLength|\[Range|\[ForeignKey|\[Column|\[Table" \
    src/Modules/*/Priamus.Trips.*/Domain/ --include="*.cs"
```

### B6 — Endpoint without authorization

**Severity**: BLOCKER

**Scope**: any `.cs` file under `src/`. This catches both `*Endpoint.cs` files and host bootstrap files such as `Program.cs` (which historically hosted placeholder `app.MapGet("/", ...)` calls that escaped narrower scans).

**Rationale**: enforces the universal authorization rule from `docs/context.md §6` — every HTTP route must declare an explicit authorization stance (policy-protected or explicitly anonymous).

**Detection recipe**:
```bash
# B6 — Every Map{Verb} call must be followed (within 5 lines) by an authorization directive.
grep -rnE '\.Map(Get|Post|Put|Delete|Patch)\(' src/ --include="*.cs" | while read -r match; do
    file=$(echo "$match" | cut -d: -f1)
    line=$(echo "$match" | cut -d: -f2)
    # Inspect the chained call within next 5 lines: must include RequireAuthorization or AllowAnonymous
    if ! sed -n "${line},$((line + 5))p" "$file" | grep -qE 'RequireAuthorization|AllowAnonymous'; then
        echo "B6 violation: $file:$line — Map call without authorization directive"
    fi
done
```

**False-positive handling**:
- `.AllowAnonymous()` is treated as an explicit, documented exemption (the developer has deliberately chosen anonymous access). Auditors should still flag it in the report as `[NEEDS HUMAN REVIEW]` when it lives on a non-health endpoint.
- The 5-line lookahead is a heuristic. If a project formats endpoint chains across more than 5 lines (e.g., long `RouteGroupBuilder` chains with one directive per line), expand the window accordingly before declaring a violation.

**Suggested fix**: add `.RequireAuthorization("PolicyName")` to the Map call. If anonymous access is truly intended, add `.AllowAnonymous()` together with a comment explaining why (e.g., `// health probe — must be reachable without auth`).


### B7 — Handler throws for business errors
In `*Handler.cs` files, find `throw new` of types other than:
- `InvalidOperationException` (truly impossible states / bugs)
- `ArgumentNullException` (guard clauses on DI dependencies)
- Domain exceptions inside try/catch that convert to Result

Business errors must be returned as `Result<T>`. Flag each occurrence for manual review.

### B8 — `double` or `float` for money
```bash
grep -rnE "\b(double|float)\b" src/Modules/ --include="*.cs"
```
Verify context: if it's in a domain file or near `Money`/`Amount`/`Price`/`Rate`, BLOCKER.

### B9 — Foreign keys cross-schema in migrations
In migration files, find `ForeignKey` constraints. Check `principalSchema` vs `schema`. If they differ, BLOCKER.

### B10 — Italian DB identifiers
In migrations and configurations, find table/column names with Italian patterns: `viaggi`, `autisti`, `clienti`, `mezzi`, `tariffe`.

### B11 — Lazy loading proxies
```bash
grep -rn "UseLazyLoadingProxies\|UseLazyLoading" src/ --include="*.cs"
```

### B12 — Sync over async
```bash
grep -rnE "\.Result;|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)" src/ tests/ --include="*.cs"
```

### B13 — `Microsoft.EntityFrameworkCore.InMemory`
```bash
grep -rn "EntityFrameworkCore\.InMemory" src/ tests/
```

### B14 — Module errors in SharedKernel
```bash
grep -rnE "Error\.(Domain|NotFound|Conflict|Validation|Forbidden|Unauthorized|Unexpected)\(\"(Vehicle|Trip|Customer|Contract|Auth|Driver|Issue|Attachment|DailySummary|Pricing)\." \
    src/Priamus.Trips.SharedKernel/
```
Any match: errors must move to a `*Errors` class in the module that owns them. Allowed in SharedKernel: only `Error.None`.

### B15 — HTTP mapping by Error.Code instead of Error.Type
```bash
grep -rnE "error\.Code (==|switch)" src/Priamus.Trips.SharedKernel/
```
Mapping must use `error.Type` (ErrorType enum).

### B16 — Dynamic OrderBy without whitelist
```bash
grep -rnE "\.OrderBy\(.*EF\.Property|\.OrderBy\(request\.SortColumn|\.OrderBy\(.*\.SortColumn\)" src/ --include="*.cs"
```
For each match, verify whitelist switch is used. If string flows directly into `OrderBy(string)`, BLOCKER.

### B17 — `Error.None` returned as a "valid" failure
```bash
grep -rnE "Failure\(Error\.None\)|return Error\.None\b" src/ --include="*.cs"
```
Each match (outside `Error.cs`): BLOCKER.

## MAJOR rules

### M1 — Missing PRD reference on domain invariants
In `Domain/` files, every `throw new DomainException` should have a comment within 5 lines above containing `PRD §` or `RF-`.

### M2 — Missing `AsNoTracking()` on queries
In query handlers, LINQ chains ending in `.ToListAsync()`, `.FirstOrDefaultAsync()`, `.CountAsync()` should start with or include `.AsNoTracking()`.

### M3 — Missing `CancellationToken` propagation
Async methods that don't receive or don't propagate `CancellationToken`. Every handler `HandleAsync` MUST receive it; every `await` of async DB/IO MUST pass it.

### M4 — Null-forgiving without justification
```bash
grep -rnE "[a-zA-Z_]\!\.[a-zA-Z_]|[a-zA-Z_]\!\;" src/ --include="*.cs"
```
For each, verify a justifying comment within 2 lines.

### M5 — `catch (Exception)` outside allowed locations
`catch (Exception` in files NOT under `Middleware/`, `*Filter.cs`, or `OutboxProcessor.cs`.

### M6 — Missing concurrency token on aggregate root
For every class extending `AggregateRoot`, verify the corresponding `*Configuration.cs` has `.IsConcurrencyToken()`.

### M7 — `EnsureCreated()` usage
```bash
grep -rn "EnsureCreated" src/ tests/ --include="*.cs"
```

### M8 — Missing OpenAPI metadata
Every endpoint registration in `*Endpoint.cs` must declare `.WithName(...)`, `.WithTags(...)`, `.Produces<...>(...)` or `.ProducesProblem(...)`.

### M9 — Domain type mocked in tests
In test files, `Substitute.For<{DomainType}>` where `DomainType` is an aggregate root, value object, or sealed type.

### M10 — `#nullable disable` per file
```bash
grep -rn "#nullable disable" src/ --include="*.cs"
```
Each occurrence MAJOR unless justified within 2 lines.

### M11 — `new Error(...)` direct construction
```bash
grep -rnE "new Error\(" src/ --include="*.cs" | grep -v "Error\.cs"
```
Use factory methods (`Error.Domain(...)`, etc.).

### M12 — Custom pagination envelope
```bash
grep -rnE "public (sealed )?record [A-Za-z]+Paged[A-Za-z]*<|public (sealed )?class [A-Za-z]+Paged" src/Modules/ --include="*.cs"
```
For each match, verify it's NOT a duplicate of `PagedResponse<T>` from SharedKernel.

### M13 — Custom `PagedRequest`-like type with non-validating setters
Custom paging requests must validate Page/PageSize in setters.

### M14 — `ProducesProblem` inconsistent with handler returns
Endpoints declaring `.ProducesProblem(...)` codes that don't match the `ErrorType → HTTP` mapping. Flag for manual review.

## MINOR rules

### Mi1 — Test name not following convention
`[Fact]` and `[Theory]` methods whose names don't follow `Method_Scenario_ExpectedResult`.

### Mi2 — DTO not declared as `sealed record`
Command DTOs in `*Request.cs` and `*Response.cs` should be `public sealed record`. Exception: query request DTOs extending `PagedRequest` should be `public sealed class` (model binding requirement).

### Mi3 — Explicit `Result.Success/Failure` when implicit conversion is cleaner
```bash
grep -rnE "Result<.*>\.Success\(|Result<.*>\.Failure\(" src/Modules/ --include="*.cs"
```
Style suggestion.

### Mi4 — `if (result.IsSuccess)` instead of `Match`
Endpoint files using if/else on result instead of `result.Match(...)`. Style suggestion.

### Mi5 — Test missing AAA markers
Tests without `// Arrange`, `// Act`, `// Assert` comments.

## ErrorType → HTTP reference

For M14:

| ErrorType | HTTP |
|---|---|
| `Validation` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `Domain` | 422 |
| `Unexpected` | 500 |

## False positive handling

Before flagging:
1. Is the match inside a string literal? (e.g. `"DateTime.UtcNow"` in an error message)
2. Is the match inside a comment that documents the rule?
3. Is the match in a test that verifies the rule?
4. Is the file under an exception path?

If unsure, use `[NEEDS HUMAN REVIEW]` severity.

## Output format

```markdown
# Architecture Audit Report

**Date**: {ISO}
**Scope**: {paths}
**Result**: {PASS | FAIL}

## Summary
- Total: {N}
- BLOCKERS: {N}
- MAJOR: {N}
- MINOR: {N}

## Violations

### BLOCKER #1: {title}
**Rule**: {rule code and name}
**File**: {path}
**Line(s)**: {numbers}
**Found**:
{code snippet, max 10 lines}
**Why**: {explanation}
**Suggested fix**: {what + which agent}

---

### MAJOR #1: ...
```

PASS only if zero BLOCKERs. FAIL otherwise.
