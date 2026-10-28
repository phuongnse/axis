# D38. Studio edits application folders through an authoring API

Status: **Agreed**

Studio edits an application's resource files through an authoring API
on the server. The routes that list applications, list, read, patch, create and
delete resources and give diagnostics are built, and so are the Studio page's
list, search, editor, diagnostics panel, Uses and Used by lists, the delete
button with its warning, and the reconnect message. A diagnostic also opens its
resource at its path. See the
[authoring API](../reference/authoring.md) for the routes and answers, the
[Studio settings](../reference/host-settings.md#studio-settings) and the
[Studio section of the frontend](../reference/studio.md).
The owner confirmed these points on issue #569 on 2026-10-18.

- **Where edits live:**
  - Studio edits the application folders on the server's disk. They are the
    same JSON files that are kept in git. There is no draft store in the
    database.
  - Studio never activates a release in the server process.
    [D47](d47-publishing-from-studio.md) replaces the earlier "Studio
    never activates a release" point: a separate publisher runs a publish
    request from Studio. Under `scripts/dev.sh`, a saved
    file restarts the server, which activates the folder, as it does today.
    Elsewhere a folder is committed and deployed as today.
- **Who may author:**
  - The server setting `Studio:Folders` lists the application folders Studio
    may edit, like `ActivateOnStartup`. It is empty by default, so Production
    has no Studio. The Development settings list every sample under
    `samples/apps/`.
    New applications that Studio creates live in `Studio:ApplicationsPath`, see
    [D51](d51-creating-applications-and-resources-from-studio.md).
  - The server setting `Studio:AuthorRole` names a roles claim value. Only a
    signed-in user whose roles claim holds it may use Studio. With no value,
    nobody may. Application policies play no part. It is the first
    permission outside application policies.
  - The development realm gets an `author` role and a user `author` who holds it.
  - `GET /api/me` gains a `studio` boolean. It is true when `Studio:Folders`
    is not empty and the user holds the author role. It extends the current
    user shape of [D23](d23-sign-in-through-oidc-and-the-bff-session.md),
    and the header uses it to show the Studio link.
- **Authoring API** under `/api/studio/apps/{application}`:
  - It lists the folder's resources, each with its `id`, `kind`, `name`, file
    and `version`. It reads one resource, with its content and `version`.
  - It creates a resource, patches one with JSON Patch (RFC 6902), and deletes
    one.
  - `version` is the SHA-256 of the file's bytes, sent as `ETag`. A write
    sends it in `If-Match`. A stale version is `409`.
  - No session is `401`, no author role is `403`, and an unknown application
    or resource is `404`. Unsafe methods need the CSRF header, as elsewhere.
  - A patch may not change `id` or `kind` (`400`). A schema violation or a
    compile error does not block a save. It is a diagnostic.
  - Files are written atomically, with 2-space JSON, LF and a final newline,
    keeping key order. A new file goes in the kind's folder, named after the
    resource. No path leaves the folder, and only `*.json` files are touched.
  - Every write answers with the folder's diagnostics. `GET .../diagnostics`
    compiles the folder in memory against the installed packages, with the
    same codes as activation. It touches no database.
  - A write takes a lock in the platform database, so the
    version check holds across server replicas. See
    [D49](d49-several-server-replicas-behind-a-tls-proxy.md).
- **Studio in the SPA:**
  - A platform route, `/studio`, outside every site. The header shows it to
    authors only.
  - A resource list grouped by kind, with search by name and id.
  - An editor: a form built from the kind's JSON Schema, and a JSON view of
    the same resource.
  - A diagnostics panel. Each diagnostic opens its file at its path.
  - [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) brings visual page, form and
    process designers into scope, and plans publishing from Studio in M10.
    [D47](d47-publishing-from-studio.md) replaces the "Studio never
    activates a release" point of this decision.
  - Its texts are platform texts under `shell.studio.*`, in `en` and `vi`.

_Why:_ [D5](d5-configuration-is-authored-as-files-first.md) says
authoring APIs use the same contracts and that a Studio writes the same
resources. [Authoring and packaging](../domain/knowledge.md#authoring-and-packaging)
forbids replace-all edits and asks for resource versions, so writes are
patches that carry a version. Editing the folders on disk keeps git the source
of truth. It also keeps the schema role out of requests, as
[D31](d31-separate-database-roles.md) asks, because only startup holds
it and Studio never activates in the server process. The cost was that Studio was a development tool.
[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) and
[D47](d47-publishing-from-studio.md) later added publishing from Studio, so Studio also serves a shared server. Authoring on a shared server, with drafts and publishing from Studio,
would need a database store and run-time activation. That could come later. An author can change every policy of an application, so no
application policy can guard authoring. That is why the author role is an
installation-level setting.
