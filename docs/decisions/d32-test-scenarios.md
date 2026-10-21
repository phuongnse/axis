# D32. Test scenarios

Status: **Agreed**

A `scenario` resource is a configuration-level test that runs against its own
temporary tenant database. It seeds data, runs actions as
given users and asserts the outcomes, including denied actions. See
[configuration](../reference/test-scenarios.md) for the resource
shape and a purchase request example.
The owner confirmed these points for issue #415 on 2026-10-16.
The owner confirmed the points on advancing time for issue #481 on 2026-10-17.

- **Scenario resource:**
  - A new `scenario` resource kind has one file per scenario, under
    `scenarios/` in the application folder.
  - Scenario files are compiled with the application but left out of the
    release and its content hash. Adding a test never makes a new release.
  - A scenario has a `name`, `users`, an optional `now`, optional `stubs`
    and ordered `steps`.
- **Users:**
  - `users` maps each user id to the role names it holds, such as
    `{ "requester-1": ["requester"] }`. A role name must be a declared `role`.
  - No identity provider takes part. The runner gives each step the user's
    id and roles, as a session would.
- **Steps:**
  - `createRecord`, `updateRecord` and `deleteRecord` go through the record
    command, with `entity`, `values` and an `as` user. A created record gets
    an alias through `save`, which later steps use.
  - `startProcess`, with `process`, the record alias and `as`.
  - `completeTask`, with the record alias, the task's `step`, the `outcome`,
    optional `values` and `as`.
  - `expectRecord`, with the record alias and the expected `values`.
  - `expectInstance`, with `process`, the record alias, the `state` and,
    optionally, the `step`.
  - `expectTask`, with the record alias, the `step` and the expected
    `assignee` or `state`.
  - An action may declare `expect`: `denied`, `notFound`, `conflict` or a
    validation error at a field. An action without `expect` must succeed.
  - Each step's references to entities, fields, processes, steps, outcomes
    and users are checked when the application compiles.
- **Running:**
  - Each scenario gets its own temporary tenant database, created under a
    new unique name (`axis_scenario_<uuid>`). The runner migrates it,
    compiles and activates the application, applies its seed data and runs
    the steps. It drops the database when the scenario ends, pass or fail.
    It drops only databases it created.
  - After each action the runner runs the due work items in-process until
    none is due, so a process reaches its next wait before the next step.
  - If more than 1,000 work items run after one action, the scenario fails.
  - `now` fixes the time that expressions and task due dates read, for the
    whole scenario. Timestamps that PostgreSQL writes, such as `created_at`,
    keep the real clock.
  - `stubs` give the output of each connector operation and package
    operation that the scenario calls. A call without a stub fails the
    scenario.
- **Advancing time**:
  - A new step, `advanceTime`, has `by`, a positive ISO 8601 duration such as
    `P1D` or `PT2H`.
  - It needs the scenario's `now`. A scenario without `now` that uses it
    fails to compile.
  - It moves `now` forward by `by`. Every later step reads the new time.
  - The runner then runs every work item that is due at the new time, until
    none is due. Schedule triggers and their fan-outs are included. The limit
    of 1,000 work items applies per step.
  - Schedules catch up as on a worker after an outage. Only the latest
    occurrence at or before the new time runs, once. The rules of
    [schedules](../reference/process-events.md#schedules) apply unchanged. To fire a
    daily schedule three times, a scenario advances one day three times.
  - Time never goes back. No step sets an earlier time.
- **Where it runs:**
  - A new console project, `Axis.Scenarios`, runs every scenario of an
    application folder against a PostgreSQL server:
    `dotnet run --project src/Axis.Scenarios -- <folder> --server <connection string>`.
  - The `--server` connection must be allowed to create databases. The
    runner uses it for every step, so scenarios do not test the separate
    database roles of #414.
  - It writes a JUnit file to `artifacts/test-results/` and exits non-zero
    when a scenario fails.
  - `scripts/integration.sh` runs the scenarios of every application under
    `samples/apps/` against its Testcontainers PostgreSQL, so CI runs them.
  - Studio runs them through the publisher, see
    [D55](d55-running-test-scenarios-from-studio.md).
- **Limits:**
  - **Outside the release.** Scenarios do not travel with a release, so a
    scenario always tests the folder, not a stored release.
  - **Stubs only.** Scenarios never call a real external system, and never
    run real package code. Package operations get unit tests in their own
    project.
  - **Time moves forward only.** A scenario starts at its `now`, and only
    `advanceTime` moves it, forward.
  - **New project.** `Axis.Scenarios` is a new host next to the server and
    the worker.

_Why:_ [D5](d5-configuration-is-authored-as-files-first.md) makes
configuration files the source of truth, so tests over them are files too and
CI can run them. A scenario sets users and roles itself, so it checks
[D24](d24-roles-and-policies.md) denials without an identity provider.
A unique database that the runner always drops keeps a failed run from
leaving data behind or touching a real tenant. The work item limit stops a
looping process from hanging CI. Advancing time lets CI test schedule
triggers. See
[testing applications](../domain/knowledge.md#testing-applications).
