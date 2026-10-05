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

Axis is at milestone **M0, Foundation**. The repository holds product and
architecture documentation only; there is no application code yet.

## Documentation

| Document | Purpose |
| --- | --- |
| [Product brief](docs/product-brief.md) | What Axis is, who uses it, the first application |
| [Decisions](docs/decisions.md) | Decisions that have been agreed, with reasons |
| [Architecture](docs/architecture.md) | Modules, hosts, storage, execution and testing |
| [Concepts](docs/domain/concepts.md) | The Axis vocabulary and what each resource does |
| [Design knowledge](docs/domain/knowledge.md) | What business applications need and the pitfalls to avoid |
| [Roadmap](docs/roadmap.md) | Milestones and the current backlog |

Contributors and agents start with [AGENTS.md](AGENTS.md).

## Technology

- Backend: C# on .NET 10 and ASP.NET Core.
- Frontend: React, TypeScript, Ant Design and ProComponents.
- Storage: PostgreSQL.
