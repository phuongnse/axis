# Contributing to Axis

Axis uses docs-first development. Use-case specs under [docs/use-cases/README.md](./docs/use-cases/README.md) are the product contract, and [AGENTS.md](./AGENTS.md) is the repo contract.

## Branches and commits

- Branch from `main`; do not push directly to `main`.
- Use `{type}/{short-description}` in kebab-case with `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `build`, `ci`, or `perf`.
- Renovate-owned dependency branches use the configured `automation/renovate/` prefix.
- Use Conventional Commits in imperative mood, max 72 characters, no trailing period.
- Repository branch and commit rules override defaults from GitHub clients, publishing tools, and agent workflows.

## Before a PR

For first-time setup, create and activate `.process-venv` with Python 3.14, install `requirements/process.txt` with `python -m pip install --require-hashes`, then run `processctl setup --project-root . --profile development --apply --allow network --allow user-files --allow project-files`. See [docs/playbooks/scripts.md](./docs/playbooks/scripts.md) for the process boundary and installation policy.

Local HTTPS material and the repository pre-push hook remain Axis-owned. Run `python scripts/axis.py local-dev certs` and `python scripts/axis.py install-hooks`; current-user certificate trust is a separate explicit `python scripts/axis.py local-dev trust-certs` action.

Follow [docs/playbooks/agent-checklist.md](./docs/playbooks/agent-checklist.md) before opening or marking a PR ready. Run checks through `python scripts/axis.py ...`; command ownership lives in [docs/playbooks/scripts.md](./docs/playbooks/scripts.md).

Create checkpoints with `python scripts/axis.py git checkpoint --branch <branch>
--subject <subject>`, then run the required profiles through `processctl change
verify`. The Axis `review` profile invokes scoped review-checks; processctl binds
that result to the immutable checkpoint before independent review. The checkpoint
command and installed pre-push publication gate reject invalid branch names or
commit subjects before publication; the review profile remains source-verification
only. Run `python scripts/axis.py check
publish-metadata` directly when inspecting that gate.

Use [docs/playbooks/local-dev.md](./docs/playbooks/local-dev.md) for the local stack. When [docker-compose.yml](./docker-compose.yml) changes, update that playbook in the same PR.

GitHub fills the PR body from the process-managed [.github/PULL_REQUEST_TEMPLATE.md](./.github/PULL_REQUEST_TEMPLATE.md). Preserve its managed markers and required sections; CI status belongs in Checks. Before creating or updating a PR, validate the exact title, body file, branch, and state with `processctl publication validate-pr --title <title> --branch <branch> --state <draft-or-ready> --body-file <path>`.
