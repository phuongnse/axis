# Scripts

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

[scripts/axis.py](../../scripts/axis.py) owns repo maintenance commands. Use `$axis-script-scope` when deciding what to run.

## Tool Versions

| Tool | Required source | Used by |
|---|---|---|
| Python | Current patched Python 3 exposed as `python`; Ubuntu/WSL provides the launcher through `python-is-python3` | repository maintenance |
| .NET SDK | [global.json](../../global.json) / [docs/TECH_STACK.md](../TECH_STACK.md); portable setup pin in [scripts/axis_setup.py](../../scripts/axis_setup.py) | build, tests, format, package scan, API contracts |
| MCP SDK | Exact `ModelContextProtocol` pin in [Directory.Packages.props](../../Directory.Packages.props) | local `Axis.Mcp` stdio bridge and focused MCP contract tests |
| Node.js | [frontend/.nvmrc](../../frontend/.nvmrc); portable setup pin in [scripts/axis_setup.py](../../scripts/axis_setup.py) | frontend commands and API types |
| Playwright browser runtime | [frontend/Dockerfile.e2e](../../frontend/Dockerfile.e2e) and [frontend/package.json](../../frontend/package.json) | Containerized `local-dev smoke` and `local-dev e2e` workflows |
| Lychee | [scripts/axis.py](../../scripts/axis.py) check and [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) pin | Markdown link checks |
| GitHub CLI | portable setup pin in [scripts/axis_setup.py](../../scripts/axis_setup.py) | optional publication adapter |
| Renovate validator | Exact version in [scripts/axis.py](../../scripts/axis.py) | Dependency automation config |

## Bootstrap and diagnosis

- Python from [Tool Versions](#tool-versions), including the standard-library tar data extraction filter, and Git are external prerequisites. Run `python scripts/axis.py setup --profile build`; select `local-dev` or `review` for cumulative preparation.
- Add `--install-user-tools` to install a missing pinned .NET SDK and Node.js. The review profile can also install pinned Lychee and GitHub CLI artifacts. It exposes the managed GitHub CLI as a stable user command without replacing an unmanaged command, and reports when the command directory is not active in `PATH`. Downloads require interactive confirmation or `--yes`, use HTTPS, verify the publisher's SHA-256/SHA-512 digest, and land under the native user data directory. `AXIS_TOOLS_DIR` overrides that location.
- Portable executables still rely on publisher-documented host libraries. Strict doctor output classifies known native-runtime failures and prints an exact host action only for a verified OS/version; unknown hosts receive publisher-level guidance. Setup never sets runtime fallbacks or installs OS packages silently.
- Use `--plan-only` to print the selected OS/architecture plan without checks, network access, downloads, or repository mutations. Add `--browsers` only when explicit host-browser debugging needs a user-local Chromium binary; standard Axis browser workflows use the containerized runtime.
- `local-dev` and `review` also create or reuse local HTTPS certificates and install the repository pre-push hook. `--trust-local-ca` explicitly opts into a confirmed current-user host trust-store change; when omitted, setup reports host trust and the browser-readiness follow-up. Setup never changes system-wide trust or invokes `sudo`. These profiles require Docker Engine, Compose, and OpenSSL in the active shell before dependency mutations.
- Portable setup validates the current OS/architecture and reports unavailable verified artifacts in `--plan-only`; unsupported tool/platform combinations remain external prerequisites. Setup never invokes an OS package manager, `sudo`, Docker Desktop, or service configuration.
- GitHub CLI authentication remains interactive and outside setup.
- Doctor profiles are cumulative: `core`, `build`, `local-dev`, and `review`. The default is `local-dev`; review-only tools such as Lychee are checked by `review`.
- Use the exact `check` subcommand for one machine-readable prerequisite or policy gate.
- During policy-script development, use repeatable `python scripts/axis.py check policy-tests --test <dotted-test-name>` selectors for only the touched regression cases. Omit `--test` only when the full policy suite is triggered at the review boundary or in CI.

## Pre-PR review checkpoint

`$axis-pull-request` owns trigger decisions, checkpoint commits, independent review, feedback loops, and publication. This playbook owns verification command behavior:

- First review covers the committed publishable branch diff; follow-up review covers only the new immutable checkpoint delta when earlier evidence remains valid.
- Follow-up verification uses `python scripts/axis.py review-readiness --since <reviewed-checkpoint>` when the delta has an immutable checkpoint.
- Reviewer unavailability or unresolved valid findings blocks publication unless the user explicitly approves the exact skip or deferral.

## Command Boundaries

- Native read-only inspection needs no wrapper. Repeatable repository workflows, repo-owned tools, and verification evidence use finite Axis subcommands. A rare one-off mutation may use an exact native command only after its command and targets are explicitly approved; recurrence requires a route. Pass-through shell access never counts as a finite route or evidence.
- Add repo workflows as `python scripts/axis.py ...` subcommands.
- Use `python scripts/axis.py git sync --branch <branch>` to fetch only that project or Renovate branch from `origin`, switch to its existing clean local branch or create a tracking branch, fast-forward without stash, rebase, reset, or deletion, and refuse dirty, detached, or diverged state.
- `python scripts/axis.py git checkpoint --branch <branch> --subject <subject>` commits staged paths only. Add `--all` only when every tracked, deleted, and untracked path is intentionally in scope.
- Use `python scripts/axis.py frontend test [test-paths] [-t <name>]` for all Vitest execution and acceptance evidence; do not add arbitrary Vitest flags.
- Use `python scripts/axis.py migration add <identity|business-objects|rules> <PascalCaseName>` to scaffold a migration with the repository-owned project, context, output, and design-time environment.
- Use `python scripts/axis.py generate theme` after editing [theme/axis-theme.json](../../theme/axis-theme.json); `python scripts/axis.py check theme` rejects stale web or email projections.
- After the required UI-system review and sign-off, use `python scripts/axis.py generate ui-baseline`; use `python scripts/axis.py check ui-baseline` for drift verification.
- Use `python scripts/axis.py dotnet test [path/to/project.csproj] -- <dotnet-test-args>`; omit the project to test `Axis.sln`.
- Keep raw Docker, dotnet, npm, Lychee, and OpenSSL calls inside wrappers or package scripts.
- The shared runner normalizes `TMPDIR`, `TEMP`, and `TMP` to one existing writable directory before every governed subprocess; inherited cross-OS paths are never passed through.
- Use `python scripts/axis.py local-dev smoke` for the fixed local-stack smoke journey and `python scripts/axis.py local-dev e2e -- <playwright-args>` for diff-triggered browser evidence. Omit E2E arguments only for CI or a cross-cutting diff that invalidates every browser surface. Both commands reconcile the local stack and run Playwright in the same Compose-managed browser environment.
- Use `python scripts/axis.py mcp serve` as the single local MCP entrypoint; it reuses a healthy stack or starts `local-dev up`, builds the bridge with diagnostics on stderr, and then keeps stdout protocol-only. It defaults to read access. Pass `--access write` only for an approved mutation task; pass `--no-build` only when the bridge output is already current. See [docs/playbooks/mcp.md](./mcp.md).
- Use `python scripts/axis.py check mcp-api-coverage`, `python scripts/axis.py check mcp-contracts`, and `python scripts/axis.py check mcp-tool-safety` when an API operation, MCP tool, auth boundary, or mutation policy changes. These are the maintained MCP parity and safety checks.
- `local-dev shell` is an unrestricted diagnostic escape hatch, not a finite workflow or evidence route. Volume-destructive local-dev commands require explicit `--yes`.
- Use `python scripts/axis.py review-readiness` on a clean checkpoint commit at the review boundary. It runs changed-path verification plus the deterministic policy profile shared with CI.
- Pass current verification evidence to delegated reviewers. The primary owns routine checks; reviewers do not repeat passing suites and run only the smallest reproducer for a finding or evidence gap.
- Treat `python scripts/axis.py verify` as the changed-path verification engine behind review-readiness, not as complete PR-readiness evidence by itself.
- Use `python scripts/axis.py verify --plan-only` to inspect changed-path routing without executing tools.
- Use `python scripts/axis.py pre-push` for ordinary Git push sanity; it is not a substitute for the pre-PR review checkpoint on published PR branches.
- Use `python scripts/axis.py check pr` to validate the current or CI head branch plus PR title/body before publication.
- Set `AXIS_PRE_PUSH_FULL=1` only when an explicit workflow wants pre-push to run `review-readiness`; ordinary pre-push remains a quick gate.
- CI remains the authoritative merge matrix. [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) runs on GitHub Actions only — not a local dev script; `ubuntu-latest` is the merge runner, not a dev OS requirement.

## Script Rules

- Keep repo maintenance scripts in Python.
- Put shared repository discovery in [scripts/axis_repo.py](../../scripts/axis_repo.py), portable setup ownership in [scripts/axis_setup.py](../../scripts/axis_setup.py), and coherent policy domains in small modules such as [scripts/axis_frontend_policy.py](../../scripts/axis_frontend_policy.py).
- Keep top-level `scripts/*.py` files non-executable.
- Keep [scripts/hooks/pre-push](../../scripts/hooks/pre-push) non-executable in the worktree; installation writes the executable copy under `.git/hooks`.
- Deterministic guards must parse explicit structure, configuration, graphs, source symbols, or executable behavior. Do not infer semantic compliance from prose keywords, fragments, or wording.
- New deterministic guards encode reusable current invariants. Keep incident details in regression fixtures, not guard rules or retired artifact names.
- Command tests prove supported subcommands and current behavior.
- Removed or renamed commands, markers, headings, and artifacts get a one-time `rg` sweep plus current owner links, not permanent denylist checks.
- Diff-aware checks include PR range plus staged, unstaged, and untracked files.
- `python scripts/axis.py check frontend-dependency-versions` requires exact direct npm versions and overrides, and keeps the Node/npm source, portable setup, and dev image on one exact baseline. Regenerate `package-lock.json` from that manifest through `python scripts/axis.py frontend sync-lock`; use `python scripts/axis.py frontend sync-lock --audit-fix` for compatible lock-only audit remediation without force or install scripts, then install the locked graph through `python scripts/axis.py frontend install`.
- The frontend vulnerability gate evaluates the full npm audit JSON. High and critical findings always fail; every lower-severity advisory needs a current exact acceptance of at most 30 days in [frontend/dependency-risk-acceptances.json](../../frontend/dependency-risk-acceptances.json). New, changed, expired, overlong, and stale acceptances fail the same gate.
- Changed-path verification runs both frontend dependency gates for every frontend diff, matching pull-request CI. [.github/workflows/dependency-security.yml](../../.github/workflows/dependency-security.yml) audits the locked npm and NuGet graphs daily on the default branch.
