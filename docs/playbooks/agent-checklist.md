# Agent checklist (one page)

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

**Daily workflow.** Detail lives in playbooks; CI enforces the non-negotiable parts.

---

## Before coding (paste in PR)

```markdown
## AC map
| AC / US | Layer | File / endpoint / test |
|---------|-------|----------------------|
| …       | …     | …                    |

## Docs touched
- docs/epics/E0N-…/features/F0N-….md (callout)
- Epic README / PROGRESS.md (if layer status changed)
```

No row with a blank implementation cell → stop and ask.

**Read:** epic README → feature file (US) → same-module code. Open playbooks only per table at bottom.

---

## Gates (every PR)

| Gate | Action |
|------|--------|
| **1** | `dotnet build` + `dotnet test unit-tests.slnf` if `src/`/`tests/` · `npm run ci` + `npm run test` if `frontend/` |
| **2** | Update docs (table below) + paste Gate 2 block in PR |
| **2b** | `./scripts/check-doc-drift.sh` — **CI fails if red** |
| **3** | Paste Gate 3 block in PR |

### Gate 2 — full row list (work through every line)

```
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
| **1 — US** | Any layer progress on a user story | `> **Implementation status**` in `docs/epics/…/features/F0N-….md` (per layer: ✅ / ⚠️ / ⏳; Gaps / Decisions lines when needed) |
| **2 — Epic** | A layer is complete for the module (all USes in that layer) | Epic `README.md` implementation table (`API`, `Application`, etc.) |
| **3 — Platform** | Module-wide layer summary changed | `docs/PROGRESS.md` — one short paragraph per module; **no** endpoint lists, class names, or per-PR detail |

Updating only `PROGRESS.md` while changing `src/` without any `docs/epics/` file → `check-doc-drift.sh` fails. Epic README `| API | ⏳` after shipping endpoints → drift fails.


---

## P0 (CI + culture)

- Spec → code, never the reverse
- No cross-module SQL / shared `DbContext` / `IMediator` for domain events
- New `*Handler.cs` → `*HandlerTests.cs` exists (`check-doc-drift.sh`)
- Module code change → `docs/epics/{module}/` changes in **same PR** (not PROGRESS alone)
- Frontend screen → wireframe + `> **Wireframe**` in feature file
- No `.Skip()`, weakened tests, or ✅ when ACs are open

---

## Epic map (code → docs)

| Code touch | Docs folder |
|------------|-------------|
| `Endpoints/Execution*`, WorkflowEngine module | `docs/epics/E06-workflow-engine/` |
| `Endpoints/Form*`, FormBuilder, FormSubmission | `docs/epics/E05-form-builder/` |
| WorkflowBuilder endpoints/module | `docs/epics/E04-workflow-builder/` |
| DataModeling | `docs/epics/E03-data-modeling/` |
| Identity, `Connect*`, `Auth*`, auth UI | `docs/epics/E02-identity-access/` |
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
