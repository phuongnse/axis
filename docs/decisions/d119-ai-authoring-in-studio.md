# D119. AI authoring in Studio

Status: **Agreed**

The owner agreed these points on 2026-10-31. Every customer now expects AI, and the small
businesses of [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) have no
IT team to set an application up by hand. NexKit builds the platform from GitHub issues with
Claude, and that stays as it is. Axis applies the same way of working to applications and modules:
an author describes a change in words, and an agent makes it, inside Axis, wherever Axis runs.
Everything here is _(planned for M35 and M36)_, see the [roadmap](../roadmap.md#milestones).

- **Why Axis needs no GitHub for this:** NexKit's agents write any code and run any command, so
  they need a runner of their own. Axis's agent writes typed configuration through the
  [authoring API](../reference/authoring.md), and Axis already holds what a pipeline needs: the
  compiler and its diagnostics, test scenarios, the preview tenant, module contracts, releases and
  the publish history.
- **The agent host:** a host of its own beside the server, the worker and the publisher, shipped in
  the same images, Helm chart and Compose file. It runs the same way in a SaaS cell and on premises,
  with no GitHub, no CI service and no particular cloud.
- **Typed tools only:** the agent acts only through tools that call the authoring API: read a
  resource, create or change one, write a customization layer, validate, run scenarios, read the
  reference index. It has no shell, no file system and no network of its own, so it can do nothing
  that an author with the same grants could not do in Studio.
- **The model is a binding:** the installation names the model endpoint, as a connector's
  deployment binding names its system
  ([D25](d25-connectors-deployment-bindings-and-secrets.md)).
  - Axis SaaS uses Claude through the Anthropic API.
  - An installation on premises or in a customer's cloud uses Claude through the API, Amazon
    Bedrock or Google Vertex AI, with the customer's own account.
  - An installation without internet access uses a model it serves itself behind an
    OpenAI-compatible endpoint. Its results are weaker, and the same checks still hold.
  - A tenant may bring its own key where the installation allows it.
- **A change is a draft:** each request works on a draft copy of the application, never on the
  folder that is live. Studio shows the draft's diff resource by resource, and the author accepts or
  drops it.
- **The same loop as NexKit:**
  - The author writes the request. The agent answers with a plan, and the author approves it.
  - The agent changes the draft. The compiler, the application's scenarios and the contract
    scenarios of its modules run as its checks.
  - A second agent session reviews the draft against the request.
  - Failed checks and review findings start a fix round, up to a set number of rounds.
  - A person decides. Publishing still needs the publisher role
    ([D47](d47-publishing-from-studio.md)), and an agent never publishes.
- **Built-in modules first:** when a request matches a built-in module, the agent declares the
  module and writes a customization layer instead of new resources, so the application keeps its
  upgrades and its contract ([D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)).
  It writes scenarios for what it adds.
- **What reaches the model:** the application's resources, diagnostics, scenario results and the
  request. Records of the tenant never do, and a scenario's data is made up for the scenario.
- **Text in resources is data:** labels, descriptions and texts never instruct the agent. The agent
  acts only through its tools, and every change still passes the compiler, the contract, the
  scenarios, the review and the author.
- **Audit:** each request keeps who asked, the model, the plan, the diff, the checks, the review and
  who accepted it.
- **Cost:** in Axis SaaS each plan includes a number of requests, as a quota of
  [D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). On premises, the customer pays
  its model provider.
- **Two milestones:**
  - **M35 AI changes an application,** in phase 1: changing an application that exists, and
    creating one from built-in modules and templates.
  - **M36 AI builds applications and modules,** in phase 2: a whole application or a reusable
    module from a description, and an MCP server over the authoring API, so a developer or a
    partner drives Axis from Claude Code or NexKit on their own machine.
- **Order:** M35 follows M25 and M26, because it writes layers over built-in modules and relies on
  their contracts. It runs beside M31 to M34. M36 follows M35.
- **NexKit and Axis:** NexKit builds the platform's code on GitHub. Axis's agent builds
  applications and modules as configuration. They share the way of working, not code.

_Why:_ an author without IT skills can describe a change but not draw it, so a request in words is
the shortest way from a business need to a working application. Agents that change typed
configuration through an API, checked by a compiler, contracts and scenarios, need no runner and no
outside pipeline, so the feature runs wherever Axis runs. Making the model a binding keeps Axis free
of any one provider and lets a closed network use a model of its own. The contract of a built-in
module is what makes an agent's change safe to accept: a change that breaks a core rule is refused
whatever wrote it. The cost is a new host, model costs, weaker results with local models, and a
whole application from one description waiting for phase 2.
