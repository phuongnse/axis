# Agent checklist (one page)

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

**Daily workflow.** Gates 0–3 while implementing. **PR description:** only Summary + Requirements ([PR template](../../.github/PULL_REQUEST_TEMPLATE.md)) — not long gate paste blocks.


---


**PR body:** Summary + ordered Requirements (spec→code, Gates 0–3). Do not list commits or CI/Doc drift status in the description — GitHub Checks tab covers that.

## Gate 0 — Ready (before code; paste in PR when shipping code)

- AC map: every row has layer + file/test — **no blank cells**
- Read: epic README → feature file → same-module code
- Before API layer: `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/epics/` — fix, defer with reason, or stop
- End of PR: [process.md § PR wrap-up](process.md) — deferred lines, host wiring, callouts (no user reminder)

```markdown
## Gate 0
| AC / US | Layer | File / test |
|---------|-------|-------------|
| …       | …     | …           |
Docs touched: docs/epics/…
```

---

## Gates (every PR)

**Doc drift:** when `src/`, `tests/`, or `docs/epics/` change — run `./scripts/check-doc-drift.sh` **before push** (P0); CI job **Doc drift** must be green. Do **not** paste script output as a gate block — paste **Gate 2** walk-through instead.

| Gate | Action |
|------|--------|
| **0** | AC map + docs touched (when `src/`, `tests/`, or `frontend/` change) |
| **1** | Full .NET + frontend verification (table below) |
| **2** | Doc walk-through (rows below) |
| **3** | Retrospective (seven questions) |

**Priority:** Gate **1** blocks commit (failing build/tests). Gate **2** keeps docs in the same PR — required before merge, not a substitute for Gate 1. In PR descriptions, list Gate 1 before Gate 2 ([template](../../.github/PULL_REQUEST_TEMPLATE.md)).

### Gate 1 — verify before push (local = CI)

| Changed | Commands (all must pass when triggered) |
|---------|----------------------------------------|
| `src/` or `tests/` | `dotnet build` then `dotnet test` (full `Axis.sln` — includes Infrastructure, API, Testcontainers) |
| `src/` or `tests/` | `dotnet format --verify-no-changes` |
| `src/`, `tests/`, or `frontend/src/` | `grep -rn "TODO\|FIXME\|NotImplementedException\|placeholder\|stub" src/ tests/ frontend/src/` → empty |
| `frontend/` | `npm run ci` then `npm run test` |
| `src/Axis.Api/Endpoints/` or API contract | Update + run `tests/Api/Axis.Api.Tests/` |

```text
Gate 1:
- dotnet build → ran / not triggered (reason)
- dotnet test (full solution) → ran / not triggered (reason)
- dotnet format --verify-no-changes → ran / not triggered (reason)
- stub/TODO grep → ran / not triggered (reason)
- npm run ci + npm run test → ran / not triggered (reason)
- ./scripts/check-doc-drift.sh → ran / not triggered (reason)
```

Example (docs-only): every line `not triggered — no src/, tests/, or frontend/ changes`.

**Docker:** integration and API tests run as part of `dotnet test`; Docker must be available locally (same as CI agents with Testcontainers).

### Gate 2 — docs walk-through

```text
Gate 2:
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
- Deferred follow-up → `**Deferred (PR #N follow-up):**` on affected US + PROGRESS if cross-cutting / not triggered
- Host wiring (`*Endpoints.cs` / `Program.cs`) → `Map*Endpoints` sweep in process.md / not triggered
```

**Deferred follow-ups (mandatory when leaving work open):** do not wait for the user. Any skipped review item, thin-endpoint refactor, or partial layer needs a named `**Deferred (...):**` line — full rules in [process.md § Deferred follow-up](process.md). Remove the line when fixed.


### Gate 3 — retrospective

Answer **Yes** or **No** on **each numbered line** (do not replace with `1–7 No`). If **Yes**, name the doc updated in this PR.

```text
Gate 3:
1. New rule from test failure? → No / Yes →
2. Invented invariant without AC? → No / Yes →
3. Infrastructure footgun? → No / Yes →
4. Non-obvious test setup? → No / Yes →
5. Changed direction mid-task? → No / Yes →
6. Spec gap discovered? → No / Yes →
7. Incident-level detail in rule text? → No / Yes →
```

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
| **1 — US** | Any layer progress on a user story | `> **Implementation status**`, `Gaps vs spec`, optional `**Deferred (PR #N follow-up):**` in `docs/epics/…/features/F0N-….md` |
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
| Layer order, TDD, gap sweep, deferred docs, PR wrap-up | [process.md](./process.md) |
| Find the right patterns section | [patterns-index.md](./patterns-index.md) |
| EF, API, Wolverine, tenancy | [patterns.md](./patterns.md) |
| React, Query, a11y | [frontend.md](./frontend.md) |
| Tests, Testcontainers | [testing.md](./testing.md) |
| Wireframe kit | [wireframes.md](./wireframes.md) |
