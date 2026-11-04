# Module resource saves

Detailed reference for the module resource save and reset routes. The rest of the authoring API is in
[authoring.md](authoring.md). The conventions of [architecture.md](../architecture.md) apply.

`PUT .../modules/{module}/resources/{id}` takes a module resource that an author edited, and writes the
application's [customization layer](customization-layers.md#in-studio) for it. The author never copies the
resource. `POST .../modules/{module}/resources/{id}/reset` takes one element out of that layer, see
[Element reset](#element-reset).

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

## Element reset

`POST .../modules/{module}/resources/{id}/reset` takes `{ "list": "<JSON Pointer>", "key": "<list key>" }` as
`application/json`. `list` is the pointer of a keyed list in the resource with the layer applied, and `key` is the key of one
element, both as the read route reports them in `marks` and `removed`. The body holds these two strings and nothing else.
`list` may also be `/texts` of a text resource, with a text key as `key`. A text key is matched exactly, with its letter
case, as a property name is. A changed key gets the module's text back and an added key is dropped.

- **What it does.** A changed element gets the module's version back, in the module's position. An added element is dropped.
  A removed element comes back where the module has it. The server applies this to the resource with the layer applied, and
  then writes the layer as [the difference](customization-layers.md#in-studio) that is left, with the same code and the same
  layer write as the save. The other changes of the layer stay.
- **Nothing is left.** The server deletes the layer file, and the answer has a null `file` and `version` and no `ETag`.
- **The version.** `If-Match` holds the layer's version and is required. A stale version, and a resource with no layer, are
  `409`. Nothing is written.
- **Refused with `400`.** The body is not exactly two strings, a module scenario, a name that two resources of the module
  share, a layer that removes the whole resource or that the difference cannot read back, and a contract reset that would
  leave more than added states and transitions. For the last two the author uses Reset all.
- **Not found.** A `list` that is neither a keyed list of the resource nor `/texts` of a text resource, or a `key` that
  neither the layer's resource nor the module has in it, is `404`.
- **Answer.** The same as the save: `200` with `{ file, version, diagnostics }` and an `ETag` header when a layer is left.
  The diagnostics can show that the layer now points at an element that is gone. The route does not refuse this.
- **Reset all.** It is the existing delete of the layer resource, `DELETE /api/studio/apps/{application}/resources/{layer id}`
  with `If-Match` of the layer's version. `layer.id` and `layer.version` come from the read route. After it, the read route
  answers `layer: null` with empty `marks` and `removed`.

After the checks of the [authoring API](authoring.md#order-of-checks), numbers 1, 2, 3 and 5, the reset checks in this order:

1. The module the application pins, and then the resource of the pinned module version, `404`.
2. `If-Match` missing, `428`.
3. A content type other than `application/json`, `415`.
4. A body that is not exactly the two string properties `list` and `key`, `400`.
5. A module scenario, or a name that two resources of the module share, `400`.
6. No layer, or a version that is not the layer's, `409`.
7. A layer that removes the whole resource, or that cannot be read back as a difference, `400`.
8. A `list` or `key` that names no element, `404`.
9. A contract reset beyond added states and transitions, `400`.
10. The write lock cannot be taken, `503`.
11. Under the lock, a stale version, `409`.
