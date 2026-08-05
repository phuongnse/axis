# Design Gate: Runtime And Dependency Alignment

> **Navigation**: [docs/playbooks/design-gate.md](./design-gate.md) · [docs/TECH_STACK.md](../TECH_STACK.md) · [docs/ENFORCEMENT.md](../ENFORCEMENT.md) · [AGENTS.md](../../AGENTS.md)

## Risk and decision

This is a full high-risk Design Gate for the backend runtime, persistence provider, authentication store, repository bootstrap, containers, migrations, dependency-abstraction workflow, and explicitly authorized user-local command exposure. Axis will use one supported production stack: .NET 10 LTS, EF Core 10, Npgsql 10, and OpenIddict 7. The exact current patches remain centralized in the owning manifests.

.NET 8 and .NET 9 both leave support in November 2026, while [.NET 10 is the active LTS through November 2028](https://dotnet.microsoft.com/en-us/platform/support/policy). [EF Core 10 requires .NET 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew), and the OpenIddict 7 `net10.0` asset depends on EF Core 10. The selected stack therefore removes the current cross-major binary boundary instead of disabling bulk behavior, bypassing the OpenIddict manager, or retaining two runtime paths.

When `setup --install-user-tools` is explicitly authorized, verified portable .NET and Node installations publish stable `dotnet`, `node`, and `npm` commands in the native user command directory when the PATH-resolved toolchain does not meet their required versions. The `dotnet` launcher sets `DOTNET_ROOT` to its verified managed SDK before executing it. Setup does not edit `PATH`, install a second version manager, or replace unmanaged commands. This host bootstrap lets independently owned product repositories use their ordinary declared prerequisites without encoding an Axis tool path or invoking an Axis wrapper as product evidence.

## Governing rules

- [AGENTS.md § Critical Rules](../../AGENTS.md#critical-rules) requires explicit approval for a stack change, migration-backed schema changes, and no intentional shortcuts.
- [docs/TECH_STACK.md § Change Rule](../TECH_STACK.md#change-rule) requires the stack owner and manifests to change together.
- [docs/playbooks/design-gate.md § Risk Tiers](./design-gate.md#risk-tiers) classifies runtime, framework, major-library, schema, and auth changes as high-risk.
- [docs/playbooks/scripts.md § Bootstrap and diagnosis](./scripts.md#bootstrap-and-diagnosis) keeps executable installation explicit, verified, user-local, and separate from host `PATH` mutation.
- [`.agents/skills/reference.md § Engineering method`](../../.agents/skills/reference.md#engineering-method) requires one-hypothesis root-cause work and forbids stacked unproven fixes.
- [docs/ARCHITECTURE.md § Boundary Rules](../ARCHITECTURE.md#boundary-rules) keeps persistence inside module Infrastructure and migration-backed.
- [docs/ENFORCEMENT.md § Ledger](../ENFORCEMENT.md#ledger) is partial for dependency compatibility because runtime tests can prove exercised paths while vendor support and abstraction ownership remain review decisions.

## Blast radius

The pre-edit sweep is:

```text
global.json
Directory.Build.props
Directory.Packages.props
**/*.csproj
Dockerfile
docker-compose.yml
scripts/axis*.py
scripts/tests/
src/Modules/*/*Infrastructure/Migrations/
src/Modules/Identity/Axis.Identity.Infrastructure/
src/Axis.Api/
tests/Modules/*/*Infrastructure.Tests/
tests/Api/Axis.Api.Tests/
docs/{TECH_STACK,ENFORCEMENT}.md
AGENTS.md
.agents/skills/{reference.md,axis-design-gate/SKILL.md,axis-script-scope/SKILL.md}
```

The targeted `rg` sweep covers `net8.0`, .NET SDK 8 assets, `bin/Debug/net8.0`, .NET 8 container tags, EF/Npgsql/OpenIddict versions, framework-specific test fixtures, current OpenIddict migration metadata, and any legacy proprietary authorization continuation.

For stable command exposure, the sweep additionally covers `user_command_dir`, `expose_managed_command`, `DOTNET_ROOT`, dotnet and Node/npm command ownership, unmanaged-target refusal, setup planning, user authorization, and direct guidance that leaks the Axis managed-tool path into a consumer repository.

## Retirement and compatibility

This is a clean runtime cutover. Retire .NET 8 targets and SDK assets, EF Core 9, Npgsql 9, OpenIddict 5, .NET 8 API test packages, hard-coded `net8.0` artifact paths, direct `DbContext` OpenIddict deletion, the legacy proprietary distributed-cache authorization continuation, and any compatibility-mode substitute. Do not keep multi-targeting, version flags, bulk-operation disabling, direct database mutation, query aliases, or dual auth paths.

Axis has no production data for the replaced schemas, so the approved strategy is a clean migration-history reset for Identity, Business Objects, and Rules. Each module receives one newly generated initial migration and model snapshot from its current production-grade model. No migration is retained solely to upgrade an obsolete schema without a supported production consumer, and local/test databases are recreated from the new initial histories. The Identity initial schema must include OpenIddict 7's 150-character token `Type` column. Business Objects and Rules schemas must have no unexplained model drift after regeneration.

Post-edit sweeps must find no retired runtime target, SDK/container path, package major, direct catalog deletion, compatibility flag, proprietary continuation path, or stale evidence prose that names OpenIddict 5 or distributed authorization caching as current behavior.

## Contract decision

REST/OpenAPI request and response shapes do not change. The authorization endpoint cleanly adopts OpenIddict 7's OAuth `request_uri` continuation and token-store-owned request token; the proprietary distributed-cache continuation and every alias are retired. The SPA still receives only one opaque continuation value and resumes only the fixed authorization endpoint. Request tokens retain the five-minute lifetime. Authorization Code with PKCE, issuer, endpoint URIs, scopes, access-token lifetime, exact redirect validation, and workspace authorization behavior remain unchanged.

The durable process contract is repository-wide: before changing approach after a failure, an agent identifies the owning contract and root cause, then compares the proposal with the required owner, execution/trust boundary, invariants, and evidence. A proposal that changes those dimensions merely to keep progressing is a workaround and stops at the Design Gate. A genuinely new path exists only when the owning contract explicitly changes and the new boundary receives complete evidence.

The setup contract is also explicit: `--install-user-tools` may expose the verified managed .NET SDK as `dotnet` when the PATH-resolved SDK is missing or does not satisfy the required major, and the verified managed Node distribution as `node` and `npm` when the PATH toolchain does not satisfy the exact repository pins. The `dotnet` launcher sets `DOTNET_ROOT` to the managed SDK directory. Node/npm destinations are validated before either is changed; every existing unmanaged destination, including `dotnet`, fails closed; and the native user command directory is reported when the current shell has not loaded it. Exposure is a developer-host bootstrap operation, not a product-source dependency or substitute for the independent product's own version pins and public verification commands.

## Verification plan

Development evidence:

- `python scripts/axis.py check policy-tests --test scripts.tests.test_axis_setup`
- `python scripts/axis.py setup --profile build --install-user-tools --yes`
- PATH-resolved `dotnet --version` from the native user command directory, with `DOTNET_ROOT` set by its managed launcher
- PATH-resolved `node --version` and `npm --version` from the native user command directory
- `python scripts/axis.py check policy-tests --test scripts.tests.test_policy_gates`
- `python scripts/axis.py setup --profile build --plan-only`
- `python scripts/axis.py dotnet restore`
- `python scripts/axis.py dotnet build`
- `python scripts/axis.py dotnet test tests/Modules/Identity/Axis.Identity.Infrastructure.Tests/Axis.Identity.Infrastructure.Tests.csproj`
- `python scripts/axis.py dotnet test tests/Modules/BusinessObjects/Axis.BusinessObjects.Infrastructure.Tests/Axis.BusinessObjects.Infrastructure.Tests.csproj`
- `python scripts/axis.py dotnet test tests/Modules/Rules/Axis.Rules.Infrastructure.Tests/Axis.Rules.Infrastructure.Tests.csproj`
- `python scripts/axis.py dotnet test tests/Api/Axis.Api.Tests/Axis.Api.Tests.csproj`
- `python scripts/axis.py check vulnerable-packages`
- `python scripts/axis.py check local-dev-docs`
- `python scripts/axis.py check repo-skills`
- `python scripts/axis.py check doc-link-targets`
- `python scripts/axis.py check doc-drift`

The OAuth catalog-removal integration test must exercise the same production-owned reconciliation boundary and prove the configured client is absent from a fresh scope. Tests must apply committed migrations to PostgreSQL containers; cleanup mechanisms cannot substitute for production behavior evidence.

Review-boundary evidence is `python scripts/axis.py verify` followed by `$axis-review-readiness`; full local-suite language is permitted only after full `python scripts/axis.py dotnet test` succeeds.

## Routing checkpoint

| Owner | Work unit | Work shape | Execution owner | Rationale |
|---|---|---|---|---|
| `$axis-design-gate` + `$axis-doc-hygiene` | Supported stack decision and durable abstraction rule | High-risk integration decision | Primary | Stack, auth, process, and current user sign-off share one decision boundary. |
| `$axis-script-scope` | SDK bootstrap, wrapper paths, containers, and deterministic policy tests | Cross-cutting tooling implementation | Primary | Toolchain edits must remain synchronized with the runtime migration and immediately prove the required SDK. |
| `$axis-use-case-implementation` | Package alignment, OpenIddict migration, reconciler restoration, and auth/persistence tests | High-risk auth/persistence integration | Primary | One owner must reconcile generated migrations, package APIs, and the exact OAuth lifecycle. |
| `$axis-doc-hygiene` | Current stack, enforcement ledger, and stale-version sweep | Durable guidance integration | Primary | Final wording depends on verified source and migration outcomes. |

This dossier is Ready for the clean runtime, authorization-continuation, and migration-history cutover described above. Any unsupported package combination, additional product behavior, or required compatibility behavior reopens the gate before further source edits.
