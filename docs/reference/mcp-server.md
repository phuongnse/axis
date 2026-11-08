# MCP server

Axis serves an MCP server over the [authoring API](authoring.md), and it is the only way any agent
acts on Axis. The [agent host](authoring-requests.md#the-agent-host) and outside clients, such as
Claude Code or NexKit, use the same tools, so there is one surface to secure, test and limit, and a
developer builds on Axis from the tools they already use. This page gives the server's route, sign-in,
client registration, tools, limits and audit. The reasons are in
[D119](../decisions/d119-ai-authoring-in-studio.md).

## Route and transport

The server speaks the Streamable HTTP transport of the MCP specification, on the tenant's own host.

| Route                                           | What it does                                                                                               |
| ----------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `POST /mcp`                                     | Takes the JSON-RPC messages of a session, see [route and transport](#route-and-transport)                  |
| `GET /mcp`                                      | Answers `405`, as the server offers no server-sent stream, see [route and transport](#route-and-transport) |
| `GET /.well-known/oauth-protected-resource/mcp` | Serves the protected resource metadata, see [sign-in](#sign-in)                                            |

- **Route.** `POST /mcp` on each tenant's host takes the JSON-RPC messages `initialize`,
  `notifications/*`, `ping`, `tools/list` and `tools/call`. The host names the tenant, as for every route. `GET /mcp` answers `405`,
  because the server offers no server-sent stream. A preview tenant's host serves no MCP server: both MCP
  routes there answer `404`.
- **Pinned version.** Axis serves MCP specification version `2025-11-25`. This line is the one place
  the version is written. An upgrade of the version is a change of its own, checked like any other
  dependency, and the [authoring eval](model-bindings.md#the-authoring-eval) runs on it.
- **Sessions.** `initialize` with the pinned version answers an `Mcp-Session-Id`. The id is a
  data-protected token that holds the person's subject, the client id (`azp`) and the protocol version.
  Its protection is bound to the tenant, so the server stores nothing and a session id of one tenant
  never reads on another. Every later call sends the id and the version in `MCP-Protocol-Version`.
  Without the id, or with another version than the session's, the answer is `400`. An id that is not
  this person's session on this tenant is `404`. An `initialize` for another version answers `400` with
  the JSON-RPC error `-32602`, names the supported version in the error's data and issues no session.
- **Refused first.** Before any method runs, a request with an `Origin` header that is not the
  tenant's host is refused with `403`, as the specification asks against DNS rebinding. A request from a
  person without the [author role](authoring.md#settings) gets `403` too.

## Sign-in

A client signs in as a person, through OAuth as the MCP specification asks. The tenant's realm in
[Axis's identity provider](identity-provider.md) is the authorization server.

- **Discovery.** `GET /.well-known/oauth-protected-resource/mcp` answers the protected resource
  metadata of RFC 9728 with the resource URL, the realm as the authorization server, the scope
  `axis:authoring` and the header as the bearer method. The route is open and has no rate limit. A call
  without a valid token answers `401` with a `WWW-Authenticate` header that points to it.
- **Flow** _(planned for M35)_. The authorization code flow with PKCE (`S256`). The client sends the
  resource indicator of RFC 8707, the MCP server's URL, such as `https://acme.vn.example.com/mcp`.
- **Tokens.** The server takes only a bearer token whose audience is the MCP server's URL on the
  request's own host, such as `https://acme.vn.example.com/mcp`, and refuses any other audience, so a
  token for another resource or another tenant never works here.
- **Token lifetime** _(planned for M35)_. Access tokens live 5 minutes. Refresh tokens rotate on each
  use and end with the realm session, at most 8 hours.
- **Who acts.** The token's subject is the person. The route needs the [author role](authoring.md#settings)
  and answers `403` without it.
- **Tool grants** _(planned for M35)_. Every tool runs with the person's grants: the author role for
  reading and writing drafts, and the rules of [accepting](authoring-requests.md#accepting) for
  `accept_draft`.
- **Consent** _(planned for M35)_. The realm shows a consent screen, in the Axis theme, that names the
  client, its home page and the scope `axis:authoring`. Without consent, no token is issued.

## Client registration

_(planned for M35)_

A client registers as the pinned specification prefers, and a tenant administrator decides which
clients may connect at all.

- **Client ID Metadata Documents.** A client may use the URL of its metadata document as its client
  id. The realm fetches the document, checks that its redirect URIs are `https` or a loopback address,
  and shows its name on the consent screen.
- **Dynamic client registration.** A client that has no metadata document registers through the
  realm's registration endpoint. The realm's registration policies allow only public clients, the
  authorization code flow, `https` or loopback redirect URIs and the scope `axis:authoring`, and
  require consent.
- **The agent host** is a confidential client of each realm, which `tenant create` sets up. It takes
  no outside registration.
- **The administrator's switch.** The tenant setting `Ai:OutsideClients`, one of `all`, `allowList`
  and `none`, decides which outside clients may connect, and `none` is the default of a new tenant.
  Under `allowList`, `Ai:AllowedClients` lists the client ids that may connect. A client that the
  switch does not allow is refused at the realm, before any token is issued.
- **Connected apps.** A tenant administrator changes both settings on the
  [users page](user-administration.md), in a Connected apps tab that lists each client with consent,
  the people who consented and the last use, and revokes a client's consent there. Each change writes a
  platform audit record.

## Tools

Each tool takes the application's `name`, and a tool that writes names a draft. Every write goes
through the authoring API's checks, so a tool can do nothing that the person could not do in Studio.

| Tool                                       | Input                                               | Output                                                                                                                        |
| ------------------------------------------ | --------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `list_applications`                        | none                                                | The applications the person may author                                                                                        |
| `read_reference` _(planned for M35)_       | a topic, such as a resource kind                    | The schema and reference text of that kind, from the installed version of Axis                                                |
| `list_resources` _(planned for M35)_       | application, optional draft                         | Each resource's `id`, `kind`, `name`, `file` and `version`                                                                    |
| `read_resource` _(planned for M35)_        | application, `id`, optional draft                   | The resource's content and version                                                                                            |
| `read_module_resource` _(planned for M35)_ | application, module, `id`                           | A resource of the pinned module version, with the application's layer applied                                                 |
| `read_names` _(planned for M35)_           | application, optional draft                         | The names the application defines, as the [names route](authoring.md#names) gives them                                        |
| `check_expression` _(planned for M35)_     | application, draft, resource, path, expression      | The expression's type and diagnostics                                                                                         |
| `create_draft` _(planned for M35)_         | application, a title                                | A new [draft](revisions-and-drafts.md#drafts), with the version of the working folder it starts from                          |
| `write_resource` _(planned for M35)_       | application, draft, resource content                | Creates or replaces a resource in the draft, with its diagnostics                                                             |
| `write_layer` _(planned for M35)_          | application, draft, module resource, edited content | Writes the layer that holds the difference, as Studio saves do, see [customization layers](customization-layers.md#in-studio) |
| `delete_resource` _(planned for M35)_      | application, draft, `id`                            | Removes a resource from the draft                                                                                             |
| `validate_draft` _(planned for M35)_       | application, draft                                  | The draft's diagnostics, contract checks included                                                                             |
| `run_scenarios` _(planned for M35)_        | application, draft, optional names                  | The results of the application's scenarios and the contract scenarios of its modules, run on the draft by the publisher       |
| `diff_draft` _(planned for M35)_           | application, draft                                  | The draft's changes from the working folder, resource by resource                                                             |
| `accept_draft` _(planned for M35)_         | application, draft                                  | Asks for an accept, under the rules of [accepting](authoring-requests.md#accepting)                                           |

- **No other tool.** No tool publishes, activates a release, reads a record, a file or a secret,
  runs a command or opens a connection. A resource never holds a binding value, so no tool returns one.
- **Checked.** `McpToolsDocumentationTests` compares the table with the tools the server serves and
  with the `tools/list` answer. `McpToolOutputTests` calls every tool of `tools/list` on a tenant that holds
  a record, a `secret` binding, a `url` binding and the sign-in client secret, and fails when an output
  holds one of them or a tool answers an error. `McpToolGuardSelfTests` shows that the guard reports a
  leaking output, a tool with no input and a tool error.
- **Text is data.** A label, a description or any text a tool returns is content, never an
  instruction to the server. The server acts only on tool calls, each checked on its own, so no text,
  and nothing an outside client read elsewhere, gives an agent more than its tools allow.
- **Errors.** A refused call answers a tool error with the authoring API's problem title and
  diagnostics, never a stack trace or a path on disk.

## Limits

A call to the server is limited in rate and size, and the agent host's requests count toward a quota.

- **Rate limit.** Every call to `/mcp` counts in the MCP partition, by tenant and client id, see
  [rate limits](headers-and-rate-limits.md#rate-limits).
- **Quota** _(planned for M35)_. Tool calls of outside clients count toward no quota, because the client brings its own
  model. Requests of the agent host count toward `authoringRequests`, see
  [tenant quotas](tenant-quotas.md#quotas).
- **Size.** `POST /mcp` refuses a request body over 1 MiB with `413`, so a tool input is at most 1 MiB.
- **Output pages** _(planned for M35)_. A tool output is at most 1 MiB. A larger output, such as the
  diff of a large draft, is cut into pages that the tool's `cursor` reads.

## Audit

What an agent did is recorded, but never the content it read or wrote.

- **Calls** _(planned for M35)_. Each write tool call writes an audit record with the person, the client id, the tool, the
  application, the draft and the resource ids.
- **Consent** _(planned for M35)_. Consent and its revocation write audit records in the realm and a platform audit
  record in Axis.
- **Logs.** The server logs each call's tool, client id and outcome, and the
  [log inventory](log-inventory.md#security-events) lists the event.
