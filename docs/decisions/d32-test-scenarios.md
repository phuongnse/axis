# D32. Test scenarios

Status: **Agreed**

## Context

Authors test an application by seeding data and checking what its processes and policies do, but
such tests usually need a hand-prepared environment and so never run in CI, as
[testing applications](../domain/knowledge.md#testing-applications) describes.
[D5](d5-configuration-is-authored-as-files-first.md) makes configuration files the source of truth,
so the tests can be files too.

## Decision

- **A scenario is a resource.** A `scenario` file in the application folder seeds data, runs actions
  as given users and asserts the outcomes, denied actions included. It compiles with the application,
  so every name it uses is checked, but it is left out of the release and its content hash, and
  adding a test never makes a release.
- **Users without an identity provider.** A scenario gives each user id the roles it holds, and the
  runner acts as that user as a session would, so policies and their denials are tested directly.
- **A throwaway tenant.** Each scenario runs on a new temporary tenant database, which the runner
  creates under a unique name and always drops, pass or fail. It drops only databases it created.
- **Processes run to their next wait.** After each step the runner runs the due work in-process
  until none is due. A scenario that runs more than 1,000 work items in one step fails.
- **A fixed clock that only moves forward.** A scenario may fix the current time, and an
  `advanceTime` step moves it forward and runs the work that becomes due, schedules included, as a
  worker would after an outage.
- **Stubs, never real calls.** A scenario gives the output of each connector and package operation
  it reaches, and a call without a stub fails it. No external system is called and no package code
  runs.
- **Run from the command line, CI and Studio.** A console host, `Axis.Scenarios`, runs every
  scenario of a folder against a PostgreSQL server and writes a JUnit file. CI runs the scenarios of
  every sample application, and Studio runs them through the publisher, as
  [D55](d55-running-test-scenarios-from-studio.md) says.

The resource, its steps, the runner and its command are in
[test scenarios](../reference/test-scenarios.md).

## Consequences

- An application's tests run in CI like any other test, and denials of
  [D24](d24-roles-and-policies.md) are tested without an identity provider.
- A failed run leaves no data behind and never touches a real tenant, and a looping process cannot
  hang CI.
- A scenario tests the folder, never a stored release.
- The runner uses one connection for every step, so scenarios do not test the separate database
  roles of [D31](d31-separate-database-roles.md).
- Package operations need unit tests in their own project.
