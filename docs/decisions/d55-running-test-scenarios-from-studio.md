# D55. Running test scenarios from Studio

Status: **Agreed**

## Context

[Test scenarios](d32-test-scenarios.md) run through `Axis.Scenarios`, a console tool, so an author
without a shell cannot run them. Each scenario needs a database of its own that is created, migrated
and dropped, and the server may hold no right to create databases
([D31](d31-separate-database-roles.md)). Publishing from Studio already goes through stored requests
that a separate publisher runs ([D47](d47-publishing-from-studio.md)).

## Decision

- **A test is a publish request.** Studio stores a request of a fourth kind, `test`, which holds a
  snapshot of the folder and, optionally, the names of the scenarios to run. With no names, every
  scenario runs. The author role is enough, because a test changes no tenant, and a test writes no
  audit record and makes no release.
- **The publisher runs it with a scenario role.** Only the publisher runs a `test` request, through a
  separate PostgreSQL role, set in `Scenarios:ServerConnectionString`, that may create databases and owns no
  tenant database. It runs each scenario as `Axis.Scenarios` does, in a new database that it drops at
  the end, pass or fail. The server never gains the right to create databases, and the request
  touches the tenant database only to store its status and outcome.
- **Tests are bounded.** The scenarios of a request run one after another under a time limit, and at
  most one `test` request per application runs at a time, in a queue of its own, so a test never
  waits behind a publish.
- **The outcome is per scenario.** The request stores whether each scenario passed, its time and the
  failing step, so Studio opens a failure at that step. What stops a run, such as a compile error or
  the time limit, is a diagnostic of the request.
- **The server shows the controls by a setting of its own,** `Studio:Tests`. The server cannot see
  the publisher's settings, and with tenants from configuration the publisher writes nothing the
  server could read, so an installation turns both on together.

The route is in [test](../reference/authoring.md#test), the run in
[publisher](../reference/administration.md#publisher), the role in
[database roles](../reference/storage.md#database-roles), the settings in
[Studio settings](../reference/host-settings.md#studio-settings) and the controls in
[Studio](../reference/studio.md).

## Consequences

- An author tests from the browser, and scenarios still test the folder, not a release, as D32 says.
- The publisher holds a third right: besides every tenant's schema role, it may create databases. It
  still has no HTTP endpoint.
- A separate PostgreSQL server for the scenario role is advised, not required. On a shared server the
  scenario databases sit next to the tenant databases, but the role owns none of them.
- Any author can spend database time on tests, so the limits apply to every request.
- A publisher that crashes leaves its scenario database behind. With several publishers none can tell
  which leftover is still in use, so the operator drops them.

## Alternatives

- **Run the scenarios in the server.** It would need the right to create databases, which D31 keeps
  from it.
