# D171. Axis owns the engines that run an application, and runs authoring requests on NexKit's engine

Status: **Agreed**

## Context

Versioning, durability, authorization and inspection have to work the same way across every
engine that runs an application. An engine that Axis does not own would set its own semantics for
all four.

An authoring request runs no application. It changes an application's configuration through the
MCP server, in a draft, as a person would in Studio. Its loop is the one NexKit runs on a
repository, as [D150](d150-each-authors-own-ai-with-axis-counting-no-requests.md) says: a plan the
author approves, the change, the checks, an independent review, bounded fix rounds and an accept.
Axis's agent host writes that loop a second time, so each rule and each lesson of the loop would be
made twice and the two would drift apart. NexKit is
[splitting into an engine and hosts](https://github.com/phuongnse/nexkit/blob/main/docs/engine.md),
so that the loop runs where the work lives, behind a protocol that any language can speak.

## Decision

These parts hold as they did:

- **Axis implements the engines that run an application:** its configuration compiler, expression
  engine, data engine, process engine, presentation runtime and policy engine.
- **Third-party libraries are used for infrastructure:** HTTP, database access, serialization, UI
  components, standard security protocols and telemetry.
- **No engine that runs an application is a third-party workflow or rule engine.**

This part is new:

- **Authoring requests run on NexKit's engine** _(planned for M35)_. The agent host keeps no loop of
  its own. Planning, approval, questions, fix and diagnosis rounds, the review's rules, splits,
  budgets, retries and when the author is needed are the engine's, and the
  [authoring requests](../reference/authoring-requests.md) page gives Axis's policies for them.
  - **Through a protocol.** The engine runs as a program beside `axis-agent`, from the same images,
    Compose file and Helm chart, at the version Axis pins. Axis never imports its code, so the
    engine's language is not Axis's choice.
  - **Axis's adapter, in C#.** The agent host stores the engine's state with the request in the
    tenant database, sends it the events of Studio's routes, the MCP server and its own turns, and
    carries out its actions through the MCP server and the publisher. Each step is still a row, and
    a restart still resumes at the last finished step.
  - **Axis's own runner.** The agent host runs each model session itself, through its
    [model bindings](../reference/model-bindings.md) and on the author's own key, so a key never
    leaves Axis and the engine never sees one. Each stage keeps its own tools.
  - **Axis's policies.** The author always approves the plan and answers its questions. The author
    accepts, or the agent does where the application and the request allow it. A conflict at the
    accept is made again on a new draft. A request too large for one change is split into requests
    that each wait for the parts before them.
  - **The engine's conformance cases run in Axis's tests,** so the adapter reads and writes the
    protocol as the engine means it.

## Consequences

- One model of versioning, durability, authorization and inspection still holds in every engine
  that runs an application, and Axis builds and maintains each of them itself.
- The authoring loop is written once, for Axis and for every repository NexKit runs on. A rule of
  the loop changes in NexKit, and reaches Axis when Axis pins the release that holds it.
- Axis gains what NexKit's loop has and its own lacks: a plan that finds no change is needed,
  questions while the change is made, diagnosis rounds, findings followed from round to round,
  criteria the review could not verify, splits, and a pause at the author's usage limit.
- The images hold one more program, the engine, with its own runtime.
- The loop in Axis waits for the NexKit release whose GitHub host runs only through the engine.

## Alternatives

- **Axis keeps its own loop.** It needs nothing from NexKit, but every rule of the loop is made
  twice, and the two loops drift apart.
- **The engine as a .NET library inside the agent host.** It saves a process, but only a host in
  .NET could use it, and NexKit would be written again in C#.
- **The agent host runs NexKit's GitHub pipeline.** Axis runs where there is no GitHub and no CI
  service, as D150 says.
- **A third-party workflow engine for the loop.** It would bring a durability and an inspection of
  its own, and none holds the loop's rules.
