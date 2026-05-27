# Axis — Project Context for Claude

> **Agents:** [agent-checklist.md](docs/playbooks/agent-checklist.md) for gates during work; [PR template](.github/PULL_REQUEST_TEMPLATE.md) for description (**Summary + Requirements** only.

## Contents

- [What is Axis](#what-is-axis)
- [Tech stack & architecture](#tech-stack--architecture)
- [Machine rules (P0–P2)](#machine-rules-p0p2)
- [Workflow](#workflow)
- [Development rules](#development-rules)
- [Definition of done](#definition-of-done)
- [Docs index](#docs-index)

---

## What is Axis

Multi-tenant low-code SaaS: custom data models, visual workflows, forms, and UI pages — no end-user coding. Architecture: **modulith with strict service boundaries** ([ADR-010](docs/TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy)) — each module is a service contract from day 1; modulith packaging is the deployment shape today, independent services tomorrow. Extraction is a redeploy, not a refactor.

---

## Tech stack & architecture

Stack, versions, and ADRs are owned by [`docs/TECH_STACK.md`](docs/TECH_STACK.md). Module list and per-module responsibilities are owned by [`docs/epics/README.md`](docs/epics/README.md). Architectural shape (containers, multi-tenancy, auth) is owned by [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). This file owns only the **rules** — the things below that must hold no matter which library version is in `Directory.Packages.props`.

**Shared kernel:** `Axis.Shared.Domain`, `Axis.Shared.Application` — **abstractions only**, no shared implementation ([ADR-017](docs/TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation)). `Axis.Shared.Infrastructure` exists only for genuinely cross-cutting infrastructure (e.g. common JSON policy), never for per-module concerns like UnitOfWork or repository base classes.

**Per-module databases:** each module owns its own PostgreSQL database (`axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, `axis_workflowengine`, `axis_formbuilder`, `axis_pagebuilder`). Schema-per-tenant `tenant_{organizationId:N}` *inside* each module DB. Per-module Wolverine envelope schema (`wolverine`) lives in the same DB as the module. Identity's `public` schema is the only registry — other modules **never** SQL-query Identity.

**Cross-module communication contract (P0):**

- **Events (`*Event` / `*Snapshot`):** Kafka topics with Avro payloads + CloudEvents envelopes. Wolverine publishes from the originating module's outbox; consumers run in other modules' Wolverine handlers. Also the store for event-sourced aggregates (per ADR-013).
- **Commands / Jobs / Saga steps (`*Command` / `*Job` / `*SagaStep`):** RabbitMQ exchanges/queues via Wolverine. Work-queue semantics (ACK, requeue, DLX). Per ADR-024 + the routing rule in ADR-025.
- **Sync RPC:** gRPC only, contracts defined in `Axis.{Module}.Contracts/*.proto`. Used as escape hatch when a local read model is insufficient.
- **Auth:** JWT issued by Identity; other modules validate locally via JWKS (no DB lookup of Identity).
- **External API to SPA:** REST/OpenAPI through the `Axis.Api` gateway.

**Service boundaries (P0 — always wrong if violated):**

- **No in-process method call into another module's Application or Infrastructure.** Cross-module = Kafka event OR gRPC call, never `services.GetRequiredService<IFooFromAnotherModule>()`.
- **No project reference** from `Axis.{ModuleA}.*` to `Axis.{ModuleB}.*` except to `Axis.{ModuleB}.Contracts` (proto + Avro schemas only).
- **No shared `DbContext`, no cross-module SQL, no cross-module aggregate references.**
- **No shared kernel implementation.** `Axis.Shared.*` projects contain interfaces, primitives, and Result types — never UnitOfWork base classes, EF helpers, or repository bases.
- **MediatR is intra-module only.** Cross-module dispatch always goes through Wolverine: commands/jobs/saga steps via RabbitMQ, events via Kafka (suffix convention in ADR-025).
- **Auth checks read JWT claims locally.** Never `IdentityDbContext.Users.Find(...)` from outside Identity — call Identity's gRPC `IdentityService` or rely on JWT claims.

**Cross-module data:** local read model synced by Kafka events — see [`patterns.md` § Cross-module communication](docs/playbooks/patterns.md). Saga orchestration ([ADR-020](docs/TECH_STACK.md#adr-020-saga-orchestration-for-cross-module-workflows)) for workflows that need transactional-looking semantics across modules.

**Layer dependency (per module):** Contracts (pure schema) ← Domain (pure C#) ← Application ← Infrastructure ← module API entrypoint. `frontend/` calls only `Axis.Api` (REST). `Axis.Api` calls module Application directly (modulith mode) or module gRPC (extracted mode).

---

## Machine rules (P0–P2)

**P0 — always wrong if violated:**

- Tech stack immutable without explicit user approval.
- Spec → code only; never rewrite specs to match shortcuts.
- Never weaken tests, `.Skip()`, or mock away behavior under test.
- Never bypass auth, skip an AC silently, or mark ✅ to avoid a hard gap.
- Domain: zero external dependencies.
- Never commit with failing Gate 1; docs and requirements satisfied before merge (agent-checklist + PR template).
- When `src/`, `tests/`, or `docs/epics/` change: run `./scripts/check-doc-drift.sh` before push (bash — on Windows use Git Bash, not PowerShell); CI **Doc drift** must be green. Tick **Gate 2** in the PR template — do not paste drift-script output.

**P1 — confirm with user before deviating:**

- Layer order: Contracts → Domain → Application → Infrastructure → module entrypoint → `Axis.Api` gateway → Frontend.
- `Result` / `Result<T>` for business failures; exceptions for infrastructure only.
- MediatR = intra-module commands/queries only; cross-module dispatch = Wolverine outbox → Kafka topic (`*Event`/`*Snapshot`) or RabbitMQ exchange (`*Command`/`*Job`/`*SagaStep`) per ADR-025.
- Minimal API: `.RequireAuthorization()` unless explicitly public; JWT validated locally via Identity JWKS (no DB call to Identity).
- Schema changes = EF Core migration ([ADR-023](docs/TECH_STACK.md#adr-023-per-module-ef-core-migrations-only)); `EnsureCreated` forbidden everywhere (prod, dev, tests).
- New cross-module RPC = `.proto` in `Axis.{Module}.Contracts` first; never expose a new sync call without a versioned contract.
- New cross-module event = Avro schema registered with Schema Registry + CloudEvents envelope ([ADR-019](docs/TECH_STACK.md#adr-019-avro-and-schema-registry-for-event-payloads-with-cloudevents-envelope)).

**P2 — every commit:**

- Zero build warnings and zero test failures.
- Docs in the same PR as code (see [agent-checklist](docs/playbooks/agent-checklist.md)).
- No new TODO/FIXME/placeholder/stub code.

**When blocked:** state blocker → cite constraint → 2–3 options → wait. Never self-unblock on P0.

**Source of truth (highest first):** feature file ACs → this file → playbooks → same-module code → agent guess (never invent IDs, events, endpoints, table names).

**Integrity:** legacy code ≠ authority if it conflicts with docs; surface conflicts. Verify with grep before claiming "done". Document deferrals in callouts (`**Deferred (PR #N follow-up):**`), not as ✅ — **proactively**, without waiting for the user to ask ([process.md § Deferred follow-up](docs/playbooks/process.md)).

**Workarounds:** if you intentionally ship code that violates a P0/P1 rule (because the proper solution is blocked), record it in [`docs/WORKAROUNDS.md`](docs/WORKAROUNDS.md) **in the same PR** with a cleanup trigger. Add a `// WORKAROUND: see docs/WORKAROUNDS.md#<slug>` comment at the violation site. The drift script and the architecture fitness tests (`tests/Architecture/Axis.Architecture.Tests`) enforce both ends.

**Work priority:** (1) gaps/bugs/failing tests (2) finish current layer (3) next layer in order. Before API work: `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/epics/` — resolve or document deferrals ([process.md § 4.5](docs/playbooks/process.md)).

---

## Workflow

### Navigate (do not read this entire file each task)

1. [`agent-checklist.md`](docs/playbooks/agent-checklist.md) — AC map, gates, epic map.
2. `docs/epics/{module}/README.md` + `docs/use-cases/{domain}/*.md` for the use case.
3. [`docs/PROGRESS.md`](docs/PROGRESS.md) for layer status.
4. Open [`process.md`](docs/playbooks/process.md) / [`patterns.md`](docs/playbooks/patterns.md) only when the checklist says so.

### Multi-file / new-layer tasks (response header)

1. Affected module(s) and layer(s)  
2. Docs to read  
3. Key decisions (2–3) — confirm with user before new API surface or library  
4. Plan  
5. Risks / ambiguities  

Skip for single-file fixes and doc-only edits.

### Gates

**Gate 0** — AC map (no blank cells); gap sweep before API. Template: [agent-checklist.md § Gate 0](docs/playbooks/agent-checklist.md).

**Gate 1** — scope-based (local = CI; full `Axis.sln`, no solution filter):

| Changed | Commands |
|---------|----------|
| `src/` or `tests/` | `dotnet build` → `dotnet test` (includes [architecture fitness](tests/Architecture/Axis.Architecture.Tests/README.md)) → `dotnet format --verify-no-changes` |
| `frontend/` | `npm run ci` then `npm run test` |
| Both | All of the above |

**Gate 2** — docs in same PR ([agent-checklist.md § Gate 2](docs/playbooks/agent-checklist.md)). **Doc drift** — run script before push when code/epics change; CI job must be green.

**Gate 3** — retrospective ([agent-checklist.md § Gate 3](docs/playbooks/agent-checklist.md)); update docs on any "yes".

### Git

- Branch: `{feat|fix|docs|refactor|test|chore}/{short-description}` — never push to `main`.
- Conventional Commits, ≤72 chars, English.
- Review fixes go on the **existing PR branch**.
- Docs-first for new features; English everywhere.

### New doc files

Add navigation back-links per [docs/README.md](docs/README.md) (playbooks, epics, features).

---

## Development rules

**Process:** [`process.md`](docs/playbooks/process.md) — layer order, TDD, gap sweep, frontend phases.

**Testing:** TDD mandatory. .NET: `{Subject}_{Condition}_{ExpectedOutcome}`; Testcontainers (no in-memory EF); update `tests/Api/Axis.Api.Tests/` when API contracts change. Frontend: Vitest + Testing Library; `userEvent`; behaviour not implementation. Details: [`testing.md`](docs/playbooks/testing.md).

**Code style (summary):**

| | .NET | Frontend |
|---|------|----------|
| Types | No `var` for locals (see `.editorconfig`); explicit types; one type per file | `const`/`let`, no `var`; strict TS, no `any` |
| Hygiene | `using` not FQCN; `dotnet format Axis.sln` before push (CI `--verify-no-changes`) | Biome via `npm run ci` |
| Scope | Fix violation class in one PR, not one file | Feature folders; no cross-feature imports except `index.ts` |

**Enforced C# rules** live in [`.editorconfig`](.editorconfig) at the repo root (naming, braces, file-scoped namespaces, analyzer severities). Do not restate style here — run `dotnet format` and follow the file. Architecture patterns: [`patterns.md`](docs/playbooks/patterns.md). Frontend: [`frontend.md`](docs/playbooks/frontend.md).

**Architecture (.NET) — consult patterns before coding:**

- Aggregates, `OwnsMany`, JSONB + `HasValueComparer`, Result pattern, response DTOs in Application, pagination `PagedResult`, idempotent commands/migrations.
- Minimal API: no logic in endpoints — `mediator.Send` + `ToProblemDetails()`.
- Repositories return materialized types, not `IQueryable`; `SaveChangesAsync` only in `IUnitOfWork`.
- List endpoints: project with `.Select()` before materialize — no full aggregate loads on lists.

**Frontend — consult [`frontend.md`](docs/playbooks/frontend.md):**

- TanStack Query = server state; Zustand = client-only; loading/empty/error on fetches; RHF + Zod; lazy routes; no tokens in `localStorage`.

**Wireframes:** `docs/epics/{epic}/wireframes/{slug}.excalidraw` + `.svg`; link in feature file; regenerate with `docs/scripts/generate-wireframes.ps1`. Kit rules: [`wireframes.md`](docs/playbooks/wireframes.md).

**Cross-cutting:** forward `CancellationToken`; audit fields in Application; soft-delete on tenant aggregates; Serilog without PII; rate limit auth endpoints; CORS before auth; `/health` + `/health/ready` anonymous.

---

## Definition of done

**Per US (any layer):** tests first and green; `> **Implementation status**` callout after *Out of scope*:

```markdown
> **Implementation status** — Domain: ✅ | Application: ⚠️ | …
> Gaps vs spec: …
> **Deferred (PR #N follow-up):** … (omit line if none)
> Decisions: …
```

**Per layer / module:** all US callouts updated; epic README table; [`PROGRESS.md`](docs/PROGRESS.md) (layer summary only — not per-class detail).

**Per PR before merge:** PR description = Summary + Linked spec + Requirements only (no CI status, no commit list — Checks tab covers that). Run `./scripts/check-doc-drift.sh` before push when `src/`, `tests/`, or `docs/epics/` change — the script enforces epic-docs same-PR, new-handler tests, the no-new `TODO`/`FIXME`/`stub` rule, and new raw-SQL call review (cross-module guard).

Diagrams/wireframes: regenerate `.svg` in same PR when source `.excalidraw` changes.

---

## Docs index

| Doc | Use |
|-----|-----|
| [agent-checklist.md](docs/playbooks/agent-checklist.md) | **Agents — daily** |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Branches, PRs, drift script |
| [process.md](docs/playbooks/process.md) | Layer workflow; deferred follow-ups; PR wrap-up |
| [patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [patterns.md](docs/playbooks/patterns.md) | Implementation detail |
| [testing.md](docs/playbooks/testing.md) | Test patterns |
| [frontend.md](docs/playbooks/frontend.md) | SPA rules |
| [docs-style.md](docs/playbooks/docs-style.md) | Doc anti-patterns + single-owner rule (read before adding a `.md`) |
| [TECH_STACK.md](docs/TECH_STACK.md) | Libraries + ADRs |
| [PROGRESS.md](docs/PROGRESS.md) | Module layer status |
| [WORKAROUNDS.md](docs/WORKAROUNDS.md) | Intentional rule violations + cleanup triggers (**read when touching legacy or shipping a known shortcut**) |
| [Architecture tests README](tests/Architecture/Axis.Architecture.Tests/README.md) | What's mechanically enforced + how to add a new rule |
| [docs/epics/](docs/epics/README.md) | Features + ACs |

**Solution tree:**

```text
frontend/src/features/{name}/         # components, hooks, api.ts, types.ts
src/Axis.Api/Endpoints/               # REST gateway endpoints (route → module call)
src/Modules/{Module}/
├── Axis.{Module}.Contracts/          # .proto + Avro schemas (pure schema, no logic)
├── Axis.{Module}.Domain/             # entities, value objects, domain events
├── Axis.{Module}.Application/        # commands, queries, handlers, saga state
└── Axis.{Module}.Infrastructure/     # DbContext, repos, Wolverine handlers, gRPC server, Kafka producer/consumer
tests/Modules/{Module}.*.Tests/       # mirror src layout
```
