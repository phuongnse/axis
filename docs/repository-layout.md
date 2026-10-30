# Repository layout

The folders of the repository and the rules that every module follows. The overview is in
[architecture.md](architecture.md).

## Repository layout

```text
src/
  Axis.Server/            ASP.NET Core host: API, BFF, SPA hosting
  Axis.Worker/            background host
  Axis.Scenarios.Runner/  library that runs an application's test scenarios, from a folder or from files in memory;
                          used by Axis.Scenarios and, later, by the publisher
  Axis.Scenarios/         console host over Axis.Scenarios.Runner that runs an application's test scenarios
  Axis.Admin/             console host for tenant provisioning, migrations, activation, backup and restore
  Axis.Administration/    library behind the Axis.Admin commands: the platform audit records so far
  Axis.PackageHost/       package runner host that runs extension package operations out of process (planned for M21)
  Axis.Core/              shared primitives: ids, results, clock, tenant context
  Axis.Configuration/     resource model, file loader, JSON Schemas, compiler, diagnostics, releases, extension package catalog
  Axis.Expressions/       expression parser, type checker, interpreter, SQL translation (M2)
  Axis.Data/              entity storage mapping, schema planning, record commands, data sources
  Axis.Processes/         work items, decision, task, operation, end and timer steps, task API, the record command, connector calls, package operations, events, triggers and schedules, inbound endpoint starts
  Axis.Policy/            policy evaluation: entity, data source and process grants, record filters, field sets
  Axis.Presentation/      site/page/widget metadata served to the SPA
  Axis.Tenancy/           tenant resolution, connection factory, platform schema
  Axis.Extensions/        contracts for extension packages: operation interface, operation context
web/                      React + TypeScript SPA (Vite), Ant Design
tests/
  Axis.<Module>.Tests/    unit tests per module (Axis.Server.Tests, Axis.Configuration.Tests, Axis.Data.Tests, Axis.Expressions.Tests, Axis.Tenancy.Tests)
  Axis.Integration.Tests/ PostgreSQL (Testcontainers) and API integration tests
  Axis.LoadData/          generator of the load test's typical-size application with seeds
  Axis.PurchasingStub/    purchasing system stub for tests, scripts/dev.sh and Docker Compose
  packages/               test-only extension package, built as three versions; covers the capability limits
  e2e/                    Playwright journeys against the running server
    fixtures/e2e-app/     generic test application the E2E server activates
    fixtures/release-app/ application a spec activates during the run, to test releases
    fixtures/subprocess-app/ parent and child sub-process the E2E server activates, for the inspector journey
samples/
  apps/purchase-requests/ the first sample application, as configuration only
  keycloak/               the development Keycloak realm with the sample's users
  postgres/               init-roles.sql, which creates the axis_schema, axis_runtime and axis_scenario database roles, and the preview tenant's database and roles
  packages/               the sample extension package payment-references and its unit tests
deploy/
  compose/                production Compose example
  helm/axis/              Helm chart for Kubernetes
```

Projects are created when the first issue needs them. The solution holds
`Axis.Server`, `Axis.Core` (only the tenant context so far),
`Axis.Configuration`, `Axis.Data`, `Axis.Expressions` (the parser, type
checker, interpreter and SQL translation), `Axis.Extensions` (the contracts
for extension packages), `Axis.Policy` (the evaluation of entity, data source and process grants, record filters and field sets),
`Axis.Presentation` (the platform site, its texts and the shapes of application site metadata), `Axis.Processes` (work items, the
`decision`, `task`, `operation`, `end` and `timer` steps, the task API, the record command, connector calls, package operations, events, triggers and schedules, and inbound endpoint starts), `Axis.Scenarios.Runner` (the library that runs an application's test scenarios), `Axis.Scenarios` (the console host over it), `Axis.Tenancy`, `Axis.Administration` (the library behind the `Axis.Admin` commands), `Axis.Worker`, the test projects, the sample extension package
`PaymentReferences` with its `PaymentReferences.Tests`, and `web/`.

## Module rules

- **Ownership.** Each module owns its tables and its public contracts. Other
  modules use only those contracts and never read another module's tables
  directly.
- **Dependency direction.** Dependencies point inward:
  - Hosts depend on modules.
  - Modules depend on `Axis.Core` and on other modules' contracts.
  - Nothing depends on a host.
- **Shared transactions.** A module that takes part in a step transaction
  receives the shared connection and transaction through a unit-of-work
  contract. Writes through EF Core and through Npgsql commit together.
- **Persistence boundary.** Engine logic does not depend on ASP.NET Core or on
  persistence details. It depends on small interfaces with infrastructure
  implementations (ports and adapters).
- **Commands and queries.** Commands enforce state transitions. Queries return
  authorized projections. Both use the same tenant database (no separate read
  store).
