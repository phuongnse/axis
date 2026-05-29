# Agent checklist (one page)

> **Navigation**: [← docs/README.md](../README.md) · [← CLAUDE.md](../../CLAUDE.md)

**Daily workflow.** Walk Gates 0–3 **locally** while implementing; reflect outcomes in the [PR template](../../.github/PULL_REQUEST_TEMPLATE.md) checkboxes. **PR description = Summary + Linked spec + Requirements only** — no Gate paste blocks, no commit list, no CI/Doc-drift status (GitHub Checks tab covers that).

The paste-block templates below are for *your own* walk-through (agent reasoning, scratchpad, or PR thread comment if asked) — not for the PR description.

---

## Gate 0 — Ready (before code)

- AC map: every row has layer + file/test — **no blank cells**
- Read: domain README → use-case file → same-module code
- Skim [`docs/WORKAROUNDS.md`](../WORKAROUNDS.md) for entries touching the same files/modules — known shortcuts may explain surprising code
- Before API layer: `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/use-cases/` — fix, defer with reason, or stop
- End of PR: [process.md § PR wrap-up](process.md) — deferred lines, host wiring, callouts (no user reminder)

```markdown
## Gate 0
| AC / use case | Layer | File / test |
|---------|-------|-------------|
| …       | …     | …           |
Docs touched: docs/use-cases/…
```

### AC coverage — avoid happy-path-only

Use-case files group ACs under **Happy path**, **Validation & errors**, **Edge cases**, and **Out of scope**. Agents must cover **every bullet in scope for the layer you are shipping**, not only the first block.

**Ownership note (single source):** This file is the source of truth for AC/path coverage expectations. Other playbooks should link here instead of re-stating these rules.

**Before writing code (per use case):**

1. Copy **each** `- [ ]` line from the use case into an AC map row (one row per bullet, or one row per bullet group only when a single test proves all of them).
2. Tag the row: `happy` | `validation` | `edge` | `out-of-scope` (skip implementation for `out-of-scope`; do not “forget” it — leave it in the map as N/A).
3. Name the **test or handler** that will prove the row (`CreateWorkflow_WhenAtPlanLimit_Returns402`, integration test for wrong-tenant isolation, etc.). **No blank “File / test” cells** for in-scope rows.
4. If a bullet is **Frontend-only** while you are on backend (or the reverse), mark the row `N/A this PR — Frontend` / `N/A this PR — API` so it is not silently dropped.

**Path coverage matrix template** (fill once per touched implementation surface):

| Surface (endpoint/handler/repo/job/consumer) | Happy | Validation/Constraint | Auth/Permission | Not-found/Isolation | Dependency-failure | Notes |
|---|---|---|---|---|---|---|
| `...` | `test: ...` | `test: ...` | `test: ...` / `N/A` | `test: ...` / `N/A` | `test: ...` / `N/A` | deferral if any |

**While implementing (TDD):**

- [process.md § Per use case workflow](./process.md#per-use-case-workflow): Domain → Application → Infrastructure → API; tests green per layer before the next.
- [testing.md § Required test coverage](./testing.md#required-test-coverage-for-integration-tests): integration tests need happy path **and** not-found/isolation **and** constraint violations where applicable — not one happy test per handler.

**Before opening / updating the PR:**

| Check | Action |
|-------|--------|
| Map complete? | Every in-scope AC row has code + test (or explicit deferral). |
| Callout honest? | `> **Implementation status**` lists remaining bullets under `Gaps vs spec` — never ✅ on a layer with open backend gaps. |
| Deferred? | `**Deferred (PR #N follow-up):**` names the **AC bullet** deferred, not a vague “later”. |
| Out of scope? | Do not implement; do not mark ✅ as if done. |

**Self-audit command** (after implementation, before push): re-read the use case in the use-case file and tick mentally each bullet against your AC map — same order as the spec (happy → validation → edge).

### Anti-pattern: `Gaps vs spec: none` after happy path only

Do **not** mark a layer ✅ or write `Gaps vs spec: none for backend` because the main API flow works. That is not Gate 0 complete.

| Wrong | Right |
|-------|--------|
| Ship register → settings → delete endpoints, then claim backend ✅ | AC map row per bullet (happy, validation, edge) with file/test or explicit deferral |
| Wait for the user to ask “đã cover đủ AC chưa?” | Run the self-audit **before the first PR push** — that question is the agent’s job |
| Fix gaps only in a follow-up commit after review | Same PR when possible; otherwise `**Deferred (PR #N):**` + **exact AC bullet text** in the feature callout |

**Before push checklist (backend feature PRs):**

1. Re-read every in-scope `- [ ]` under the use case (all sections, not only *Happy path*).
2. For each bullet: implemented + test, `N/A this PR — Frontend`, or named deferral.
3. Only then set `Gaps vs spec: none for backend` (or list what remains).
4. For **every implementation surface** touched in this PR (API endpoint, application handler, gRPC method, repository, background job, consumer), verify path coverage is explicit:
   - valid request/flow (happy path),
   - validation/constraint failure path,
   - authz/authn or permission boundary where applicable,
   - not-found and tenant/isolation boundary where applicable (no data leak),
   - downstream dependency failure path where applicable (transport/storage/service unavailable).
   If a path does not apply to that surface, mark it `N/A` in the AC map instead of skipping it silently.

**Lesson (platform-foundation organization management):** A first pass shipped profile/settings/deletion APIs but missed Redis usage TTL (≤5 min), schedule rollback on queue failure, hard-delete purge, and form-task cancel — caught by spec review, not by “flow works.” See [organization-management callouts](../use-cases/platform-foundation/README.md) for what “done” looks like after self-audit.

---

## Gates (every PR)

**Doc drift:** when `src/`, `tests/`, or `docs/use-cases/` change — run `./scripts/check-doc-drift.sh` **before push** (P0; bash — use Git Bash on Windows); CI job **Doc drift** must be green. When `docker-compose.yml` changes, update [local-dev.md](./local-dev.md) in the same PR (`check-local-dev-docs.py` runs inside the drift script). Script output is not a PR artefact — walk Gate 2 rows below mentally and tick the Gate 2 checkbox.

| Gate | Action |
|------|--------|
| **0** | AC map + docs touched (when `src/`, `tests/`, or `frontend/` change) |
| **1** | Full .NET + frontend verification (table below) |
| **2** | Doc walk-through (rows below) |
| **3** | Retrospective (questions below) |

**CI-only gates** (run automatically on PR, no local action required):

- **Doc drift** — enforces same-PR docs, new-handler tests, no-new TODO/FIXME, new raw-SQL review, [WORKAROUND comment ↔ inventory sync](../WORKAROUNDS.md), [speculation guard](./docs-style.md#anti-patterns-dont-ship-these), `GetAwaiter().GetResult()` ban, hardcoded connection-string ban, `DateTime.Now` ban (use `UtcNow`), and a stale-terminology guard (current pattern list lives in [`scripts/check-doc-drift.sh`](../../scripts/check-doc-drift.sh) — search for `STALE_TERM_PATTERN`). **Module/API → use-case domain** — [`doc_drift_domains.py`](../../scripts/doc_drift_domains.py) + [`axis_repo.py`](../../scripts/axis_repo.py). **Layout drift** in the same job: [`sync_buf_yaml.py --check`](../../scripts/sync_buf_yaml.py), [`check_kafka_wiring.py`](../../scripts/check_kafka_wiring.py), [`regenerate-domain-readme-index.py --check`](../../scripts/regenerate-domain-readme-index.py).
- **Markdown link check** — `lychee` verifies internal links and `#anchors`. **Relative file/image targets** (`![alt](./asset.svg)`, `[text](./file.md)`) are double-checked by [`scripts/check-doc-link-targets.py`](../../scripts/check-doc-link-targets.py) inside the drift script — catches the broken-image class lychee misses.
- **Code-fence integrity** — [`scripts/check-doc-code-fences.py`](../../scripts/check-doc-code-fences.py) (inside the drift script) flags code-block lines with collapsed indentation (a lone leading space). Catches the bulk-find-replace corruption class that lychee, prettier, and the structural checks all let through.
- **Use-case docs** — [`scripts/check-use-case-docs.py`](../../scripts/check-use-case-docs.py) validates use-case file structure (required sections + tables + status callout), flags template placeholders (`_(One sentence...)_`, `_(Actor)_`, `_(What starts...)_`), flags self-links `[name](./README.md)` and truncated summary rows in domain READMEs, and counts use cases still on the stock Main flow.
- **Secret scanning** — TruffleHog scans the full PR diff for committed secrets (API keys, passwords, tokens) and verifies each finding against the alleged service before reporting (`--only-verified` cuts false positives).
- **Vulnerable packages** — `dotnet list package --vulnerable --include-transitive` fails on any known CVE in the dep tree (covers transitive packages too).
- **Architecture fitness tests** run as part of `dotnet test` — failures there mean a CLAUDE.md P0/P1 rule got violated structurally. See [tests README](../../tests/Architecture/Axis.Architecture.Tests/README.md).
- **EF migrations** — only `dotnet ef migrations add` (no hand-written `.cs` / orphan `.Designer.cs`). Each `{Name}.cs` needs `{Name}.Designer.cs`. See [local-dev.md § EF Core migrations](./local-dev.md#ef-core-migrations-dotnet-ef).
- **Local dev docs** — [`docker-compose.yml`](../../docker-compose.yml) changes require [`docs/playbooks/local-dev.md`](./local-dev.md) in the same PR; CI runs [`scripts/check-local-dev-docs.py`](../../scripts/check-local-dev-docs.py).
- **Async-safety analyzers** (`Microsoft.VisualStudio.Threading.Analyzers`) — type-aware checks at build time for sync-over-async (VSTHRD002), async-void (VSTHRD100), unobserved async results (VSTHRD110). Rule selection rationale in [patterns.md § Async patterns](./patterns.md#async-patterns).
- **Coverage report** uploaded as artifact (`dotnet-coverage`). No threshold yet — see [CONTRIBUTING.md § Coverage](../../CONTRIBUTING.md#coverage).

**Adding new CI checks — verify GitHub plan support first.** Some GitHub-native security workflows require **GitHub Advanced Security (GHAS)** on private repos (a paid add-on). On `phuong-labs/axis` this includes `actions/dependency-review-action` and CodeQL code-scanning *upload* (analysis runs, only the SARIF upload fails). Verify GHAS provisioning before adding such checks; otherwise the PR will fail and need a follow-up to disable. Dependabot security updates work on any plan and cover the same threat model with a publish-time delay — use it as the baseline. The disabled-job comment in [`.github/workflows/build-and-test.yml`](../../.github/workflows/build-and-test.yml) lists the specific jobs to restore when GHAS is provisioned.

**Priority:** Gate **1** blocks commit (failing build/tests). Gate **2** keeps docs in the same PR — required before merge, not a substitute for Gate 1. The [PR template](../../.github/PULL_REQUEST_TEMPLATE.md) lists Gate 1 before Gate 2.

### Gate 1 — verify before push (local = CI)

| Changed | Commands (all must pass when triggered) |
|---------|----------------------------------------|
| `src/` or `tests/` | `dotnet build` then `dotnet test` (full `Axis.sln` — includes Infrastructure, API, Testcontainers) |
| `src/` or `tests/` | `dotnet format --verify-no-changes` |
| `frontend/` | `npm run ci` then `npm run test` |
| `src/Axis.Api/Endpoints/` or API contract | Update + run `tests/Api/Axis.Api.Tests/` |
| Any of the above + `docs/use-cases/` | `./scripts/check-doc-drift.sh` (bash) — also runs `check-use-case-docs.py --check` and `check-local-dev-docs.py`, enforces no-new `TODO`/`FIXME`/`stub`, reviews new raw-SQL calls |
| `docker-compose.yml` | Update [local-dev.md](./local-dev.md) in same PR; `./scripts/check-doc-drift.sh` |

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
- Use-case layer callout → docs/use-cases/{domain}/… (layout per [docs-style § Use case files](./docs-style.md#use-case-files-flow-first)) / not triggered
- Domain README + PROGRESS → … / not triggered
- Architecture rule → CLAUDE.md / not triggered
- process.md workflow → … / not triggered
- Project structure → CLAUDE.md § Solution tree + process.md / not triggered
- Wireframe/diagram path move → grep docs/ / not triggered
- Program.cs host → patterns.md host section / not triggered
- Stale code comment → same file / not triggered
- Library rename → grep docs/ + src comments / not triggered
- Deferred follow-up → `**Deferred (PR #N follow-up):**` on affected US + PROGRESS if cross-cutting / not triggered
- Host wiring (`*Endpoints.cs` / `Program.cs`) → `Map*Endpoints` sweep in process.md / not triggered
- Repo layout (module, event, proto, domain README) → [repo-layout-discovery.md](./repo-layout-discovery.md) checklists A–E / not triggered
```

**Deferred follow-ups (mandatory when leaving work open):** do not wait for the user. Any skipped review item, thin-endpoint refactor, or partial layer needs a named `**Deferred (...):**` line — full rules in [process.md § Deferred follow-up](process.md). Remove the line when fixed.

### Review feedback (CodeRabbit / human)

Apply **before** resolving review threads (no user reminder required). Bots are **signal**, not authority — validate against [patterns.md](./patterns.md) and [CLAUDE.md](../../CLAUDE.md).

Do **not** ship the first diff that only makes CI green or closes the thread. For each comment, ask:

1. **Is this already best practice** for this codebase (patterns, layer boundaries, siblings in the same module)?
2. **Can I improve or enhance** beyond what the reviewer suggested (clearer ownership, fewer round-trips, one transaction boundary, consistent error handling)?
3. If a better design is feasible but skipped, is that a deliberate **minimal diff** (user asked) or should it be **`**Deferred (...):**`**?

**Default:** prefer the design you would defend in review. **Exception:** user explicitly requests the smallest change — say so in the PR Summary.

**PR Summary (one line when review fixes are non-trivial):** `Review fixes: improved — <what>` or `Review fixes: minimal — <why>`.

**Examples** (illustrative — not an exhaustive list): splitting an invariant across “mutate then query”; external calls with catch-only patches; duplicating logic to silence a linter. In those cases, look for a single owner of the invariant or parity with existing guards/handlers in the repo.

### Gate 3 — retrospective

Answer **Yes** or **No** on **each line** (same `-` bullet style as Gate 2 — do not collapse to a single `No`). If **Yes**, name the doc updated in this PR.

```text
Gate 3:
- New rule from test failure? → No
- Invented invariant without AC? → No
- Infrastructure footgun? → No
- Non-obvious test setup? → No
- Changed direction mid-task? → No
- Review-driven change left as a shortcut when a better design was feasible? → No
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

Never ✅ and "pending …" in the same callout. Checkboxes in use-case files are spec-only — **do not** tick them.

### Status updates (three levels — same PR)

| Level | When | What to write |
|-------|------|----------------|
| **1 — Use case** | Any layer progress on a use case | `> **Implementation status**`, `Gaps vs spec`, optional `**Deferred (PR #N follow-up):**` in `docs/use-cases/{domain}/*.md` |
| **2 — Domain** | A layer is complete for the module | Domain `README.md` implementation table + **Open work (agents)** section (remove or reword items you closed) |
| **3 — Platform** | Module-wide summary changed | `docs/PROGRESS.md` — layer status only |

Updating only `PROGRESS.md` while changing `src/` without `docs/use-cases/` → drift fails. Domain README `| API | ⏳` after endpoints ship → drift fails.

**Agents starting a task:** read [use cases README § How agents find open work](../use-cases/README.md#how-agents-find-open-work) — checkboxes in use-case files are not progress.

**Chore/style PRs that touch module code:** drift still applies — add one small, accurate detail to the matching domain doc (a chunk size, a behavior nuance, a deferral note already true). Don't propose loosening the script, don't strand the format gunk waiting for a "real" PR, and don't invent fake content. The script's intent is *prompt the developer to look at docs*, not *require rewrite proportional to code change*.

---

## P0 (CI + culture)

- Spec → code, never the reverse
- No cross-module SQL / shared `DbContext` / `IMediator` for domain events
- New `*Handler.cs` → `*HandlerTests.cs` (drift script)
- Module code → `docs/use-cases/{module}/` in **same PR**
- Frontend screen → wireframe row in use-case `## Wireframes` table
- No `.Skip()`, weakened tests, or ✅ when ACs are open
- **Full solution only:** always `dotnet build` + `dotnet test` on `Axis.sln` (no solution filter)

---

## Domain map (code → docs)

**Full rules + agent checklists:** [repo-layout-discovery.md](./repo-layout-discovery.md) (auto vs manual tables, commands, checklists A–E).

**Summary:** [`doc_drift_domains.py`](../../scripts/doc_drift_domains.py) maps `src/Modules/*` and `*Endpoints.cs` → `docs/use-cases/{slug}/`. New module → create domain folder (or `MODULE_DOMAIN_SLUG_OVERRIDES` in [`axis_repo.py`](../../scripts/axis_repo.py) for `Identity` → `identity-access`). Cross-cutting only in `EXTRA_CODE_TO_DOC_RULES`.

| Manual exception | Docs folder |
|------------------|-------------|
| `OrganizationVerifiedHandler` in any module | `docs/use-cases/platform-foundation/` |
| `frontend/src/features/auth`, `routes/`, `AppShell` | `docs/use-cases/identity-access/` |

---

## Playbooks (open when needed)

| Need | File |
|------|------|
| Layer order, TDD, gap sweep, deferred docs, PR wrap-up | [process.md](./process.md) |
| New module / event / proto / domain README — what to update & how CI checks | [repo-layout-discovery.md](./repo-layout-discovery.md) |
| Find the right patterns section | [patterns-index.md](./patterns-index.md) |
| EF, API, Wolverine, tenancy | [patterns.md](./patterns.md) |
| React, Query, a11y | [frontend.md](./frontend.md) |
| Tests, Testcontainers | [testing.md](./testing.md) |
| Wireframe kit | [wireframes.md](./wireframes.md) |
