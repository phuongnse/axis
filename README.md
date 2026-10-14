# Axis

Axis is an enterprise application platform. Organizations build complete
business applications on it by configuring them: data models, queries, rules,
processes, sites, pages, widgets, security policies and text. Axis validates
the configuration, publishes it as an immutable release, then runs it on
shared platform engines. An application does not need its own source build.

The first application built on Axis is **purchase requests**. An employee
drafts a request, a manager approves it, finance reviews it above a configured
amount, and an approved request is sent to a purchasing system.

## Status

Milestone **M5, Events, triggers and integration**, is complete. The current milestone is
**M6, Releases and versioning**. The [roadmap](docs/roadmap.md) says what each
milestone delivers and its status. See [AGENTS.md](AGENTS.md#commands) for
the commands.

## Run Axis

Run `docker compose up --build`. It needs only Docker. It builds Axis from source
and starts PostgreSQL, Keycloak with the development realm at http://localhost:8080,
the server with the purchase request sample at http://localhost:5206, the worker,
and a purchasing system stub at http://localhost:5207 that stands in for the
external purchasing system.
The SPA signs people in through Keycloak at `/api/auth/sign-in`, but sign-in through
Compose is not wired yet. Every application page needs a session, so under Compose
opening a sample page starts a sign-in that cannot complete yet. See
[AGENTS.md](AGENTS.md#commands) for the port variables.

For development with hot reload, run `scripts/dev.sh`. It needs Docker, .NET and
Node. It runs the server, the worker and the SPA with hot reload. See
[AGENTS.md](AGENTS.md#commands) for details.

## Documentation

| Document | Purpose |
| --- | --- |
| [Product brief](docs/product-brief.md) | What Axis is, who uses it, the first application |
| [Decisions](docs/decisions.md) | Decisions that have been agreed, with reasons |
| [Architecture](docs/architecture.md) | Modules, hosts, tenancy, security, execution and testing |
| [Reference](docs/reference/) | Detailed behaviour: configuration, storage, record API, frontend, expressions, data sources, processes and extension packages |
| [Concepts](docs/domain/concepts.md) | The Axis vocabulary and what each resource does |
| [Design knowledge](docs/domain/knowledge.md) | What business applications need and the pitfalls to avoid |
| [Roadmap](docs/roadmap.md) | Milestones, what each delivers and its status |
| [Delivery](docs/delivery.md) | How work flows through NexKit, from issue to merged pull request |

Contributors and agents start with [AGENTS.md](AGENTS.md).

## Technology

- Backend: C# on .NET 10 and ASP.NET Core.
- Frontend: React, TypeScript, Ant Design and ProComponents.
- Storage: PostgreSQL.
