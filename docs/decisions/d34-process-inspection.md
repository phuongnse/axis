# D34. Process inspection

Status: **Agreed**

## Context

[Product goal 4](../product-brief.md#product-goals) asks that people see what a process did and why
it failed. The engine stores that, but values in a process's history and calls must not reach a user
whom [D24](d24-roles-and-policies.md)'s grants would not let read them, and
[operations](../domain/knowledge.md#operations) asks that sensitive fields are masked by a field flag.

## Decision

- **Inspecting is a grant.** A `process` grant takes the action `inspect`. An instance whose subject
  record the user may not read does not exist for them.
- **An instance list and an instance detail.** The detail gives the graph of the release the instance
  is pinned to, the path it took, its tasks, and its connector and package calls with every attempt.
- **What was sent stays visible.** Each attempt and lookup of a call is kept, without request or
  response bodies, and a connector call keeps its input after it settles.
- **Values follow grants and fail closed.** A call's input is shown only when the user may read every
  field that the call's mapping reads or writes, on any entity, through reference paths and named
  rules. Otherwise the whole input is hidden.
- **A field can be `sensitive`,** and its value is never shown in inspection, to anyone. The record API
  still follows the grants.
- **A step error is stored as the exception type or a fixed text,** never the exception's message or
  a value.
- **A `processInspector` widget** lists the instances of one process and draws an instance's path on
  its graph, and the record history links to an instance when the user may inspect it.

The detail is in [process inspection](../reference/process-inspection.md#process-inspection) and the
[process inspector widget](../reference/process-inspector-widget.md).

## Consequences

- An operator can see where an instance waits and why it failed without reading the database.
- The graph comes from the pinned release and the grants from the active one, as
  [D29](d29-release-pinning-and-text-resources.md) keeps them.
- A user may see less than they could read field by field, because a partly readable input is
  hidden whole.
