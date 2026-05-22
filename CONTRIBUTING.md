# Contributing to Axis

Docs-first development: feature specs in `docs/epics/` are the contract; code implements them. The full agent and human workflow lives in [CLAUDE.md](CLAUDE.md) and [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) — this file is the short pointer for first-time contributors.

---

## Branch and commit

- **Base branch:** `main` — pull latest before branching. Never push directly to `main`.
- **Branch names:** `{type}/{short-description}` in kebab-case. `type` ∈ `feat` · `fix` · `docs` · `refactor` · `test` · `chore`.
- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) — imperative, ≤ 72 chars, no trailing period (e.g. `feat: add execution cancel endpoint`).
- **Tool-named branches** (`cursor/...`, etc.): rename to the convention above before pushing unless you intentionally keep that name.

## Before you push

1. Walk **Gates 0–3** in [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) locally; tick the matching boxes in the PR body.
2. When `src/`, `tests/`, or `docs/epics/` change: run `./scripts/check-doc-drift.sh` (bash — use Git Bash on Windows). CI job **Doc drift** must be green.
3. PR description: **Summary + Linked spec + Requirements only** — no commit list, no CI status (the Checks tab covers that). Template: [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md).

## Where to read more

| Doc | Purpose |
|-----|---------|
| [CLAUDE.md](CLAUDE.md) | Architecture rules, P0 stops, machine rules |
| [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) | Daily workflow + Gates 0–3 |
| [docs/playbooks/process.md](docs/playbooks/process.md) | Layer-by-layer implementation + deferred follow-ups |
| [docs/playbooks/patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [docs/README.md](docs/README.md) | Documentation hub + single source of truth per topic |
