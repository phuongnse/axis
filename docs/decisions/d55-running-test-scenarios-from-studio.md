# D55. Running test scenarios from Studio

Status: **Agreed**

An author runs an application's test scenarios from Studio. Studio stores a
new kind of publish request, `test`, and the publisher runs it with a separate
database role that may create databases. The server still gets no such right.
The route, the stored request and the publisher's run are built. The limit and the stop are built too, with `AXC0219` and `AXC0220`. The wiring of the scenario role in Compose, `scripts/dev.sh`, E2E, `init-roles.sql` and the chart is built as well. Studio's Run tests, Run and Test results list are built too. See
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
for M11, the [authoring API](../reference/authoring.md#test) for the route, the
[publisher](../reference/administration.md#publisher) for how it runs, and
[database roles](../reference/storage.md#database-roles) for the new role.
The owner confirmed these points on issue #782 on 2026-10-20.

- **A test request:**
  - A new publish request kind, `test`. It holds a snapshot of the folder, as
    `validate` does, and optionally the names of the scenarios to run. With no
    names, it runs them all.
  - `POST /api/studio/apps/{application}/test` answers `202` with the request
    and a `Location`, as `validate` does. It needs the author role only,
    because it changes no tenant.
  - The request status route answers its outcome. For each scenario it gives
    passed or failed, the failing step and its message, and the time it took.
  - The outcome goes in a new `scenarios` field of the request, stored in a new
    `scenario_results` column. Each item is
    `{ name, resourceId, file, outcome, durationMs, step, path, message }`.
    With `resourceId` and `path`, Studio opens a failing scenario at its step,
    as it opens a resource from a diagnostic.
  - A request ends `failed` when any scenario fails. Scenario failures go only
    in `scenarios`. Compile errors, the missing setting, an unknown scenario
    name and the time limit go in `diagnostics`.
  - An unknown scenario name is accepted with `202`. The request then fails
    with a diagnostic. The route does not answer `400`, so diagnostics keep one
    path, as for a folder with errors.
  - A `test` request always has the target `tenant`. It writes no audit record
    and has no release id. It uses the existing lease and three-claim rules. A
    claim after a lost lease runs every scenario again.
- **Who runs it:**
  - The publisher runs `test` requests. The server never does.
  - A new publisher setting, `Scenarios:ServerConnectionString`, names a
    PostgreSQL role that may create databases and owns no tenant database.
  - The publisher runs each scenario as `Axis.Scenarios` does. It creates a new
    `axis_scenario_<uuid>` database, migrates it, activates the snapshot,
    applies the seed data, runs the steps with stubs and drops the database at
    the end, pass or fail. It drops only databases it created.
  - Without the setting, a `test` request fails with a diagnostic that names it.
  - A `test` request never touches the tenant database except to store its own
    status and results. It does not wait behind a `publish` of the same
    application.
  - On `SIGTERM`, the publisher stops a running `test` request at once, drops
    its database and sets the request `failed`. A test may run for 10 minutes,
    which is longer than the stop grace period of both installs.
  - A database left behind by a publisher that crashed is not cleaned up
    automatically. The operator drops the `axis_scenario_*` databases that the
    scenario role owns. With several publishers, none can tell which leftover
    database is still in use. Automatic cleanup needs its own decision.
- **Limits:**
  - The scenarios of one request run one after another.
  - A request has a time limit of 10 minutes. Past it, the scenario that runs
    fails, its database is dropped and the request fails.
  - The work item limit of [D32](d32-test-scenarios.md) applies per
    scenario, as today.
  - At most one `test` request per application runs at a time, oldest first.
- **Studio:**
  - A **Run tests** button next to **Validate**, and a **Run** action on each
    scenario.
  - A results list: each scenario with passed or failed, its time, and the
    failing step. Selecting a failure opens the scenario at that step.
  - They show only when `GET /api/me` answers `studioTest: true`.
- **Settings in each place:**
  - Development, Compose and E2E set `Scenarios:ServerConnectionString` to a
    development role with `CREATEDB`, created by
    `samples/postgres/init-roles.sql`.
  - The production installs leave it empty by default. The installation docs
    say how to set it, and that it should point at a PostgreSQL server that
    holds no tenant data.
  - `studioTest` comes from a new server setting, `Studio:Tests`, which is off
    by default. The installs turn it on together with the publisher's
    `Scenarios:ServerConnectionString`. The server cannot see the publisher's
    settings, and under `Tenancy:Source=configuration` the publisher writes
    nothing the server could read. The owner confirmed this on issue #782.
  - The docs name no new diagnostic codes. The issues that build the feature
    choose the next free `AXC02xx` codes.
- **Effect on earlier decisions:**
  - [D31](d31-separate-database-roles.md): a third kind of role, which
    may create databases, on a host other than the server.
  - [D32](d32-test-scenarios.md): Studio is a second place where
    scenarios run, next to `Axis.Scenarios`.
  - [D47](d47-publishing-from-studio.md): a fourth request kind, with its
    own queue per application.

*Why:* [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
plans that Studio runs test scenarios in M11. Scenarios ran only through
`Axis.Scenarios`, a console tool, so an author without a shell could not run
them. The server may not create databases
([D31](d31-separate-database-roles.md)), and the publisher holds no such
right, so a request and a publisher with a new role keep that rule. The costs:

- **The publisher gets a third right.** It already holds every tenant's schema
  role. Now it can also create databases. It still has no HTTP endpoint.
- **A separate PostgreSQL server is advised, not required.** On one server, a
  scenario database sits next to tenant databases, but the scenario role owns
  none of them.
- **Authors may run tests without the publisher role.** A test changes no
  tenant. It costs database time, so the limits above apply.
- **Scenarios test the folder, not a release,** as D32 says.
