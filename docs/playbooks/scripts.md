# Scripts

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

[scripts/axis.py](../../scripts/axis.py) owns Axis build, generation, local-development,
and product-policy commands. The shared process owns lifecycle and evidence routing;
use `$run-project-command` when deciding what to run.

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
- Add `--install-user-tools` to install a missing pinned .NET SDK and Node.js. When PATH `dotnet` is missing or does not satisfy the required SDK major, setup exposes the verified managed `dotnet` through the native user command directory; its launcher sets `DOTNET_ROOT` to that managed SDK. When the PATH-resolved Node/npm pair does not satisfy the exact repository pins, setup similarly exposes the verified managed `node` and `npm` after validating both targets; the review profile likewise exposes the managed GitHub CLI. Setup never replaces an unmanaged command or edits `PATH`, and it reports when the command directory is not active in the current shell. Downloads require interactive confirmation or `--yes`, use HTTPS, verify the publisher's SHA-256/SHA-512 digest, and land under the native user data directory. `AXIS_TOOLS_DIR` overrides that location.
- Portable executables still rely on publisher-documented host libraries. Before authorized downloads, setup reports every proven local-dev host blocker together. Linux native-library discovery is tri-state: a positive result is accepted, authoritative absence may fail early, and inconclusive discovery defers to the installed .NET host. Diagnostics print an exact host action only for a verified OS/version, never set runtime fallbacks, and never install OS packages silently.
- Use `--plan-only` to print the selected OS/architecture plan without checks, network access, downloads, or repository mutations. Add `--browsers` only when explicit host-browser debugging needs a user-local Chromium binary; standard Axis browser workflows use the containerized runtime. The browser installer does not provision native OS libraries. `python scripts/axis.py check playwright-browsers` launch-probes that optional host runtime and reports native-prerequisite failures; a downloaded executable alone is not considered ready.
- `local-dev` and `review` also create or reuse local HTTPS certificates and install the repository pre-push hook. Hook installation migrates only a verified repository-local legacy `core.hooksPath`, rechecks inherited configuration, writes only inside the repository Git hooks directory, updates only a digest-owned Axis copy, and refuses unmanaged targets; resolve the reported config scope or hook explicitly before retrying. `--trust-local-ca` explicitly opts into a confirmed current-user host trust-store change; when omitted, setup reports host trust and the browser-readiness follow-up. Setup never changes system-wide trust or invokes `sudo`. These profiles require Docker Engine, Compose, and OpenSSL in the active shell before dependency mutations.
- Portable setup validates the current OS/architecture and reports unavailable verified artifacts in `--plan-only`; unsupported tool/platform combinations remain external prerequisites. Setup never invokes an OS package manager, `sudo`, Docker Desktop, or service configuration.
- GitHub CLI authentication remains interactive and outside setup.
- Doctor profiles are cumulative: `core`, `build`, `local-dev`, and `review`. The default is `local-dev`; review-only tools such as Lychee are checked by `review`.
- Use the exact `check` subcommand for one machine-readable prerequisite or policy gate. Host Playwright readiness is deliberately absent from cumulative doctor profiles because required browser evidence runs in Compose; use the explicit `playwright-browsers` check only for opted-in host debugging.
- During policy-script development, use repeatable `python scripts/axis.py check policy-tests --test <dotted-test-name>` selectors for only the touched regression cases. Omit `--test` only when the full policy suite is triggered at the review boundary or in CI.

## Pre-PR review checkpoint

The shared engineering-process lifecycle owns checkpoint-bound verification,
independent review, and finding loops. `$publish-change` owns only authorized
publication. This playbook owns Axis verification command behavior:

- The enforced sequence is focused proof, clean immutable checkpoint with repository-valid branch and commit metadata, readiness bound to the exact checkpoint/comparison base, then independent review. Readiness and review never run concurrently.
- First review covers the committed publishable branch diff; follow-up review covers only the new immutable checkpoint delta when earlier evidence remains valid.
- Follow-up verification uses `python scripts/axis.py review-checks --since <reviewed-checkpoint>` when the delta has an immutable checkpoint.
- Reviewer unavailability or unresolved valid findings blocks publication unless the user explicitly approves the exact skip or deferral.

## Command Boundaries

- Native read-only inspection needs no wrapper. Repeatable repository workflows, repo-owned tools, and verification evidence use finite Axis subcommands. A rare one-off mutation may use an exact native command only after its command and targets are explicitly approved; recurrence requires a route. Pass-through shell access never counts as a finite route or evidence.
- Use `python scripts/axis.py local-dev --compose-overlay <path> <command>` when an independently owned deployment must compose trusted external services or public-client configuration with the Axis local stack. Repeat the option in deployment order and keep the same ordered set across lifecycle commands; Axis validates explicit YAML paths but does not discover or own product configuration. For product-owned browser evidence, `local-dev ... e2e --service <name> -- <test-args>` builds and runs the named trusted Compose verification service without adding a shell pass-through.
- Add repo workflows as `python scripts/axis.py ...` subcommands.
- Use `python scripts/axis.py git sync --branch <branch>` to fetch only that project or Renovate branch from `origin`, switch to its existing clean local branch or create a tracking branch, fast-forward without stash, rebase, reset, or deletion, and refuse dirty, detached, or diverged state.
- `python scripts/axis.py git checkpoint --branch <branch> --subject <subject>` validates the project branch and Conventional Commit subject before mutation, then commits staged paths only. Add `--all` only when every tracked, deleted, and untracked path is intentionally in scope.
- Use `python scripts/axis.py frontend format <source-paths>` for scoped formatting and safe fixes, `python scripts/axis.py frontend test [test-paths] [-t <name>]` for explicit frontend unit evidence, and `python scripts/axis.py frontend test-related --since <checkpoint>` for the dependency-related unit scope of an immutable delta. Do not reproduce tool-specific routing flags outside the wrapper.
- Use `python scripts/axis.py migration add <audit|identity|business-objects|rules|authorization|solutions> <PascalCaseName>` to serially build only the owning Infrastructure project, then scaffold a migration with the repository-owned context, output, isolated design-time environment, and `--no-build` reuse of that scoped build. Use `python scripts/axis.py migration remove <module>` only for the latest unpublished migration; it uses the same scoped build/context boundary and lets EF restore the prior snapshot before a clean re-scaffold.
- Use `python scripts/axis.py dotnet restore [path/to/project.csproj] -- <dotnet-restore-args>`; omit the project to restore `Axis.sln`.
- `python scripts/axis.py check dotnet-dependency-locks` requires one valid sibling `packages.lock.json` per .NET project. Package changes regenerate locks through `python scripts/axis.py dotnet restore`; CI and scheduled audits pass `-- --locked-mode` and never update the graph.
- Use `python scripts/axis.py dotnet build [path/to/project.csproj] -- <dotnet-build-args>`; omit the project to build `Axis.sln`.
- Use `python scripts/axis.py dotnet format [path/to/project.csproj] -- <dotnet-format-args>`; omit the project only at an intentional solution-wide formatting boundary.
- Finalize the source model before scaffolding a migration that requires handwritten data preflight, backfill, cutover, or downgrade behavior. Once such behavior exists, never replace the migration by relying on an agent or maintainer to remember and reapply it. Before any explicitly approved pre-publication replacement, first encode every handwritten invariant as executable database behavior tests against the old migration, preserve the exact replacement boundary, and require those same tests to pass against the new scaffold before the replacement is accepted. Edit only the migration body; EF owns its designer and model snapshot. Published migrations are immutable and require a forward migration or reviewed restore path.
- Use `python scripts/axis.py generate theme` after editing [theme/axis-theme.json](../../theme/axis-theme.json); `python scripts/axis.py check theme` rejects stale web or email projections.
- After the required UI-system review and sign-off, use `python scripts/axis.py generate ui-baseline`; use `python scripts/axis.py check ui-baseline` for drift verification.
- Use `python scripts/axis.py dotnet test [path/to/project.csproj] -- <dotnet-test-args>`; omit the project to test `Axis.sln`.
- Keep raw Docker, dotnet, npm, Lychee, and OpenSSL calls inside wrappers or package scripts.
- The shared runner normalizes `TMPDIR`, `TEMP`, and `TMP` to one existing writable directory before every governed subprocess; inherited cross-OS paths are never passed through.
- Use `python scripts/axis.py local-dev smoke` for the fixed local-stack smoke journey and `python scripts/axis.py local-dev e2e -- <playwright-args>` for diff-triggered browser evidence. Intentional visual-baseline writes add `--snapshot-output e2e/<test>.pw.ts-snapshots` before the Playwright arguments and `--update-snapshots` inside them; the wrapper validates and persists only that test-owned directory. Omit E2E arguments only for CI or a cross-cutting diff that invalidates every browser surface. All paths reconcile the local stack and run Playwright in the same Compose-managed browser environment.
- Use `python scripts/axis.py mcp serve` as the single local MCP entrypoint; it reuses a healthy stack or starts `local-dev up`, builds the bridge with diagnostics on stderr, and then keeps stdout protocol-only. The wrapper defaults to read access; a client may select write access only for an explicitly authorized mutation task. Pass `--no-build` only when the bridge output is already current. See [docs/playbooks/mcp.md](./mcp.md).
- Use `python scripts/axis.py check mcp-api-coverage`, `python scripts/axis.py check mcp-contracts`, and `python scripts/axis.py check mcp-tool-safety` when an API operation, MCP tool, auth boundary, or mutation policy changes. These are the maintained MCP parity and safety checks.
- `local-dev shell` is an unrestricted diagnostic escape hatch, not a finite workflow or evidence route. Volume-destructive local-dev commands require explicit `--yes`.
- The engineering-process `review` profile runs `python scripts/axis.py review-checks` against the current branch diff. Use `--since <reviewed-checkpoint>` only for a focused immutable follow-up delta. `review-checks` owns Axis verification and the deterministic policy profile shared with CI; processctl owns clean-checkpoint binding, evidence freshness, and independent-review readiness. Publication metadata remains a separate project gate.
- Pass current verification evidence to delegated reviewers. The primary owns routine checks; reviewers do not repeat passing suites and run only the smallest reproducer for a finding or evidence gap.
- Treat `python scripts/axis.py verify` as the changed-path verification engine behind review-checks, not as complete PR-readiness evidence by itself.
- Use `python scripts/axis.py verify --plan-only` to inspect changed-path routing without executing tools.
- Frontend source verification uses the maintained dependency graph to run related tests plus any changed unit-test files. Only shared test setup or test-runtime configuration triggers the full frontend unit suite; dependency manifests and TypeScript project changes remain on their dependency, type, lint, and build gates. Browser evidence remains acceptance- and diff-triggered.
- A changed .NET test class without source, project, or shared-fixture changes runs that class only. When changed source has changed test classes, those classes are the focused proof; source without focused proof and shared test infrastructure fall back to the affected test project.
- Use `python scripts/axis.py pre-push` for ordinary Git push sanity; the installed hook passes Git's exact update records so the first quick gate validates every pushed branch name and pushed commit range rather than the checked-out branch alone. Direct invocation falls back to the current branch. It is not a substitute for the pre-PR review checkpoint on published PR branches.
- Use `python scripts/axis.py check publish-metadata` to validate the current branch plus every commit subject from its merge base. CI passes the exact PR base/head range to the same command.
- Use `python scripts/axis.py check pr --title <title> --body-file <path> --branch <branch>` to validate the exact current or CI pull-request title/body/branch before creating or updating it.
- Set `AXIS_PRE_PUSH_FULL=1` only when an explicit workflow wants pre-push to run the complete Axis review profile; ordinary pre-push remains a quick gate.
- CI remains the authoritative merge matrix. [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) runs on GitHub Actions only — not a local dev script; `ubuntu-latest` is the merge runner, not a dev OS requirement.

## Script Rules

- Keep repo maintenance scripts in Python.
- Put shared repository discovery in [scripts/axis_repo.py](../../scripts/axis_repo.py), portable setup ownership in [scripts/axis_setup.py](../../scripts/axis_setup.py), and coherent policy domains in small modules such as [scripts/axis_frontend_policy.py](../../scripts/axis_frontend_policy.py).
- Keep top-level `scripts/*.py` files non-executable.
- Keep [scripts/hooks/pre-push](../../scripts/hooks/pre-push) non-executable in the worktree; installation atomically writes the executable copy and its ownership digest under `.git/hooks` without replacing an unmanaged hook.
- Deterministic guards must parse explicit structure, configuration, graphs, source symbols, or executable behavior. Do not infer semantic compliance from prose keywords, fragments, or wording.
- New deterministic guards encode reusable current invariants. Keep incident details in regression fixtures, not guard rules or retired artifact names.
- Clean cutovers over finite contracts use exact positive assertions for the current registry, package contents, dependency graph, or wire surface; unexpected extras fail generically. The one-time retired-identifier sweep stays in task or review evidence and is never copied into repository tests, fixtures, scripts, or guidance.
- Command tests prove supported subcommands and current behavior.
- Removed or renamed commands, markers, headings, and artifacts get a one-time `rg` sweep plus current owner links, not permanent denylist checks.
- Diff-aware checks include PR range plus staged, unstaged, and untracked files.
- `python scripts/axis.py check frontend-dependency-versions` requires exact direct npm versions and overrides, and keeps the Node/npm source, portable setup, and dev image on one exact baseline. Regenerate `package-lock.json` from that manifest through `python scripts/axis.py frontend sync-lock`; use `python scripts/axis.py frontend sync-lock --audit-fix` for compatible lock-only audit remediation without force or install scripts, then install the locked graph through `python scripts/axis.py frontend install`.
- The frontend vulnerability gate evaluates the full npm audit JSON. High and critical findings always fail; every lower-severity advisory needs a current exact acceptance of at most 30 days in [frontend/dependency-risk-acceptances.json](../../frontend/dependency-risk-acceptances.json). New, changed, expired, overlong, and stale acceptances fail the same gate.
- Changed-path verification runs both frontend dependency gates for every frontend diff, matching pull-request CI. [.github/workflows/dependency-security.yml](../../.github/workflows/dependency-security.yml) audits the locked npm and NuGet graphs daily on the default branch.
