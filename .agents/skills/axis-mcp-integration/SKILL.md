---
name: axis-mcp-integration
description: Design, implement, verify, and maintain the Axis MCP tool surface and its agent workflow in parity with authenticated REST/OpenAPI operations.
---

# Axis MCP Integration

## Goal

Keep the local MCP adapter useful for real agent work without creating a second domain/API gateway. Every exposed product capability is a typed semantic tool mapped to one current Axis OpenAPI operation; narrowly scoped MCP-only confirmation helpers may protect a product mutation without becoming a second domain API. The running Axis API remains the authority for authentication, authorization, workspace isolation, validation, concurrency, and business status.

## Hard gates

Follow [reference.md](../reference.md).
- Non-trivial MCP, auth, workflow, or tool-surface work **Requires** current `$axis-design-gate` evidence and user sign-off for high-risk changes.
- REST/OpenAPI request or response changes **Requires** `$axis-api-contract`.
- Repeatable build, test, generation, and check commands **Requires** `$axis-script-scope`.
- Durable MCP guidance, skill routing, and process changes **Requires** `$axis-doc-hygiene`.
- `Axis.Mcp` must call `Axis.Api` over the authenticated loopback HTTPS boundary. It must not reference module projects, MediatR, EF Core, DbContext, database storage, shell execution, or a generic arbitrary-path HTTP proxy.
- User and workspace identity are always derived from the access token; MCP arguments must never accept `userId` or `workspaceId` for product operations.
- A tool must be classified as read, write, or destructive; mutation tools must preserve the owning API's revision/idempotency/confirmation contract and must not retry writes after timeout or 5xx.
- `read` is the default server access mode. A client registration may explicitly select `write` during active development, but exposure is not mutation authorization: every write still requires an approved task, and registration must not rely only on a runtime guard.
- Every OpenAPI operation must be classified as exposed, blocked with an owning handoff, or intentionally internal/bootstrap. A blocked operation is not silently omitted from the catalog.
- The supported MCP client lifecycle is part of runtime evidence. If the registry is stale or authentication, browser consent, host trust, permissions, restart, or reload requires user-controlled state, stop under the [blocker and completion protocol](../reference.md#blocker-and-completion-protocol); do not kill app-managed processes, inject credentials, bypass security, or claim live-agent completion from a separate harness. When a supported-client `tools/call` yields for browser authorization, preserve the pending call and follow the single-attempt resume contract in [docs/playbooks/mcp.md](../../../docs/playbooks/mcp.md); do not start a parallel call, bridge, client session, or authorization attempt.
- A standalone protocol harness, raw REST call, temporary proxy, or alternate MCP process may diagnose the bridge, but is protocol-only evidence. It must never substitute for the supported client reload/authenticated-session/read-back boundary or silently close a blocked runtime step.

## Inputs

- Current `openapi.json`, endpoint operation IDs, owning use-case acceptance criteria, API contracts, `src/Axis.Mcp`, and the MCP coverage catalog.
- The changed API operation, tool, auth/config, skill/process, or verification boundary.
- Existing compatibility requirements and any retired or excluded operation IDs.

## Workflow

1. Classify the change as coverage, tool schema, API contract, auth/scope, mutation safety, runtime, workflow, or maintenance enforcement. Select the required typed handoffs before editing.
2. Rebuild the operation inventory from `openapi.json`. Add or remove one explicit catalog entry per operation; classify protocol/account bootstrap operations explicitly instead of silently omitting them.
3. Implement one typed MCP tool per exposed operation. Reuse the stable tool name when a tool already exists; keep request shapes aligned with the REST contract and keep responses server-owned. If a mutation needs a confirmation helper, bind it to the authoritative resource snapshot, caller/workspace identity, revision, expiry, and single-use consumption.
4. For writes, pass through the API's expected revision, idempotency, confirmation, and problem-details behavior. Keep the MCP client retry policy to authentication refresh only; never add a generic write retry.
5. Update the focused MCP protocol, API coverage, tool-safety, and authenticated integration evidence. If the REST contract changed, regenerate OpenAPI/frontend artifacts through the owning workflow and update the MCP catalog in the same slice.
6. For a runtime or tool-registry change, use the supported lifecycle: build the current bridge, run the MCP gates, reload/reconnect the app-managed MCP client through its supported control, verify the current `tools/list`, then perform the authorized `tools/call` and re-read the persisted resource. If any step needs user-controlled state, stop and ask; do not substitute a killed process, injected token, direct database/API call, temporary proxy, or separate harness for the live-agent boundary.
7. Update the MCP playbook and maintenance owner once, then prune duplicate workflow prose. Keep `scripts/axis.py` as the owner of repeatable commands and checks.
8. At review, verify the full operation classification, no direct module/database dependency, stdout protocol isolation, auth/workspace scoping, read/write registration behavior, mutation guard behavior, supported client lifecycle evidence, and the triggered API/use-case evidence.

## Output

Report operation coverage, tool changes, mutation/auth decisions, owner handoffs, focused commands, full review-boundary verification, and unresolved gaps.
