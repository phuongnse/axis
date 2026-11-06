# Repository layout

This page shows where each part of Axis lives in the repository and the rules that every module
follows, so that a new project or folder lands in the right place. How the hosts fit together is in
[architecture](architecture.md).

## Repository layout

The tree below names each top-level folder and the projects in it. A project is created when the
first issue needs it, so the tree holds only projects that exist, and planned ones are listed under
it. The .NET projects are in the solution `Axis.slnx`.

```text
src/
  Axis.Server/            ASP.NET Core host: API, BFF, SPA hosting
  Axis.Worker/            background host
  Axis.Agent/             the agent host, which runs authoring requests
  Axis.Hosting/           code that the hosts share: the health endpoints
  Axis.Admin/             console host for tenant provisioning, migrations, activation, backup and
                          restore, and the publisher
  Axis.Administration/    library behind the Axis.Admin commands, such as the tenant setting commands
                          and the platform audit records
  Axis.Scenarios.Runner/  library that runs an application's test scenarios, from a folder or from
                          files in memory; used by Axis.Scenarios and the publisher
  Axis.Scenarios/         console host over Axis.Scenarios.Runner
  Axis.Core/              shared primitives: the tenant context and telemetry
  Axis.Configuration/     resource model, file loader, JSON Schemas, compiler, diagnostics, releases,
                          extension package catalog
  Axis.Expressions/       expression parser, type checker, interpreter, SQL translation
  Axis.Data/              entity storage mapping, schema planning, record commands, data sources
  Axis.Processes/         work items, steps, the task API, the record command, connector calls,
                          package operations, events, triggers, schedules and inbound endpoint starts
  Axis.Policy/            policy evaluation: entity, data source and process grants, record filters,
                          field sets
  Axis.Presentation/      the platform site, its texts and the site, page and widget metadata served
                          to the SPA
  Axis.Tenancy/           tenant resolution, connection factory, platform schema
  Axis.Extensions/        contracts for extension packages: operation interface, operation context
web/                      React + TypeScript SPA (Vite), Ant Design, and the Keycloak login theme
tests/
  Axis.<Module>.Tests/    unit tests per module
  Axis.Integration.Tests/ PostgreSQL and Keycloak (Testcontainers) and API integration tests
  Axis.LoadData/          generator of the load test's typical-size application with seeds
  Axis.PurchasingStub/    purchasing system stub for tests, scripts/dev.sh and Docker Compose
  packages/               test-only extension package, built as three versions; covers the
                          capability limits
  load/                   the k6 scenarios of the load tests
  e2e/                    Playwright journeys against the running server, and their fixtures
controlplane/
  src/Axis.ControlPlane/  the control plane service: its settings, its own schema and migrations,
                          its job queue and audit records, health and telemetry
  tests/                  its unit tests, and its integration tests on PostgreSQL (Testcontainers)
modules/                  built-in modules, as configuration, installed with Axis: organization and
                          approvals
applications/             built-in applications, as configuration, installed with Axis: approvals
samples/
  apps/                   the sample applications, as configuration only
  modules/                the sample module department-budgets, as configuration
  packages/               the sample extension package PaymentReferences and its unit tests
  keycloak/               the development Keycloak realms and their keys
  postgres/               init-roles.sql: the development database roles and the preview tenant's
                          database
deploy/
  compose/                production Compose example
  helm/axis/              Helm chart for Kubernetes
  keycloak/               the breached-password list that tenant realms check
scripts/                  the scripts of AGENTS.md and the checks that lint runs
```

- **`src/Axis.PackageHost/`** _(planned for M21)_. The package runner host, which runs extension
  package operations out of process, see
  [package runners](reference/extension-packages.md#package-runners).

## Module rules

These rules keep the modules apart, so that each one can change behind its contracts.

- **Ownership.** Each module owns its tables and its public contracts. Other modules use only those
  contracts and never read another module's tables directly.
- **Dependency direction.** Dependencies point inward:
  - Hosts depend on modules.
  - Modules depend on `Axis.Core` and on other modules' contracts.
  - Nothing depends on a host.
- **Shared transactions.** A module that takes part in a step transaction receives the shared
  connection and transaction through a unit-of-work contract, so writes through EF Core and through
  Npgsql commit together.
- **Persistence boundary.** Engine logic does not depend on ASP.NET Core or on persistence details.
  It depends on small interfaces with infrastructure implementations (ports and adapters).
- **Commands and queries.** Commands enforce state transitions, and queries return authorized
  projections. Both use the same tenant database, with no separate read store.
