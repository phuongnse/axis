# D38. Studio edits application folders through an authoring API

Status: **Agreed**

## Context

[D5](d5-configuration-is-authored-as-files-first.md) makes an application a folder of resource files
kept in git, and asks that any visual authoring write the same resources through the same contracts.
Studio needs a way to read and change those files from the browser. Two authors, or an author and a
file edit in git, can change the same resource, and
[authoring and packaging](../domain/knowledge.md#authoring-and-packaging) warns against edits that
replace a whole resource blindly.

## Decision

- **The folder is the source.** Studio edits the application folders on the server's disk, the same
  JSON files that are kept in git. The database holds no copy of an application being authored.
- **Through an authoring API.** The server serves an API under `/api/studio/apps/{application}` that
  lists, reads, creates, patches and deletes resources. A write is a JSON Patch that carries the
  resource's version, so a write over someone else's change is refused rather than lost.
- **A broken resource still saves.** A schema violation or a compile error does not block a save: it
  is a diagnostic, answered with every write and from a route of its own, with the same codes as
  activation.
- **Authoring is an installation permission.** Which folders Studio may edit, and which role may use
  Studio, are host settings. Application policies play no part, because an author can change every
  policy of the application they edit.
- **Authoring does not activate.** Saving changes a folder and nothing else. Making it live is
  publishing, which [D47](d47-publishing-from-studio.md) gives to a separate publisher.

The routes are in the [authoring API](../reference/authoring.md), the settings in
[Studio settings](../reference/host-settings.md#studio-settings), and the page in
[Studio](../reference/studio.md).

## Consequences

- Git stays the source of truth: a folder that Studio changed is committed and reviewed like any
  other.
- The server process never needs the schema role to save, as
  [D31](d31-separate-database-roles.md) asks.
- Writes on several server replicas need a lock that all of them share, so that the version check
  holds, see [D49](d49-several-server-replicas-behind-a-tls-proxy.md).
- The author role is set per installation, so an installation cannot give one person Studio for one
  application only.

## Alternatives

- **A draft store in the database.** It would let Studio work without a writable folder, but it would
  make the database, rather than git, the source of an application, and need a second way to review
  and deploy changes.
- **Replace a whole file on save.** Simpler, but a save would silently undo a change that someone else
  made to the same file meanwhile.
