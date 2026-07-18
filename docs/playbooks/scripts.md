# Scripts

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

[scripts/axis.py](../../scripts/axis.py) owns repo maintenance commands. Use `$axis-script-scope` when deciding what to run.

## Tool Versions

| Tool | Required source | Used by |
|---|---|---|
| .NET SDK | [global.json](../../global.json) / [docs/TECH_STACK.md](../TECH_STACK.md); portable setup pin in [scripts/axis_setup.py](../../scripts/axis_setup.py) | build, tests, format, package scan, API contracts |
| Node.js | [frontend/.nvmrc](../../frontend/.nvmrc); portable setup pin in [scripts/axis_setup.py](../../scripts/axis_setup.py) | frontend commands and API types |
| Playwright Chromium | [frontend/package.json](../../frontend/package.json) and `python scripts/axis.py frontend install-browsers` | `local-dev smoke`, host browser E2E, and fast layout smoke |
| Lychee | [scripts/axis.py](../../scripts/axis.py) check and [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) pin | Markdown link checks |
| GitHub CLI | portable setup pin in [scripts/axis_setup.py](../../scripts/axis_setup.py) | optional publication adapter |
| Renovate validator | [scripts/axis.py](../../scripts/axis.py) check | Dependency automation config |
| Pre-PR review checkpoint | [scripts/axis.py](../../scripts/axis.py) check | `$axis-pull-request` before GitHub PR actions |

Project commands use `python`; substitute `python3` on WSL/Linux or `py -3` on Windows when that is the available Python 3 launcher.

## Bootstrap and diagnosis

- A current patched Python 3 with the standard-library tar data extraction filter and Git are external prerequisites. Run `python scripts/axis.py setup --profile build`; select `local-dev`, `review`, or `all` for cumulative preparation.
- Add `--install-user-tools` to install a missing pinned .NET SDK and Node.js. The review profile can also install pinned Lychee and GitHub CLI artifacts. Downloads require interactive confirmation or `--yes`, use HTTPS, verify the publisher's SHA-256/SHA-512 digest, and land under the native user data directory. `AXIS_TOOLS_DIR` overrides that location.
- Portable executables still rely on publisher-documented host libraries. For example, the Linux .NET SDK requires ICU and other native runtime libraries; strict doctor output identifies these as external OS prerequisites rather than setting globalization fallbacks or installing packages silently.
- Use `--plan-only` to print the selected OS/architecture plan without checks, network access, downloads, or repository mutations. The legacy `--browsers` option remains an additive compatibility alias; `local-dev` and `review` include Chromium automatically.
- `local-dev` and `review` also create or reuse local HTTPS certificates and install the repository pre-push hook. `--trust-local-ca` explicitly opts into a confirmed current-user host trust-store change; setup never changes system-wide trust or invokes `sudo`. These profiles require Docker Engine, Compose, and OpenSSL in the active shell before dependency mutations.
- Build and local-dev setup support Windows, glibc-based Linux/WSL, and macOS on x64/arm64. Review-tool auto-install follows published artifacts: Lychee is portable on Linux x64/arm64, macOS arm64, and Windows x64; other review hosts must provide the pinned version externally. Setup never invokes an OS package manager, `sudo`, Docker Desktop, or service configuration. Missing system tools get diagnostic guidance.
- CodeRabbit is not auto-installed because its release service does not publish checksums and its official installer does not support Windows native. Install and authenticate it separately; use WSL for the review profile on Windows when needed. Authentication for GitHub CLI and CodeRabbit always remains interactive and outside setup.
- Doctor profiles are cumulative: `core`, `build`, `local-dev`, `review`, and `all`. The default is `local-dev`; review-only tools such as Lychee and CodeRabbit are checked by `review`/`all`.
- Use the exact `check` subcommand for one machine-readable prerequisite or policy gate.

## Pre-PR review checkpoint

`$axis-pull-request` owns trigger decisions, checkpoint commits, feedback loops, and publication. This playbook owns command behavior:

- `python scripts/axis.py check coderabbit-cli` validates the review tool before a triggered review.
- First review covers the committed publishable branch diff; follow-up review uses `--base-commit <reviewed-checkpoint>` and optional `--dir`.
- Follow-up verification uses `python scripts/axis.py ready-review --since <reviewed-checkpoint>` when the delta has an immutable checkpoint.
- Tool failure, missing authentication, timeout, or unresolved valid findings blocks publication unless the user explicitly approves the exact skip or deferral.

## Command Boundaries

- Add repo workflows as `python scripts/axis.py ...` subcommands.
- Use `python scripts/axis.py generate theme` after editing [theme/axis-theme.json](../../theme/axis-theme.json); `python scripts/axis.py check theme` rejects stale web or email projections.
- Use `python scripts/axis.py dotnet test [path/to/project.csproj] -- <dotnet-test-args>`; omit the project to test `Axis.sln`.
- Keep raw Docker, dotnet, npm, Lychee, and OpenSSL calls inside wrappers or package scripts.
- Use `python scripts/axis.py local-dev smoke -- <playwright-args>` for fast host-browser smoke against a running local stack; use `python scripts/axis.py local-dev e2e -- <playwright-args>` for Compose-backed browser evidence.
- Use `python scripts/axis.py ready-review` on a clean checkpoint commit at the review boundary. It runs changed-path verification plus the deterministic policy profile shared with CI.
- Treat `python scripts/axis.py verify` as the changed-path verification engine behind ready-review, not as complete PR-readiness evidence by itself.
- Use `python scripts/axis.py verify --plan-only` to inspect changed-path routing without executing tools.
- Use `python scripts/axis.py pre-push` for ordinary Git push sanity; it is not a substitute for the pre-PR review checkpoint on published PR branches.
- Use `python scripts/axis.py check pr` to validate the current or CI head branch plus PR title/body before publication.
- Set `AXIS_PRE_PUSH_FULL=1` only when an explicit workflow wants pre-push to run `ready-review`; ordinary pre-push remains a quick gate.
- CI remains the authoritative merge matrix. [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) runs on GitHub Actions only — not a local dev script; `ubuntu-latest` is the merge runner, not a dev OS requirement.

## Script Rules

- Keep repo maintenance scripts in Python.
- Put shared repository discovery in [scripts/axis_repo.py](../../scripts/axis_repo.py), portable setup ownership in [scripts/axis_setup.py](../../scripts/axis_setup.py), and coherent policy domains in small modules such as [scripts/axis_frontend_policy.py](../../scripts/axis_frontend_policy.py).
- Keep top-level `scripts/*.py` files non-executable.
- Keep [scripts/hooks/pre-push](../../scripts/hooks/pre-push) non-executable in the worktree; installation writes the executable copy under `.git/hooks`.
- New deterministic guards encode reusable current invariants. Keep incident details in regression fixtures, not guard rules or retired artifact names.
- Command tests prove supported subcommands and current behavior.
- Removed or renamed commands, markers, headings, and artifacts get a one-time `rg` sweep plus current owner links, not permanent denylist checks.
- Diff-aware checks include PR range plus staged, unstaged, and untracked files.
