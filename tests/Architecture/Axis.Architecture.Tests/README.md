# Axis Architecture Fitness Tests

> [← CLAUDE.md](../../../CLAUDE.md) · [← docs/TECH_STACK.md](../../../docs/TECH_STACK.md) · [← docs/WORKAROUNDS.md](../../../docs/WORKAROUNDS.md)

Tests in this project run under `dotnet test` like any other and enforce **CLAUDE.md P0/P1 architectural rules** as automated checks. When one of these tests fails, an agent (human or AI) has introduced a regression that the design forbids — fix the code, not the test.

## What this catches

### Structural rules

| Test file | CLAUDE.md rule enforced |
|---|---|
| [DomainPurityTests](./DomainPurityTests.cs) | P0 — "Domain: zero external dependencies." Bans EF, MediatR, Wolverine, ASP.NET, etc. inside any `*.Domain` assembly. |
| [ModuleBoundaryTests](./ModuleBoundaryTests.cs) | P0 — "No project reference from `Axis.{ModuleA}.*` to `Axis.{ModuleB}.*` except `Contracts`." Theory runs every (A,B,layer) tuple. Pre-existing violations are tracked in `KnownBoundaryWorkarounds` and must also appear in [`docs/WORKAROUNDS.md`](../../../docs/WORKAROUNDS.md). |
| [GatewayBoundaryTests](./GatewayBoundaryTests.cs) | P0 — `Axis.Api.Endpoints` must not depend on another module's `Application.Repositories`; `Axis.Api.Infrastructure` must not depend on another module's `Application.Repositories` or `Application.Services`. |
| [SharedKernelTests](./SharedKernelTests.cs) | P0 (ADR-017) — `Axis.Shared.{Domain,Application}` are abstractions only; no EF/Wolverine/Npgsql. (MediatR is allowed in `Shared.Application` because the project-wide `ICommand`/`IQueryHandler` adapters live there.) Also future-proofs against a re-introduced shared `UnitOfWorkBase`. |
| [MediatorScopeTests](./MediatorScopeTests.cs) | P0 — "MediatR is intra-module only." Marker test; the actual enforcement is in `ModuleBoundaryTests`. |

### Convention rules

These rules don't enforce a P0/P1 from CLAUDE.md directly — they encode the implementation conventions in `docs/playbooks/patterns.md` so a new agent writing a handler/repo/aggregate is forced into the same shape as existing code.

| Test file | Convention enforced |
|---|---|
| [HandlerConventionTests](./HandlerConventionTests.cs) | Every MediatR handler accepts `CancellationToken` as the last `Handle` parameter and is `sealed`. |
| [RepositoryConventionTests](./RepositoryConventionTests.cs) | Repository public methods don't return `IQueryable<>` (must materialize); repositories don't expose `SaveChanges*`/`Commit*` (that's `IUnitOfWork`'s job). |
| [AggregateConventionTests](./AggregateConventionTests.cs) | Aggregate roots have no public mutable setters (init-only allowed for EF) and no public parameterless ctor — forces factory + behavior-method pattern. |
| [EndpointConventionTests](./EndpointConventionTests.cs) | Static classes hosting `Map*` extension methods are named `*Endpoints`. Authorization presence is enforced separately by [`EndpointAuthorizationTests`](../../Api/Axis.Api.Tests/Architecture/EndpointAuthorizationTests.cs), which walks runtime endpoint metadata. |

## What this does NOT catch

These tests work at the **assembly reference / namespace** level. They cannot detect:
- Raw SQL strings that cross module boundaries (use `python scripts/axis.py check doc-drift` grep instead).
- Runtime DI patterns (e.g. `services.GetRequiredService<IFromAnotherModule>()` where the interface lives in `Contracts`).
- Conceptual violations like "this domain event should have been an integration event".
- Quality of names, comments, or test coverage.

Use these for **structural** rules. Use code review for **semantic** rules.

## Adding a new module

When a new module appears under `src/Modules/{NewModule}/`:

1. ~~Add `"NewModule"` to `Conventions.ModuleNames`~~ — **automatic** (folder discovery in [`Conventions.cs`](./Conventions.cs)).
2. Add `<ProjectReference>` entries for the new module's layers in [`Axis.Architecture.Tests.csproj`](./Axis.Architecture.Tests.csproj) so the DLLs ship into our bin folder.
3. If the module provisions tenant schema: add `OrganizationVerifiedHandler` and update `TenantModuleNames` — see [`TenantProvisioningConventionTests`](./TenantProvisioningConventionTests.cs) and [repo-layout-discovery.md § A](../../../docs/playbooks/repo-layout-discovery.md).
4. Run `dotnet test` against this project — all theory tests will now generate cases for the new module automatically.
5. Full agent checklist (docs, Kafka, buf): [repo-layout-discovery.md](../../../docs/playbooks/repo-layout-discovery.md).

## Pre-existing violations and the allow-list pattern

When these tests landed (PR for `chore/architecture-fitness-and-workarounds-inventory`) the codebase already contained known workarounds — `FormStepReachedHandler` etc. importing another module's `Domain`. We chose **explicit allow-list over silencing**:

- Each pre-existing violation is listed by **full type name** in `KnownBoundaryWorkarounds` inside `ModuleBoundaryTests.cs`.
- Each entry **must** have a matching section in [`docs/WORKAROUNDS.md`](../../../docs/WORKAROUNDS.md) explaining why it exists and what triggers cleanup.
- The test fails on two events:
  1. **A new type starts violating the same rule** — caught immediately ("unexpected types: …"). Forces a conscious decision to add to the list or fix.
  2. **A listed type no longer violates** (cleanup happened) — fails with "stale entries: …" instructing the dev to remove the allow-list entry and move the WORKAROUNDS.md section to *Resolved*.

This way the test ratchets in one direction: the allow-list can only shrink. Never add to it without a matching WORKAROUNDS.md entry and PR-level discussion.

## Adding a new rule

1. Decide whether the rule is structural (use NetArchTest here) or semantic (use code review / drift script).
2. Add a new `*Tests.cs` file with a clear class-level docstring quoting the CLAUDE.md rule it enforces.
3. Each test must fail with a message that tells the reader **what to fix and why** — never a bare assertion.
4. Update the table at the top of this README.

## Why NetArchTest (and not source-only grep)

Grep (in `python scripts/axis.py check doc-drift`) catches text patterns; it cannot follow type relationships, generics, or transitive references. NetArchTest loads the built assemblies and inspects them via reflection — it sees the same thing the runtime sees, which is the only source of truth for "what does this code depend on?"

Both layers stay in the repo: grep catches the obvious (`SqlQueryRaw`); NetArchTest catches the structural (type-graph rules).

## CI integration

This project is included in `Axis.sln`, so `dotnet test Axis.sln` runs it alongside every other test. Failures appear in the standard CI output. There is no separate "architecture" job — these tests pay for themselves only when they're treated as part of the normal test gate.

## Maintenance burden

These tests should be **stable**: they encode rules that change rarely (every CLAUDE.md rewrite, not every feature). If a test starts to feel noisy or starts blocking legitimate work, the right response is:

1. Re-read CLAUDE.md — has the rule actually changed?
2. If yes, update CLAUDE.md first, then the test, in the same PR.
3. If no, the code is violating the rule; fix the code.

Never silence a test by adding the violating namespace to an allow-list without a corresponding CLAUDE.md change.
