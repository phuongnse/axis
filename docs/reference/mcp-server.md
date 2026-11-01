# MCP server

Axis serves an MCP server over the [authoring API](authoring.md). It is the only way any agent acts
on Axis: the [agent host](authoring-requests.md) and outside clients, such as Claude Code or NexKit,
use the same tools. [D119](../decisions/d119-ai-authoring-in-studio.md) agrees the idea, and
[D125](../decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) the shapes below.
Everything here is _(planned for M35)_. The conventions of [architecture.md](../architecture.md)
apply.

## Route and transport

- **Route.** `POST /mcp` and `GET /mcp` on each tenant's host, as the Streamable HTTP transport of
  the MCP specification asks. The host names the tenant, as for every route. A preview tenant's host
  serves no MCP server.
- **Pinned version.** Axis serves MCP specification version `2025-11-25`. This line is the one place
  the version is written. An upgrade of the version is a change of its own, checked like any other
  dependency, and the authoring eval runs on it.
- **Sessions.** The server gives each connection an `Mcp-Session-Id` and keeps no state in it beyond
  the client's identity and its protocol version. Every tool call is checked again on its own.
- **Origin.** A request with an `Origin` header that is not the tenant's host is refused with `403`,
  as the specification asks against DNS rebinding.

## Sign-in

The tenant's realm in [Axis's identity provider](identity-provider.md) is the authorization server.

- **Discovery.** `GET /.well-known/oauth-protected-resource/mcp` answers the protected resource
  metadata of RFC 9728, naming the realm as the authorization server. A call without a valid token
  answers `401` with a `WWW-Authenticate` header that points to it.
- **Flow.** The authorization code flow with PKCE (`S256`). The client sends the resource indicator
  of RFC 8707, the MCP server's URL, such as `https://acme.vn.example.com/mcp`.
- **Tokens.** The access token's audience is that URL, and the server refuses any other audience,
  so a token for another resource or another tenant never works here. Access tokens live 5 minutes.
  Refresh tokens rotate on each use and end with the realm session, at most 8 hours.
- **Who acts.** The token's subject is the person. Every tool runs with the person's grants: the
  [author role](authoring.md#settings) for reading and writing drafts, and the rules of
  [accepting](authoring-requests.md#accepting) for `accept_draft`. A person without the author role
  gets `403` on every tool.
- **Consent.** The realm shows a consent screen that names the client, its home page and the scope
  `axis:authoring`, in the Axis theme. Without consent, no token is issued.

## Client registration

- **Client ID Metadata Documents.** A client may use the URL of its metadata document as its client
  id, as the pinned specification prefers. The realm fetches the document, checks that its redirect
  URIs are `https` or a loopback address, and shows its name on the consent screen.
- **Dynamic client registration.** A client that has no metadata document registers through the
  realm's registration endpoint. The realm's registration policies allow only public clients, the
  authorization code flow, `https` or loopback redirect URIs and the scope `axis:authoring`, and
  require consent.
- **The agent host** is a confidential client of each realm that `tenant create` sets up. It takes
  no outside registration.
- **The administrator's switch.** A tenant setting `Ai:OutsideClients`, one of `all`, `allowList`
  and `none`, decides which outside clients may connect. `none` is the default of a new tenant.
  - Under `allowList`, `Ai:AllowedClients` lists the client ids that may connect.
  - A tenant administrator changes both on the [users page](user-administration.md), in a "Connected
    apps" tab that lists each client with consent, the people who consented and the last use, and
    revokes a client's consent. Each change writes a platform audit record.
  - A client the switch does not allow is refused at the realm, before any token is issued.

## Tools

Each tool takes the application's `name`. A tool that writes names a draft. Every write goes through
the authoring API's checks, so a tool can do nothing that the person could not do in Studio.

| Tool | Input | Output |
| --- | --- | --- |
| `list_applications` | none | The applications the person may author |
| `read_reference` | a topic, such as a resource kind | The schema and reference text of that kind, from the installed version of Axis |
| `list_resources` | application, optional draft | Each resource's `id`, `kind`, `name`, `file` and `version` |
| `read_resource` | application, `id`, optional draft | The resource's content and version |
| `read_module_resource` | application, module, `id` | A resource of the pinned module version, with the application's layer applied |
| `read_names` | application, optional draft | The names the application defines, as the names route gives them |
| `check_expression` | application, draft, resource, path, expression | The expression's type and diagnostics |
| `create_draft` | application, a title | A new draft, with the version of the working folder it starts from |
| `write_resource` | application, draft, resource content | Creates or replaces a resource in the draft, with its diagnostics |
| `write_layer` | application, draft, module resource, edited content | Writes the layer that holds the difference, as Studio saves do, see [customization layers](customization-layers.md#in-studio) |
| `delete_resource` | application, draft, `id` | Removes a resource from the draft |
| `validate_draft` | application, draft | The draft's diagnostics, contract checks included |
| `run_scenarios` | application, draft, optional names | The results of the application's scenarios and the contract scenarios of its modules, run on the draft by the publisher |
| `diff_draft` | application, draft | The draft's changes from the working folder, resource by resource |
| `accept_draft` | application, draft | Asks for an accept, under the rules of [accepting](authoring-requests.md#accepting) |

- **No other tool.** No tool publishes, rolls back, reads a record, a file or a secret, runs a
  command or opens a connection. A resource never holds a binding value, so no tool returns one.
- **Text is data.** A label, a description or any text a tool returns is content, never an
  instruction to the server. The server acts only on tool calls, each checked on its own.
- **Errors.** A refused call answers a tool error with the authoring API's problem title and
  diagnostics, never a stack trace or a path on disk.

## Limits

- **Rate limit.** Tool calls count in the `mcp` partition, by tenant and client, see
  [rate limits](headers-and-rate-limits.md#rate-limits). The agent host's calls count there too.
- **Quota.** Tool calls of outside clients count toward no quota, because the client brings its own
  model. Requests of the agent host count toward `authoringRequests`, see
  [tenant quotas](tenant-quotas.md#quotas).
- **Size.** A tool input or output is at most 1 MiB. A larger output, such as the diff of a large
  draft, is cut into pages that the tool's `cursor` reads.

## Audit

- **Calls.** Each write tool call writes an audit record with the person, the client id, the tool,
  the application, the draft and the resource ids, never the content.
- **Consent.** Consent and its revocation write audit records in the realm and a platform audit
  record in Axis.
- **Logs.** The server logs each call's tool, client id and outcome, never the content. The
  [log inventory](log-inventory.md) gains the event when M35 builds it.
