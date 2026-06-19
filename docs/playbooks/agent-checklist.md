# Agent checklist (one page)

> **Navigation**: [← docs/README.md](../README.md) · [← AGENTS.md](../../AGENTS.md)

**Daily workflow.** Walk the Ready review, Docs review, and Retrospective review while implementing; run the Verification gate before marking a PR ready for review. Use `$axis-use-case-implementation` for use-case slices and `$axis-ready-review` before PR review. **PR description = Summary + Linked spec + Requirements only** — no review/check paste blocks, no commit list, no CI/Doc-drift status (GitHub Checks tab covers that).

**Skill routing.** API contracts: `$axis-api-contract`; cross-module contracts: `$axis-cross-module-contract`; frontend: `$axis-frontend-feature`; visuals: `$axis-visual-artifact`; review fixes: `$axis-review-feedback`.

**Large use cases:** split into **genuinely isolated PRs** (each branch from `main`, each passing the two-sided isolation test). See [pr-slicing.md](./pr-slicing.md) — never stack slice B on slice A's branch, never claim the Verification gate is green when you did not run it, and assign one owner per shared seam.

---

## Ready Review — before code

- **Design Gate** ([design-gate.md](./design-gate.md)): use `$axis-design-gate` before non-trivial code; **high-risk surfaces require user sign-off before code**
- AC map: every row has layer + file/test — **no blank cells**
- Read: domain README → use-case file → same-module code
- Skim [`docs/WORKAROUNDS.md`](../WORKAROUNDS.md) for entries touching the same files/modules — known shortcuts may explain surprising code
- Before API layer: `grep -r "Application: ⚠️\|Infrastructure: ⚠️" docs/use-cases/` — fix, defer with reason, or stop
- If the use case crosses the SPA/API boundary or depends on local-dev services (auth, email, provisioning, storage, Redis, Kafka, RabbitMQ, MailDev), start the Docker local-dev stack from [local-dev.md](./local-dev.md) during Ready review and include a smoke path in the plan. Do not wait until PR wrap-up to discover compose-only failures.
- End of PR: [process.md § PR wrap-up](process.md) — deferred lines, host wiring, callouts (no user reminder)

### AC coverage — avoid happy-path-only

Use-case files group ACs under **Happy path**, **Validation & errors**, **Edge cases**, and **Out of scope**. Cover every in-scope bullet for the layer you are shipping, not only the happy path.

**Ownership note (single source):** This file is the source of truth for AC/path coverage expectations. Other playbooks should link here instead of re-stating these rules.

**AC map:** before code, copy each `- [ ]` AC bullet into a row, tag it `happy` / `validation` / `edge` / `out-of-scope`, and name the proving test or handler. Mark cross-layer bullets explicitly (`N/A this PR — Frontend` / `N/A this PR — API`). No blank in-scope file/test cells.

**Path coverage matrix template** (fill once per touched implementation surface):

| Surface (endpoint/handler/repo/job/consumer) | Happy | Validation/Constraint | Auth/Permission | Not-found/Isolation | Dependency-failure | Notes |
|---|---|---|---|---|---|---|
| `...` | `test: ...` | `test: ...` | `test: ...` / `N/A` | `test: ...` / `N/A` | `test: ...` / `N/A` | deferral if any |

**During implementation:**

- [process.md § Per use case workflow](./process.md#per-use-case-workflow): Domain → Application → Infrastructure → API; tests green per layer before the next.
- [testing.md § Required test coverage](./testing.md#required-test-coverage-for-integration-tests): integration tests need happy path **and** not-found/isolation **and** constraint violations where applicable — not one happy test per handler.
- **Docker local-dev smoke:** when triggered by the Ready-review rule above, run compose early enough to influence implementation. Minimum evidence: `/health`, `/health/ready`, the route/endpoint under test, and the observable dependency effect.

| Check | Action |
|-------|--------|
| Map complete? | Every in-scope AC row has code + test or an exact deferral. |
| Path matrix complete? | Each touched endpoint/handler/repo/job/consumer covers happy, validation, auth/permission, not-found/isolation, and dependency-failure paths, or marks `N/A`. |
| Callout honest? | `> **Implementation status**` lists remaining bullets under `Gaps vs spec`; never mark a layer ✅ with open in-scope gaps. |
| Deferred? | `**Deferred follow-ups:**` names the exact AC bullet deferred, not a vague “later”. |

Self-audit before review: re-read the use case and tick each in-scope bullet against the AC map in spec order.

---

## Automated gates and review checkpoints

Use the terms from [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md#enforcement-taxonomy). "Gate" means a command, build, or CI job can fail the PR. Ready review, Docs review, Retrospective review, and Design Gate are required review artifacts/checkpoints unless a deterministic subset is listed below as CI-enforced.

**Doc drift:** CI runs `python scripts/axis.py check policy-tests` and `python scripts/axis.py check doc-drift` on every PR. Run them locally when touching docs, scripts, repo layout, handlers, endpoints, generated-contract surfaces, or bulk file rewrites. This job enforces deterministic policy/doc checks; it does not require a docs edit for every code diff.

| Item | Type | Action |
|------|------|--------|
| **Ready review** | Review-only | AC map + docs identified when shipping behavior |
| **Verification gate** | Enforced | Local ready-PR verification; CI/branch protection owns the full suite |
| **Docs review** | Review-only | Docs walkthrough when behavior/spec/status changes |
| **Retrospective review** | Review-only | Retrospective and REVIEW_FINDINGS update when a rule/finding repeats |

**CI-enforced checks** (run automatically on PR, no local action required unless debugging):

- **PR guard** — [`scripts/check-pr.py`](../../scripts/check-pr.py) validates PR metadata shape and checkbox self-attestation. It cannot prove that review-only checkpoints happened.
- **Policy gate tests** — `python scripts/axis.py check policy-tests` runs counterexample tests for custom Python gates so a regex/path change cannot silently disable enforcement.
- **Doc drift** — runs deterministic docs/policy/layout checks: text encoding, module/API discovery, governance registry/owner-boundary/truth wiring, handler/test ratchets, endpoint DTO ratchets, workaround sync, doc hygiene, script standards, and layout checks (`buf`, Kafka wiring, domain README index). [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md) owns finding-class status; script output owns the exact failing guard.
- **Markdown link check** — `lychee` verifies internal links and `#anchors`; the required version is owned by [scripts.md § Tool Versions](./scripts.md#tool-versions), and the local command is `python scripts/axis.py check markdown-links`. **Relative file/image targets** (`![alt](./asset.svg)`, `[text](./file.md)`) are double-checked by `python scripts/axis.py check doc-link-targets` inside the drift command — catches the broken-image class lychee misses.
- **Doc navigation** — `python scripts/axis.py check doc-navigation` requires every `docs/**/*.md` file to start with an H1 and a `> **Navigation**:` block so docs never become dead ends.
- **Doc size budgets** — `python scripts/axis.py check doc-size-budgets` keeps reference docs/playbooks within the budgets in [docs-style.md](./docs-style.md#size-budgets), including the tighter pattern-router budget.
- **Code-fence integrity** — `python scripts/axis.py check doc-code-fences` (inside the drift command) flags code-block lines with collapsed indentation (a lone leading space). Catches the bulk-find-replace corruption class that lychee, prettier, and the structural checks all let through.
- **Use-case docs** — `python scripts/axis.py check use-case-docs` validates use-case file structure (required sections + tables + status callout), flags template placeholders (`_(One sentence...)_`, `_(Actor)_`, `_(What starts...)_`), flags self-links `[name](./README.md)` and truncated summary rows in domain READMEs, and counts use cases still on the stock Main flow.
- **Codex skill metadata** — `python scripts/axis.py check codex-skills` validates repo-scoped `.agents/skills/*/SKILL.md` frontmatter, concise bodies, doc references, required skill chaining, concrete wording, and UI metadata/default prompts.
- **Secret scanning** — TruffleHog scans the full PR diff for committed secrets (API keys, passwords, tokens) and verifies each finding against the alleged service before reporting (`--only-verified` cuts false positives).
- **Vulnerable packages** — `python scripts/axis.py check vulnerable-packages` wraps `dotnet list package --vulnerable --include-transitive` and fails on any known CVE in the dep tree (covers transitive packages too).
- **Architecture fitness tests** run as part of `dotnet test` — failures there mean a AGENTS.md P0/P1 rule got violated structurally. See [tests README](../../tests/Architecture/Axis.Architecture.Tests/README.md).
- **EF migrations** — drift verifies each migration `.cs` has its `.Designer.cs`. The command used to create the migration remains review-owned. See [local-dev.md § EF Core migrations](./local-dev.md#ef-core-migrations-dotnet-ef).
- **Local dev docs** — [`docker-compose.yml`](../../docker-compose.yml) changes require [`docs/playbooks/local-dev.md`](./local-dev.md) in the same PR; CI runs `python scripts/axis.py check local-dev-docs`.
- **Async-safety analyzers** (`Microsoft.VisualStudio.Threading.Analyzers`) — type-aware checks at build time for sync-over-async (VSTHRD002), async-void (VSTHRD100), unobserved async results (VSTHRD110). Rule selection rationale in [runtime patterns § Async patterns](./runtime-patterns.md#async-patterns).
- **Coverage report** uploaded as artifact (`dotnet-coverage`). No threshold yet — see [CONTRIBUTING.md § Coverage](../../CONTRIBUTING.md#coverage).

**Adding new CI checks — verify GitHub plan support first.** Some GitHub-native security workflows require **GitHub Advanced Security (GHAS)** on private repos (a paid add-on). On `phuong-labs/axis` this includes `actions/dependency-review-action` and CodeQL code-scanning *upload* (analysis runs, only the SARIF upload fails). Verify GHAS provisioning before adding such checks; otherwise the PR will fail and need a follow-up to disable. Dependabot security updates work on any plan and cover the same threat model with a publish-time delay — use it as the baseline. The disabled-job comment in [`.github/workflows/build-and-test.yml`](../../.github/workflows/build-and-test.yml) lists the specific jobs to restore when GHAS is provisioned.

**Priority:** the Verification gate and CI-enforced checks block merge. Ready, Docs, and Retrospective reviews are review-only self-audits captured by the PR checklist.

### Verification Gate — verify before PR review

**One command:** `python scripts/axis.py verify` runs the local ready-PR gate — build + vulnerable package scan + `dotnet format --verify` + **unit test projects only** + frontend `ci`/test + markdown link check when markdown changed + drift. It only runs the layers whose files changed (so doc-only and frontend-only work stays quick).

Run `python scripts/axis.py bootstrap` once to install the committed **pre-push hook** explicitly (`core.hooksPath = scripts/hooks`); build commands must not mutate Git config. The hook runs `python scripts/axis.py pre-push`, a quick policy/doc sanity gate for ordinary network pushes. It intentionally does not run the full local Verification gate on every push. Set `AXIS_PRE_PUSH_FULL=1` when you explicitly want the hook to run `python scripts/axis.py verify` before pushing.

CI/branch protection remains the authoritative full gate and runs full `dotnet test` including Testcontainers before merge.

The .NET branch of `python scripts/axis.py verify` also runs the enforced
`{Subject}_{Condition}_{ExpectedOutcome}` test-name check.

**Development loop vs enforcement:** while implementing, run the narrow check for the surface you are changing; do not repeatedly run `python scripts/axis.py verify` after every small edit. The pre-push hook gives fast feedback before a network push; `python scripts/axis.py verify` is the local enforcement point before requesting review; CI/branch protection runs the full suite before merge.

| During development | Prefer |
|--------------------|--------|
| Process/docs change | `python scripts/axis.py check doc-drift`; for Markdown links/anchors also run `python scripts/axis.py check markdown-links` |
| Test change | `python scripts/axis.py check test-naming`; for project changes also run `python scripts/axis.py check test-project-classification` and `python scripts/axis.py test unit` |
| Frontend change | `npm run ci` and/or `npm run test` |
| Backend compile-sensitive change | `dotnet build` or the directly affected test project |
| Review fix touching a rule/guard | The specific guard for that rule, then run `python scripts/axis.py verify` before requesting review |

| Changed | Commands (all must pass when triggered) |
|---------|----------------------------------------|
| `tests/` | `python scripts/axis.py check test-naming` |
| `src/` or `tests/` | `dotnet build` then `python scripts/axis.py test unit` (auto-discovers `*.Domain.Tests` and `*.Application.Tests`) |
| `src/`, `tests/`, dependency props, or API contract | `python scripts/axis.py check vulnerable-packages` |
| `src/` or `tests/` | `dotnet format --verify-no-changes` |
| `frontend/` | `npm run ci` then `npm run test` |
| `src/Axis.Api/Endpoints/` or API contract | Update + run `tests/Api/Axis.Api.Tests/` |
| Docs/scripts/layout/policy change | `python scripts/axis.py check policy-tests` then `python scripts/axis.py check doc-drift`; for Markdown/`lychee.toml` changes also run `python scripts/axis.py check markdown-links` |
| `docker-compose.yml` | Update [local-dev.md](./local-dev.md) in same PR; `python scripts/axis.py check doc-drift` |

When reporting verification, state each triggered command as `ran`, `not triggered` with reason, or `failed` with the blocker. `$axis-ready-review` provides the repeatable reporting shape.

**Full suite:** integration and API tests run in CI as part of full `dotnet test`; Docker/Testcontainers is required there. Run full local `dotnet test Axis.sln --nologo` when debugging CI, changing Infrastructure/API behavior, or preparing a high-risk backend PR. If Docker is not visible to the process running .NET, use the [local-dev Docker endpoint adapter](./local-dev.md#docker-endpoint-adapter) and report the execution context instead of rewriting verification commands.

### Docs Review

Use `$axis-ready-review` for the walkthrough. Mark pure refactor/style/test-only changes as `not triggered` instead of inventing docs churn.

Common owner mapping:

| Trigger | Owner |
|---|---|
| Library or stack change | [TECH_STACK.md](../TECH_STACK.md) |
| New pattern or repeated finding | Focused owner from [patterns-index.md](./patterns-index.md) or [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md) |
| Behavior/spec/status change | Owning use-case callout, domain README, and [PROGRESS.md](../PROGRESS.md) when their summaries change |
| Repo layout, module, event, proto, API group | [repo-layout-discovery.md](./repo-layout-discovery.md) |
| Frontend screen or use-case visual | Owning use-case wireframe/diagram section and [visual-artifact-checklist.md](./visual-artifact-checklist.md) |
| Intentional P0/P1 shortcut | [WORKAROUNDS.md](../WORKAROUNDS.md) plus site reference |

**Deferred follow-ups (mandatory when leaving work open):** do not wait for the user. Any skipped review item, thin-endpoint refactor, or partial layer needs a named `**Deferred follow-ups:**` line — full rules in [process.md § Deferred follow-up](process.md). Remove the line when fixed.

### Review feedback (CodeRabbit / human)

Use `$axis-review-feedback`. Apply fixes before resolving threads. Bots are **signal**, not authority — validate against the focused owner from [patterns-index.md](./patterns-index.md) and [AGENTS.md](../../AGENTS.md).

Classify each comment as fixed, improved beyond suggestion, false positive with evidence, or deferred with owner. Prefer the design you would defend in review; when the user explicitly asks for the smallest change, say `Review fixes: minimal — <why>` in the PR Summary. For non-trivial improvements, say `Review fixes: improved — <what>`.

### Retrospective Review

Answer **Yes** or **No** on **each line** (same `-` bullet style as Docs review — do not collapse to a single `No`). If **Yes**, name the doc, test, or ledger row updated in this PR.

```text
Retrospective review:
- New rule from test failure? → No
- Invented invariant without AC? → No
- Infrastructure footgun? → No
- Non-obvious test setup? → No
- Changed direction mid-task? → No
- Review-driven change left as a shortcut when a better design was feasible? → No
- Spec gap discovered? → No
- Incident-level detail in rule text? → No
- Repeat of a prior review finding class? → No
```

If the last line is **Yes**, record the class in [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md): mark it Enforced, Partial, Review-only, Guidance, or Not a rule. A finding class should be reviewed once, then prevented or explicitly kept human-owned.

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
| **1 — Use case** | Any layer progress on a use case | `> **Implementation status**`, `Gaps vs spec`, optional `**Deferred follow-ups:**` in `docs/use-cases/{domain}/*.md` |
| **2 — Domain** | A layer is complete for the module | Domain `README.md` implementation table + **Open work (agents)** section (remove or reword items you closed) |
| **3 — Platform** | Module-wide summary changed | `docs/PROGRESS.md` — layer status only |

Updating only `PROGRESS.md` while changing `src/` without a use-case callout still fails because platform status needs an owning source. Domain README pending API status after endpoints ship also fails.

**Agents starting a task:** read [use cases README § How agents find open work](../use-cases/README.md#how-agents-find-open-work) — checkboxes in use-case files are not progress.

---

## Review-only project expectations

These expectations still matter, but do not call them CI gates unless [REVIEW_FINDINGS.md](../REVIEW_FINDINGS.md) marks the class **Enforced**.

- Spec → code, never the reverse.
- No cross-module SQL / shared `DbContext` / `IMediator` for domain events. Structural subsets are enforced; semantic SQL and runtime DI remain review-only.
- Changed `*Handler.cs` → matching `*HandlerTests.cs`. The diff ratchet enforces changed Application handlers; untouched legacy files are not swept.
- Behavior/spec/status changes → update the owning docs in the same PR. Pure refactor, style, dependency, and test-only changes do not need a token docs edit.
- Frontend screen → wireframe row in the owning use-case `## Wireframes` table when the screen changes.
- Use-case diagram → row only if the `.excalidraw` lives **in that use-case folder**; link other use cases in `**Related:**` prose, not in `## Diagrams` table.
- No test `Skip = ...`, weakened tests, or completed layer status when ACs are open. New test skips are enforced; weakened assertions/status honesty remain review-only.
- **Full suite honesty:** local `python scripts/axis.py verify` uses the ready-PR Verification gate command matrix; CI/branch protection runs full `dotnet test Axis.sln`. If you claim the full suite ran locally, it must be full `Axis.sln` with integration/API tests, not a solution filter or unit-only run.

---

## Domain layout discovery

**Full rules + agent checklists:** [repo-layout-discovery.md](./repo-layout-discovery.md) (auto vs manual tables, commands, checklists A–E).

**Summary:** [`doc_drift_domains.py`](../../scripts/doc_drift_domains.py) validates that module folders and endpoint groups map to existing `docs/use-cases/{slug}/` domains. It does not require a token docs edit for every module-code change; behavior/spec/status doc accuracy is Docs review.

---

## Playbooks (open when needed)

| Need | File |
|------|------|
| Layer order, TDD, gap sweep, deferred docs, PR wrap-up | [process.md](./process.md) |
| New module / event / proto / domain README — what to update & how CI checks | [repo-layout-discovery.md](./repo-layout-discovery.md) |
| Find the right patterns section | [patterns-index.md](./patterns-index.md) |
| EF, API, Wolverine, workspace isolation | [patterns-index.md](./patterns-index.md) routes to the focused owner doc |
| React, Query, a11y | [frontend.md](./frontend.md) |
| Tests, Testcontainers | [testing.md](./testing.md) |
| Wireframe kit | [wireframes.md](./wireframes.md) · **Agent contract:** [wireframes/README § Agent contract](../wireframes/README.md#agent-contract) |
