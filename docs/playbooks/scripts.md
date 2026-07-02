# Scripts

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

[scripts/axis.py](../../scripts/axis.py) owns repo maintenance commands. Use `$axis-script-scope` when deciding what to run.

## Tool Versions

| Tool | Required source | Used by |
|---|---|---|
| .NET SDK | [global.json](../../global.json) / [docs/TECH_STACK.md](../TECH_STACK.md) | build, tests, format, package scan, API contracts |
| Node.js | [frontend/.nvmrc](../../frontend/.nvmrc) | frontend commands and API types |
| Lychee | [scripts/axis.py](../../scripts/axis.py) check and [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) pin | Markdown link checks |
| Renovate validator | [scripts/axis.py](../../scripts/axis.py) check | Dependency automation config |
| Pre-PR review checkpoint | [scripts/axis.py](../../scripts/axis.py) check | `$axis-pull-request` before GitHub PR actions |

Use `python scripts/axis.py doctor` or the exact `check` subcommand to verify local tool resolution.

## Pre-PR review checkpoint

Run the checkpoint before create, branch/diff update, push-only update to an existing PR branch, or mark-ready PR actions when the diff contains non-trivial implementation, behavior, contract, or high-risk changes. For docs-only, metadata-only, or small guidance/tooling-text changes, record `not triggered` with the reason; when unsure, run the checkpoint.

When triggered:

1. Confirm toolchain: `python scripts/axis.py check coderabbit-cli`
2. After readiness passes, create an intentional checkpoint commit on the branch that will be published.
3. Review only committed work that will be published. If the user blocks committing, stop PR publication until a delta-safe reviewed commit exists.
4. Review the committed diff with the CodeRabbit CLI: full branch diff on the first pass; for follow-up reruns, scope with `--base-commit <reviewed-checkpoint>` and optional `--dir` for affected directories.
5. If the review raises issues, record `git rev-parse HEAD`, read `.agents/skills/axis-review-feedback/SKILL.md` (`$axis-review-feedback`), commit the follow-up, then rerun scoped to the checkpoint.
6. For follow-up verification when a checkpoint exists, run `python scripts/axis.py verify --since <reviewed-checkpoint>`; use full `verify` only at the review boundary or when the follow-up scope cannot be represented by a checkpoint.
7. Push and open the PR only after steps 4–6 are complete or explicitly skipped with user approval.

Skip only when the user explicitly requested no pre-PR review. If the checkpoint is triggered and the tool is unavailable, unauthenticated, fails, or times out, stop and report the blocker.

Metadata-only PR title/body updates do not require the checkpoint.

## Command Boundaries

- Add repo workflows as `python scripts/axis.py ...` subcommands.
- Keep raw Docker, dotnet, npm, Lychee, and OpenSSL calls inside wrappers or package scripts.
- Use `python scripts/axis.py verify` only at the ready-review boundary; it is changed-path scoped.
- Use `python scripts/axis.py pre-push` for ordinary Git push sanity; it is not a substitute for the pre-PR review checkpoint on published PR branches.
- Set `AXIS_PRE_PUSH_FULL=1` only when pre-push should run the full ready-review command.
- CI remains the authoritative merge matrix. [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) runs on GitHub Actions only — not a local dev script; `ubuntu-latest` is the merge runner, not a dev OS requirement.

## Script Rules

- Keep repo maintenance scripts in Python.
- Put shared repository discovery in [scripts/axis_repo.py](../../scripts/axis_repo.py) or small Python helpers.
- Keep top-level `scripts/*.py` files non-executable.
- Keep [scripts/hooks/pre-push](../../scripts/hooks/pre-push) non-executable in the worktree; installation writes the executable copy under `.git/hooks`.
- New deterministic guards encode reusable current invariants, not one-off incidents or removed artifact names.
- Command tests prove supported subcommands and current behavior.
- Removed or renamed commands, markers, headings, and artifacts get a one-time `rg` sweep plus current owner links, not permanent denylist checks.
- Diff-aware checks include PR range plus staged, unstaged, and untracked files.
