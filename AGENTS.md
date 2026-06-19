# Axis — Project Context for Agents

> **Agents:** [agent-checklist.md](docs/playbooks/agent-checklist.md) for gates/checkpoints during work; [PR template](.github/PULL_REQUEST_TEMPLATE.md) for description (**Summary + Linked spec + Requirements** only; enforced by CI job **PR body guard**).

## Contents

- [What is Axis](#what-is-axis)
- [Tech stack & architecture](#tech-stack--architecture)
- [Severity rules (P0–P2)](#severity-rules-p0p2)
- [Workflow](#workflow)
- [Development rules](#development-rules)
- [Definition of done](#definition-of-done)
- [Docs index](#docs-index)

---

## What is Axis

Multi-workspace low-code SaaS: custom data models, visual workflows, forms, and UI pages — no end-user coding. Architecture: **modulith with strict service boundaries** ([ADR-010](docs/TECH_STACK.md#adr-010-modulith-with-strict-service-boundaries-so-extraction-is-a-redeploy)) — each module is a service contract from day 1; modulith packaging is the deployment shape today, independent services tomorrow. Extraction is a redeploy, not a refactor.

---

## Tech stack & architecture

Stack, versions, and ADRs are owned by [`docs/TECH_STACK.md`](docs/TECH_STACK.md). Module list and per-module responsibilities are owned by [`docs/use-cases/README.md`](docs/use-cases/README.md). Architectural shape (containers, multi-workspace isolation, auth) is owned by [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). This file owns only the **rules** — the things below that must hold no matter which library version is in `Directory.Packages.props`.

**Enforcement language:** P0/P1/P2 describe severity, not whether a rule is machine-enforced. Enforcement status and allowed wording (`Enforced`, `Partial`, `Review-only`, `Guidance`, `Not a rule`) are owned by [`docs/REVIEW_FINDINGS.md`](docs/REVIEW_FINDINGS.md). Do not call a rule a "gate" unless CI/build/tooling can fail the PR for that class.

**Shared kernel:** `Axis.Shared.Domain`, `Axis.Shared.Application` — **abstractions only**, no shared implementation ([ADR-017](docs/TECH_STACK.md#adr-017-axisshared-is-abstractions-only-no-shared-implementation)). `Axis.Shared.Infrastructure` exists only for genuinely cross-cutting infrastructure (e.g. common JSON policy), never for per-module concerns like UnitOfWork or repository base classes.

**Per-module databases:** each module owns its own PostgreSQL database (`axis_identity`, `axis_datamodeling`, `axis_workflowbuilder`, `axis_workflowengine`, `axis_formbuilder`, `axis_pagebuilder`). Schema-per-workspace `workspace_{workspaceId:N}` *inside* each module DB. Per-module Wolverine envelope schema (`wolverine`) lives in the same DB as the module. Identity's `public` schema is the only registry — other modules **never** SQL-query Identity.

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

**Cross-module data:** local read model synced by Kafka events — see [`cross-module-patterns.md`](docs/playbooks/cross-module-patterns.md). Saga orchestration ([ADR-020](docs/TECH_STACK.md#adr-020-saga-orchestration-for-cross-module-workflows)) for workflows that need transactional-looking semantics across modules.

**Layer dependency (per module):** Contracts (pure schema) ← Domain (pure C#) ← Application ← Infrastructure ← module API entrypoint. `frontend/` calls only `Axis.Api` (REST). `Axis.Api` calls module Application directly (modulith mode) or module gRPC (extracted mode).

---

## Severity rules (P0–P2)

**P0 — always wrong if violated:**

- Tech stack immutable without explicit user approval.
- Spec → code only; never rewrite specs to match shortcuts.
- Never weaken tests, add test `Skip = ...`, or mock away behavior under test.
- Never bypass auth, skip an AC silently, or mark ✅ to avoid a hard gap.
- Domain: zero external dependencies.
- No implementation of a non-trivial change without producing the **Design Gate** review artifact ([design-gate.md](docs/playbooks/design-gate.md)); high-risk surfaces require user sign-off before code.
- Never mark a PR ready with a failing Verification gate; the command matrix and "full suite" honesty rules live in [agent-checklist.md](docs/playbooks/agent-checklist.md#verification-gate--verify-before-pr-review).
- Deterministic policy/doc checks must pass when triggered by touched paths; command ownership lives in [agent-checklist.md](docs/playbooks/agent-checklist.md#verification-gate--verify-before-pr-review), enforcement status in [REVIEW_FINDINGS.md](docs/REVIEW_FINDINGS.md).

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
- Owning docs in the same PR when behavior/spec/status changes; pure refactor/style/test-only changes do not need a token docs edit (see [agent-checklist](docs/playbooks/agent-checklist.md)).
- No new TODO/FIXME/placeholder/stub code.
- For **wireframe** changes (`docs/wireframes/`, `docs/use-cases/**` screen `.excalidraw`/`.svg`): run [`docs/playbooks/visual-artifact-checklist.md`](docs/playbooks/visual-artifact-checklist.md) before commit. **Diagrams** are Mermaid in `docs/README.md` and use-case `README.md` — one theme in [`docs/diagrams/mermaid_theme.py`](docs/diagrams/mermaid_theme.py); run `python docs/scripts/sync-mermaid-theme.py` after editing `MERMAID_INIT` ([`docs/playbooks/mermaid.md`](docs/playbooks/mermaid.md)).

**When blocked:** state blocker → cite constraint → 2–3 options → wait. Never self-unblock on P0.

**Source of truth (highest first):** use-case file ACs → this file → playbooks → same-module code → agent guess (never invent IDs, events, endpoints, table names).

**Integrity:** legacy code ≠ authority if it conflicts with docs; surface conflicts. Verify with grep before claiming "done". Document deferrals in callouts (`**Deferred follow-ups:**`), not as ✅ — **proactively**, without waiting for the user to ask ([process.md § Deferred follow-up](docs/playbooks/process.md)).

**Workarounds:** intentional P0/P1 violations must follow [`docs/WORKAROUNDS.md`](docs/WORKAROUNDS.md): inventory entry, cleanup trigger, and site reference in the same PR. This file owns severity; WORKAROUNDS owns the workflow and current debt.

**Work priority:** (1) gaps/bugs/failing tests (2) finish current layer (3) next layer in order. Before API work: `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/use-cases/` — resolve or document deferrals ([process.md § 4.5](docs/playbooks/process.md)).

---

## Workflow

### Navigate (do not read this entire file each task)

1. [`agent-checklist.md`](docs/playbooks/agent-checklist.md) — AC map, gates/checkpoints, domain map. New module/event/proto → [`repo-layout-discovery.md`](docs/playbooks/repo-layout-discovery.md).
2. `docs/use-cases/{domain}/README.md` + `docs/use-cases/{domain}/*.md` for the use case.
3. [`docs/PROGRESS.md`](docs/PROGRESS.md) for layer status.
4. Open [`process.md`](docs/playbooks/process.md) / [`patterns-index.md`](docs/playbooks/patterns-index.md) only when the checklist says so.

### Design Gate (mandatory before code)

Before a non-trivial change, complete the **Design Gate** ([design-gate.md](docs/playbooks/design-gate.md)): governing rules quoted with `file:section`, blast-radius search, contract/casing decision, and verification plan. Use `$axis-design-gate` for the repeatable workflow.

Design Gate is a required review artifact, not a machine-enforced CI gate. Reviewers and agents own the evidence; deterministic subsets belong in scripts/tests and then in [REVIEW_FINDINGS.md](docs/REVIEW_FINDINGS.md).

**High-risk surfaces** — new/changed endpoint or contract/required field, migration/schema, cross-module interaction, auth, new library or public API surface — require **user sign-off via plan mode before writing code**.

Use repo skills for repeatable workflows: `$axis-use-case-spec`, `$axis-use-case-implementation`, `$axis-api-contract`, `$axis-cross-module-contract`, `$axis-frontend-feature`, `$axis-visual-artifact`, `$axis-review-feedback`, and `$axis-ready-review`.

### Reviews And Gates

**Ready review / Verification gate ownership:** [agent-checklist.md](docs/playbooks/agent-checklist.md) owns AC-map/path coverage details and the Verification gate command matrix. This file keeps only policy-level constraints: no blank in-scope AC rows, no happy-path-only completion claims, and no unit-only/local-fast run presented as a full suite.

**Docs review** — docs walkthrough when behavior/spec/status changes ([agent-checklist.md](docs/playbooks/agent-checklist.md)). **Doc drift** — CI-enforced deterministic policy/doc checks; it does not require a token docs edit for every code diff.

**Retrospective review** — update docs, tests, or [REVIEW_FINDINGS.md](docs/REVIEW_FINDINGS.md) when a durable rule or repeat finding emerges.

When adding or rewording a rule, keep the owner boundary intact: this file states severity, [agent-checklist.md](docs/playbooks/agent-checklist.md) states workflow/commands, and [REVIEW_FINDINGS.md](docs/REVIEW_FINDINGS.md) states enforcement status. Link instead of restating command matrices or gate semantics.

### Git

- Branch: `{feat|fix|docs|refactor|test|chore}/{short-description}` — never push to `main`.
- Conventional Commits, ≤72 chars, English.
- Review fixes go on the **existing PR branch**.
- Docs-first for new features; English everywhere.

### New doc files

Add navigation back-links per [docs/README.md](docs/README.md) (playbooks, use-cases).

---

## Development rules

**Process:** [`process.md`](docs/playbooks/process.md) — layer order, TDD, gap sweep, frontend phases.

**Testing:** TDD mandatory. .NET: `{Subject}_{Condition}_{ExpectedOutcome}`; Testcontainers (no in-memory EF); update `tests/Api/Axis.Api.Tests/` when API contracts change. Frontend: Vitest + Testing Library; `userEvent`; behaviour not implementation. Details: [`testing.md`](docs/playbooks/testing.md).

**Code style (summary):**

| | .NET | Frontend |
|---|------|----------|
| Types | No `var` for locals (see `.editorconfig`); explicit types; keep files focused | `const`/`let`, no `var`; strict TS, no `any` |
| Hygiene | Prefer `using` over inline FQCN; `dotnet format Axis.sln` before review (CI `--verify-no-changes`) | Biome via `npm run ci` |
| Scope | Fix violation class in one PR, not one file | Feature folders; no cross-feature imports except `index.ts` |

**Enforced C# rules** live in [`.editorconfig`](.editorconfig) at the repo root. Do not restate style here — run `dotnet format` and follow the file.

**Architecture/API contracts:** use `$axis-api-contract` for REST/OpenAPI/API-type work and `$axis-cross-module-contract` for events, protos, Wolverine, Kafka, RabbitMQ, or gRPC. Details live in [`api-patterns.md`](docs/playbooks/api-patterns.md), [`cross-module-patterns.md`](docs/playbooks/cross-module-patterns.md), [`wolverine-patterns.md`](docs/playbooks/wolverine-patterns.md), [`grpc-patterns.md`](docs/playbooks/grpc-patterns.md), and [`repo-layout-discovery.md`](docs/playbooks/repo-layout-discovery.md).

**Frontend:** use `$axis-frontend-feature`; details live in [`frontend.md`](docs/playbooks/frontend.md).

**Visual artifacts:** use `$axis-visual-artifact` for wireframes, generated SVG previews, and diagrams. Source rules live in [`wireframes.md`](docs/playbooks/wireframes.md), [`wireframes/README.md`](docs/wireframes/README.md), and [`visual-artifact-checklist.md`](docs/playbooks/visual-artifact-checklist.md).

**Cross-cutting:** forward `CancellationToken`; audit fields in Application; soft-delete on workspace aggregates; Serilog without PII; rate limit auth endpoints; CORS before auth; `/health` + `/health/ready` anonymous.

---

## Definition of done

**Per US (any layer):** tests first and green; update the `> **Implementation status**` callout after *Out of scope*. Format and status rules live in [docs-style.md](docs/playbooks/docs-style.md#implementation-status-after-each-us-ac-block).

**Per layer / module:** all use-case callouts updated; domain README table; [`PROGRESS.md`](docs/PROGRESS.md) (layer summary only — not per-class detail).

**Per PR before merge:** PR description = Summary + Linked spec + Requirements only (no CI status, no commit list — Checks tab covers that). Run the triggered Verification gate commands from [agent-checklist.md](docs/playbooks/agent-checklist.md#verification-gate--verify-before-pr-review); CI/Doc drift enforcement status is tracked in [REVIEW_FINDINGS.md](docs/REVIEW_FINDINGS.md).

Diagrams/wireframes: regenerate `.svg` in same PR when source `.excalidraw` changes. Agents must pass [`docs/playbooks/visual-artifact-checklist.md`](docs/playbooks/visual-artifact-checklist.md) before commit.

---

## Docs index

| Doc | Use |
|-----|-----|
| [agent-checklist.md](docs/playbooks/agent-checklist.md) | **Agents — daily** |
| [repo-layout-discovery.md](docs/playbooks/repo-layout-discovery.md) | **Agents —** module/event/proto → docs/config checklists + CI discovery rules |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Branches, PRs, drift script |
| [process.md](docs/playbooks/process.md) | Layer workflow; deferred follow-ups; PR wrap-up |
| [patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into focused implementation-pattern playbooks |
| [patterns.md](docs/playbooks/patterns.md) | Compatibility router for legacy pattern anchors |
| [testing.md](docs/playbooks/testing.md) | Test patterns |
| [frontend.md](docs/playbooks/frontend.md) | SPA rules |
| [docs-style.md](docs/playbooks/docs-style.md) | Doc anti-patterns + single-owner rule (read before adding a `.md`) |
| [TECH_STACK.md](docs/TECH_STACK.md) | Libraries + ADRs |
| [PROGRESS.md](docs/PROGRESS.md) | Module layer status |
| [WORKAROUNDS.md](docs/WORKAROUNDS.md) | Intentional rule violations + cleanup triggers (**read when touching legacy or shipping a known shortcut**) |
| [REVIEW_FINDINGS.md](docs/REVIEW_FINDINGS.md) | Recurring review finding classes → Enforced / Partial / Review-only / Guidance status; wired to Retrospective review |
| [docs/playbooks/visual-artifact-checklist.md](docs/playbooks/visual-artifact-checklist.md) | **Required when changing diagrams/wireframes/use-case visuals** |
| [Architecture tests README](tests/Architecture/Axis.Architecture.Tests/README.md) | What's mechanically enforced + how to add a new rule |
| [docs/use-cases/](docs/use-cases/README.md) | Features + ACs |

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
