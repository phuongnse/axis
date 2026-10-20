# Authoring API

Detailed reference for the authoring API that Studio uses: settings, routes,
versions, the order of checks, answers, writes and diagnostics. The conventions
of [architecture.md](../architecture.md) apply: sections marked
*(planned for Mx)* are not built yet, and Dn refers to
[decisions.md](../decisions.md).

The authoring API edits the resource files of an
application folder on the server's disk. They are the same files that are kept
in git. The application list, resource list, read, patch, create, delete, diagnostics,
references and expression check routes are built. It is
agreed in
[D38](../decisions.md#d38-studio-edits-application-folders-through-an-authoring-api--agreed).
Its edit routes never activate a release. Under `scripts/dev.sh`, a saved file
restarts the server, which activates the folder, as a hand edit does today.
A write takes a lock in the platform database, see [Versions](#versions). Reads
and diagnostics touch no database. Going live from Studio is
[Publishing](#publishing). `validate`, `publish`, `preview`, `rollback` and the release history are built. It asks for an activation: the
server stores a publish request in the tenant database, and a separate
[publisher](administration.md#publisher) runs it. The server never activates in
its own process, and
[D47](../decisions.md#d47-publishing-from-studio--agreed) agrees this.
A `changes` route that writes several files in one call is
*(planned for M11)*, see [Changes](#changes).

The [expression check](#expression-check) route helps Studio's expression
editor, and is built. *(Planned for M11.)* Two more routes help its pickers and
label texts: a [names](#names) route and a [text write](#text-writes). They are
agreed, with the expression check, in
[D52](../decisions.md#d52-reference-pickers-label-texts-and-the-expression-editor--agreed).

## Settings

Two server settings decide who may use the API and which
folders it may edit. A third, `Studio:PublisherRole`, decides who may go live. See [Studio](configuration.md#studio) for their defaults.

- `Studio:Folders` lists the application folders. With no folder, the API
  has nothing to edit, and every route answers `403`. `studio: false` on
  `GET /api/me` says the same.
- `Studio:ApplicationsPath` names the folder where Studio creates new
  applications _(planned for M11)_. With no value, Studio cannot create an
  application. See [Creating an application](#creating-an-application).
- `Studio:AuthorRole` names the roles claim value that may use the API. With
  no value, nobody may. Application policies play no part.
- `Studio:PublisherRole` names the roles claim value that
  may publish and roll back, on top of the author role. With no value, nobody
  may publish or roll back. `validate`, the request status and the release
  history need the author role only. A `preview` needs the author role only
  too, see [Preview](#preview).
- `Studio:Tests` turns on `studioTest` on `GET /api/me`, which shows the test
  controls in Studio. It is off by default. A `test` request needs the author
  role only, see [Test](#test).

## Routes

`{application}` is the `name` of the application's manifest,
such as `PurchaseRequests`, matched ignoring letter case. The server reads the
manifest of each listed folder on every request, in list order, and the first
match wins. A resource is addressed by its `id`, which is permanent and unique
in the application. File names and names can change.

| Method and path | Response |
| --- | --- |
| `GET /api/studio/apps` | `200` with `{ "items": [{ "name" }] }`, one item for each application in `Studio:Folders`, in list order. A folder without a readable manifest `name` is left out, and a name repeated ignoring letter case appears once, as the first folder wins in the routes below. After the `Studio:Folders` entries come the direct subfolders of `Studio:ApplicationsPath` that hold an `application.json`, sorted by folder name ordinally, and the same first-match rule by name applies. A folder whose name starts with `.` is skipped _(planned for M11)_ |
| `POST /api/studio/apps` | `201` with `{ name, diagnostics }` and a `Location: /api/studio/apps/{name}/resources` header. The body is `{ "name": "...", "template": "<template id>" }` as `application/json`, and `template` is optional. See [Creating an application](#creating-an-application) _(planned for M11)_ |
| `GET /api/studio/templates` | `200` with `{ "items": [{ id, version, title: { en, vi }, description: { en, vi }, diagnostics }] }`, one item for the newest installed version of each application template, sorted by `id`. See [application templates](configuration.md#application-templates) _(planned for M11)_ |
| `GET /api/studio/apps/{application}/resources` | `200` with `{ "items": [...] }`, one item for every resource of the folder, sorted by `file`. Each item has `id`, `kind`, `name`, `file`, `version` and `usedBy`, see [References](#references) |
| `GET /api/studio/apps/{application}/resources/{id}` | `200` with `{ id, kind, name, file, version, content }` and an `ETag` header |
| `POST /api/studio/apps/{application}/resources` | `201` with the new resource's `{ id, kind, name, file, version, diagnostics }`, a `Location` header and an `ETag` header. The body is the full resource as `application/json` |
| `PATCH /api/studio/apps/{application}/resources/{id}` | `200` with `{ version, diagnostics }` and an `ETag` header. The body is `application/json-patch+json`, as RFC 6902 describes |
| `POST /api/studio/apps/{application}/changes` *(planned for M11)* | `200` with `{ items: [{ id, version }], diagnostics }`. The body is `application/json`, see [Changes](#changes) |
| `DELETE /api/studio/apps/{application}/resources/{id}` | `200` with `{ diagnostics }` |
| `GET /api/studio/apps/{application}/resources/{id}/references` | `200` with `{ uses, usedBy }`, see [References](#references) |
| `GET /api/studio/apps/{application}/texts/{key}/references` | `200` with `{ usedBy }`, see [References](#references) |
| `GET /api/studio/apps/{application}/diagnostics` | `200` with `{ "diagnostics": [...] }`, the folder's diagnostics. A folder with errors still answers `200` |
| `GET /api/studio/apps/{application}/names` *(planned for M11)* | `200` with the names the application defines, see [Names](#names) |
| `PUT /api/studio/apps/{application}/texts/{key}` *(planned for M11)* | `200` with `{ resources: [{ id, locale, version }], diagnostics }`. The body is `application/json`, `{ texts: { <locale>: <text> } }`, see [Text writes](#text-writes) |
| `POST /api/studio/apps/{application}/expressions/check` | `200` with `{ type, diagnostics }`. The body is `application/json`, `{ resourceId, path, expression }`, see [Expression check](#expression-check) |

`file` is the path of the resource's file relative to the application folder,
written with `/`. `id` is in the hyphenated 8-4-4-4-12 hex form, in either
letter case, and the answers write it in lower case. A file is a resource only
when it is a JSON object with a UUID `id` and a string `kind`. Other files are
left out of the list and cannot be read. When two files share an `id`, the
first in path order is the resource. `usedBy` is the number of entries in the
resource's `usedBy` list, see [References](#references).

## Versions

The `version` of a resource is the SHA-256 of its file's
bytes, in lower-case hex.

- A read sends it as a strong `ETag`, in quotes.
- A patch sends it back in `If-Match`, as the quoted `ETag` or as the bare
  hex. `*`, a weak tag and a list of versions count as stale. A delete
  does the same.
- When the file no longer has that version, the answer is `409` and nothing
  is written. The client reads the resource again and retries. One lock
  covers the check and the write of every write, so two patches that carry the
  same version cannot both succeed, and neither can two deletes.
- The lock is a PostgreSQL advisory lock in the platform database, so it
  holds across server replicas that share the folder. It is keyed by the
  folder. A create checks that its `id` is unused anywhere in the folder, so a
  lock per file would let two creates with the same `id` both succeed. Writes
  to two files of one folder wait for each other. Writes to different folders
  do not. The server takes it over `ConnectionStrings:Platform`, for the
  check and the write, and releases it when the write ends, whether it
  succeeded or was refused. It needs no grant.
- A write waits up to 10 seconds for the lock. When the wait times out, or the
  platform database cannot be reached, the answer is `503` and nothing is
  written. The snapshot of `validate` and `publish` is read under the same
  lock, so those routes answer `503` too, and store no request.
- A patch or a delete that finds its resource gone once it holds the lock
  answers `409`, not `404`. Another write removed it after the request was
  checked, so the version the client holds is stale.
- Every replica must mount the folder at the same path, because the lock is
  keyed by the full path. See [D49](../decisions.md#d49-several-server-replicas-behind-a-tls-proxy--agreed).
- A patch or a delete without `If-Match` is `428`.
- A create sends the new version as an `ETag`, so the client can patch next
  without reading the resource again.

## Order of checks

Like the [record API](record-api.md#errors), a request is
checked in this order, and the first failure is the response. Checks 1 to 6
and 8 to 10 are built for every route that is built, and so is check 5a. Check 7 is built for a patch and a delete.
Check 6 is built for a request and for a release:

1. The CSRF header `X-Axis-Csrf: 1` on a `POST`, `PATCH` or `DELETE`, `403`.
2. The session, `401`. A session from another tenant counts as no session.
3. The author role, `403`. It needs `Studio:AuthorRole` to be set and held in
   the session's roles, and `Studio:Folders` to be non-empty. This holds for
   every route. From M11, a set `Studio:ApplicationsPath` also turns the API
   on, so the check needs a non-empty `Studio:Folders` or a set
   `Studio:ApplicationsPath` _(planned for M11)_.
4. The publisher role, `403`, on `publish` and `rollback` only. It needs
   `Studio:PublisherRole` to be set and held in the session's roles. It comes
   right after the author role and before the application is looked up.
5. An unknown application, `404`. It is an application that is not in
   `Studio:Folders`.
6. An unknown resource or text key, `404`. An unknown request id is `404`, and
   so is a release that is not in this tenant's history for the application.
   A request or a release of another application or tenant counts as unknown.
7. A patch or a delete without `If-Match`, `428`.
8. A bad body, `400`. That is a body that is not JSON, a patch that is not
   valid RFC 6902, a patch that does not apply, a patch that changes `id` or
   `kind`, a create without a valid `id`, with an unknown `kind` or with a
   `name` or `locale` that does not form a safe file name, or a create or a
   delete of the `application.json` manifest. The kind `application` counts as
   unknown here. For `publish`, it is also a body that is not a JSON object, a
   key other than `label` and `note`, a value that is not a string, a `label`
   of more than 100 characters or a `note` of more than 2000.
9. A stale version, `409`.
10. A create whose `id` or whose target file already exists, `409`.

The expression check and the two routes planned for M11 use these checks. None of
them has check 6 or a later numbered check, except where a line says so:

- **Names.** Checks 2, 3 and 5. It is a `GET`, so there is no CSRF check and no body.
- **Text write.** Checks 1, 2, 3 and 5, then `415` for a body that is not
  `application/json`, then `400` for a bad body, then `503` when the
  [write lock](#versions) cannot be taken. There is no `404` for an unknown key,
  because a `PUT` creates the key.
- **Expression check.** Checks 1, 2, 3 and 5, then `415`, then `400` for a bad
  body: a body that is not a JSON object, a `resourceId` that is not a UUID, a
  `path` that is not a JSON Pointer or an `expression` that is not a string.
  Then `404` for an unknown `resourceId`, which comes after the body because
  the id is in it. Then `400` for a `path` that holds no expression place. It
  takes no lock, so it has no `503`.

The `changes` route *(planned for M11)* uses checks 1 to 3 and 5. It then checks every change, and the first failure
refuses the whole call:

- Check 6 is `404` when a patch names an unknown `id`.
- Check 7 is `428` when a patch change has no `version`.
- Check 8 is `400` for the rules of [Changes](#changes), such as a list that is
  empty or longer than 20 changes, a change with both or neither of `create`
  and `patch`, a delete, or the same `id` twice. It is also `400` for every
  create and patch rule of check 8.
- Check 9 is `409` when any version is stale, or when a patched resource is
  gone under the lock.
- Check 10 is `409` when any create's `id` or target file exists.

`415` answers a body that is not `application/json`, just before the body is
read. The lock's `503` falls where it does for the single routes: after the
`400` for a body that cannot be read, a patch that is not valid RFC 6902 and a
create without a valid `id`, `kind` or file name, and before the checks that
need the folder under the lock.

A write that cannot take the [write lock](#versions) answers `503`. It comes after the `400` for a body that
cannot be read, a patch that is not valid RFC 6902 and a create that has no valid `id`, `kind` or file name. It comes
before the checks that need the folder under the lock: a patch that does not apply, a patch that changes `id` or
`kind`, the stale version, and the existing `id` or file of a create. A `validate`, `publish` or `preview` answers `503` the
same way, before it stores a request.

A check 5a sits between checks 5 and 6, on `validate`, `publish` and `preview`. A `rollback` takes no body, and it has no check 5a, because it names a release of the history. A folder whose manifest has no UUID `id` answers `409`,
because the request needs the application id for the publisher's queue. It comes after the unknown application `404` and, for a
`publish`, before the body is read.

A check 5b sits between checks 5 and 5a, on `preview` only.
When the session's tenant has no served preview tenant, the answer is `409`. It comes
after the unknown application `404`, so a caller cannot learn from it which
applications exist. A preview tenant that is suspended is not served, so it counts as none.

The session comes before the role, and the roles before the application. So a
caller without the roles cannot find out which applications, resources,
requests or releases exist.

## Responses

Every error is problem details
(`application/problem+json`). The titles are fixed and never contain text
from the request. The rows for List applications, List resources, Read, Create, Patch, Delete, Diagnostics,
Resource references, Text key references, Release history, Validate, Publish, Preview, Rollback and Request status are built.

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
| Changes *(planned for M11)* | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application, or a patch that names an unknown resource | Any stale version, a patched resource that is gone, or a create whose `id` or target file already exists |
| Diagnostics | No session | No author role, or no `Studio:Folders` | Unknown application | Not used |
| Names *(planned for M11)* | No session | No author role, or no `Studio:Folders` | Unknown application | Not used |
| Text write *(planned for M11)* | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application | Not used |
| Expression check | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application, or an unknown `resourceId` | Not used |
| Validate | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application | The manifest has no UUID `id` |
| Publish | No session | No author role, no publisher role, no `Studio:Folders`, or no CSRF header | Unknown application | The manifest has no UUID `id` |
| Request status | No session | No author role, or no `Studio:Folders` | Unknown application or request | Not used |
| Release history | No session | No author role, or no `Studio:Folders` | Unknown application | Not used |
| Preview | No session | No author role, no `Studio:Folders`, or no CSRF header | Unknown application | This tenant has no served preview tenant, or the manifest has no UUID `id` |
| Rollback | No session | No author role, no publisher role, no `Studio:Folders`, or no CSRF header | Unknown application, or a release not in the tenant's history | Not used |
| Create application _(planned for M11)_ | No session | No author role, no CSRF header, or no `Studio:ApplicationsPath` | Unknown template | The name or the folder already exists |
| List templates _(planned for M11)_ | No session | No author role, or Studio is off | Not used | Not used |

The `409` of Validate and Publish is problem details titled "The application
manifest has no valid id." The `403` for a missing publisher role is titled "You do not hold the publisher role."
The `409` of Preview for a tenant without a served preview tenant is problem details titled "This tenant has no preview
tenant." It names no tenant. A manifest without a UUID `id` answers the title of Validate and Publish. `400` is a bad body, and `428`
a missing `If-Match`, as [Order of checks](#order-of-checks) says. `400` also answers a bad `publish`
body, titled "The body is not a valid publish request." A successful `validate`, `publish` or `rollback`
answers `202`, see [Publishing](#publishing). `415` answers a body of the wrong
content type, just before the body is read. A create needs `application/json`
and a patch needs `application/json-patch+json`. An expression check needs `application/json`. It answers
`400` for a bad body or a path with no expression place. *(Planned for M11.)* A text write also needs
`application/json`. It answers `400` for a bad body, and also `503` when it cannot take the write lock.

## References

The two references routes answer where a resource or a text key is used. They read the [reference index](configuration.md#reference-index)
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
  used. `{key}` is matched ordinally, as keys are compared. A key that contains
  `/` cannot be addressed, because the route does not decode `%2F`.
- **Entry.** Each entry is `{ relation, file, path, node }`.
  - `relation` is the name of the relation, such as `task form`.
  - `file` and `path` are always in the resource that holds the reference. So
    for `usedBy` they point into the other resource, and for `uses` into this
    one. An entry opens its resource at that path.
  - `node` is the other end: `{ type: "resource", id, kind, name, file }`,
    `{ type: "text", key }`, `{ type: "package", id }` or
    `{ type: "module", id }`. In `usedBy`, it names the resource that holds
    the reference.
- **Order.** Entries are sorted by the using file, then by path, both with
  ordinal string comparison. So `/steps/10` sorts before `/steps/2`.
- **Compiled in memory.** The folder is compiled in memory, as for
  [diagnostics](#diagnostics), and no database is touched. A folder with errors
  still answers with the edges that resolved.
- **The list's `usedBy`.** The resource list gives each resource's `usedBy`
  count, so Studio can warn before a delete. The warning never blocks the
  delete. The next compile reports what broke.

## Names

*(Planned for M11.)* `GET .../names` answers every name the application
defines, so Studio's [reference pickers](frontend.md#studio) load them once.
[D52](../decisions.md#d52-reference-pickers-label-texts-and-the-expression-editor--agreed)
agrees it. The answer is:

```
{ locales: ["en", "vi"],
  entities: [{ id, name, module, fields: [{ name, type, target, values }] }],
  dataSources: [{ id, name, module, parameters: [{ name, type }], columns: [{ name, type }] }],
  processes: [{ id, name, steps: [{ name, type, outcomes: [...] }] }],
  rules: [{ id, name, module, parameters: [{ name, type }], resultType }],
  connectors: [{ id, name, operations: [{ name }] }],
  events: [{ id, name, fields: [{ name, type }] }],
  roles, sequences, forms, pages: [{ id, name }],
  texts: [{ key, texts: { <locale>: <text> }, usedBy }] }
```

- **`name`.** It is written as a reference writes it. A module entity is
  `<module id>/<name>`, and a module rule is `<module id>:<name>`. `module` is
  the module id, or null for an entity or rule of the application.
- **`locales`.** The locales of the folder's text resources.
- **`texts`.** Each key comes with its text in every locale, and `usedBy`, the
  number of `label text` edges in the [reference index](configuration.md#reference-index).
- **Compiled in memory.** The folder is compiled in memory, as for
  [diagnostics](#diagnostics), and no database is touched. A folder with errors
  still answers with what resolved. A file that was left out for its own errors is
  absent.
- **Size.** The answer holds every name and every text. At the typical
  application size it is a few hundred kilobytes.

## Text writes

*(Planned for M11.)* `PUT .../texts/{key}` writes one text key in every locale
at once, so a key is never in one locale only. The body is
`{ texts: { <locale>: <text> } }`.

- **Every locale.** The body must hold exactly the locales of the folder's
  `text` resources, matched ignoring letter case. With duplicate locales
  (`AXC0026`), the first file in path order counts. A missing locale, an extra
  locale, a value that is not a string, or another top-level key is `400`. A
  value may be any string, as the text schema allows.
- **The key.** `{key}` is matched ordinally and cannot contain `/`, as in the
  [text key references](#references) route. A new key is added at the end of
  `texts`. An existing key keeps its position.
- **The lock.** The write takes the folder [write lock](#versions). Under it,
  the server reads each text file and writes every temporary file. Then it renames
  each one over its target. It changes only that key, and leaves the other
  keys as they are.
- **No `If-Match`.** A text write takes no version, and the last write to a
  key wins. One version per file does not fit a write that spans files. Only
  that key can be lost.
- **Not atomic across files.** Each rename is atomic, but all of them are not. A
  crash between two renames can leave the key in some locales only. The next
  compile reports it as `AXC0027`.
- **Stale versions.** A text resource that Studio has open has a new version
  after the write, so its next save is `409`. The answer lists each text
  resource's new `version`, as `{ resources: [{ id, locale, version }] }`.
- **Answer.** `200` with the new versions and the folder's
  [diagnostics](#diagnostics). `503` answers when the lock cannot be taken, and
  nothing is written.

## Expression check

`POST .../expressions/check` checks one expression without saving it, so
Studio's expression editor can show errors as the author types. The body is
`{ resourceId, path, expression }`.

- **The place.** The server puts `expression` into a copy of the saved resource
  at `path` and compiles the folder in memory. The compile checks the
  expression where it stands, so the names in scope, the expected type, the
  [SQL subset](expressions.md#sql-subset) and the policy-filter rules are the
  compile's own, not a copy of them. The body carries no draft content, so a
  place that the author has just added and not saved is not found.
- **Where it works.** In every place the compile checks an expression: computed
  fields, validations, rules, data source filters, process conditions and
  values, policy filters, migration values, endpoint values and trigger
  conditions.
- **No expression place.** A `path` that does not point at a string in the saved
  resource is `400`. So is a place where the compile checks no expression
  because another problem in the resource skips the check, such as a process
  step whose subject entity is unknown. A save shows no problem at such a place
  either.
- **The rules.** The scope, types, limits and codes are those of
  [expression diagnostics](expressions.md#diagnostics). An expression of more than
  2,000 characters answers `AXC0036` as a diagnostic, not `400`.
- **The answer.** `{ type, diagnostics }`. `type` is the result type in the
  notation of [Types](expressions.md#types): `text`, `integer`, `decimal`,
  `boolean`, `date`, `date-time`, `enum`, `reference` or `null`. It is the
  expression's own type, so a text literal is `text` even where an enum is
  expected. `type` is null when there is a diagnostic, and when the compile
  cannot find the type yet. A migration value whose type check waits for
  activation is the second case.
- **Diagnostics.** There is at most one, because the compile stops at the first
  problem. It is `{ code, message, start, end }`. `start` and `end` are offsets
  into the expression text after JSON decoding, in UTF-16 code units, and `end`
  is exclusive.
- **The range.** The compile reports one character position, so the range is
  the token at that position: a name, a literal or an operator. A qualified
  call such as `departments:IsManager` is one range. A character that starts no
  token covers that one character, and text that is never closed runs to the
  end. `AXC0036` and `AXC0048` cover the whole expression. A limit that the
  compile reports at the place but outside the expression check, such as a rule
  cycle (`AXC0055`) or nesting that is too deep (`AXC0065`), is answered over the
  whole expression. A range is narrower than the faulty subexpression: for
  `a + 'x'` only the `+` is covered.
- **Nothing is written.** It compiles in memory, takes no lock and touches no
  database.

## Writes

Writes are built for patches, creates and deletes.

- **Atomic.** A write goes to a hidden temporary file in the same folder and
  is then renamed over the target. A reader never sees half a file. A delete removes
  the file and leaves its kind folder, even when it is empty.
- **Format.** The file is written as JSON with 2-space indents, LF line ends
  and a final newline. The order of keys is kept, so a patch of one value
  changes one line in git.
- **New files.** A new file goes in the kind's plural folder, as in
  `samples/apps/purchase-requests`: `entity` in `entities`, `dataSource` in
  `data-sources`, `policy` in `policies`, and so on. The file name is the
  kebab-case `name`, such as `entities/purchase-request.json`. A `text`
  resource is named after its locale in lower case, such as `texts/en.json`
  and `texts/pt-br.json`. A patch that changes `name` keeps the file name. A
  missing kind folder is created.
  - **Kind folders.** `entity` in `entities`, `site` in `sites`, `page` in
    `pages`, `text` in `texts`, `seed` in `seeds`, `dataSource` in
    `data-sources`, `rule` in `rules`, `sequence` in `sequences`, `process` in
    `processes`, `form` in `forms`, `role` in `roles`, `policy` in `policies`,
    `connector` in `connectors`, `event` in `events`, `endpoint` in
    `endpoints`, `trigger` in `triggers`, `migration` in `migrations` and
    `scenario` in `scenarios`. The `kind` is matched with its letter case.
  - **Name pattern.** A create's `name` must be `^[A-Za-z][A-Za-z0-9]*$`
    with at most 60 characters, which is the pattern of every kind's schema
    except `role`. A role's `name` must be `^[a-z][a-z0-9-]*$` with at most 60
    characters, as its schema says. Letters, digits and hyphens cannot form a
    path, so `../x` is `400`. A text's
    `locale` must match the text schema's pattern, `^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$`,
    with at most 60 characters.
  - **Kebab-case.** A hyphen goes before a capital that follows a lower-case
    letter or a digit, or before the last capital of an acronym that a
    lower-case letter follows. Then everything is lower-cased. So
    `PurchaseRequestList` is `purchase-request-list`, and `HTTPCall` is
    `http-call`. A role's file name is its name as it is, such as
    `roles/department-manager.json`.
  - **Checked fields.** A create checks only `id`, `kind`, `name` and, for a
    text, `locale`. Any other schema problem is saved and shows up as a
    diagnostic.
  - **No overwrite.** A create whose `id` is in use, or whose target file
    exists, is `409`. A create never replaces a file, even when another
    process writes the folder.
- **The manifest.** `application.json` at the folder root can be patched. It
  cannot be created or deleted, because
  `AXC0009` (see the diagnostic codes in [configuration.md](configuration.md)) requires it there.
- **Paths.** A path is resolved and must stay under the application folder,
  so `..` and links cannot leave it. Only `*.json` files are read or written.
- **Patches.** A patch may not change `id` or `kind`, and a change of letter case
  alone counts as a change. Every other change is
  allowed, including one that breaks the resource. The new content is written
  and the diagnostics report the breakage.
- **Never an activation.** A write never compiles against a database, never
  stores a release and never activates one. Only a [publish request](#publishing)
  leads to an activation, and the
  [publisher](administration.md#publisher) runs it, never the server.

## Creating an application

_(planned for M11)_ `POST /api/studio/apps` creates a new application folder in
`Studio:ApplicationsPath`, blank or from an installed
[application template](configuration.md#application-templates). It follows
[D51](../decisions.md#d51-creating-applications-and-resources-from-studio--agreed).
The body is `{ "name": "...", "template": "<template id>" }`, and `template` is
optional. Without it the application is blank.

This route has its own order of checks. The first failure is the response:

1. The CSRF header `X-Axis-Csrf: 1`, `403`.
2. The session, `401`.
3. The author role, `403`.
4. `Studio:ApplicationsPath` not set, `403`, titled "Studio cannot create
   applications here."
5. A body that is not `application/json`, `415`.
6. A bad body, `400`. That is:
   - a body that is not a JSON object, or has a key other than `name` and
     `template`;
   - a `name` that does not match `^[A-Za-z][A-Za-z0-9]*$` or is longer than 60
     characters;
   - a `name` whose [kebab-case](#writes) form is longer than 60 characters, or
     is a reserved site path: `api`, `health`, `assets` or `studio`;
   - a `template` that is not a string.
7. An unknown template, `404`. It is an `id` that no installed template has.
8. A write lock that cannot be taken, `503`. See [Versions](#versions).
9. A name already used, ignoring letter case, by an application of
   `Studio:Folders` or of `Studio:ApplicationsPath`, or a target folder that
   already exists, `409`.

- **Atomic.** The whole folder is written to a hidden temporary folder,
  `.<kebab>-<random>`, inside `Studio:ApplicationsPath`, and then renamed to
  the kebab-case name. A reader never sees half an application. The write lock
  is the folder write lock of [Versions](#versions), keyed by
  `Studio:ApplicationsPath`. A temporary folder starts with `.`, so the
  application list skips it.
- **Never an activation.** The route never compiles against a database, never
  stores a release and never activates one. The author publishes the new
  application, see [Publishing](#publishing). The diagnostics in the answer are
  the folder's, compiled in memory as for [Diagnostics](#diagnostics).
- **Blank.** Without a `template`, the folder holds the smallest application
  that compiles with no error. See
  [blank application](configuration.md#blank-application).
- **From a template.** With a `template`, the server uses the newest installed
  version, copies its files and changes the ids, the name and the site path. See
  [application templates](configuration.md#application-templates). The answer
  has the template's diagnostics, so a template that does not compile gives a
  copy that does not compile.
- **Listing templates.** `GET /api/studio/templates` checks the session, the
  author role and Studio as for every read route, in that order. It needs no
  CSRF header and does not need `Studio:ApplicationsPath`. It answers each
  template's title and description in both `en` and `vi`, and the SPA picks the
  locale.

## Changes

*(Planned for M11.)* The entity designer saves an entity and its migration in
one call, so the two files never split. See
[D54](../decisions.md#d54-the-entity-designer-and-the-policy-matrix--agreed).

```json
{ "changes": [
  { "create": { "id": "…", "kind": "migration", … } },
  { "patch": [ { "op": "replace", … } ], "id": "…", "version": "<hex>" }
] }
```

- A call holds 1 to 20 changes.
- Each change has exactly one of `create` or `patch`. A `create` is a full
  resource, as for [Writes](#writes). A `patch` is an RFC 6902 list and goes
  with the `id` it patches.
- There are no deletes.
- A patch change carries its `version`, the bare hex of [Versions](#versions).
  It has no `If-Match` header.
- The same `id` twice is `400`. That includes a create of an `id` that
  another change patches.
- One lock covers the folder. Every change is checked against the folder
  first. Then the files are written in list order, each one atomic as in
  [Writes](#writes).
- The `items` of the answer follow the request order. Each holds the `id` and
  the new `version` of its file. `diagnostics` are the folder's diagnostics
  after the last write.
- A refused call writes nothing.
- A server that dies during the writes can leave the first files written. The
  next diagnostics show the mismatch, such as `AXC0015` or `AXC0171`.
- It never activates a release, as for the other writes.

## Diagnostics

A patch, a create and a delete answer with the folder's diagnostics, and
`GET /api/studio/apps/{application}/diagnostics` returns them on demand. All
are built. A diagnostic never blocks a save. A delete that breaks references is
saved, and the diagnostics list the broken references.

- The folder is compiled in memory against the extension packages installed
  under `Extensions:Path` and the modules installed under `Modules:Path`, see
  [extension packages](configuration.md#extension-packages) and
  [modules](configuration.md#modules). No database is touched.
- The codes, severities and fields are those of
  [startup activation](configuration.md#startup-activation). Each diagnostic
  is `{ code, severity, message, file, path, resourceId }`. `severity` is
  `error` or `warning`. `resourceId` is the lower-case id of the resource,
  or `null` when the problem concerns a whole file or the whole folder.
- The compile diagnostics are returned, and the problems of a broken extension
  package are not, because they belong to the server's install and not to the
  folder. The checks that activation runs against a tenant are not part of it
  either: bindings, name and site path conflicts, and storage changes. They
  need a tenant database or the tenant's settings.
- A folder with an error is not activated. Under `scripts/dev.sh`, the
  restart after a saved file then stops the server, as a broken hand edit
  does today.

## Publishing

An author validates, publishes and rolls back an
application from Studio, and previews it on the preview tenant. `validate`, `publish`, `preview`, `rollback` and the request status are built. `test` is built too, except its 10-minute limit, which is _(planned for M11)_. The server never activates in its own process and
holds no tenant schema connection. It stores a **publish request** in the
tenant database, and a separate [publisher](administration.md#publisher) runs
it with the tenant's schema role. See
[D47](../decisions.md#d47-publishing-from-studio--agreed), and
[publish requests and release history](storage.md#publish-requests-and-release-history)
for the tables.

A request runs for the tenant of the author's session. Studio on a tenant's
host publishes to that tenant only.

| Method and path | Response |
| --- | --- |
| `POST /api/studio/apps/{application}/validate` | `202` with the request and a `Location` header for `.../requests/{id}`. It needs the author role |
| `POST /api/studio/apps/{application}/publish` | `202` with the request and a `Location` header. The body is `application/json` with an optional `label` and an optional `note`. It needs the author role and the publisher role |
| `POST /api/studio/apps/{application}/preview` | `202` with the request and a `Location` header. It takes no body. It needs the author role only. `409` when the tenant has no preview tenant, see [Preview](#preview) |
| `POST /api/studio/apps/{application}/test` | `202` with the request and a `Location` header. The body is optional `application/json`, `{ "scenarios": ["<name>", ...] }`. None or an empty list means every scenario. It needs the author role only, see [Test](#test) |
| `GET /api/studio/apps/{application}/requests/{id}` | `200` with the request, its status, its diagnostics and, for `test`, its scenario outcomes |
| `GET /api/studio/apps/{application}/releases` | `200` with the activation history, newest first, and which release is active |
| `POST /api/studio/apps/{application}/releases/{id}/rollback` | `202` with the request and a `Location` header. It takes no body. It needs the author role and the publisher role |

`validate`, `preview` and `rollback` take no body, and a body that is sent is
ignored. A `publish` without a body stores no label and no note. A body that is sent
must be `application/json` (`415` otherwise). A `test` without a body runs every
scenario, and a body that is sent must be `application/json` (`415` otherwise).
`{id}` of a request is its UUID. `{id}` of a release is the release id from
the history. The answers for `401`, `403`, `404` and the CSRF header are those
of the rest of the API, see [Order of checks](#order-of-checks) and
[Responses](#responses).

### The request

A request is shown as `{ id, application, kind, target, status, label, note, requestedBy, requestedAt, startedAt, finishedAt, releaseId, diagnostics, scenarios }`.

| Field | Meaning |
| --- | --- |
| `id` | The request id, a UUID |
| `application` | The application `name` |
| `kind` | `validate`, `publish`, `rollback` or `test` |
| `target` | `tenant` or `preview`. `preview` marks a request that runs on the session tenant's preview tenant, see [Preview](#preview). Only the preview route writes `preview`. The request status route reports the stored value |
| `status` | `queued`, `running`, `succeeded` or `failed` |
| `label` | The optional label of a `publish`, up to 100 characters, or null |
| `note` | The optional note of a `publish`, up to 2000 characters, or null |
| `requestedBy` | The requesting user's id |
| `requestedAt` | When the request was stored |
| `startedAt` | When a publisher first started it, or null while `queued` |
| `finishedAt` | When it reached `succeeded` or `failed`, or null before |
| `releaseId` | The release a `publish` activated or a `rollback` named, or null. A `validate` and a `test` have none |
| `diagnostics` | The diagnostics, in the form of [Diagnostics](#diagnostics). Empty until the request ends. A `succeeded` request can hold warnings |
| `scenarios` | Null for every kind but `test`. For `test`, an array with one item for each scenario that ran, in file order, in the form `{ name, resourceId, file, outcome, durationMs, step, path, message }`. `outcome` is `passed` or `failed`. `durationMs` is the time the scenario took. `step` is the 0-based index of the failing step, or null. `path` is `/steps/<index>`, in the same form as the paths of [Diagnostics](#diagnostics), or null. `message` is the failure message, or null. `resourceId` and `file` name the scenario resource, so Studio can open it at `path`. Empty until the request ends |

- **A snapshot.** `validate` and `publish` store the folder as it is when the
  request is made: every `*.json` file that Studio can reach, with its `version`, as for
  [Versions](#versions). A file that is not a valid resource is kept, so the request fails with the
  diagnostic that the diagnostics route shows. The snapshot is read under the write lock, so it never
  holds a write half done. A later edit in Studio does not change a queued
  request, so the release is exactly what the author sent.
- **A folder with errors.** A `publish` of a folder with compile errors is
  still accepted with `202`. The request then ends `failed` with those
  diagnostics. There is one path for diagnostics, and the snapshot shows what
  the author sent.
- **`validate`.** The publisher runs every activation check against the
  tenant inside a transaction that it rolls back. It returns the codes of
  [startup activation](configuration.md#startup-activation), including the
  checks that the in-memory diagnostics skip: bindings, name and site path
  conflicts, and storage changes. It stores no release and changes nothing.
  It ends `succeeded` when there is no error.
- **`publish`.** The publisher compiles the snapshot against the tenant,
  stores the release and activates it, then seeds, with the same steps as
  [`axis-admin activate`](administration.md#activate). A `seed` failure
  happens after activation has committed, so the request ends `failed` with
  `releaseId` set, and the release stays active and stays in the history.
- **`test`.** The server stores the request with the snapshot, taken as for
  `validate`. The publisher runs the scenarios as the [publisher](administration.md#publisher)
  describes, and stores their outcomes in `scenarios`. The request ends
  `failed` when any scenario fails. Scenario failures go only in `scenarios`.
  Compile diagnostics, the missing `Scenarios:ServerConnectionString` setting
  (`AXC0217`), an unknown scenario name (`AXC0218`) and the 10-minute limit
  *(planned for M11)* go in `diagnostics`. See [Test](#test).
- **Order.** For one tenant and application the publisher runs requests one at
  a time, oldest first. When two publishes are queued, the newer one ends up
  live. A `test` request has its own queue and does not wait behind them, see
  [Test](#test).

### Preview

`POST .../preview` tries an application on the session
tenant's [preview tenant](../architecture.md#preview-tenants) before it goes
live. [D48](../decisions.md#d48-preview-tenants--agreed) agrees the route. The
request has the kind `publish` and the target `preview`.

- **Who may.** The author role is enough. It needs no publisher role, because
  nothing goes live for real users.
- **Where it is stored and where it runs.** The request is stored in the
  session tenant's database, so Studio polls `GET .../requests/{id}` on the main
  host as for any request. The publisher runs it against the preview tenant's
  database, see [preview requests](administration.md#publisher).
- **History.** The activation goes in the preview tenant's own release history,
  with the request id. `GET .../releases` on the main host still shows only the
  main tenant's history. The preview's release number is its own.
- **Audit.** A preview writes no audit record, like `validate`. The preview's
  history records who published it.
- **A preview that is not served.** A request stays `queued` while the preview
  tenant is not served. If the main tenant loses its preview tenant after the
  request was stored, the request ends `failed` with `AXC0216`.
- **Not required.** Publishing to the main tenant does not need a preview first.
- **Data.** A preview starts with the application's seed data. Records created
  in it stay until the operator recreates the preview tenant. Studio cannot reset
  it.
- **Bindings.** A preview has its own bindings, empty by default. A missing
  binding is a warning at activation, and the call fails at run time. See
  [preview tenants](configuration.md#preview-tenants).

### Test

`POST .../test` stores a request to run the application's
[test scenarios](configuration.md#test-scenarios) from Studio. The route and the
stored request are built, and so is the publisher's run. Its 10-minute limit and
the immediate stop on `SIGTERM` are *(planned for M11)*.
[D55](../decisions.md#d55-running-test-scenarios-from-studio--agreed) agrees
the route. The request has the kind `test` and the target `tenant`.

- **Who may.** The author role is enough. It needs no publisher role, because
  a test changes no tenant. It costs database time, so the limits below apply.
- **The body.** The optional `scenarios` list names the scenarios to run, by
  their `name`. With no body, no list or an empty list, every scenario runs.
  An unknown name is accepted with `202`, and the request then ends `failed`
  with `AXC0218`, as for a folder with errors.
- **Where it runs.** The publisher runs it with `Scenarios:ServerConnectionString`,
  in a new database for each scenario, see
  [publisher](administration.md#publisher). It touches the tenant database
  only to store the request's status and outcomes. Without the setting, the
  request ends `failed` with `AXC0217`, which names it. A scenario name that the
  application does not have ends it `failed` with `AXC0218`. A snapshot that does
  not compile ends it `failed` with the compile diagnostics. In all three cases no
  scenario runs.
- **The outcome.** `scenarios` holds one item for each scenario that ran. A
  scenario that fails has the failing `step`, its `path` and its `message`.
  Selecting it in Studio opens the scenario at that step, as a diagnostic opens
  a resource.
- **Limits.** The scenarios of a request run one after another. A request stops
  after 10 minutes *(planned for M11)*: the scenario that runs fails, its database is dropped and
  the request ends `failed` with a diagnostic. The work item limit of
  [D32](../decisions.md#d32-test-scenarios--agreed) applies to each scenario.
- **Order.** At most one `test` request per application runs at a time,
  oldest first. The queue is separate from the one of `validate`, `publish` and
  `rollback`, so a `test` does not wait behind a `publish`.
- **Audit.** A `test` writes no audit record.
- **A request that runs again.** A request that is claimed again after a lost
  lease runs every scenario again.

### Lifecycle

```mermaid
stateDiagram-v2
    [*] --> queued: request stored
    queued --> running: publisher claims it
    running --> running: lease expired, claimed again
    running --> succeeded: all steps passed
    running --> failed: a step failed, or third expired claim (AXC0215)
    succeeded --> [*]
    failed --> [*]
```

- Only the publisher changes `status`. The server only inserts requests and
  reads them.
- The publisher claims a request with a lease, and renews the lease while the
  request runs, so a long recompute is not claimed twice. A request whose
  publisher died is claimed again after its lease ran out. It stays `running`.
- A request that has been claimed three times, and whose lease runs out again,
  ends `failed` with `AXC0215` in place of a fourth claim. A request that
  crashes the publisher cannot block the queue for good.
- `succeeded` and `failed` are final. Studio polls
  `GET .../requests/{id}` until the status is one of them.
- A request that runs again does no harm. Activating the same content hash
  twice changes nothing, see [Release history](#release-history).

### Release history

This route is built. The publisher writes the rows of a publish and a rollback.

`GET .../releases` answers
`{ active: { releaseId, number, contentHash }, folderContentHash, items: [{ releaseId, number, contentHash, activatedAt, actor, actorName, label, note, requestId }] }`,
newest first.

- `active` is the application's active release in this tenant, or null when
  there is none.
- `folderContentHash` is the content hash that the folder would compile to now,
  so Studio can show that the live release and the folder differ. It is null
  when the folder has errors.
- Each item is one activation: a startup, an `axis-admin activate`, a publish or
  a rollback. `actor` is a user id, `startup` or `operator`. A row that a
  migration added for a release that was live before the history existed has
  the actor `unknown`. `actorName` is the
  user's display name, or null. `requestId` is null for a startup and for an
  `axis-admin activate`.
- A release gets its `number` the first time it is activated in the tenant:
  1, 2, 3… per application. Activating it again keeps the number, so the same
  content can have different numbers in two tenants.
- Activating the release that is already active adds no row and keeps the
  number. Otherwise every restart under `scripts/dev.sh` would fill the history
  with copies.
- `GET /api/me` gains a boolean `studioPublisher`, true when the user holds
  both the author role and the publisher role and `Studio:Folders` is not
  empty. It is built. Studio uses it to hide Publish and the rollback button.
- `GET /api/me` gains a boolean `studioTest`, true when
  `studio` is true and `Studio:Tests` is true. Studio uses it to hide **Run
  tests** and the **Run** action of a scenario.
- `GET /api/me` also gains `studioPreviewHost`, the first host of the served tenant
  whose `PreviewOf` is the session's tenant. It is `null` when the user may not
  use Studio, or when the tenant has no served preview tenant. It is built. Studio
  shows Preview only when it is set. It names a host and no port, so Studio takes
  the port and the protocol from its own page.

### Rollback

A `rollback` request names a release from the history. The publisher activates
the stored release as it is, **without compiling it again**. It rebuilds the
release's model from its stored files, never from the folder, and stores no new
release. It records an activation with the rollback's actor, which is the
requesting user, and the request id. The row has no label and no note. The
route takes no body. Instances that are already pinned to a newer release keep
running on it. New starts use the rolled-back release. A rollback runs the
`seed` step of the release as well, so its schedule triggers and seed records
are the ones of the release that is live.

It is refused, and the request ends `failed`, in these cases:

| Case | Code |
| --- | --- |
| A migration that was applied after that release is missing from it, see [applied migrations](storage.md#applied-migrations) | `AXC0174` |
| An extension package version that the release pins is no longer installed | `AXC0146` |

These are the codes that activation already gives for the same causes. A
rollback also gets every other refusal of activation, because those checks
protect stored data. For example, rolling back past a release that added a
field is refused with `AXC0015`, because the column has no field in the older
release. So a rollback is possible only across releases that did not change the
storage. Records that the newer release created stay in place, and seeds with
`sync` go back to the values of the older release. A refused
rollback changes nothing: the active release and the history stay as they were.
Rolling back to the release that is already active succeeds and adds no row.

A rollback does not change the folder. Studio shows when the active release
does not match the folder, with `folderContentHash`.

### Audit

Each `publish` and `rollback` writes an
[audit record](storage.md#audit-records-and-sequence-counters) with the actor,
the application, the release and the request, when it succeeds and when it
fails. A `validate` writes none.
