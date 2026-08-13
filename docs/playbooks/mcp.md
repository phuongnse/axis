# MCP Bridge

> **Navigation**: [docs/README.md](../README.md) · [docs/playbooks/local-dev.md](./local-dev.md) · [docs/playbooks/scripts.md](./scripts.md) · [AGENTS.md](../../AGENTS.md)

Axis exposes a local, typed MCP adapter so an agent can inspect and operate the running product through the same authenticated REST contracts as the SPA. This is developer tooling, not a second domain gateway or a generic HTTP proxy.

## Boundary

```text
Agent -> Axis.Mcp (stdio JSON-RPC) -> authenticated HTTPS loopback -> Axis.Api -> modules
```

`Axis.Mcp` must not reference module projects, MediatR, EF Core, a `DbContext`, database storage, shell execution, or arbitrary request paths. The API remains authoritative for authentication, authorization, workspace isolation, validation, concurrency, problem details, and business semantics. The bridge is not a Compose service.

## Run

The MCP host registers this local server through its own client configuration. Keep host-specific registration and machine paths outside the repository; Axis exposes one finite wrapper for any compatible MCP client:

```text
python scripts/axis.py mcp serve
```

The wrapper checks the local root CA, reuses a healthy `local-dev` stack or starts it when necessary, builds `Axis.Mcp` with diagnostics redirected to stderr, and then hands stdin/stdout to the stdio bridge. The MCP protocol owns stdout; no detached or background bridge is supported. On first use, the bridge opens the Axis authorization URL in a browser and uses OAuth Authorization Code + PKCE with client `axis_mcp` and the fixed loopback callback `http://127.0.0.1:48123/callback`.

The bridge defaults to `read` access. The repository Codex registration explicitly selects `write` during active development so its live registry supports approved mutation tasks without registration churn; registration never authorizes an agent to mutate product state without task-level approval:

```text
python scripts/axis.py mcp serve --access read
python scripts/axis.py mcp serve --access write
python scripts/axis.py mcp serve --no-build
```

The wrapper passes the selected mode as `AXIS_MCP_ACCESS` and keeps its own diagnostics off protocol stdout. `--no-build` is only for a bridge output that is already current. This is an MCP-side exposure boundary, not a replacement for API authorization or use-case validation.

## Operation coverage

The coverage catalog classifies every committed OpenAPI operation and is updated in the same slice as regenerated OpenAPI:

- Rule tools expose typed lifecycle, authoring, simulation, binding, and evaluation operations; destructive writes pass the API's expected revision.
- Service-identity tools list/read non-secret state and create/add/revoke without credentials or private keys; product-role tools list and mutate exact `{kind, subjectId}` references without caller-supplied user or Workspace scope.
- Workspace-governance tools list Product Builder authority and grant or revoke it for an explicit active human member while deriving the governing Workspace and administrator authority from OAuth claims; mutations forward the current membership revision.
- Solutions tools list/read safe version and installation status, publish only a bounded regular local package file, and pass exact version/operation identifiers plus API idempotency to install/resume operations. Package bytes never appear in tool output, and no publisher-management or generic proxy tool exists.
- Account/browser-bootstrap operations remain internal to browser/OAuth session handling.

`AxisMcpOperationCatalog` and its coverage tests own every typed tool name and schema.

The account/browser-session operations (`RegisterUser`, `SignInUser`, `VerifyEmail`, `ResendEmailVerification`, `SignOutUser`, and `GetBrowserSession`) and the SPA-only `GetModuleNavigationAvailability` projection are classified but not exposed as product-work tools. Browser/OAuth bootstrap owns those session concerns, the App Shell owns its navigation projection, `/connect/*` is MCP's internal OAuth transport, and identity/scope always come from the access token.

## Mutation contract

- Tool descriptions classify operations as `[READ]`, `[WRITE]`, or `[WRITE/DESTRUCTIVE]`.
- Draft and lifecycle APIs receive the owning API's `expectedRevision`; stale writes remain API conflicts. Activating a version also receives its exact version number.
- The MCP client retries only once after a `401` by refreshing the in-memory token. It never retries a write after a timeout or `5xx`.
- MCP passes through the existing API lifecycle semantics. It does not retain the retired start-draft/publish model, silently choose a newer rule version, or emulate a transaction outside the owning module.
- Business-object publication is a two-step MCP flow: prepare captures the authoritative unpublished snapshot, returns a short-lived single-use confirmation token, and publish re-reads the snapshot before consuming that token and forwarding the API's expected revision. The process-local token is a confirmation guard, not a domain lock.
- Archive, binding deletion, service/key revocation, and product-role revocation are `[WRITE/DESTRUCTIVE]`; callers choose write access and pass current revisions, while role mutations and solution installation forward `Idempotency-Key` without a local idempotency substitute.
- Durable idempotency for the three duplicate-producing create commands (`rules`, `rule-bindings`, and `business-object-definitions`) remains owning-module/API work. MCP does not pretend a process-local token is request idempotency.

## Runtime lifecycle and blocker protocol

The running client registry is a separate evidence boundary from a compiled bridge or a protocol test. After changing an MCP tool, its auth/configuration, or a server-side contract used by the tool:

1. Build the current bridge and run the three MCP gates through `scripts/axis.py`.
2. Start or refresh the bridge through the supported `python scripts/axis.py mcp serve` route and keep diagnostics on stderr.
3. Reload or reconnect the app-managed MCP client through its supported control. If the client has no supported reload control, ask the user to restart/reload it; do not terminate or replace an app-managed MCP process.
4. Verify the current client `tools/list` contains the changed tool and its current schema.
5. For an authorized operation, use the current client's `tools/call` and re-read the affected resource through MCP. If the call yields while browser authorization is pending, keep that call and its one authorization attempt in flight, ask the user to complete the pending browser action, and resume the same yielded call. Do not issue a second `tools/call`, start another bridge/client session, or open another authorization attempt; start a new call only after the original reaches a recorded terminal failure or cancellation. Record the response or problem code.

Stop and ask the user when OAuth/browser consent, a host dependency, certificate/trust state, permissions, client lifecycle control, or approval is required. Preserve the exact error and requested action. Do not disable authentication/TLS/sandbox controls, inject credentials, mutate the database directly, use a temporary proxy, or treat a separate protocol harness, raw REST call, stale registry, or unit test as proof that the registered live-agent boundary passed. Protocol harnesses remain useful for focused protocol diagnostics and tests.

## Agent workflow

Use MCP for product state and product operations:

1. Read current user/workspace, service identities, product-role state, solution versions/installations, definitions, records, rules, bindings, authoring metadata, simulations, and evaluations through MCP.
2. For a source change, use the MCP product tools to inspect and exercise the running API; use `scripts/axis.py` and the repository skills for source edits, builds, migrations, tests, and documentation.
3. The repository Codex registration exposes write tools during active development; invoke one only when the task explicitly includes the relevant mutation, and pass the latest server revision. Other clients select `--access write` only for the same authorized scope. Lifecycle archive and binding deletion are destructive operations; use the business-object prepare/confirm flow only where that API workflow requires it.
4. For a business-object workflow, create a Draft record from the published object, save values with the latest revision, submit it, and treat a non-match as an expected recoverable Draft rather than a transport failure.
5. Re-read the affected resource and report the API response/problem code.

## Maintenance process

When an API operation changes, the same slice must update the OpenAPI contract, `AxisMcpOperationCatalog`, the typed tool/request shape, focused tests, and this playbook when the workflow changes. Do not add a generic proxy to avoid updating parity. The committed OpenAPI is the generated-coverage source of truth; when endpoint operation IDs change, regenerate it before running the coverage gate.

Use the MCP owner skill [`.agents/skills/axis-mcp-integration/SKILL.md`](../../.agents/skills/axis-mcp-integration/SKILL.md) and run:

```text
python scripts/axis.py check mcp-api-coverage
python scripts/axis.py check mcp-contracts
python scripts/axis.py check mcp-tool-safety
```

Focused evidence is in `tests/Tools/Axis.Mcp.Tests`; review-boundary verification also includes the owning API tests, OpenAPI snapshot, architecture checks, and the full `python scripts/axis.py dotnet test` matrix when the change crosses those surfaces.

## Security and limits

- Only HTTPS loopback API URLs are accepted; local certificates are validated with `.dev-certs/rootCA.pem` without disabling hostname validation.
- The client is a public PKCE client. Access tokens stay in process memory, never argv, tool arguments, stdout, or logs; tokens are discarded on process exit.
- API authorization and workspace isolation remain server-owned. MCP never connects to module databases or accepts caller-supplied scope identifiers.
- The bridge is local tooling. It is not a production remote MCP deployment, and its current OAuth client does not provide separate read/write scopes yet.
