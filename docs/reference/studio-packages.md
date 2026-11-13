# Packages in Studio

_(planned for M37)_

This page describes how Studio lists, edits and compares the files of an application's own
[packages](extension-packages.md#an-applications-packages), and what the authoring API checks when a
file is written. It is the screen and API side of those packages. How they run, are counted and are
switched on is in [extension packages](extension-packages.md#an-applications-packages), and the rest of
Studio is in [Studio](studio.md) and [Studio workspace](studio-workspace.md).

## The Packages entry

The Packages entry gives an author the application's packages. It ends the sidebar's Integration group,
see [the workspace](studio-workspace.md#workspace).

- **The list.** It is the standard table, an `AxisTable`, of each package's label, id, language and
  version, one row for each folder under `packages/`.
- **New package.** The "New package…" action is for a person with the developer role only. It asks for an
  id and a language, and creates the package's manifest, adapter and lock file for that language.
- **Opening one.** A row opens the package's files, see [a package's files](#a-packages-files).

## A package's files

An author reads and edits the text of each file of a package, so a package can be written in the browser.

- **The file list.** An open package lists its files by path, the manifest, the adapter, the lock file and
  the tests among them.
- **The code editor.** Opening a file shows its text in a code editor. It builds on the CodeMirror editor of
  the [JSON view](studio-resource-form.md#json-view), in `JsonView.tsx` and `codeEditorTheme.ts`, with the
  highlighting of the file's language and the same colours, so no editor draws in CodeMirror's own.
- **Saving.** A save uses the version check of [versions](authoring.md#versions), and a save over a stale
  version answers `409`, as it does for a resource.
- **Without the developer role.** The editor is read-only, and the file shows no save action.
- **Problems.** A build failure of a package shows in the [Problems panel](studio-workspace.md#problems-panel)
  like any other diagnostic.

## Diffs

A change to a package's files shows as text, line by line, because a package file is code and not a
resource's JSON.

- **Where.** A revision's diff, a draft's diff and the publish dialog show each changed package file as
  Added, Changed or Removed, with its text before and after, line by line, see
  [revisions](ai-authoring-in-studio.md#revisions) and [the diff](ai-authoring-in-studio.md#the-diff).
- **The same rules.** The summary, the markers and the order follow those of a resource's diff. Only a
  changed file's text is drawn differently.

## The authoring API

Package files take the same checks as a resource, so Studio, an author's own tools and the MCP server
reach them one way.

- **The checks.** A write to a package's files is under the [versions](authoring.md#versions) and the write
  lock, the storage quota, see [refusals](tenant-quotas.md#refusals), and the
  [revisions and drafts](revisions-and-drafts.md) of the application, so a package file has history, can be
  undone and can be written in a draft.
- **Who may write.** A write needs the author role, the developer role and the tenant's switch on, see
  [application packages](user-administration.md#application-packages). Otherwise it answers `403`. A read
  needs the author role only.
- **Publishing.** A publish of an application that holds a package needs the publisher role only, and
  builds and scans the package, see [an application's packages](extension-packages.md#an-applications-packages).
- **The tools.** The MCP server's tools read, write and test package files, see [tools](mcp-server.md#tools).
