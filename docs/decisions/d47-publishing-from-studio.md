# D47. Publishing from Studio

Status: **Agreed**

An author validates, publishes and rolls back an application from Studio. The
server never gets schema rights. It stores a **publish request**, and a
separate **publisher** runs it. Every activation also gets a release number in
a per-tenant history. It was built in M10.
See [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
for M10, the [authoring API](../reference/authoring.md#publishing) for the routes,
the [publisher](../reference/administration.md#publisher) and the
[publish request and release history tables](../reference/storage.md#publish-requests-and-release-history).
The owner confirmed these points on issue #704 on 2026-10-19.

- **Publish requests:**
  - Studio never activates in the server process. It writes a publish request
    to the tenant database, and a separate publisher runs it.
  - A request has an `id`, an `application` and a `kind`: `validate`, `publish`
    or `rollback`. Its input is a snapshot of the folder for `validate` and
    `publish`, or a release id for `rollback`.
  - It has an optional `label` and `note`, and the requesting user. Its
    `status` is `queued`, `running`, `succeeded` or `failed`. It keeps its
    diagnostics and its times.
  - The snapshot holds every resource file of the folder at the moment of the
    request, with its content hash. Later edits in Studio do not change a
    queued request.
  - The runtime role may insert and read requests. Only the publisher changes
    their status.
- **Target tenant:** a request runs for the tenant of the author's session.
  Studio on a tenant's host publishes to that tenant only. A preview request
  runs for the session tenant's preview tenant instead, see
  [D48](d48-preview-tenants.md).
- **Who may publish:**
  - A new setting, `Studio:PublisherRole`, names a roles claim value.
  - `validate` needs the author role. `publish` and `rollback` need both the
    author role and the publisher role.
  - With no value, nobody may publish or roll back.
  - The development realm gives `author` a `publisher` role.
- **The publisher:**
  - It is a long-running command of the admin tool, `axis-admin publisher`, in
    the `axis-admin` image.
  - It reads tenants from the same source as the server, `configuration` or
    `directory`. It connects to each tenant database with that tenant's schema
    role.
  - It claims queued requests with `FOR UPDATE SKIP LOCKED` and a lease. A
    request whose publisher died is claimed again after its lease.
  - It runs the same compile, check, migrate, activate and seed steps as
    [`axis-admin activate`](../reference/administration.md#activate). Activating
    the same content hash twice changes nothing, so a request that runs again
    does no harm.
  - `validate` runs every activation check against the tenant in a transaction
    that is rolled back. It returns the same diagnostic codes as activation and
    changes nothing.
  - `scripts/dev.sh`, Docker Compose, the E2E script and the production installs
    start one publisher.
  - The server still holds no tenant schema connection under
    `Tenancy:Source=directory`. See
    [database roles](../reference/storage.md#database-roles).
- **Release history:**
  - Every activation adds one row to a new tenant table of activations: the
    release, the application, the content hash and the time. It also holds the
    actor, which is a user, `startup` or `operator`, and the label, the note and
    the request.
  - This covers activations at startup, from `axis-admin activate`, from a
    publish and from a rollback.
  - The first time a release is activated in a tenant it gets a release
    **number**: 1, 2, 3… per application. Activating it again keeps its number.
  - The same content can have different numbers in two tenants.
- **Rollback:**
  - A `rollback` request names a release from the history. The publisher
    activates the stored release as it is, without compiling it again.
  - It is refused with a diagnostic when a migration applied after that release
    is missing from it, and when a package version it pins is no longer
    installed. See [rollback](../reference/authoring.md#rollback).
  - A rollback does not change the folder. Studio shows when the active release
    does not match the folder.
- **Routes** under `/api/studio/apps/{application}`:
  - `POST .../validate`, `POST .../publish` and
    `POST .../releases/{id}/rollback` answer `202` with the request and a
    `Location`. `publish` takes an optional `label` and `note`.
  - `GET .../requests/{id}` answers `200` with the request, its status and its
    diagnostics.
  - `GET .../releases` answers `200` with the activation history, newest first,
    and which release is active.
  - The answers for `401`, `403`, `404` and CSRF are those of the rest of the
    authoring API.
- **Audit:** each `publish` and `rollback` writes an
  [audit record](../reference/storage.md#audit-records-and-sequence-counters) with
  the actor, the application, the release and the request. It does so when the
  request succeeds and when it fails.
- **Effect on D38:** D47 replaces the "Studio never activates a release" point
  of [D38](d38-studio-edits-application-folders-through-an-authoring-api.md).
  Studio still never activates in the server process.

*Why:* [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
plans publishing from Studio in M10 without the admin CLI. Activation ran only
at server start or through `axis-admin activate`, so an author on a shared
server had no way to go live. [D31](d31-separate-database-roles.md) and
[D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md)
keep the schema role out of long-running request hosts, so the server cannot
run the activation itself. A request in the tenant database and a separate
publisher keep the server free of schema rights. Studio's diagnostics skip the
checks that need a tenant, so a clean folder can still fail activation.
`validate` runs those checks without changing anything. The tenant database
kept one active release row per application, with no history, no numbers and no
rollback. The history gives each release a number and gives rollback a target.
The costs:

- **A snapshot, not git.** The request holds the files as they were. The release
  is exactly what the author validated, but nothing commits Studio's edits to
  git. Syncing with git can come later.
- **The publisher holds every tenant's schema role.** That keeps the schema role
  out of the server, but the publisher becomes a high-value host. It runs no
  HTTP endpoint.
- **A separate publisher role.** An author can edit and validate. Going live
  needs a second, explicit role. There is no approval flow between two people.
- **Rollback leaves the folder alone.** The live release and the folder can
  differ until the author fixes the folder.
- **Release numbers per tenant.** The same content can have different numbers in
  two tenants.
