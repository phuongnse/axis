# Agent checklist (one page)

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

**Daily workflow.** CI enforces build/test and doc drift; you paste gates in the PR as an audit trail.

---

## Gate 0 — Ready (before code; paste in PR when shipping code)

- AC map: every row has layer + file/test — **no blank cells**
- Read: epic README → feature file → same-module code
- Before API layer: `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/epics/` — fix, defer with reason, or stop

```markdown
## Gate 0
| AC / US | Layer | File / test |
|---------|-------|-------------|
| …       | …     | …           |
Docs touched: docs/epics/…
```

---

## Gates (every PR)

| Gate | Action |
|------|--------|
| **0** | AC map + docs touched (when `src/`, `tests/`, or `frontend/` change) |
| **1** | Full .NET + frontend verification (table below) — **always paste** |
| **2a** | `./scripts/check-doc-drift.sh` — **CI required** |
| **2b** | Human doc walk-through (rows below) |
| **3** | Retrospective (seven questions) |

### Gate 1 — paste in every PR (local = CI)

| Changed | Commands (all must pass when triggered) |
|---------|----------------------------------------|
| `src/` or `tests/` | `dotnet build` then `dotnet test` (full `Axis.sln` — includes Infrastructure, API, Testcontainers) |
| `src/` or `tests/` | `dotnet format --verify-no-changes` |
| `src/`, `tests/`, or `frontend/src/` | `grep -rn "TODO\|FIXME\|NotImplementedException\|placeholder\|stub" src/ tests/ frontend/src/` → empty |
| `frontend/` | `npm run ci` then `npm run test` |
| `src/Axis.Api/Endpoints/` or API contract | Update + run `tests/Api/Axis.Api.Tests/` |

```
Gate 1:
- dotnet build → ran / not triggered
- dotnet test (full solution) → ran / not triggered
- dotnet format --verify-no-changes → ran / not triggered
- stub/TODO grep → ran / not triggered
- npm run ci + npm run test → ran / not triggered
```

Example (docs-only): every line `not triggered — no src/, tests/, or frontend/ changes`.

**Docker:** integration and API tests run as part of `dotnet test`; Docker must be available locally (same as CI agents with Testcontainers).

### Gate 2a — automated

```
Gate 2a:
- ./scripts/check-doc-drift.sh → ran (green) / not triggered
```

### Gate 2b — full row list (work through every line)

```
Gate 2b:
- Library → TECH_STACK.md / not triggered
- New pattern → patterns.md / not triggered
- US layer callout → docs/epics/…/features/… / not triggered
- Epic README + PROGRESS → … / not triggered
- Architecture rule → CLAUDE.md / not triggered
- process.md workflow → … / not triggered
- Project structure → ARCHITECTURE.md + process.md / not triggered
- Wireframe/diagram path move → grep docs/ / not triggered
- Program.cs host → patterns.md host section / not triggered
- Stale code comment → same file / not triggered
- Library rename → grep docs/ + src comments / not triggered
- Deferred scope → feature callout gap / not triggered
```

### Gate 3

```
Gate 3: 1–7 No — or: N → updated <file>
```

Questions: (1) test uncovered rule? (2) invented invariant? (3) infra footgun? (4) test setup quirk? (5) direction change? (6) spec gap? (7) incident-only doc text? → fix docs before merge.

---

## Layer status (feature callouts)

| Symbol | Meaning |
|--------|---------|
| ✅ | All ACs for that layer in this US done |
| ⚠️ | Started; list gaps in callout |
| ⏳ | Not started |

Never ✅ and "pending …" in the same callout. Checkboxes in feature files are spec-only — **do not** tick them.

### Status updates (three levels — same PR)

| Level | When | What to write |
|-------|------|----------------|
| **1 — US** | Any layer progress on a user story | `> **Implementation status**` in `docs/epics/…/features/F0N-….md` |
| **2 — Epic** | A layer is complete for the module | Epic `README.md` implementation table |
| **3 — Platform** | Module-wide summary changed | `docs/PROGRESS.md` — layer status only |

Updating only `PROGRESS.md` while changing `src/` without `docs/epics/` → drift fails. Epic README `| API | ⏳` after endpoints ship → drift fails.

---

## P0 (CI + culture)

- Spec → code, never the reverse
- No cross-module SQL / shared `DbContext` / `IMediator` for domain events
- New `*Handler.cs` → `*HandlerTests.cs` (drift script)
- Module code → `docs/epics/{module}/` in **same PR**
- Frontend screen → wireframe + `> **Wireframe**` in feature file
- No `.Skip()`, weakened tests, or ✅ when ACs are open
- **Full solution only:** always `dotnet build` + `dotnet test` on `Axis.sln` (no solution filter)

---

## Epic map (code → docs)

| Code touch | Docs folder |
|------------|-------------|
| `Endpoints/Execution*`, WorkflowEngine module | `docs/epics/E06-workflow-engine/` |
| `Endpoints/Form*`, FormBuilder | `docs/epics/E05-form-builder/` |
| `Endpoints/Workflow*`, WorkflowBuilder | `docs/epics/E04-workflow-builder/` |
| `Endpoints/Model*`, DataModeling | `docs/epics/E03-data-modeling/` |
| Identity, `Connect*`, auth UI | `docs/epics/E02-identity-access/` |
| `TenantSchema*`, org registration | `docs/epics/E01-platform-foundation/` |

---

## Playbooks (open when needed)

| Need | File |
|------|------|
| Layer order, TDD, gap sweep | [process.md](./process.md) |
| Find the right patterns section | [patterns-index.md](./patterns-index.md) |
| EF, API, Wolverine, tenancy | [patterns.md](./patterns.md) |
| React, Query, a11y | [frontend.md](./frontend.md) |
| Tests, Testcontainers | [testing.md](./testing.md) |
| Wireframe kit | [wireframes.md](./wireframes.md) |
