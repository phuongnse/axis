# D47. Publishing from Studio

Status: **Agreed**

## Context

An author must be able to make an application live from Studio, without the admin tool. Activation
migrates the tenant's storage, so it needs the tenant's schema role, which
[D31](d31-separate-database-roles.md) and
[D43](d43-tenant-provisioning-and-migrations-across-tenant-databases.md) keep out of the hosts that
serve requests. Studio's diagnostics also skip the checks that need a tenant, so a folder without
problems can still fail activation.

## Decision

- **The server stores a publish request, and a separate publisher runs it.** Studio asks to
  validate, publish or activate a release by storing a request in the tenant database. The
  publisher, a long-running command of the admin tool, runs it with the tenant's schema role. The
  server never activates a release in its own process.
- **A request carries a snapshot of the folder,** so the release is exactly what the author sent.
- **`validate` runs every activation check and changes nothing.**
- **A request runs for the tenant of the author's session,** or for its preview tenant as
  [D48](d48-preview-tenants.md) says.
- **Going live needs a second role.** The author role is enough to edit and validate, and publishing
  and activating a release also need the publisher role.
- **Every activation is recorded** in a release history in the tenant database, and a release gets
  a number per application the first time it is activated there.
- **Activating a stored release takes it as it is,** without compiling it again and with every
  refusal of activation. It leaves the folder unchanged.
- **Publishing and activating a release are audited,** whether they succeed or fail.

The detail is in [publishing](../reference/authoring.md#publishing), the
[publisher](../reference/administration.md#publisher),
[publish requests and release history](../reference/storage.md#publish-requests-and-release-history)
and the [Studio settings](../reference/host-settings.md#studio-settings).

## Consequences

- The server holds no schema rights, but the publisher holds every tenant's schema role, so it is a
  high-value host and runs no HTTP endpoint.
- Activating the same content twice changes nothing, so a request that runs again does no harm.
- Nothing commits Studio's edits to git.
- There is no approval flow between two people, only the second role.
- After a stored release is activated, the live release and the folder differ until the author
  fixes the folder.
- The same content can have different release numbers in two tenants.

## Alternatives

- **Activate in the server process.** It would give the server the schema role that D31 keeps out of
  it.
- **Publish the folder as it is when the request runs.** The release could then differ from what the
  author validated.
