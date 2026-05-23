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

## Local dev stack

`docker-compose.yml` starts the default services (`postgres`, `redis`, `maildev`, `localstack`, `api`, `web`) with `docker compose up`. The Kafka + Schema Registry + Vault trio for the modulith service-boundary work (ADR-010, ADR-013, ADR-019, ADR-022) lives behind the `distributed` profile so a default up stays light:

```bash
docker compose up                            # default stack (postgres, redis, …)
docker compose --profile distributed up      # adds kafka, schema-registry, vault
```

| Service | Host port | Notes |
|---|---|---|
| Kafka (KRaft) | `29092` | Connect from local processes; `kafka:9092` from other containers. Cluster ID is fixed so metadata survives container recreate. |
| Schema Registry | `8081` | `BACKWARD`-only compatibility — breaking schema changes need an explicit override at publish time. |
| Vault dev | `8200` | Root token `axis-dev-root-token`. **In-memory only** — secrets are wiped on container restart, matching the "dev convenience, prod is a separate cluster" stance in [ADR-022](docs/TECH_STACK.md#adr-022-secrets-management-via-hashicorp-vault-in-production). |

The `distributed` profile will be removed once the first module's outbox publishes to Kafka — see the rollout in [docs/PROGRESS.md § Distributed-ready foundation rollout](docs/PROGRESS.md).

## Where to read more

| Doc | Purpose |
|-----|---------|
| [CLAUDE.md](CLAUDE.md) | Architecture rules, P0 stops, machine rules |
| [docs/playbooks/agent-checklist.md](docs/playbooks/agent-checklist.md) | Daily workflow + Gates 0–3 |
| [docs/playbooks/process.md](docs/playbooks/process.md) | Layer-by-layer implementation + deferred follow-ups |
| [docs/playbooks/patterns-index.md](docs/playbooks/patterns-index.md) | Jump table into patterns |
| [docs/README.md](docs/README.md) | Documentation hub + single source of truth per topic |
