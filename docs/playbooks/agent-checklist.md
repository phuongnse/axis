# Agent checklist (one page)

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

**Daily workflow.** Walk Gates 0–3 **locally** while implementing; reflect outcomes in the [PR template](../../.github/PULL_REQUEST_TEMPLATE.md) checkboxes. **PR description = Summary + Linked spec + Requirements only** — no Gate paste blocks, no commit list, no CI/Doc-drift status (GitHub Checks tab covers that).

The paste-block templates below are for *your own* walk-through (agent reasoning, scratchpad, or PR thread comment if asked) — not for the PR description.

---

## Gate 0 — Ready (before code)

- AC map: every row has layer + file/test — **no blank cells**
- Read: epic README → feature file → same-module code
- Skim [`docs/WORKAROUNDS.md`](../WORKAROUNDS.md) for entries touching the same files/modules — known shortcuts may explain surprising code
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

**Doc drift:** when `src/`, `tests/`, or `docs/epics/` change — run `./scripts/check-doc-drift.sh` **before push** (P0; bash — use Git Bash on Windows); CI job **Doc drift** must be green. Script output is not a PR artefact — walk Gate 2 rows below mentally and tick the Gate 2 checkbox.

| Gate | Action |
|------|--------|
| **0** | AC map + docs touched (when `src/`, `tests/`, or `frontend/` change) |
| **1** | Full .NET + frontend verification (table below) |
| **2** | Doc walk-through (rows below) |
| **3** | Retrospective (seven questions) |

**CI-only gates** (run automatically on PR, no local action required): **Doc drift** (enforces same-PR docs, new-handler tests, no-new TODO/FIXME, new raw-SQL review, [WORKAROUND comment ↔ inventory sync](../WORKAROUNDS.md), [speculation guard](./docs-style.md#anti-patterns-dont-ship-these)) and **Markdown link check** (`lychee` — verifies internal links and `#anchors`). The [architecture fitness tests](../../tests/Architecture/Axis.Architecture.Tests/README.md) run as part of `dotnet test` — failures there mean a CLAUDE.md P0/P1 rule got violated structurally.

**Adding new CI checks — verify GitHub plan support first.** Some GitHub-native security workflows require **GitHub Advanced Security (GHAS)** on private repos (a paid add-on). On `phuong-labs/axis` this includes `actions/dependency-review-action` and CodeQL code-scanning *upload* (analysis runs, only the SARIF upload fails). Verify GHAS provisioning before adding such checks; otherwise the PR will fail and need a follow-up to disable. Dependabot security updates work on any plan and cover the same threat model with a publish-time delay — use it as the baseline. The disabled-job comment in [`.github/workflows/build-and-test.yml`](../../.github/workflows/build-and-test.yml) lists the specific jobs to restore when GHAS is provisioned.

**Priority:** Gate **1** blocks commit (failing build/tests). Gate **2** keeps docs in the same PR — required before merge, not a substitute for Gate 1. The [PR template](../../.github/PULL_REQUEST_TEMPLATE.md) lists Gate 1 before Gate 2.

### Gate 1 — verify before push (local = CI)

| Changed | Commands (all must pass when triggered) |
|---------|----------------------------------------|
| `src/` or `tests/` | `dotnet build` then `dotnet test` (full `Axis.sln` — includes Infrastructure, API, Testcontainers) |
| `src/` or `tests/` | `dotnet format --verify-no-changes` |
| `frontend/` | `npm run ci` then `npm run test` |
| `src/Axis.Api/Endpoints/` or API contract | Update + run `tests/Api/Axis.Api.Tests/` |
| Any of the above + `docs/epics/` | `./scripts/check-doc-drift.sh` (bash) — also enforces no-new `TODO`/`FIXME`/`stub` and reviews new raw-SQL calls |

```text
Gate 1 self-check:
- dotnet build → ran / not triggered (reason)
- dotnet test (full solution) → ran / not triggered (reason)
- dotnet format --verify-no-changes → ran / not triggered (reason)
- npm run ci + npm run test → ran / not triggered (reason)
- ./scripts/check-doc-drift.sh → ran / not triggered (reason)
```

Example (docs-only): every line `not triggered — no src/, tests/, or frontend/ changes`.

**Docker:** integration and API tests run as part of `dotnet test`; Docker must be available locally (same as CI runners with Testcontainers).

### Gate 2 — docs walk-through

Paste block format: header `Gate 2:` then one `-` line per row (Gate 3 uses the same bullet style).

```text
Gate 2:
- Library → TECH_STACK.md / not triggered
- New pattern → patterns.md / not triggered
- US layer callout → docs/epics/…/features/… / not triggered
- Epic README + PROGRESS → … / not triggered
- Architecture rule → CLAUDE.md / not triggered
- process.md workflow → … / not triggered
- Project structure → CLAUDE.md § Solution tree + process.md / not triggered
- Wireframe/diagram path move → grep docs/ / not triggered
- Program.cs host → patterns.md host section / not triggered
- Stale code comment → same file / not triggered
- Library rename → grep docs/ + src comments / not triggered
- Deferred follow-up → `**Deferred (PR #N follow-up):**` on affected US + PROGRESS if cross-cutting / not triggered
- Host wiring (`*Endpoints.cs` / `Program.cs`) → `Map*Endpoints` sweep in process.md / not triggered
```

**Deferred follow-ups (mandatory when leaving work open):** do not wait for the user. Any skipped review item, thin-endpoint refactor, or partial layer needs a named `**Deferred (...):**` line — full rules in [process.md § Deferred follow-up](process.md). Remove the line when fixed.


### Gate 3 — retrospective

Answer **Yes** or **No** on **each line** (same `-` bullet style as Gate 2 — do not collapse to `1–7 No`). If **Yes**, name the doc updated in this PR.

```text
Gate 3:
- New rule from test failure? → No
- Invented invariant without AC? → No
- Infrastructure footgun? → No
- Non-obvious test setup? → No
- Changed direction mid-task? → No
- Spec gap discovered? → No
- Incident-level detail in rule text? → No
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

**Chore/style PRs that touch module code:** drift still applies — add one small, accurate detail to the matching epic doc (a chunk size, a behavior nuance, a deferral note already true). Don't propose loosening the script, don't strand the format gunk waiting for a "real" PR, and don't invent fake content. The script's intent is *prompt the developer to look at docs*, not *require rewrite proportional to code change*.

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
