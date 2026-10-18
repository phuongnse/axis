# Authoring API

Detailed reference for the authoring API that Studio uses: settings, routes,
versions, the order of checks, answers, writes and diagnostics. The conventions
of [architecture.md](../architecture.md) apply: sections marked
*(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

The authoring API edits the resource files of an
application folder on the server's disk. They are the same files that are kept
in git. The list, read and diagnostics routes are built. *(Planned for M8: the other
routes, with `If-Match`, the CSRF header and writes.)* It is
agreed in
[D38](../decisions.md#d38-studio-edits-application-folders-through-an-authoring-api--agreed).
It never activates a release and touches no database. Under `scripts/dev.sh`,
a saved file restarts the server, which activates the folder, as a hand edit
does today.

## Settings

Two server settings decide who may use the API and which
folders it may edit. See [Studio](configuration.md#studio) for their defaults.

- `Studio:Folders` lists the application folders. With no folder, the API
  has nothing to edit, and every route answers `403`. *(Planned for M8:
  `studio: false` on `GET /api/me` says the same.)*
- `Studio:AuthorRole` names the roles claim value that may use the API. With
  no value, nobody may. Application policies play no part.

## Routes

`{application}` is the `name` of the application's manifest,
such as `PurchaseRequests`, matched ignoring letter case. The server reads the
manifest of each listed folder on every request, in list order, and the first
match wins. A resource is addressed by its `id`, which is permanent and unique
in the application. File names and names can change.

Routes marked *(planned for M8)* are not built yet.

| Method and path | Response |
| --- | --- |
| *(planned for M8)* `GET /api/studio/apps` | `200` with the applications in `Studio:Folders`, each with its `name` |
| `GET /api/studio/apps/{application}/resources` | `200` with `{ "items": [...] }`, one item for every resource of the folder, sorted by `file`. Each item has `id`, `kind`, `name`, `file` and `version`. *(Planned for M8: `usedBy`.)* |
| `GET /api/studio/apps/{application}/resources/{id}` | `200` with `{ id, kind, name, file, version, content }` and an `ETag` header |
| *(planned for M8)* `POST /api/studio/apps/{application}/resources` | `201` with the new resource's `{ id, kind, name, file, version }`, a `Location` header and `diagnostics`. The body is the full resource |
| *(planned for M8)* `PATCH /api/studio/apps/{application}/resources/{id}` | `200` with `{ version, diagnostics }`. The body is `application/json-patch+json`, as RFC 6902 describes |
| *(planned for M8)* `DELETE /api/studio/apps/{application}/resources/{id}` | `200` with `{ diagnostics }` |
| *(planned for M8)* `GET /api/studio/apps/{application}/resources/{id}/references` | `200` with `{ uses, usedBy }`, see [References](#references) |
| *(planned for M8)* `GET /api/studio/apps/{application}/texts/{key}/references` | `200` with `{ usedBy }`, see [References](#references) |
| `GET /api/studio/apps/{application}/diagnostics` | `200` with `{ "diagnostics": [...] }`, the folder's diagnostics. A folder with errors still answers `200` |

`file` is the path of the resource's file relative to the application folder,
written with `/`. `id` is in the hyphenated 8-4-4-4-12 hex form, in either
letter case, and the answers write it in lower case. A file is a resource only
when it is a JSON object with a UUID `id` and a string `kind`. Other files are
left out of the list and cannot be read. When two files share an `id`, the
first in path order is the resource. *(Planned for M8)* `usedBy` is the number
of entries in the resource's `usedBy` list, see [References](#references).

## Versions

The `version` of a resource is the SHA-256 of its file's
bytes, in lower-case hex.

- A read sends it as a strong `ETag`, in quotes.
- *(Planned for M8)* A patch or a delete sends it back in `If-Match`.
- *(Planned for M8)* When the file no longer has that version, the answer is
  `409`. The client reads the resource again and retries.
- *(Planned for M8)* A patch or a delete without `If-Match` is `428`.

## Order of checks

Like the [record API](record-api.md#errors), a request is
checked in this order, and the first failure is the response. Checks 2 to 5
are built, and the others are *(planned for M8)*:

1. The CSRF header `X-Axis-Csrf: 1` on a `POST`, `PATCH` or `DELETE`, `403`.
2. The session, `401`. A session from another tenant counts as no session.
3. The author role, `403`. It needs `Studio:AuthorRole` to be set and held in
   the session's roles, and `Studio:Folders` to be non-empty. This holds for
   every route.
4. An unknown application, `404`. It is an application that is not in
   `Studio:Folders`.
5. An unknown resource or text key, `404`.
6. A patch or a delete without `If-Match`, `428`.
7. A bad body, `400`. That is a body that is not JSON, a patch that is not
   valid RFC 6902, a patch that does not apply, a patch that changes `id` or
   `kind`, a create without `id` or `kind`, or a create or a delete of the
   `application.json` manifest.
8. A stale version, `409`.
9. A create whose `id` or whose target file already exists, `409`.

The session comes before the role, and the role before the application. So a
caller without the role cannot find out which applications or resources
exist.

## Responses

Every error is problem details
(`application/problem+json`). The titles are fixed and never contain text
from the request. The rows for List resources, Read and Diagnostics are built, and the
others are *(planned for M8)*.

| Route | `401` | `403` | `404` | `409` |
| --- | --- | --- | --- | --- |
| List applications | No session | No author role, or no `Studio:Folders` | Not used | Not used |
| List resources | No session | No author role, or no `Studio:Folders` | Unknown application | Not used |
| Read | No session | No author role, or no `Studio:Folders` | Unknown application or resource | Not used |
| Create | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application | The `id` or the target file already exists |
| Patch | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application or resource | Stale `If-Match` version |
| Delete | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application or resource | Stale `If-Match` version |
| Resource references | No session | No author role, or no `Studio:Folders` | Unknown application or resource | Not used |
| Text key references | No session | No author role, or no `Studio:Folders` | Unknown application, or a key that no text resource defines and no label uses | Not used |
| Diagnostics | No session | No author role, or no `Studio:Folders` | Unknown application | Not used |

`400` is a bad body, and `428` a missing `If-Match`, as
[Order of checks](#order-of-checks) says. `415` answers a body of the wrong
content type, before the body is read.

## References

*(planned for M8)* The two references routes answer where a resource or a text
key is used. They read the [reference index](configuration.md#reference-index)
that every compile builds, as
[D39](../decisions.md#d39-reference-index-and-where-a-resource-is-used--agreed)
agrees.

- **Resource route.** `GET .../resources/{id}/references` answers
  `{ uses, usedBy }`. `uses` lists what the resource refers to. `usedBy` lists
  what refers to it.
- **Text key route.** `GET .../texts/{key}/references` answers `{ usedBy }`:
  the labels that use the key and the `text` resources that define it. A key
  that a label uses but no locale defines still answers with its uses, so an
  author who opens a missing key from an `AXC0028` diagnostic sees where it is
  used. `{key}` is matched ordinally, as keys are compared.
- **Entry.** Each entry is `{ relation, file, path, node }`.
  - `relation` is the name of the relation, such as `task form`.
  - `file` and `path` are always in the resource that holds the reference. So
    for `usedBy` they point into the other resource, and for `uses` into this
    one. An entry opens its resource at that path.
  - `node` is the other end: `{ type: "resource", id, kind, name, file }`,
    `{ type: "text", key }`, `{ type: "package", id }` or
    `{ type: "module", name }`. In `usedBy`, it names the resource that holds
    the reference.
- **Order.** Entries are in path order of the using file, then by path.
- **Compiled in memory.** The folder is compiled in memory, as for
  [diagnostics](#diagnostics), and no database is touched. A folder with errors
  still answers with the edges that resolved.
- **The list's `usedBy`.** The resource list gives each resource's `usedBy`
  count, so Studio can warn before a delete. The warning never blocks the
  delete. The next compile reports what broke.

## Writes

*(planned for M8)*

- **Atomic.** A write goes to a temporary file in the same folder and is then
  renamed over the target. A reader never sees half a file. A delete removes
  the file.
- **Format.** The file is written as JSON with 2-space indents, LF line ends
  and a final newline. The order of keys is kept, so a patch of one value
  changes one line in git.
- **New files.** A new file goes in the kind's plural folder, as in
  `samples/apps/purchase-requests`: `entity` in `entities`, `dataSource` in
  `data-sources`, `policy` in `policies`, and so on. The file name is the
  kebab-case `name`, such as `entities/purchase-request.json`. A `text`
  resource is named after its locale, such as `texts/en.json`. A patch that
  changes `name` keeps the file name.
- **The manifest.** `application.json` at the folder root can be patched. It
  cannot be created or deleted, because
  `AXC0009` (see the diagnostic codes in [configuration.md](configuration.md)) requires it there.
- **Paths.** A path is resolved and must stay under the application folder,
  so `..` and links cannot leave it. Only `*.json` files are read or written.
- **Patches.** A patch may not change `id` or `kind`. Every other change is
  allowed, including one that breaks the resource. The new content is written
  and the diagnostics report the breakage.
- **Never an activation.** A write never compiles against a database, never
  stores a release and never activates one.

## Diagnostics

`GET /api/studio/apps/{application}/diagnostics` returns the folder's
diagnostics on demand, and is built. *(Planned for M8: every write answers
with them too.)* A diagnostic never blocks a save.

- The folder is compiled in memory against the extension packages installed
  under `Extensions:Path` and the modules installed under `Modules:Path`, see
  [extension packages](configuration.md#extension-packages) and
  [modules](configuration.md#modules). No database is touched.
- The codes, severities and fields are those of
  [startup activation](configuration.md#startup-activation). Each diagnostic
  is `{ code, severity, message, file, path, resourceId }`. `severity` is
  `error` or `warning`. `resourceId` is the lower-case id of the resource,
  or `null` when the problem concerns a whole file or the whole folder.
- The route returns compile diagnostics only. The checks that activation runs
  against a tenant are not part of it: bindings, name and site path
  conflicts, and storage changes. They need a tenant database or the
  tenant's settings.
- A folder with an error is not activated. Under `scripts/dev.sh`, the
  restart after a saved file then stops the server, as a broken hand edit
  does today.
