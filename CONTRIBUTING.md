# Contributing to Axis

Docs-first development: feature specs in `docs/use-cases/` are the contract; code implements them. The full agent and human workflow lives in [CLAUDE.md](CLAUDE.md) and [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) - this file is the short pointer for first-time contributors.

---

## Branch and commit

- **Base branch:** `main` - pull latest before branching. Never push directly to `main`.
- **Branch names:** `{type}/{short-description}` in kebab-case. `type` is one of `feat`, `fix`, `docs`, `refactor`, `test`, `chore`.
- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) - imperative, max 72 chars, no trailing period (e.g. `feat: add execution cancel endpoint`).
- **Tool-named branches** (`cursor/...`, etc.): rename to the convention above before pushing unless you intentionally keep that name.

## Before you push

Install the local hook once with `python scripts/axis.py bootstrap`, then use `python scripts/axis.py verify` for the fast pre-push gate. During implementation, prefer targeted checks for the surface you are editing; the hook is the local enforcement point before push. The authoritative Gate 1 policy and command matrix live in [agent-checklist.md § Gate 1](docs/playbooks/agent-checklist.md#gate-1--verify-before-push-fast-local-gate); unit-only feedback is available via `python scripts/axis.py test unit`. Script standards live in [scripts.md](docs/playbooks/scripts.md).

1. Walk **Gates 0-3** in [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) locally; tick the matching boxes in the PR body.
2. When you touch C# under `src/` or `tests/`, run `dotnet format Axis.sln` - style and naming rules live in [`.editorconfig`](.editorconfig) (CI runs `dotnet format --verify-no-changes`).
3. Run `python scripts/axis.py check doc-drift` when `src/`, `tests/`, or `docs/use-cases/` change. **New module, endpoint, Kafka event, or proto?** Follow [docs/playbooks/repo-layout-discovery.md](docs/playbooks/repo-layout-discovery.md) (checklists A-E - what CI auto-checks vs what you still edit by hand). Use-case layout: [USE_CASE_TEMPLATE.md](docs/use-cases/USE_CASE_TEMPLATE.md). If `docker-compose.yml` changes, update [local-dev.md](docs/playbooks/local-dev.md). CI job **Doc drift** must be green.
4. PR description: **Summary + Linked spec + Requirements only** - no commit list, no CI status (the Checks tab covers that). GitHub auto-fills [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md); CI job **PR body guard** enforces the required sections and checklist state.
5. When adding or changing `.proto` files: run `python scripts/axis.py generate buf-yaml` (updates [`buf.yaml`](buf.yaml) module paths), then `buf lint` - see [repo-layout-discovery.md section D](docs/playbooks/repo-layout-discovery.md). CI **Protobuf** job runs on proto/`buf.yaml` changes.

## Dependency updates (Dependabot)

[`.github/dependabot.yml`](.github/dependabot.yml) runs **monthly** (first day of month, 06:00 Asia/Ho_Chi_Minh) and opens at most **one grouped PR per ecosystem**:

| Ecosystem | Typical PR |
|-----------|------------|
| NuGet | All minor + patch bumps in `Directory.Packages.props` |
| npm (`frontend/`) | All minor + patch bumps in `frontend/package.json` / lockfile |
| GitHub Actions | All minor + patch action bumps |

**Security advisories** still open a dedicated PR as soon as GitHub publishes them (not waiting for the monthly schedule).

**Semver-major** bumps (NuGet, npm, GitHub Actions) are **ignored** by Dependabot - upgrade in an intentional `chore(deps)` PR when ready (major versions often need code changes; auto-PRs will fail CI until then).

CI also runs `python scripts/axis.py check vulnerable-packages` on every build; merge security PRs promptly even if the monthly bundle is still open.

## Coverage

CI collects code coverage via coverlet on every PR and uploads `coverage.cobertura.xml` as an artifact (`dotnet-coverage`). **No threshold is enforced yet** - we want a measured baseline first before locking a floor. Open the artifact on a failing PR to see line/branch coverage per assembly; use it as a sanity check, not as a gate.

When introducing a threshold, set it from the **current measured value** of Domain + Application test projects minus a small buffer (e.g. 5 percentage points). Never set it above the baseline - that breaks unrelated PRs.

## Local dev stack

`docker compose up -d` starts the full stack. **Ports, URLs, startup order, hot reload, and reset commands** live in one place: [docs/playbooks/local-dev.md](docs/playbooks/local-dev.md).

When you change [`docker-compose.yml`](docker-compose.yml), update that playbook in the same PR. CI runs `python scripts/axis.py check local-dev-docs` (also wired into `python scripts/axis.py check doc-drift`).

## Where to read more

| Doc | Purpose |
|-----|---------|
| [CLAUDE.md](CLAUDE.md) | Architecture rules, P0 stops, machine rules |
| [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) | Daily workflow + Gates 0-3 |
| [docs/playbooks/process.md](docs/playbooks/process.md) | Layer-by-layer implementation + deferred follow-ups |
| [docs/playbooks/patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [docs/README.md](docs/README.md) | Documentation hub + single source of truth per topic |
