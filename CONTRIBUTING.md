# Contributing to Axis

Axis uses docs-first development. Use-case specs under [docs/use-cases/README.md](./docs/use-cases/README.md) are the product contract, and [AGENTS.md](./AGENTS.md) is the repo contract.

## Branches and commits

- Branch from `main`; do not push directly to `main`.
- Use `{type}/{short-description}` in kebab-case with `feat`, `fix`, `docs`, `refactor`, `test`, or `chore`.
- Renovate-owned dependency branches use the configured `renovate/` prefix.
- Use Conventional Commits in imperative mood, max 72 characters, no trailing period.

## Before a PR

Install the local pre-push hook with `python scripts/axis.py install-hooks`.

Follow [docs/playbooks/agent-checklist.md](./docs/playbooks/agent-checklist.md) before opening or marking a PR ready. Run checks through `python scripts/axis.py ...`; command ownership lives in [docs/playbooks/scripts.md](./docs/playbooks/scripts.md).

Use [docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md) for the local stack. When [docker-compose.yml](./docker-compose.yml) changes, update that playbook in the same PR.

GitHub fills the PR body from [.github/PULL_REQUEST_TEMPLATE.md](./.github/PULL_REQUEST_TEMPLATE.md). Keep the description to Summary, Linked spec, and Requirements; CI status belongs in Checks.
