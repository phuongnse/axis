# D39. Reference index and where a resource is used

Status: **Agreed**

## Context

At the [typical application size](../domain/knowledge.md#typical-application-size), authoring needs
a dependency graph and "where is this used". The compiler resolves every reference, but each kind's
model holds its own, so nothing answers what refers to a resource or a text key.

## Decision

- **Every compile builds a reference index** of each reference it resolves, in memory and in the
  same pass. It is not stored in the release, and an unresolved reference is a diagnostic, not an
  edge.
- **Studio shows it as lists.** Each resource has a **Uses** list and a **Used by** list, read
  through the authoring API, and an entry opens the other resource.
- **A delete warns and never blocks.** The next compile reports what broke.
- **The index has a cost bound:** at most 30% of compile time, or 300 ms when that is more.

The detail is in the [reference index](../reference/configuration.md#reference-index), the
[authoring API](../reference/authoring.md#references) and the
[Studio resource form](../reference/studio-resource-form.md#references).

## Consequences

- The index costs nothing at run time. A later feature that needs it there builds it from the
  release.
- A delete that does not block follows
  [D38](d38-studio-edits-application-folders-through-an-authoring-api.md), where a broken resource
  still saves.

## Alternatives

- **A drawn dependency graph.** It needs a graph drawing library, while lists stay accessible and
  need none.
