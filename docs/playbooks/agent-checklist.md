# Agent checklist (one page)

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

Use this instead of re-reading all of `CLAUDE.md`. Details live in playbooks; this page is **what to do and what CI enforces**.

---

## Before coding (write in PR description)

```markdown
## AC map
| AC | Layer | File / endpoint |
|----|-------|-----------------|
| …  | …     | …               |

## Docs touched
- Feature: docs/epics/E0N-…/features/F0N-….md (callout US-…)
- Epic README + PROGRESS.md if layer status changed
```

Stop if any AC has no file/method — ask the user first.

**Read (minimum):** epic README → feature file for the US → same-module code for patterns.

---

## Gates (every PR)

| Gate | Command / action |
|------|------------------|
| **1 — Build** | `src/` → `dotnet build` then `dotnet test unit-tests.slnf` · `frontend/` → `npm run ci` + `npm run test` |
| **2 — Docs** | Update feature callout + epic README + `docs/PROGRESS.md` when a layer changes. Paste **Gate 2** block in PR (see template). |
| **2b — Drift** | `./scripts/check-doc-drift.sh` (CI runs this — must pass) |
| **3 — Retro** | Paste **Gate 3** block in PR (yes/no + what you changed in docs if yes) |

### Gate 2 (copy into PR)

```
Gate 2:
- Library → …
- New pattern → …
- US layer callout → …
- Epic README / PROGRESS → …
```

### Gate 3 (copy into PR)

```
Gate 3: 1–7 all No — or list number + doc updated
```

---

## Layer status rules

- **✅** = every AC for that layer in this US is done.
- **⚠️** = started but gaps remain (list in callout).
- **⏳** = not started.
- Never **✅** and "pending X" in the same callout.

---

## P0 reminders (no exceptions)

- Spec → code, not the reverse.
- No cross-module SQL / shared DbContext.
- New `*Handler.cs` → matching `*HandlerTests.cs` must exist in the repo (CI checks).
- Frontend screen → wireframe exists + linked in feature file.
- Do not only update `PROGRESS.md` when epic/feature callouts are stale.

---

## Epic map (code → docs folder)

| If you change… | Update docs under… |
|----------------|-------------------|
| `Endpoints/Execution*` | `docs/epics/E06-workflow-engine/` |
| `Endpoints/Form*` / FormBuilder module | `docs/epics/E05-form-builder/` |
| `Endpoints/Workflow*` / WorkflowBuilder | `docs/epics/E04-workflow-builder/` |
| `Endpoints/Model*` / DataModeling | `docs/epics/E03-data-modeling/` |
| Identity / `Connect*` / `Auth*` | `docs/epics/E02-identity-access/` |
| `TenantSchema*` / org registration | `docs/epics/E01-platform-foundation/` |
| `frontend/src/features/auth` or `routes/login` | `docs/epics/E02-identity-access/` |

---

## When to open a playbook

| Task | Open |
|------|------|
| Layer order, TDD steps | [process.md](./process.md) |
| EF, API, Wolverine, tenancy | [patterns.md](./patterns.md) |
| React, Query, routes | [frontend.md](./frontend.md) |
| Tests | [testing.md](./testing.md) |
