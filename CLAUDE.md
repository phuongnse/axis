# Axis — Project Context for Claude

> **Agents:** [`agent-checklist.md`](docs/playbooks/agent-checklist.md) first. Every PR: paste **Gates 0–3** from [PR template](.github/PULL_REQUEST_TEMPLATE.md). **Doc drift** = CI check only (not a pasted gate).

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

Multi-tenant low-code SaaS: custom data models, visual workflows, forms, and UI pages — no end-user coding.

---

## Tech stack & architecture

**Versions and ADRs:** [`docs/TECH_STACK.md`](docs/TECH_STACK.md).

| Area | Choice |
|------|--------|
| Backend | .NET 8, ASP.NET Core, DDD, CQRS (MediatR) |
| Data | PostgreSQL 16, EF Core 9, schema-per-tenant |
| Messaging / jobs | Wolverine (not Hangfire) — all domain events |
| Auth | OpenIddict 5 — PKCE SPA + client credentials |
| Cache | Redis 7 |
| Frontend | React 18, TypeScript, Vite, TanStack Query/Router, Zustand, shadcn, Tailwind |
| API docs | Swashbuckle + Scalar |
| Tests | xUnit, FluentAssertions, NSubstitute, Testcontainers |

**Modules** (each: Domain → Application → Infrastructure; endpoints in `Axis.Api`):

| Module | Responsibility |
|--------|----------------|
| Identity | Auth, users, roles, RBAC (`public` schema) |
| DataModeling | Models, fields, records |
| WorkflowBuilder | Definitions, steps, triggers |
| FormBuilder | Forms, fields, submissions |
| WorkflowEngine | Execution, handlers, history |
| PageBuilder | Pages, widgets (Phase 2) |

**Shared:** `Axis.Shared.Domain`, `Axis.Shared.Application`, `Axis.Shared.Infrastructure`.

**Multi-tenancy:** schema `tenant_{organizationId:N}`; tenant from JWT `org_id`; schema cached in Redis. Identity stays on `public`.

**Module boundaries (P0):**

- Communicate only via Wolverine domain events or Application-layer interfaces — no cross-module DB transactions.
- **Never:** reference another module's `Infrastructure`; query another module's tables (`DbSet`, raw SQL, Dapper); share a `DbContext`; dispatch domain events with `IMediator`.
- Cross-module data: local read model synced by events — see [`patterns.md` § Cross-module data](docs/playbooks/patterns.md).

**Layer dependency:** Domain (pure C#) ← Application ← Infrastructure ← `Axis.Api` ← `frontend/`.

---

## Machine rules (P0–P2)

**P0 — always wrong if violated:**

- Tech stack immutable without explicit user approval.
- Spec → code only; never rewrite specs to match shortcuts.
- Never weaken tests, `.Skip()`, or mock away behavior under test.
- Never bypass auth, skip an AC silently, or mark ✅ to avoid a hard gap.
- Domain: zero external dependencies.
- Never commit with failing Gate 1, or without **written** Gate 2 and Gate 3 in the PR.
- Run `./scripts/check-doc-drift.sh` before push (CI **Doc drift** job).

**P1 — confirm with user before deviating:**

- Layer order: Domain → Application → Infrastructure → API → Frontend.
- `Result` / `Result<T>` for business failures; exceptions for infrastructure only.
- MediatR = commands/queries only; domain events = Wolverine outbox only.
- Minimal API: `.RequireAuthorization()` unless explicitly public.

**P2 — every commit:**

- Zero build warnings and zero test failures.
- Docs in the same PR as code (see [agent-checklist](docs/playbooks/agent-checklist.md)).
- No new TODO/FIXME/placeholder/stub code.

**When blocked:** state blocker → cite constraint → 2–3 options → wait. Never self-unblock on P0.

**Source of truth (highest first):** feature file ACs → this file → playbooks → same-module code → agent guess (never invent IDs, events, endpoints, table names).

**Integrity:** legacy code ≠ authority if it conflicts with docs; surface conflicts. Verify with grep before claiming "done". Document deferrals in callouts, not as ✅.

**Work priority:** (1) gaps/bugs/failing tests (2) finish current layer (3) next layer in order. Before API work: `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/epics/` — resolve or document deferrals ([process.md § 4.5](docs/playbooks/process.md)).

---

## Workflow

### Navigate (do not read this entire file each task)

1. [`agent-checklist.md`](docs/playbooks/agent-checklist.md) — AC map, gates, epic map.
2. `docs/epics/{module}/README.md` + `features/F0N-*.md` for the US.
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
| `src/` or `tests/` | `dotnet build` → `dotnet test` → `dotnet format --verify-no-changes` |
| `frontend/` | `npm run ci` then `npm run test` |
| Both | All of the above |

**Gate 2** — doc walk-through in PR ([agent-checklist.md § Gate 2](docs/playbooks/agent-checklist.md)). **Doc drift** — CI job only; script also fails when epic docs are missing alongside module code changes, or new handlers lack tests.

**Gate 3** — seven yes/no questions in [agent-checklist.md § Gate 3](docs/playbooks/agent-checklist.md); update `patterns.md` / feature file / `TECH_STACK.md` on any "yes".

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
| Types | No `var`; explicit types; one type per file | `const`/`let`, no `var`; strict TS, no `any` |
| Hygiene | `using` not FQCN; `dotnet format` | Biome via `npm run ci` |
| Scope | Fix violation class in one PR, not one file | Feature folders; no cross-feature imports except `index.ts` |

Full rules: [`patterns.md`](docs/playbooks/patterns.md) (EF, API, Wolverine, aggregates) and [`frontend.md`](docs/playbooks/frontend.md).

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
> Decisions: …
```

**Per layer / module:** all US callouts updated; epic README table; [`PROGRESS.md`](docs/PROGRESS.md) (layer summary only — not per-class detail).

**Per PR before merge:** Gates 0–3 written; CI **Doc drift** green when applicable; Gate 1 includes:

```bash
grep -rn "TODO\|FIXME\|NotImplementedException\|placeholder\|stub" src/ tests/ frontend/src/ 2>/dev/null | grep -v obj/ | grep -v node_modules/
```

Diagrams/wireframes: regenerate `.svg` in same PR when source `.excalidraw` changes.

---

## Docs index

| Doc | Use |
|-----|-----|
| [agent-checklist.md](docs/playbooks/agent-checklist.md) | **Agents — daily** |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Branches, PRs, drift script |
| [process.md](docs/playbooks/process.md) | Layer workflow |
| [patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [patterns.md](docs/playbooks/patterns.md) | Implementation detail |
| [testing.md](docs/playbooks/testing.md) | Test patterns |
| [frontend.md](docs/playbooks/frontend.md) | SPA rules |
| [TECH_STACK.md](docs/TECH_STACK.md) | Libraries + ADRs |
| [PROGRESS.md](docs/PROGRESS.md) | Module layer status |
| [docs/epics/](docs/epics/README.md) | Features + ACs |

**Solution tree:**

```text
frontend/src/features/{name}/   # components, hooks, api.ts, types.ts
src/Axis.Api/Endpoints/         # all HTTP endpoints
src/Modules/{Module}/           # Domain, Application, Infrastructure
tests/Modules/{Module}.*.Tests/
```
