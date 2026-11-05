# D29. Release pinning and text resources

Status: **Agreed**

## Context

[D11](d11-durable-process-engine.md) and [execution](../domain/knowledge.md#execution) keep running
work on the release it started with. That must cover everything the work shows and calls, while an
old task must not keep a policy that a newer release tightened.

## Decision

- **New work starts on the active release.** The record API, data sources, process starts, inbound
  endpoints, triggers, sites and pages use it. A trigger that old-release work sets off starts new
  work on the active release.
- **Running work stays on its release until it ends.** Its steps, connector and package calls,
  tasks, forms and texts all use that release, and a task tells its client which release it is on.
- **Roles and policies always come from the active release,** for old tasks too, so a tightened
  policy applies at once. The assignee check of a task is unchanged.
- **Each release carries its own texts,** and they stay readable by release id for as long as the
  release is stored. A newer release may remove a key without changing a task that still runs on an
  older one.
- **A host does not start without the code its releases pin.** At the start of the server and the
  worker, every active release and every release with running or waiting work is rebuilt, and a
  pinned package version that is not installed stops the start.

The release that each endpoint and command uses, the texts endpoint and a walkthrough are in
[releases and running work](../reference/processes.md#releases-and-running-work).

## Consequences

- A purchase request that waits for approval finishes under the rules it started with, even after a
  new threshold goes live.
- Removing or renaming a policy can lock a user out of an old task's fields.
- An installation must keep every package version that a running instance pins, because
  [D28](d28-extension-packages.md) pins package versions in the release.
