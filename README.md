# Axis

Axis is a general-purpose low-code platform. Organizations build complete
business applications on it by configuring them: data models, queries, rules,
processes, sites, pages, widgets, security policies and text. Axis validates
the configuration, publishes it as an immutable release, then runs it on
shared platform engines. An application does not need its own source build.

The first application built on Axis is **purchase requests**. An employee
drafts a request, a manager approves it, finance reviews it above a configured
amount, and an approved request is sent to a purchasing system.

Leave requests is the second sample. A third, a service desk, runs as a sample
too. A Playwright journey builds, tests and publishes it with Studio only. See
[D56](docs/decisions/d56-a-service-desk-as-the-third-sample-application.md).

## Status

Milestone **M13, Visual page and form designer**, is complete. The current milestone is
**M14, Workflow, automation and notifications**.
The [roadmap](docs/roadmap.md) says what each milestone delivers and its status. See [AGENTS.md](AGENTS.md#commands) for
the commands.

## Run Axis

Run `docker compose up --build`. It needs only Docker. It builds Axis from source
and starts PostgreSQL, Keycloak with the development realm at http://localhost:8080,
the server with the purchase request, leave request and service desk samples at http://localhost:5206, the worker,
and a purchasing system stub at http://localhost:5207 that stands in for the
external purchasing system. The server also serves a preview tenant of the same samples at
http://preview.localhost:5206, with its own database and no purchasing system bindings. An existing
Compose volume has no preview database, so run `docker compose down -v` once. That deletes its data.
The SPA signs people in through Keycloak at `/api/auth/sign-in`, but sign-in through
Compose is not wired yet. Every application page needs a session, so under Compose
opening a sample page starts a sign-in that cannot complete yet. See
[development](docs/development.md#docker-compose) for the port variables. This setup is for local
use. Production installs follow the [installation guide](docs/reference/installation.md),
for Docker Compose on one machine and for Kubernetes.

For development with hot reload, run `scripts/dev.sh`. It needs Docker, .NET and
Node. It runs the server, the worker and the SPA with hot reload. See
[development](docs/development.md#hot-reload-with-scriptsdevsh) for details.

## Documentation

The [docs map](docs/README.md) lists every doc and what it holds: the product brief, the roadmap,
the decisions, the vocabulary, the architecture and the detailed reference. Contributors and agents
start with [AGENTS.md](AGENTS.md).

## Technology

- Backend: C# on .NET 10 and ASP.NET Core.
- Frontend: React, TypeScript and Ant Design. The UI library stays inside the
  SPA, see
  [D58](docs/decisions/d58-ant-design-is-a-detail-of-the-spa-without-procomponents.md).
- Storage: PostgreSQL.
