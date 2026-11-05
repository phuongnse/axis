# D4. Modular monolith

Status: **Agreed**

## Context

A small team gets the most speed from one codebase that builds and deploys as one. Axis still needs
background work that outlives a request, and may later need to run parts of itself apart.

## Decision

- **One codebase with explicit module boundaries.**
- **A few hosts run the same modules.** The server serves the API, the BFF and the SPA, and the
  worker runs the background work. The other hosts are in the
  [architecture overview](../architecture.md#overview).
- **No message broker and no microservices.** The hosts share work through the databases.

## Consequences

- A change that crosses modules is one pull request, built and tested once.
- The module boundaries keep the option to split hosts later.

## Alternatives

- **Microservices with a message broker.** A small team gets more speed from one codebase, and the
  module boundaries already keep a later split open.
