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

`docker compose up` starts the full stack — Postgres, Redis, Maildev, LocalStack, Kafka (KRaft single-broker), Schema Registry, Vault (dev mode), the .NET API, and the Vite frontend. The api container waits for `kafka` and `schema-registry` to be healthy before starting; Wolverine's `UseKafka` configuration in `Axis.Api/Program.cs` would fail-fast otherwise.

| Service | Host port | Notes |
|---|---|---|
| Postgres | `5432` | `axis` database; `axis_dev_pass` |
| Redis | `6379` | |
| Maildev | `1025` SMTP / `1080` UI | Outbound mail viewer at `http://localhost:1080` |
| LocalStack | `4566` | S3-only |
| Kafka (KRaft) | `29092` | Connect from local `dotnet run`; in-network containers reach `kafka:9092` (intentionally not host-published). Cluster ID is fixed so metadata survives container recreate. Carries `*Event`/`*Snapshot` per [ADR-025](docs/TECH_STACK.md#adr-025-transport-selection-rule-by-message-name-suffix). |
| Schema Registry | `8081` | `BACKWARD`-only compatibility — breaking schema changes need an explicit override at publish time (ADR-019). |
| RabbitMQ | `5672` AMQP / `15672` UI | Credentials `axis` / `axis_dev_pass`. Carries `*Command`/`*Job`/`*SagaStep` per [ADR-024](docs/TECH_STACK.md#adr-024-rabbitmq-for-commands-background-jobs-and-saga-orchestration). Open `http://localhost:15672` for the management UI. |
| Vault dev | `8200` | Root token `axis-dev-root-token`. **In-memory only** — secrets wiped on restart; production uses a separately-provisioned cluster (ADR-022). |

## Where to read more

| Doc | Purpose |
|-----|---------|
| [CLAUDE.md](CLAUDE.md) | Architecture rules, P0 stops, machine rules |
| [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) | Daily workflow + Gates 0–3 |
| [docs/playbooks/process.md](docs/playbooks/process.md) | Layer-by-layer implementation + deferred follow-ups |
| [docs/playbooks/patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [docs/README.md](docs/README.md) | Documentation hub + single source of truth per topic |
