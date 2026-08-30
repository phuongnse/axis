# Scripts

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/agent-checklist.md](./agent-checklist.md) · [AGENTS.md](../../AGENTS.md)

[scripts/axis.py](../../scripts/axis.py) owns Axis build, generation, local-development,
and product-policy commands. The shared process owns lifecycle and evidence routing;
use `verify-change` with `.process/project.json` when deciding what to run.

## Tool Versions

| Tool | Required source | Used by |
|---|---|---|
| Python | Python 3.14 with `venv` and `pip` for the locked process bootstrap | process runtime and repository maintenance |
| Engineering process | Direct public pin in [requirements/process.in](../../requirements/process.in), complete graph and hashes in [requirements/process.txt](../../requirements/process.txt), and distribution resources in [.process/process.lock](../../.process/process.lock) | lifecycle, verification, and managed adoption |
| .NET SDK | [global.json](../../global.json) / [docs/TECH_STACK.md](../TECH_STACK.md) | build, tests, format, package scan, API contracts |
| MCP SDK | Exact `ModelContextProtocol` pin in [Directory.Packages.props](../../Directory.Packages.props) | local `Axis.Mcp` stdio bridge and focused MCP contract tests |
| Node.js | [frontend/.nvmrc](../../frontend/.nvmrc) and bundled npm in [frontend/package.json](../../frontend/package.json) | frontend commands and API types |
| Playwright browser runtime | [frontend/Dockerfile.e2e](../../frontend/Dockerfile.e2e) and [frontend/package.json](../../frontend/package.json) | Containerized `local-dev smoke` and `local-dev e2e` workflows |
| Lychee | Exact CI action version in the documentation job | Markdown link checks |
| GitHub CLI | External prerequisite supplied by the operator or CI image | optional publication adapter |
| Renovate validator | Exact version in [scripts/axis.py](../../scripts/axis.py) | Dependency automation config |

## Bootstrap and diagnosis

- Python 3.14 with `venv`/`pip` and Git are external bootstrap prerequisites. Create and activate `.process-venv`, then install the exact public graph with `python -m pip install --require-hashes -r requirements/process.txt`. The repository never requires a global Python package install.
- GitHub CI installs the exact hash-locked process graph directly after setting up Python; Axis does not copy an installer implementation under `scripts/`.
- Use `processctl doctor --project-root . --profile development` or `--profile review` to validate process adoption and profile selection. `processctl setup --project-root . --profile <profile> --apply` runs only the consumer-owned project-dependency command declared in `.process/project.json`; host runtimes remain external or CI-owned prerequisites.
- The declared project-dependency action restores NuGet in locked mode and installs the npm lock graph. `python scripts/axis.py check dependency-state` diagnoses that graph without mutation.
- Local HTTPS certificates, optional current-user trust, Docker availability, and repository hooks are product concerns, not portable process concerns. Use `python scripts/axis.py local-dev certs`, the separately authorized `python scripts/axis.py local-dev trust-certs`, and `python scripts/axis.py install-hooks`. Hook installation migrates only a verified repository-local legacy `core.hooksPath`, writes only inside the repository Git hooks directory, and refuses unmanaged targets.
- Standard browser evidence uses the Compose-managed Playwright runtime. `python scripts/axis.py check playwright-browsers` only launch-probes an independently provisioned host browser for explicit host debugging.
- GitHub CLI authentication remains interactive and outside setup. The `publication` profile verifies its executable contract but never authenticates on the user's behalf.
- Use the exact Axis `check` subcommand for one machine-readable product prerequisite or policy gate. Environment and distribution integrity stay owned by `processctl doctor`.
- During policy-script development, use repeatable `python scripts/axis.py check policy-tests --test <dotted-test-name>` selectors for only the touched regression cases. Omit `--test` only when the full policy suite is triggered at the review boundary or in CI.

Automatic process dependency updates and their merge boundary are owned by
[process-adoption.md](./process-adoption.md).

## Pre-PR review checkpoint

The shared engineering-process lifecycle owns checkpoint-bound verification,
independent review, and finding loops. Axis owns publication commands and policy.
This playbook owns Axis verification command behavior:

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
- The engineering-process `development` profile runs Axis's changed-path impact graph once. Its required `review` companion runs `python scripts/axis.py review-checks --supplemental`, which evaluates only policy and doc-drift gates not already covered by that development run. Standalone `python scripts/axis.py review-checks` still composes both layers; use `--since <reviewed-checkpoint>` only for a focused immutable follow-up delta. CI's supplemental policy-verification job is static evidence only; `processctl` owns clean-checkpoint binding, semantic review, evidence freshness, and publication readiness.
- Pass current verification evidence to delegated reviewers. The primary owns routine checks; reviewers do not repeat passing suites and run only the smallest reproducer for a finding or evidence gap.
- Treat `python scripts/axis.py verify` as the changed-path verification engine behind review-checks, not as complete PR-readiness evidence by itself.
- Use `python scripts/axis.py verify --plan-only` to inspect changed-path routing without executing tools.
- Frontend source verification uses the maintained dependency graph to run related tests plus any changed unit-test files. Only shared test setup or test-runtime configuration triggers the full frontend unit suite; dependency manifests and TypeScript project changes remain on their dependency, type, lint, and build gates. Browser evidence remains acceptance- and diff-triggered.
- A changed .NET test class without source, project, or shared-fixture changes runs that class only. When changed source has changed test classes, those classes are the focused proof; source without focused proof and shared test infrastructure fall back to the affected test project.
- Use `python scripts/axis.py pre-push` for ordinary Git push sanity; the installed hook passes Git's exact update records so the first quick gate validates every pushed branch name and pushed commit range rather than the checked-out branch alone. Direct invocation falls back to the current branch. It is not a substitute for the pre-PR review checkpoint on published PR branches.
- Use `python scripts/axis.py check publish-metadata` as the local checkpoint and pre-push adapter for the current branch plus every commit subject from its merge base. It delegates the grammar and range decision to the pinned `processctl publication` authority.
- Use `processctl publication validate-pr --title <title> --branch <branch> --state <draft-or-ready> --body-file <path>` to validate the exact current or CI pull-request title/body/branch/state before creating or updating it. CI also binds commit metadata to the exact base/head range with `processctl publication validate-range`.
- Set `AXIS_PRE_PUSH_FULL=1` only when an explicit workflow wants pre-push to run the complete Axis review profile; ordinary pre-push remains a quick gate.
- CI remains the authoritative merge matrix. [.github/workflows/build-and-test.yml](../../.github/workflows/build-and-test.yml) runs on GitHub Actions only — not a local dev script; `ubuntu-latest` is the merge runner, not a dev OS requirement.

## Script Rules

- Keep repo maintenance scripts in Python.
- Put shared repository discovery in [scripts/axis_repo.py](../../scripts/axis_repo.py), finite profile ownership in [.process/project.json](../../.process/project.json), and coherent product-policy domains in small modules such as [scripts/axis_frontend_policy.py](../../scripts/axis_frontend_policy.py).
- Keep top-level `scripts/*.py` files non-executable.
- Keep [scripts/hooks/pre-push](../../scripts/hooks/pre-push) non-executable in the worktree; installation atomically writes the executable copy and its ownership digest under `.git/hooks` without replacing an unmanaged hook.
- Deterministic guards must parse explicit structure, configuration, graphs, source symbols, or executable behavior. Do not infer semantic compliance from prose keywords, fragments, or wording.
- New deterministic guards encode reusable current invariants. Keep incident details in regression fixtures, not guard rules or retired artifact names.
- Clean cutovers over finite contracts use exact positive assertions for the current registry, package contents, dependency graph, or wire surface; unexpected extras fail generically. The one-time retired-identifier sweep stays in task or review evidence and is never copied into repository tests, fixtures, scripts, or guidance.
- Command tests prove supported subcommands and current behavior.
- Removed or renamed commands, markers, headings, and artifacts get a one-time `rg` sweep plus current owner links, not permanent denylist checks.
- Diff-aware checks include PR range plus staged, unstaged, and untracked files.
- `python scripts/axis.py check frontend-dependency-versions` requires exact direct npm versions and overrides, and keeps the Node/npm source, portable process contract, and dev image on one exact baseline. Regenerate `package-lock.json` from that manifest through `python scripts/axis.py frontend sync-lock`; use `python scripts/axis.py frontend sync-lock --audit-fix` for compatible lock-only audit remediation without force or install scripts, then install the locked graph through `python scripts/axis.py frontend install`.
- The frontend vulnerability gate evaluates the full npm audit JSON. High and critical findings always fail; every lower-severity advisory needs a current exact acceptance of at most 30 days in [frontend/dependency-risk-acceptances.json](../../frontend/dependency-risk-acceptances.json). New, changed, expired, overlong, and stale acceptances fail the same gate.
- Changed-path verification runs both frontend dependency gates for every frontend diff, matching pull-request CI. [.github/workflows/dependency-security.yml](../../.github/workflows/dependency-security.yml) audits the locked npm and NuGet graphs daily on the default branch.
