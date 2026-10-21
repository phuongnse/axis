# D48. Preview tenants

Status: **Agreed**

An author tries an application on a **preview tenant** before publishing it to
the real tenant. A preview tenant belongs to one main tenant and keeps its own
data, database roles, hosts and bindings. It was built in M10, with one exception:
refusing to restore a preview tenant's archive is planned for M16. See [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
for M10, [preview tenants](../architecture.md#preview-tenants) for how the hosts
treat one, [preview](../reference/authoring.md#preview) for the route,
[tenant create](../reference/administration.md#tenant-create) for the command and
[preview tenant settings](../reference/host-settings.md#preview-tenants) for the
`PreviewOf` setting.
The owner confirmed these points on issue #705 on 2026-10-19.

- **What a preview tenant is:**
  - It is a tenant that belongs to one main tenant. A main tenant has at most
    one preview tenant. A preview tenant has no preview of its own.
  - It has its own database, database roles and hosts, like any tenant. Its
    data never mixes with the main tenant's.
  - Under `directory`, the operator creates it once with
    `tenant create <id> --preview-of <main> --host <host>`.
  - Under `configuration`, a tenant setting `PreviewOf` names the main tenant.
    Development, Compose and E2E configure one preview tenant.
- **Sign-in:**
  - A preview tenant uses its main tenant's sign-in settings: the same
    authority, client and claim names.
  - The operator registers the preview host's redirect address at the identity
    provider.
  - A session is still bound to the tenant it signed in on.
- **Bindings:**
  - A preview tenant has its own bindings. They are empty by default, so a
    preview never calls a real system by accident.
  - Under `configuration` they are set like any tenant's. Under `directory`,
    `tenant create --preview-of` takes `--binding <name>=<value>` options for
    `url` bindings.
  - On a preview tenant, a missing binding is a warning at activation, not an
    error. A call that needs it fails at run time, where the inspector shows
    it.
- **Publishing to the preview:**
  - `POST /api/studio/apps/{application}/preview` writes a publish request of
    kind `publish` for the session tenant's preview tenant. It answers `202`,
    as for publish.
  - It needs the author role only, not the publisher role.
  - Preview activations go in the preview tenant's own release history, not
    the main tenant's.
  - When the main tenant has no preview tenant, the route answers `409` with a
    problem that says so.
  - Publishing to the main tenant does not require a preview first.
- **Data in a preview:**
  - A preview starts with the application's seed data. Records created in it
    stay until the operator recreates the preview tenant.
  - Resetting a preview from Studio comes later.
- **Effect on earlier decisions:**
  - [D42](d42-tenant-directory-in-the-platform-database.md): a directory
    entry may name its main tenant.
  - [D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md):
    `tenant create` gains `--preview-of` and `--binding`.
  - [D47](d47-publishing-from-studio.md): a request normally runs for
    the session tenant. A preview request runs for its preview tenant.

*Why:* [D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
asked for a preview on a preview tenant in M10. Before it, the only way to try a
change was `scripts/dev.sh`, where a saved file restarted the server and
activated the folder for every tenant. A tenant had no notion of a preview, and
its bindings pointed at real systems. A preview tenant gives the author a place
to try a change with its own data and bindings. The costs:

- **One preview per tenant, shared by every application of that tenant.** That
  is simpler than one per application. Two authors who preview different
  applications share one preview tenant.
- **Empty bindings by default.** A preview with a connector shows failed calls
  until the operator gives it bindings, for example to a test system.
- **No reset from Studio in M10.** A preview tenant collects test records until
  the operator recreates it.
- **Preview needs no publisher role.** Any author can preview. Only publishing
  to the main tenant needs the second role.
