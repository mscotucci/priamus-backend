# Appendix: Architectural Invariants

Some architectural rules cannot be enforced by C# alone and must be codified at the build-system level. This appendix lists MSBuild-level invariants embedded directly in project files, the error each one emits, and a pointer to the rationale documented elsewhere.

## SharedKernel framework-agnostic guard

**Where it lives**: `backend/src/Priamus.Trips.SharedKernel/Priamus.Trips.SharedKernel.csproj`

**What it does**: A `<Target Name="ValidateNoAspNetCoreReference" BeforeTargets="Build">` fails the build if any `FrameworkReference` to `Microsoft.AspNetCore.App` (or its `.Ref` variant) is added to `Priamus.Trips.SharedKernel`.

**Error emitted**:

> `SharedKernel must remain framework-agnostic. Do NOT add FrameworkReference to Microsoft.AspNetCore.App. HTTP bridge code belongs in Priamus.Trips.SharedKernel.Http.`

A matching message is emitted for the `Microsoft.AspNetCore.App.Ref` identity.

**Rationale**: The split between `Priamus.Trips.SharedKernel` (framework-agnostic primitives) and `Priamus.Trips.SharedKernel.Http` (the ASP.NET Core bridge) exists so that non-web consumers — workers, batch jobs, future CLIs — can use `Result`, `Error`, `Money`, and the rest of the kernel without taking ASP.NET Core as a transitive dependency. See `docs/backend/appendices/errors-and-results.md` for the full reasoning behind the HTTP mapping split and the `ResultHttpExtensions` placement.

A developer who accidentally adds `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to SharedKernel will see the build fail with an actionable message pointing to the correct project, instead of silently leaking the web framework into every consumer of SharedKernel.

## Future invariants

Additional MSBuild-level invariants — for example, cross-module reference whitelists, banned package detection, or per-project analyzer requirements — will be documented in this appendix as they are introduced.
