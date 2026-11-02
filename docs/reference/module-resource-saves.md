# Module resource saves

Detailed reference for the module resource save route. The rest of the authoring API is in
[authoring.md](authoring.md). The conventions of [architecture.md](../architecture.md) apply.

`PUT .../modules/{module}/resources/{id}` takes a module resource that an author edited, and writes the
application's [customization layer](customization-layers.md#in-studio) for it. The author never copies the
resource.

- **The difference.** The body is the edited resource whole. The server compares it with the file of the pinned module
  version, by the list keys, and writes `customizations/<module>/<name>.json` with only the changed properties, the added
  elements and the removes. The compile merges the layer back into the module resource.
- **Nothing differs.** The server deletes the layer file. When there is no layer, it writes nothing. The answer has a null
  `file` and `version`.
- **The layer.** An existing layer is found by its `target`: the module id matches exactly and the name ignoring letter
  case, and the first such file in path order counts. It keeps its `id`, its file and its `target`. A new layer gets a new
  `id` and the module's spelling of the name. A layer that removes the whole resource is replaced by the difference.
- **The version.** `If-Match` holds the layer's version, as for a patch. `If-None-Match: *` says that the resource has no
  layer. Both, or neither, is refused. A stale version, a missing layer for `If-Match`, a layer for `If-None-Match: *` and an
  `If-None-Match` other than `*` are `409`, and nothing is written. The check runs under the [write lock](authoring.md#versions).
- **Refused with `400`.** A module scenario, a name that two resources of the module share, a change to `id`, `kind`,
  `name` or `formatVersion`, a property of the module that the body drops, and the other edits that
  [the difference](customization-layers.md#in-studio) cannot express. A contract edit that changes or removes a state or a
  transition, or changes anything but its lifecycle, is `400` too. A transition into a protected state is not refused. It
  shows up as a diagnostic.
- **Answer.** `200` with `{ file, version, diagnostics }` and an `ETag` header of the version, when a layer is written. The
  diagnostics are the folder's, as for the other writes. `GET` of the same route answers the resource with the layer
  applied and the layer's `{ id, file, version }`, so Studio reads the version for its next save from there.

## Order of checks

After the checks of the [authoring API](authoring.md#order-of-checks), numbers 1, 2, 3 and 5, the route checks in this order,
and the first failure is the response:

1. The module the application pins, and then the resource of the pinned module version, `404`.
2. `If-Match` or `If-None-Match` missing, `428`, or both sent, `400`.
3. A content type other than `application/json`, `415`.
4. A body that is not a JSON object, `400`.
5. A module scenario, or a name that two resources of the module share, `400`.
6. An edit that cannot be a layer, or a contract edit beyond added states and transitions, `400`.
7. An `If-None-Match` other than `*`, `409`.
8. The write lock cannot be taken, `503`.
9. Under the lock, a stale version, `409`.
