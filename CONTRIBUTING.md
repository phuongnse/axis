# Contributing to Axis

Thank you for contributing. This repo uses docs-first development: feature specs in `docs/epics/` are the contract; code implements them.

---

## Branch and commit

- **Base branch:** `main` — pull latest before creating a branch.
- **Branch names:** `{type}/{short-description}` in kebab-case.
  - `type` ∈ `feat` | `fix` | `docs` | `refactor` | `test` | `chore`
  - Example: `chore/docs-governance`, `feat/workflow-execution-api`
- **Do not push to `main`.** Open a pull request instead.
- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) — e.g. `feat: add execution cancel endpoint` (≤ 72 chars, imperative, no trailing period).
- **Cloud / auto branches:** If a tool creates a `cursor/...` branch, rename to the convention above before pushing unless you intentionally keep that name.

---

## Before you code

1. Read the epic README and feature file for the module you touch (`docs/epics/E0N-*/`).
2. Map every acceptance criterion to layer, file, and test (paste the AC map in the PR — see [agent-checklist.md](docs/playbooks/agent-checklist.md)).
3. Follow layer order for new work: Domain → Application → Infrastructure → API → Frontend.

---

## Before you open a PR

| Check | Command / action |
|-------|------------------|
| Build & tests (backend) | `dotnet build` then `dotnet test` (full solution) when `src/` or `tests/` changed |
| Build & tests (frontend) | `npm run ci` then `npm run test` when `frontend/` changed |
| Doc drift | Run `./scripts/check-doc-drift.sh` before push when `src/`, `tests/`, or `docs/epics/` change; CI job **Doc drift** must be green |
| Agent gates | Paste **Gates 0–3** from [agent-checklist.md](docs/playbooks/agent-checklist.md) (Gate 1: `not triggered` when docs-only) |

### Documentation in the same PR

- **Every US:** update `> **Implementation status**` in the feature file when you change that layer.
- **Layer complete for a module:** update the epic `README.md` status table.
- **Module summary:** update `docs/PROGRESS.md` (layer status only — not per-class or endpoint lists).
- **Module code change:** also change files under `docs/epics/{that-module}/` (enforced by `check-doc-drift.sh`).
- **New `*Handler.cs`:** add matching `*HandlerTests.cs` (enforced by drift script).
- **Patterns:** use [patterns-index.md](docs/playbooks/patterns-index.md); extend `patterns.md` only for Axis-specific rules.

---


### Updating the PR description

Keep the description **short**. Only three sections (see [PR template](.github/PULL_REQUEST_TEMPLATE.md)):

1. **Summary** — what the whole PR does (update only if scope changes)
2. **Commits** — one table row per commit; **append** rows, never drop earlier commits
3. **Requirements & rules followed** — checklist of what applies (CI, Gate 1, doc drift, docs same PR, spec → code, Gate 3)

Gates 0–3 in [agent-checklist.md](docs/playbooks/agent-checklist.md) are how you **verify** work before push; summarize the outcome in **Requirements**, do not paste long gate blocks into the PR.

Agents: read the existing PR body before `update_pr`; merge new commits and tick requirements — do not replace Summary with only the latest commit.

## Pull request template

Use [`.github/PULL_REQUEST_TEMPLATE.md`](.github/PULL_REQUEST_TEMPLATE.md). During implementation, follow [agent-checklist.md](docs/playbooks/agent-checklist.md) (Gates 0–3).

---

## Where to read more

| Doc | Purpose |
|-----|---------|
| [CLAUDE.md](CLAUDE.md) | Architecture rules and P0 stops |
| [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) | Daily agent workflow |
| [docs/playbooks/process.md](docs/playbooks/process.md) | Layer-by-layer implementation |
| [docs/playbooks/patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [docs/README.md](docs/README.md) | Documentation hub |
