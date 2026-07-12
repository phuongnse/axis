# Scripts

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

[scripts/axis.py](../../scripts/axis.py) owns repo maintenance commands. Use `$axis-script-scope` when deciding what to run.

## Tool Versions

| Tool | Required source | Used by |
|---|---|---|
| .NET SDK | [global.json](../../global.json) / [docs/TECH_STACK.md](../TECH_STACK.md) | build, tests, format, package scan, API contracts |
| Node.js | [frontend/.nvmrc](../../frontend/.nvmrc) | frontend commands and API types |
| Playwright Chromium | [frontend/package.json](../../frontend/package.json) and `python scripts/axis.py frontend install-browsers` | `local-dev smoke`, host browser E2E, and fast layout smoke |
| Lychee | [scripts/axis.py](../../scripts/axis.py) check and [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) pin | Markdown link checks |
| Renovate validator | [scripts/axis.py](../../scripts/axis.py) check | Dependency automation config |
| Pre-PR review checkpoint | [scripts/axis.py](../../scripts/axis.py) check | `$axis-pull-request` before GitHub PR actions |

Use `python scripts/axis.py doctor` or the exact `check` subcommand to verify local tool resolution.

## Pre-PR review checkpoint

`$axis-pull-request` owns trigger decisions, checkpoint commits, feedback loops, and publication. This playbook owns command behavior:

- `python scripts/axis.py check coderabbit-cli` validates the review tool before a triggered review.
- First review covers the committed publishable branch diff; follow-up review uses `--base-commit <reviewed-checkpoint>` and optional `--dir`.
- Follow-up verification uses `python scripts/axis.py ready-review --since <reviewed-checkpoint>` when the delta has an immutable checkpoint.
- Tool failure, missing authentication, timeout, or unresolved valid findings blocks publication unless the user explicitly approves the exact skip or deferral.

## Command Boundaries

- Add repo workflows as `python scripts/axis.py ...` subcommands.
- Keep raw Docker, dotnet, npm, Lychee, and OpenSSL calls inside wrappers or package scripts.
- Use `python scripts/axis.py local-dev smoke -- <playwright-args>` for fast host-browser smoke against a running local stack; use `python scripts/axis.py local-dev e2e -- <playwright-args>` for Compose-backed browser evidence.
- Use `python scripts/axis.py ready-review` on a clean checkpoint commit at the review boundary. It runs changed-path verification plus the deterministic policy profile shared with CI.
- Treat `python scripts/axis.py verify` as the changed-path verification engine behind ready-review, not as complete PR-readiness evidence by itself.
- Use `python scripts/axis.py pre-push` for ordinary Git push sanity; it is not a substitute for the pre-PR review checkpoint on published PR branches.
- Set `AXIS_PRE_PUSH_FULL=1` only when an explicit workflow wants pre-push to run `ready-review`; ordinary pre-push remains a quick gate.
- CI remains the authoritative merge matrix. [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) runs on GitHub Actions only — not a local dev script; `ubuntu-latest` is the merge runner, not a dev OS requirement.

## Script Rules

- Keep repo maintenance scripts in Python.
- Put shared repository discovery in [scripts/axis_repo.py](../../scripts/axis_repo.py) or small Python helpers.
- Keep top-level `scripts/*.py` files non-executable.
- Keep [scripts/hooks/pre-push](../../scripts/hooks/pre-push) non-executable in the worktree; installation writes the executable copy under `.git/hooks`.
- New deterministic guards encode reusable current invariants. Keep incident details in regression fixtures, not guard rules or retired artifact names.
- Command tests prove supported subcommands and current behavior.
- Removed or renamed commands, markers, headings, and artifacts get a one-time `rg` sweep plus current owner links, not permanent denylist checks.
- Diff-aware checks include PR range plus staged, unstaged, and untracked files.
