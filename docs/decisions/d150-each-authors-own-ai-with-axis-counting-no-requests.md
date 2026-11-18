# D150. Each author's own AI, with Axis counting no requests

Status: **Agreed**

## Context

D119 and D125 let an agent change an application inside Axis, on a draft that a person or an allowed
agent accepts. In Axis SaaS a plan includes a number of requests, counted as a quota, and the
installation's key pays the model provider. Axis then carries the model cost, and must price and limit
each tenant's use of a model it does not own.
[D149](d149-a-persons-secrets-are-sealed-for-the-host-that-uses-them-and-every-secret-rests-on-a-key-service.md)
lets a person seal their own AI key for the one host that uses it, so each author's key can pay for that
author alone.

## Decision

This decision replaces D119 and D125 whole. It restates every part of both that still holds, and
changes the rest.

These parts still hold:

- **An author describes a change in words, and an agent makes it inside Axis,** wherever Axis runs,
  with no GitHub, no CI service and no particular cloud.
- **One MCP server is the only way any agent acts on Axis.** It serves tools over the authoring API,
  with no shell, file system or network behind them, so an agent can do nothing that its person could
  not do in Studio. A client signs in as a person through OAuth on the tenant's realm and acts with that
  person's grants. Outside clients, such as Claude Code or NexKit, use the same tools as Axis's own
  agent. See [the MCP server](../reference/mcp-server.md).
- **The MCP server follows the MCP specification for a remote server:** Streamable HTTP on each tenant's
  host, and one pinned version of the specification. Clients register by a metadata document, or
  dynamically as a fallback, and a person consents to each. A tenant administrator decides which outside
  clients may connect.
- **The agent host is one client among others,** a host of its own beside the server, the worker and the
  publisher, with no shortcut into a tenant. See [authoring requests](../reference/authoring-requests.md).
- **The agent host runs requests durably and acts for the author.** A request and each step of its loop
  are rows in the tenant database, claimed with a lease, so a restart resumes at the last finished step.
  The server exchanges the author's session for a token of the agent host's client by OAuth token
  exchange.
- **The same loop as NexKit.** The agent proposes a plan, the author approves it, the agent changes a
  draft, the compiler and the scenarios check it, a second agent session reviews it, and failures start a
  bounded number of fix rounds.
- **A change is a draft, and an agent never publishes.** Accepting a draft moves it into the working
  folder, all or nothing, after the checks run again on the folder as it is now. The author accepts, or an
  agent does where the application allows it. Only a person with the publisher role publishes, as
  [D47](d47-publishing-from-studio.md) says. Each save and each accept is a
  [revision](../reference/revisions-and-drafts.md) that can be undone. Revisions and drafts live in git.
- **An accept is a publish request,** so the publisher runs the accepts of one application one at a time.
- **Only a person with the publisher role turns on auto-accept** for an application, so no tool or draft
  can change it.
- **Built-in modules first.** When a request matches a built-in module, the agent declares it and writes
  a customization layer, so the application keeps its upgrades and the module's contract
  ([D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)).
- **No model is built in.** The agent host calls a model through an adapter, by a binding the
  installation names, as a connector's deployment binding names its system
  ([D161](d161-connector-calls-run-on-bindings-and-secrets-and-process-inspection-keeps-what-a-run-sent-and-answered-with-step-errors-as-codes.md)). An authoring eval decides which models are
  supported, whoever makes them. See [model bindings](../reference/model-bindings.md).
- **The tenant's records never reach a model.** The model sees the application's resources, its
  diagnostics, scenario results and the request.
- **Text is data.** No label, description or text an agent reads gives it more than its tools allow.
- **A budget bounds each request,** in tokens and in fix rounds.
- **Outside clients are rate limited.** See [the MCP server's limits](../reference/mcp-server.md#limits).

These parts change:

- **Each author's own key runs their requests.** _(planned for M35)_ An author connects their own
  Anthropic API key, sealed as
  [D149](d149-a-persons-secrets-are-sealed-for-the-host-that-uses-them-and-every-secret-rests-on-a-key-service.md)
  says, and that key pays for the requests the author sends. See
  [a person's own key](../reference/model-bindings.md#a-persons-own-key).
- **Axis counts no requests.** _(planned for M35)_ There is no `authoringRequests` quota, and a plan
  includes no requests.
- **No installation key runs requests, and no tenant holds a model key.** _(planned for M35)_ The
  installation's key encrypts the installation's own secrets only, and `Ai:AllowTenantKeys` goes. See
  [a tenant's own key](../reference/model-bindings.md#a-tenants-own-key).
- **A tenant setting turns AI authoring off.** _(planned for M35)_ It is named "Authors may use AI with
  their own Anthropic key" and is on by default. Off, it refuses new sends, and requests already sent
  go on. See [AI authoring](../reference/user-administration.md#ai-authoring).
- **Claude comes first,** _(planned for M35)_ through an Anthropic API key, and the Axis SaaS default
  binding uses it.
- **A Claude subscription works only through the MCP server,** that is through an outside client such as
  Claude Code, which signs in as the person and uses the subscription itself.
- **The review runs on the request's own binding, with its author's key.** _(planned for M35)_ There is
  no review model of its own, so `Ai:ReviewBinding` goes.

## Consequences

_(planned for M35)_

- Each author pays for their own AI use, and Axis carries no model cost and no request quota.
- A person who has no key cannot send an authoring request, and a binding that needs no key, such as a
  model the installation serves itself in a closed network, still runs requests with none.
- Axis cannot cap an author's spending. The provider's own spending limit does, and Axis advises a key
  made for Axis alone with such a limit.
- A tenant administrator can switch AI authoring off for every author, but cannot pay for it for them.
- A lost host private key means that each author sets their key again, as D149 says.
- The review uses the author's key, so a request's review costs the author too.
- Axis keeps the request loop, its checks and its audit. It gives up the request quota and every
  installation or tenant key that pays for requests.

## Alternatives

- **Keep the installation key and a quota per plan.** Axis or the operator pays for every request, so
  every plan must be priced from model use, and each tenant's quota must be counted and enforced.
- **A tenant's own key.** It gives one bill per tenant, but one key then runs every author's requests, so
  the bill and the audit lose the person who sent each request.
- **A fixed review model.** It keeps one reviewer for every request, but adds a second binding to set up
  and pay for, and a second key to hold.
