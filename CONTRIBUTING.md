# Contributing to Axis

Docs-first development: feature specs in `docs/use-cases/` are the contract; code implements them. The full agent and human workflow lives in [CLAUDE.md](CLAUDE.md) and [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) — this file is the short pointer for first-time contributors.

---

## Branch and commit

- **Base branch:** `main` — pull latest before branching. Never push directly to `main`.
- **Branch names:** `{type}/{short-description}` in kebab-case. `type` ∈ `feat` · `fix` · `docs` · `refactor` · `test` · `chore`.
- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) — imperative, ≤ 72 chars, no trailing period (e.g. `feat: add execution cancel endpoint`).
- **Tool-named branches** (`cursor/...`, etc.): rename to the convention above before pushing unless you intentionally keep that name.

## Before you push

1. Walk **Gates 0–3** in [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) locally; tick the matching boxes in the PR body.
2. When you touch C# under `src/` or `tests/`, run `dotnet format Axis.sln` — style and naming rules live in [`.editorconfig`](.editorconfig) (CI runs `dotnet format --verify-no-changes`).
3. Run `./scripts/check-doc-drift.sh` (bash — use Git Bash on Windows) when `src/`, `tests/`, or `docs/use-cases/` change. Use the flow-first layout in [docs/use-cases/_template-use-case.md](docs/use-cases/_template-use-case.md) and run `python3 scripts/check-use-case-docs.py --check` (also invoked by the drift script). If `docker-compose.yml` changes, update [docs/playbooks/local-dev.md](docs/playbooks/local-dev.md) in the same PR (`check-local-dev-docs.py` runs inside the drift script). CI job **Doc drift** must be green.
4. PR description: **Summary + Linked spec + Requirements only** — no commit list, no CI status (the Checks tab covers that). Template: [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md).
5. When adding or changing `.proto` files: register the module `Protos` path in [`buf.yaml`](buf.yaml), run `buf lint`, and `./scripts/check-buf-modules.sh` (included in **Doc drift**). CI job **Protobuf — Buf lint and breaking** runs on proto/`buf.yaml` changes — see [patterns.md § gRPC](docs/playbooks/patterns.md).

## Dependency updates (Dependabot)

[`.github/dependabot.yml`](.github/dependabot.yml) runs **monthly** (first day of month, 06:00 Asia/Ho_Chi_Minh) and opens at most **one grouped PR per ecosystem**:

| Ecosystem | Typical PR |
|-----------|------------|
| NuGet | All minor + patch bumps in `Directory.Packages.props` |
| npm (`frontend/`) | All minor + patch bumps in `frontend/package.json` / lockfile |
| GitHub Actions | All minor + patch action bumps |

**Security advisories** still open a dedicated PR as soon as GitHub publishes them (not waiting for the monthly schedule).

**Semver-major** bumps (NuGet, npm, GitHub Actions) are **ignored** by Dependabot — upgrade in an intentional `chore(deps)` PR when ready (major versions often need code changes; auto-PRs will fail CI until then).

CI also runs `dotnet list package --vulnerable` on every build; merge security PRs promptly even if the monthly bundle is still open.

## Coverage

CI collects code coverage via coverlet on every PR and uploads `coverage.cobertura.xml` as an artifact (`dotnet-coverage`). **No threshold is enforced yet** — we want a measured baseline first before locking a floor. Open the artifact on a failing PR to see line/branch coverage per assembly; use it as a sanity check, not as a gate.

When introducing a threshold (planned follow-up), set it from the **current measured value** of Domain + Application test projects minus a small buffer (e.g. 5 percentage points). Never aspirationally above the baseline — that breaks unrelated PRs.

## Local dev stack

`docker compose up -d` starts the full stack. **Ports, URLs, startup order, hot reload, and reset commands** live in one place: [docs/playbooks/local-dev.md](docs/playbooks/local-dev.md).

When you change [`docker-compose.yml`](docker-compose.yml), update that playbook in the same PR. CI runs [`scripts/check-local-dev-docs.py`](scripts/check-local-dev-docs.py) (also wired into [`scripts/check-doc-drift.sh`](scripts/check-doc-drift.sh)).

## Where to read more

| Doc | Purpose |
|-----|---------|
| [CLAUDE.md](CLAUDE.md) | Architecture rules, P0 stops, machine rules |
| [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) | Daily workflow + Gates 0–3 |
| [docs/playbooks/process.md](docs/playbooks/process.md) | Layer-by-layer implementation + deferred follow-ups |
| [docs/playbooks/patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [docs/README.md](docs/README.md) | Documentation hub + single source of truth per topic |
