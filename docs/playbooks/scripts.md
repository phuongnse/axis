# Scripts

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

[scripts/axis.py](../../scripts/axis.py) owns repo maintenance commands. Use `$axis-script-scope` when deciding what to run.

## Tool Versions

| Tool | Required source | Used by |
|---|---|---|
| .NET SDK | [global.json](../../global.json) / [docs/TECH_STACK.md](../TECH_STACK.md) | build, tests, format, package scan, API contracts |
| Node.js | [frontend/.nvmrc](../../frontend/.nvmrc) | frontend commands and API types |
| Lychee | [scripts/axis.py](../../scripts/axis.py) check and [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) pin | Markdown link checks |
| CodeRabbit CLI | [scripts/axis.py](../../scripts/axis.py) check | `$axis-pull-request` pre-PR review checkpoint |

Use `python scripts/axis.py doctor` or the exact `check` subcommand to verify local tool resolution.

## Command Boundaries

- Add repo workflows as `python scripts/axis.py ...` subcommands.
- Keep raw Docker, dotnet, npm, Lychee, and OpenSSL calls inside wrappers or package scripts.
- Use `python scripts/axis.py verify` only at the ready-review boundary; it is changed-path scoped.
- Use `python scripts/axis.py pre-push` for ordinary Git push sanity.
- Set `AXIS_PRE_PUSH_FULL=1` only when pre-push should run the full ready-review command.
- CI remains the authoritative merge matrix.

## Script Rules

- Keep repo maintenance scripts in Python.
- Put shared repository discovery in [scripts/axis_repo.py](../../scripts/axis_repo.py) or small Python helpers.
- Keep top-level `scripts/*.py` files non-executable.
- Keep [scripts/hooks/pre-push](../../scripts/hooks/pre-push) non-executable in the worktree; installation writes the executable copy under `.git/hooks`.
- New deterministic guards encode reusable current invariants, not one-off incidents or removed artifact names.
- Command tests prove supported subcommands and current behavior.
- Removed or renamed commands, markers, headings, and artifacts get a one-time `rg` sweep plus current owner links, not permanent denylist checks.
- Diff-aware checks include PR range plus staged, unstaged, and untracked files.
