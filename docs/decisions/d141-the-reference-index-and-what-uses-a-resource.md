# D141. The reference index, and what uses a resource

Status: **Agreed**

## Context

At the [typical application size](../domain/knowledge.md#typical-application-size), authoring needs
a dependency graph and "where is this used". The compiler resolves every reference, but each kind's
model holds its own, so nothing answers what refers to a resource or a text key.

Studio showed the answer as two lists at the foot of every resource page, Uses and Used by. What a
resource uses is what its own editor already shows, so one list repeated the page and needed a rule to
leave out what the form shows. Only what uses a resource matters before a rename or a delete, and at the
foot of the page it was in the way the rest of the time.

## Decision

- **Every compile builds a reference index** of each reference it resolves, in memory and in the
  same pass. It is not stored in the release, and an unresolved reference is a diagnostic, not an
  edge.
- **Studio shows what uses a resource, as a list.** Each resource that something can use has a **Used
  by** tab of its own, read through the authoring API, and an entry opens the other resource. What a
  resource uses is shown by its own editor, so Studio has no Uses list. The API still answers both.
- **A delete warns and never blocks.** The next compile reports what broke.
- **The index has a cost bound:** at most 30% of compile time, or 300 ms when that is more.

The detail is in the [reference index](../reference/configuration.md#reference-index), the
[authoring API](../reference/authoring.md#references) and the
[Studio resource form](../reference/studio-resource-form.md#used-by).

## Consequences

- The index costs nothing at run time. A later feature that needs it there builds it from the
  release.
- A delete that does not block follows
  [D38](d38-studio-edits-application-folders-through-an-authoring-api.md), where a broken resource
  still saves.
- Deleting a resource breaks nothing it uses, so only Used by bears on a delete.
- An author who wants a resource's uses in one place reads its editor, or the JSON view.

## Alternatives

- **A drawn dependency graph.** It needs a graph drawing library, while lists stay accessible and
  need none.
- **Two panels at the foot of the page.** Both show on every visit, and Uses repeats the form.
- **A Dependencies tab with a switch between Used by and Uses.** A tab within a tab, for a list that
  has little to add.
