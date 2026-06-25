# Scripts

> **Navigation**: [<- docs/README.md](../README.md) . [<- agent-checklist.md](./agent-checklist.md) . [<- AGENTS.md](../../AGENTS.md)

`scripts/axis.py` is the source of truth for repo maintenance commands. Use `$axis-script-scope` when deciding what to run. Docs, skills, and CI describe repo workflows through Axis wrappers; underlying tools stay implementation details.

## Tool Versions

| Tool | Required source | Used by |
|---|---|---|
| .NET SDK | `global.json` / `docs/TECH_STACK.md` | build, tests, format, package scan, API contracts |
| Node.js | `frontend/.nvmrc` | frontend commands, API types, wireframe export |
| Lychee | `0.23.0` | Markdown link checks |
| Buf CLI | `1.50.0` | protobuf lint/breaking checks |
| CodeRabbit CLI | `>= 0.6.0` | `$axis-pull-request` pre-PR review checkpoint |

Use `python scripts/axis.py doctor` or the exact `check` subcommand to verify local tool resolution.

## Inner Loop

During development, run the smallest wrapper that proves the current edit:

| Change | Prefer while iterating |
|---|---|
| One backend behavior | Targeted test or `python scripts/axis.py test unit` |
| Compile-sensitive backend edit | `python scripts/axis.py dotnet build` |
| One frontend behavior | `python scripts/axis.py frontend test` |
| Frontend type/lint risk | `python scripts/axis.py frontend ci` |
| Docs shape | The focused `python scripts/axis.py check ...` command |
| Unsure / before review | `$axis-ready-review` |

Do not run `python scripts/axis.py verify` after every small edit. It is the local ready-review boundary.

## Boundaries

| Boundary | Command |
|---|---|
| Local environment diagnosis | `python scripts/axis.py doctor` |
| Ordinary Git push sanity | `python scripts/axis.py pre-push` |
| Ready for review | `python scripts/axis.py verify` |
| CI / merge | GitHub workflow full checks |

Set `AXIS_PRE_PUSH_FULL=1` only when you intentionally want pre-push to run the full ready-review command.

## Rules

- Keep repo maintenance scripts in Python.
- Add repo workflows as `scripts/axis.py` subcommands.
- Keep command examples in docs and repo skills behind Axis wrappers.
- Put shared repository discovery in `scripts/axis_repo.py` or small Python helpers.
- Keep top-level `scripts/*.py` files non-executable.
- Keep `scripts/hooks/pre-push` non-executable in the worktree; installation writes the executable copy under `.git/hooks`.
- New deterministic guards must encode reusable invariants, not one-off incidents.
- Diff-aware checks must include PR range plus staged, unstaged, and untracked files.
- Repo-scoped skills under `.agents/skills/` must stay concise, concrete, linked to existing docs, and valid under `python scripts/axis.py check codex-skills`.
