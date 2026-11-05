# D119. AI authoring in Studio

Status: **Agreed**

## Context

Customers expect AI, and the small businesses of
[D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) have no IT team to set up an
application by hand: an author can describe a change in words but not draw it. NexKit builds the
platform from issues with agents, and needs GitHub and a runner because its agents write any code and
run any command. Agents that change an application write typed configuration, and Axis already holds
what their pipeline needs: the authoring API, the compiler, test scenarios, preview tenants, module
contracts and releases.

## Decision

- **An author describes a change in words, and an agent makes it inside Axis,** wherever Axis runs,
  with no GitHub, no CI service and no particular cloud.
- **One MCP server is the only way any agent acts on Axis.** It serves tools over the authoring API,
  with no shell, file system or network behind them, so an agent can do nothing that its person could
  not do in Studio. A client signs in as a person through OAuth on Axis's identity provider and acts
  with that person's grants. Outside clients, such as Claude Code or NexKit, use the same tools as
  Axis's own agent. See [the MCP server](../reference/mcp-server.md).
- **The agent host is one client among others,** a host of its own beside the server, the worker and
  the publisher, with no shortcut into a tenant. See [authoring requests](../reference/authoring-requests.md).
- **The same loop as NexKit.** The agent proposes a plan, the author approves it, the agent changes a
  draft, the compiler and the scenarios check it, a second agent session reviews it, and failures
  start a bounded number of fix rounds.
- **A change is a draft, and an agent never publishes.** Accepting a draft moves it into the working
  folder, all or nothing, after the checks run again on the folder as it is now. The author accepts,
  or an agent does where the application allows it. Only a person with the publisher role publishes,
  as [D47](d47-publishing-from-studio.md) says. Each save and each accept is a
  [revision](../reference/revisions-and-drafts.md) that can be undone.
- **Built-in modules first.** When a request matches a built-in module, the agent declares it and
  writes a customization layer, so the application keeps its upgrades and the module's contract
  ([D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)).
- **No model is built in.** The agent host calls a model through an adapter, by a binding the
  installation names, as a connector's deployment binding names its system
  ([D25](d25-connectors-deployment-bindings-and-secrets.md)). An authoring eval decides which models
  are supported, whoever makes them. See [model bindings](../reference/model-bindings.md).
- **The tenant's records never reach a model.** The model sees the application's resources, its
  diagnostics, scenario results and the request.
- **Text is data.** No label, description or text an agent reads gives it more than its tools allow.

[D125](d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) sets the shapes, and the
milestones and their order are in the [roadmap](../roadmap.md#milestones).

## Consequences

- The feature runs wherever Axis runs, on premises and in a closed network included, with a model
  the installation serves itself if it chooses.
- One server for every agent leaves one surface to secure, test and limit, and lets a developer or a
  partner build on Axis from the tools they already use.
- A module's contract makes an agent's change safe to accept: a change that breaks a core rule is
  refused, whatever wrote it. Checks on an application take seconds, so each accept checks the latest
  folder, and no change is lost or breaks another.
- In Axis SaaS each plan includes a number of requests as a quota of
  [D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). On premises, the customer pays
  its model provider.
- The cost is a new host, a new trust boundary with its sign-in, an eval to keep, model costs, and
  weaker results with some models.
- NexKit and Axis share a way of working, not code. NexKit builds the platform, and Axis's agents
  build applications and modules.

## Alternatives

- **Run Axis's agents through NexKit and GitHub.** It would tie the feature to GitHub, a runner and a
  cloud, and not run on premises.
- **One model provider built in.** It would tie every installation to that provider, and a model would
  not have to earn its place.
- **A shortcut for Axis's own agent.** A second way into a tenant would be a second surface to secure,
  and outside clients would get less than Axis's own agent.
