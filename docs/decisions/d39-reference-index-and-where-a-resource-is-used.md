# D39. Reference index and where a resource is used

Status: **Agreed**

Every compile collects each reference it resolves into a reference
index. Studio reads it through two authoring API routes and shows a **Uses**
list and a **Used by** list for each resource. The compile already builds the
index with every relation of the design. Studio's lists and delete warning are
built. See the [reference index](../reference/configuration.md#reference-index),
the [authoring API](../reference/authoring.md#references) and the
[Studio section of the frontend](../reference/studio.md).
The owner confirmed these points on issue #570 on 2026-10-18.

- **Reference index:**
  - Every compile builds the index, in the same pass and in memory. It is not
    stored in the release.
  - Nodes are resources (id, kind, name, file), text keys, extension packages
    and modules.
  - Edges go from a resource and a JSON Pointer path in it to a node, with a
    relation such as `field target`, `data source entity`, `task form`,
    `rule call`, `label text`, `grant` or `widget process`.
  - It holds every reference the compiler resolves: the entity, data source,
    form, page, process, event, endpoint, connector, role, rule, sequence,
    package and module references, rule calls inside expressions, and the
    text key of every label.
  - An unresolved reference is a diagnostic, not an edge. A resource with
    errors keeps the edges that did resolve.
- **Authoring API:**
  - `GET /api/studio/apps/{application}/resources/{id}/references` answers
    `uses` and `usedBy`. Each entry has the other resource, the file, the path
    and the relation.
  - `GET /api/studio/apps/{application}/texts/{key}/references` answers where a
    text key is used.
  - Both follow the `401`, `403` and `404` rules of the rest of the authoring
    API.
  - The resource list shows each resource's `usedBy` count, so Studio can warn
    before a delete. The warning does not block the delete. The next compile
    reports what broke.
- **Studio:**
  - Each resource shows a **Uses** list and a **Used by** list. A Used by entry
    opens the other resource at the path of the reference. A Uses entry opens
    it at its top. An entry expands one more step on request.
  - Uses and Used by stay lists, with no graph drawing library. The process graph of
    [D53](d53-typed-editors-and-the-process-graph-in-studio.md) is the only drawn graph, and it
    does not change the index.
- **Cost:**
  - The index adds at most 30% of compile time, or 300 ms when that is more.
    A where-used answer for an application of typical size returns within 10
    seconds. The [reference index](../reference/configuration.md#reference-index)
    states both bounds.

_Why:_ [Typical application size](../domain/knowledge.md#typical-application-size)
says authoring at 30–90 entities and 150–250 data sources needs a dependency
graph and "where is this used", because lists do not work at that size. The
compiler already resolves every reference, but spreads them over the per-kind
models, so one index collects them and gives the reverse direction. Building it
on every compile keeps it out of the release and costs nothing at run time. A
later feature that needs it at run time would build it again from the release.
Two lists that expand stay accessible and need no new frontend library. Warning
before a delete without blocking it follows
[D38](d38-studio-edits-application-folders-through-an-authoring-api.md),
where a broken resource is a diagnostic and never blocks a save, and
[D5](d5-configuration-is-authored-as-files-first.md), where the files stay
the source of truth.
