# Setup Guide — Priamus Trips

Step-by-step guide to create the project from scratch.

---

## Prerequisites check

Before doing anything else, verify your machine has the required tooling. Run these commands; if any of them fails or returns the wrong version, stop and install first.

```bash
dotnet --version
# Expected: 10.x. If you see 8.x or 9.x, install .NET 10 SDK from https://dotnet.microsoft.com/download

docker --version
# Any recent version is fine. Needed for Testcontainers in integration tests.

git --version
# For version control.

claude --version
# Or your equivalent Claude Code invocation. Install from https://docs.claude.com/claude-code if missing.

dotnet tool list --global | grep dotnet-ef
# Should show dotnet-ef 10.x. If missing:
# dotnet tool install --global dotnet-ef --version 10.*
```

---

## Step 1 — Create the project directory

```bash
mkdir priamus-trips
cd priamus-trips
git init
```

---

## Step 2 — Copy the pre-built files

Extract the content of this package into the new directory. You should end up with:

```
priamus-trips/
├── .claude/agents/                    (6 agent .md files)
├── docs/
│   ├── context.md                     (system overview, universal rules, backend technical context)
│   ├── glossary.md                    (Italian PRD terms → English code identifiers)
│   └── appendices/                    (9 .md files loaded on demand)
└── README.md
```

Verify the structure:

```bash
find . -name "*.md" | sort
```

You should see 17 markdown files total (1 README + 1 SETUP + 1 backend context + 1 glossary + 9 appendices + 6 agents).

---

## Step 3 — Bootstrap the .NET configuration files at the root

These are boilerplate that need to be at the root of the repository.

```bash
# .gitignore using the official .NET template
dotnet new gitignore

# global.json pinning the SDK version
cat > global.json << 'EOF'
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
EOF
```

Adjust `10.0.100` to the exact patch version reported by `dotnet --version`.

---

## Step 4 — Initial commit (important)

Before invoking any agent, commit this clean starting state. If anything goes wrong, you can return here.

```bash
git add .
git commit -m "Initial repository setup with documentation and agents"
```

---

## Step 5 — Start Claude Code

```bash
claude
```

Verify the agents are recognized:

```
/agents
```

You should see six agents listed:
- `domain-modeler`
- `persistence-engineer`
- `feature-implementer`
- `test-writer`
- `architecture-guardian`
- `pricing-engine-specialist`

If they don't appear, check that `.claude/agents/` is in the directory where you launched Claude Code.

---

## Step 6 — Diagnostic dry run (RECOMMENDED)

Before lanching a real task, do a "dry run" to verify the agents understand the new directory structure. Paste this prompt into Claude Code:

> Use the domain-modeler agent. Without writing any files yet, do the following:
>
> 1. Read `docs/context.md`, `docs/glossary.md`, and `docs/appendices/domain-modeling.md`.
> 2. Confirm the file paths you read are correct (no 404s).
> 3. Describe to me:
>    - The expected backend solution structure at the repository root
>    - The types that should live in `Priamus.Trips.SharedKernel`
>    - Which types depend on which (build order)
>    - Which NuGet packages will be needed for the SharedKernel
> 4. Do NOT write any code yet.

This validates:
- Agents load correctly
- The documentation path structure (`docs/context.md`, `docs/appendices/...`) is found
- The model understands the plan before writing files

Review the output. If something looks wrong, tell Claude what to adjust BEFORE proceeding.

---

## Step 7 — First real task: SharedKernel + solution skeleton

After the dry run looks good, give the actual task:

> Use the domain-modeler agent to:
>
> 1. At the repository root, initialize the .NET solution structure:
>    - `Priamus.Trips.slnx`
>    - `Directory.Build.props` with the project-wide settings from `docs/context.md`
>    - `Directory.Packages.props` with central package management
>    - `src/Priamus.Trips.Host/` (`webapi` template, .NET 10)
>    - `src/Priamus.Trips.SharedKernel/` (classlib)
>    - The four modules: `src/Modules/{Identity,Registry,Operations,BackOffice}/` each with their implementation project and Contracts project
>    - The test projects: `tests/Priamus.Trips.{Module}.{UnitTests,IntegrationTests}/`
>
> 2. Configure all `ProjectReference` entries according to the rules in `docs/context.md`:
>    - Host references all module implementations + SharedKernel
>    - Each module references its own Contracts + SharedKernel + other modules' Contracts
>    - Each Contracts project references only SharedKernel
>    - SharedKernel references nothing internal
>
> 3. Implement the full content of `Priamus.Trips.SharedKernel`:
>    - `Money` (readonly record struct, EUR currency, decimal arithmetic, comparison operators)
>    - `Tons`, `Kilometers`, `Hours` value objects
>    - `IClock` and `SystemClock` (the only place where `DateTime.UtcNow` is allowed)
>    - `AggregateRoot` base class with `IReadOnlyList<IDomainEvent> DomainEvents`, `RaiseEvent`, `ClearDomainEvents`, `Version` (concurrency token)
>    - `IDomainEvent` marker interface
>    - `DomainException` (constructed from an `Error`, preserves `ErrorCode`)
>    - `Error`, `ErrorType` (enum), `Result`, `Result<T>` (struct readonly with `Match`, `Map`, `Bind`, implicit conversions)
>    - `ResultHttpExtensions.ToProblemDetails(...)` (mapping via `ErrorType`, never via `Error.Code`)
>    - `PagedRequest`, `PagedResponse<T>`, `SortDirection` enum
>    - `ICurrentUser` interface
>
> 4. Verify the solution builds: `dotnet restore && dotnet build`
>
> 5. Summarize: files created, NuGet packages added with version, and confirm `dotnet build` succeeded.

This is a substantial task. Expect it to take several minutes and several rounds of file creation.

---

## Step 8 — Architecture audit

After the agent finishes:

> Use the architecture-guardian agent to audit the backend code created so far.

The guardian will run its checks. Review the report. If there are BLOCKERs, they need fixing before proceeding.

If the audit produces false positives (the guardian flags something that's actually correct), update the appropriate rule in `docs/appendices/audit-rules.md` and commit the change. The audit rules are themselves living documentation.

---

## Step 9 — Local build verification

Outside Claude Code, from the repository root:

```bash
dotnet restore
dotnet build
```

If build errors appear:
- If they are C# 14 / .NET 10 syntax issues that the agent didn't anticipate, share them with Claude Code and ask the agent to fix
- If they're missing NuGet packages or wrong versions, do the same
- If they're conceptual (e.g., wrong namespace), share them and let Claude correct

A clean build is the success criterion of this milestone.

---

## Step 10 — Commit the milestone

If build + audit pass:

```bash
git add .
git commit -m "Add SharedKernel and solution structure"
```

---

## What to expect (honest reality check)

This will not go perfectly on the first run. Things that may need iteration:

1. **Package versions**: NuGet package versions for .NET 10 LTS are still settling. The agent will propose versions; if `restore` complains, adjust.
2. **C# 14 features**: Some features (like the `field` keyword) may require `<LangVersion>preview</LangVersion>` even on .NET 10. The build error will say so.
3. **`Microsoft.Extensions.Validation`**: The API surface may have small differences from what I documented. The compiler will tell us.
4. **EF Core 10 + Npgsql 10 compatibility**: They should ship together but version pinning may need adjustment.

These are normal first-setup frictions. Each one resolved gets folded back into the appendices, making future tasks smoother.

---

## What NOT to do at this stage

- **Do not skip the architecture audit** after each task. It's the cheapest way to catch drift early.
- **Do not run multiple agents in parallel**. One agent at a time, sequential sessions, until you trust the setup.
- **Do not start any frontend work in this repository**. The frontend lives in a separate repository and is consumed via the OpenAPI contract.
- **Do not "fix" agent output by editing prompts inline** during a session. If an agent makes a systematic error, update its `.md` file in `.claude/agents/` between sessions.
- **Do not skip the dry run (Step 6)**. The 2 minutes saved by skipping it can become hours wasted later.

---

## Where to go next (after SharedKernel works)

Suggested order of subsequent tasks:

1. **Identity module skeleton**: a minimal user/role model + JWT issuer. Without it, no endpoint can require authorization meaningfully.
2. **Registry module — Vehicle aggregate**: simpler than Trip, lets us validate the modeling approach (sealed types, value objects, persistence).
3. **Registry module — Customer + Contract + Rate**: more complex, introduces the rate matrix concepts.
4. **Pricing engine**: critical, must be done before Trip.
5. **Operations module — Trip aggregate**: the heart of the system, depending on most of the above.
6. **Operations — DailySummary, attachments, issues**.
7. **BackOffice — reporting and exports**.
8. **Frontend kickoff** (in the separate frontend repository, once the backend OpenAPI contract is stable).

Each milestone gets its own ticket / branch / commit, with architecture audit at the end.

Good luck. When something is unclear, ask before assuming.
