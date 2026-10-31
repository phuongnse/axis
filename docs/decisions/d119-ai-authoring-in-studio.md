# D119. AI authoring in Studio

Status: **Agreed**

The owner agreed these points on 2026-10-31. Every customer now expects AI, and the small
businesses of [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) have no
IT team to set an application up by hand. NexKit builds the platform from GitHub issues with
Claude, and that stays as it is. Axis applies the same way of working to applications and modules:
an author describes a change in words, and an agent makes it, inside Axis, wherever Axis runs, with
whichever model the installation chooses. Everything here is _(planned for M35 and M36)_, see the
[roadmap](../roadmap.md#milestones).

- **Why Axis needs no GitHub for this:** NexKit's agents write any code and run any command, so
  they need a runner of their own. Axis's agents write typed configuration through the
  [authoring API](../reference/authoring.md), and Axis already holds what a pipeline needs: the
  compiler and its diagnostics, test scenarios, the preview tenant, module contracts, releases and
  the publish history.
- **One tool surface, the MCP server:** Axis serves an MCP server over the authoring API. It is the
  only way any agent acts on Axis.
  - Its tools read a resource, change a draft, write a customization layer, validate, run
    scenarios, read diagnostics and read the reference index. There is no shell, no file system
    and no network behind them, so an agent can do nothing that its user could not do in Studio.
  - A client acts with the grants of the person it signs in as, through OAuth as the MCP
    specification asks, on Axis's own identity provider. It changes only drafts.
  - Publishing is not a tool. A person publishes in Studio, with the publisher role
    ([D47](d47-publishing-from-studio.md)). Accepting a draft is a tool, under the rules below.
  - The MCP server has rate limits and counts toward the tenant's AI quota. Axis pins the version
    of the MCP specification it serves, and an upgrade of that version is checked like any other
    dependency.
  - Outside clients, such as Claude Code, NexKit or any other MCP client, use the same tools as
    Axis's own agent, so a developer or a partner builds on Axis from the tools they already use.
- **The agent host is one client:** a host of its own beside the server, the worker and the
  publisher, shipped in the same images, Helm chart and Compose file. It runs the request loop
  below and calls the MCP server like any other client, with no shortcut of its own. It runs the
  same way in a SaaS cell and on premises, with no GitHub, no CI service and no particular cloud.
- **The model is a binding, and no provider is built in:**
  - The agent host calls a model through an adapter of Axis. The request loop, the tools and the
    checks do not know which model is behind it, and a new provider is a new adapter.
  - Two kinds of binding stand side by side, and any installation chooses either: `anthropic`, for
    Claude through the Anthropic API, Amazon Bedrock or Google Vertex AI, and `openai-compatible`,
    for any endpoint that speaks that API, such as OpenAI, Azure OpenAI, Gemini, DeepSeek, Qwen,
    Mistral, or a model the installation serves itself through vLLM or Ollama.
  - The installation names the binding, as a connector's deployment binding names its system
    ([D25](d25-connectors-deployment-bindings-and-secrets.md)). A tenant may bring its own key
    where the installation allows it.
  - Axis SaaS uses Claude by default, because it does well at long runs of tool calls. That is a
    setting of the SaaS, not a rule of the platform.
- **An authoring eval decides which models are supported:** a set of authoring requests with known
  outcomes, such as changing an application, adding an approval step, and changes the contract must
  refuse, runs through the MCP server against any model. A model is supported when it passes the
  eval, whoever makes it. The eval runs for every supported model before a release, an operator can
  run it against their own model, and the SaaS changes its default model on the eval's numbers.
- **A change is a draft:** each request works on a draft of the application, never on its working
  folder. The draft records the version of each resource it changes. Studio shows the draft's diff
  resource by resource.
- **Accepting a draft:** accepting moves the draft's changes into the working folder. It does not
  put them live: only publishing does.
  - The author of the request accepts or drops the draft in Studio. An agent may accept it too,
    through the tool, when the application allows agents to accept and the request asked for it,
    as NexKit's own automatic merge works.
  - An agent accepts only a draft whose checks and review passed, and only for a person who holds
    an author role on the application.
- **No lost and no broken changes:**
  - The authoring API already refuses a write to a resource whose version changed since it was
    read, so no write overwrites another.
  - Accepting is all or nothing. Every resource the draft changes must still have the version the
    draft recorded, or nothing is written and the accept answers a conflict.
  - Accepting runs the compiler and the scenarios again on the working folder as it is now, with the
    draft applied, so two changes that pass alone cannot break the application together.
  - An application has one accept at a time, in a queue, so two accepts never interleave.
  - On a conflict, an agent reads the folder again, makes its change on a new draft and runs the
    checks again. A person's draft shows the conflict in Studio, and the person decides.
  - Each accept, and each save in Studio, is a revision of the working folder that names who made
    it, and through which request. A revision can be undone.
  - Publishing shows the whole diff from the live release, with what each agent changed marked.
- **The same loop as NexKit:**
  - The author writes the request. The agent answers with a plan, and the author approves it.
  - The agent changes the draft. The compiler, the application's scenarios and the contract
    scenarios of its modules run as its checks.
  - A second agent session reviews the draft against the request.
  - Failed checks and review findings start a fix round, up to a set number of rounds.
  - The draft is accepted as above. An agent never publishes.
- **Built-in modules first:** when a request matches a built-in module, the agent declares the
  module and writes a customization layer instead of new resources, so the application keeps its
  upgrades and its contract ([D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)).
  It writes scenarios for what it adds.
- **What reaches the model:** the application's resources, diagnostics, scenario results and the
  request. Records of the tenant never do, and a scenario's data is made up for the scenario.
- **Text is data:** labels, descriptions, texts, and anything an outside client read elsewhere,
  never give an agent more than its tools allow. Every change still passes the compiler, the
  contract, the scenarios, the review and the author. The MCP server, its sign-in and its clients
  are new trust boundaries, so M35 adds them to the [threat model](../reference/threat-model.md).
- **Audit:** each request keeps who asked, the client, the model, the plan, the diff, the checks,
  the review and who accepted it.
- **Cost:** in Axis SaaS each plan includes a number of requests, as a quota of
  [D118](d118-axis-saas-in-cells-with-sign-up-quotas-and-billing.md). On premises, the customer pays
  its model provider.
- **Two milestones:**
  - **M35 AI changes an application,** in phase 1: the MCP server, the agent host, the model
    adapters, the authoring eval, changing an application that exists, and creating one from
    built-in modules and templates.
  - **M36 AI builds applications and modules,** in phase 2: a whole application or a reusable
    module from a description, through the same server and loop.
- **Order:** M35 follows M25 and M26, because it writes layers over built-in modules and relies on
  their contracts. It runs beside M31 to M34. M36 follows M35.
- **NexKit and Axis:** NexKit builds the platform's code on GitHub. Axis's agents build
  applications and modules as configuration, and NexKit can be one of their MCP clients. They share
  the way of working, not code.

_Why:_ an author without IT skills can describe a change but not draw it, so a request in words is
the shortest way from a business need to a working application. Agents that change typed
configuration through an API, checked by a compiler, contracts and scenarios, need no runner and no
outside pipeline, so the feature runs wherever Axis runs. One MCP server for every agent, Axis's own
included, leaves one surface to secure, test and limit, and lets any MCP client build on Axis. An
adapter, two kinds of binding and an eval keep Axis free of any one provider: a model earns its place
by what it does, and a closed network can use a model of its own. The contract of a built-in module
is what makes an agent's change safe to accept: a change that breaks a core rule is refused whatever
wrote it. Checks on an application take seconds, so every accept can check the latest folder, and
no change is lost or breaks another. The cost is a new host, a new trust boundary with its sign-in, an eval to keep, model
costs, weaker results with some models, and a whole application from one description waiting for
phase 2.
