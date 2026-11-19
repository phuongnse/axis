# Packages in Studio

This page describes how Studio lists, edits and compares the files of an application's own
[packages](extension-packages.md#an-applications-packages), and what the authoring API checks when a
file is written. It is the screen and API side of those packages. How they run, are counted and are
switched on is in [extension packages](extension-packages.md#an-applications-packages), and the rest of
Studio is in [Studio](studio.md) and [Studio workspace](studio-workspace.md).

## The Packages entry

The Packages entry gives an author the application's packages. It ends the sidebar's Integration group,
see [the workspace](studio-workspace.md#workspace). It is kept in the address as `&view=packages`, and a
package's page as `&view=packages&package=<id>`.

- **The list.** It is the standard table, an `AxisTable`, of each package's label, id, language and
  version, one row for each folder under `packages/`. The id comes from the files that
  `GET .../package-files` lists, so a folder with no file has no row. The label, language and version come
  from a read of the package's `axis-package.json`. A package whose manifest is missing or is not valid
  JSON still has its row, with its id as the label and empty language and version cells. The table has a
  filter on every column, a sort on Label and ID, and no search or pager.
- **New package.** The "New package…" action sits on the right of the page's heading and shows only when
  `GET /api/me` answers `studioDeveloper: true`. It asks for an id and a language, C#, TypeScript or
  Python, and writes the language's starter one file at a time with `PUT` and `If-None-Match: *`, the
  manifest first, so no file is replaced. The dialog checks the id against the manifest's id pattern
  `^[a-z][a-z0-9-]*$` and sends nothing until it passes. An id that exists already, a `409` on the
  manifest, shows under the id field and the dialog stays open. The new row then appears, and the
  [Problems panel](studio-workspace.md#problems-panel) shows the diagnostics of the writes.
- **Tested starters.** The files that New package writes come from `web/src/platform/packageStarters.json`,
  with one starter each for C#, TypeScript and Python. A C# package gets `axis-package.json`, a project file,
  `EchoOperation.cs` and `packages.lock.json`. A TypeScript package gets `axis-package.json`, `index.js`,
  `package.json` and `package-lock.json`. A Python package gets `axis-package.json`, `adapter.py` and
  `requirements.txt`. Each starter has version `0.1.0`, one `echo` operation and a label taken from the id.
  A Vitest test checks the placeholders and the manifest against its schema. A server test compiles each
  starter in an application with application packages on, and expects no diagnostic under `packages/<id>/`.
- **Opening one.** A row opens the package's files, see [a package's files](#a-packages-files).

## A package's files

An author reads and edits the text of each file of a package, so a package can be written in the browser.

- **The file list.** An open package lists its files by path, the manifest, the adapter, the lock file and
  the tests among them. The page is headed with the package's label, and its table has a filter and a sort on
  Path, and no search or pager. It shows the working folder.
- **The code editor** _(planned for M37)_. Opening a file shows its text in a code editor. It builds on the CodeMirror editor of
  the [JSON view](studio-resource-form.md#json-view), in `JsonView.tsx` and `codeEditorTheme.ts`, with the
  highlighting of the file's language and the same colours, so no editor draws in CodeMirror's own.
- **Saving** _(planned for M37)_. A save uses the version check of [versions](authoring.md#versions), and a save over a stale
  version answers `409`, as it does for a resource.
- **Without the developer role** _(planned for M37)_. The editor is read-only, and the file shows no save action.
- **Problems** _(planned for M37)_. A build failure of a package shows in the [Problems panel](studio-workspace.md#problems-panel)
  like any other diagnostic.

## Diffs

_(planned for M37)_

A change to a package's files shows as text, line by line, because a package file is code and not a
resource's JSON.

- **Where.** A revision's diff, a draft's diff and the publish page show each changed package file as
  Added, Changed or Removed, with its text before and after, line by line, see
  [revisions](ai-authoring-in-studio.md#revisions), [the diff](ai-authoring-in-studio.md#the-diff) and
  [publishing](studio.md#publishing).
- **The same rules.** The summary, the markers and the order follow those of a resource's diff. Only a
  changed file's text is drawn differently.

## The authoring API

Package files take the same checks as a resource, so Studio, an author's own tools and the MCP server
reach them one way. The files are the files under `packages/` of the application's working folder, and a
path names one as `packages/<package>/<path>`.

| Route                                                                  | Response                                                                                                                                                                                                                                              |
| ---------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/studio/apps/{application}/package-files`                     | `200` with `{ items: [{ package, path, version }] }`, every file under `packages/` in a package folder, sorted by file                                                                                                                                 |
| `GET /api/studio/apps/{application}/packages/{package}/files/{**path}` | `200` with `{ package, path, version, content }` and the version as an `ETag`, `content` being the file's text                                                                                                                                         |
| `PUT /api/studio/apps/{application}/packages/{package}/files/{**path}` | `200` with `{ version, diagnostics }` and an `ETag`, both for a create and for a replace. The body is `{ content }` as `application/json`, with `If-None-Match: *` to create or `If-Match` of the file's version to replace                           |
| `DELETE /api/studio/apps/{application}/packages/{package}/files/{**path}` | `200` with `{ diagnostics }`. It needs `If-Match` of the file's version                                                                                                                                                                            |

- **The checks.** A write to a package's files is under the [versions](authoring.md#versions) and the write
  lock, the storage quota, see [refusals](tenant-quotas.md#refusals), and the
  [revisions](revisions-and-drafts.md) of the application, so a package file has history and can be
  undone. A stale version, or a create over a file that exists, answers `409`.
- **The file.** A file is read and written as UTF-8 text. A write puts the bytes of `content` in the file
  as they are, with no reformatting, and the version is the SHA-256 of those bytes as lower-case hex.
- **The path.** The package must be a plain folder name. No segment of the path may be empty, `.` or
  `..`, start with `.` or hold `\`, because a hidden file is left out of revisions and snapshots. A bad
  path answers `404` on a read and a delete, and `400` on a save.
- **Who may write.** A write needs the author role, the developer role (the roles claim value of `Studio:DeveloperRole`) and the
  tenant's switch on, see [application packages](user-administration.md#application-packages). Otherwise
  it answers `403` and writes nothing. A tenant that stores no switch counts as off. A read and a list
  need the author role only.
- **The order.** A write is checked in the order of the [authoring API](authoring.md#order-of-checks): the
  CSRF header, the storage quota on a save, the session, the author role, the developer role, the
  switch, the application, then the path, the precondition headers (`428` for none, and `400` for both
  on a save), the content type (`415`) and the body (`400`, which is anything but an object with a
  string `content`). The write lock (`503`) and the version (`409`) come last. With a draft, the write lock
  (`503`), the draft (`404`), the missing file of a read or a delete (`404`) and the version (`409`) come
  last.
- **Each write is a revision.** It is named "Create package file packages/x/adapter.py", "Change package
  file packages/x/adapter.py" or "Delete package file packages/x/adapter.py", and the
  [undo](revisions-and-drafts.md#undo) puts the file back.
- **Diagnostics.** The answer carries the diagnostics of the folder, as a resource write does.
- **Drafts.** Each route takes an optional `?draft=<draft id>`. The list, the read, the write and the delete
  then work on the files of the signed-in author's [draft](revisions-and-drafts.md#drafts), and leave the
  working folder and `main` as they are. The checks, the roles, the switch and the storage quota are those
  of the working folder. `If-Match` and `If-None-Match` are checked against the file in the draft. A draft
  that is another person's, or does not exist, answers `404`, as does any server that keeps no revisions.
  Each write is one commit on the draft's branch, named as a revision is, and the answer's diagnostics are
  those of the draft. A read, a write or a delete records the file's version on `main` by its path, and
  the [accept](revisions-and-drafts.md#accepting-a-draft) of the draft fails with `AXC0377` when that
  file has changed on `main` since.
- **Publishing.** _(planned for M37)_ A publish of an application that holds a package needs the publisher
  role only, and builds and scans each package that Axis runs, see
  [an application's packages](extension-packages.md#an-applications-packages).
- **The tools.** The MCP server's `list_package_files`, `read_package_file`, `write_package_file` and
  `delete_package_file` list, read, write and delete package files, in the person's own draft for a write, see
  [tools](mcp-server.md#tools). A write or a delete needs the developer role and the switch, as the routes do, takes
  no version and does not check the storage quota, as `write_resource` does not.
- **Testing a package.** The MCP server's `test_package` tests a package's files in a draft, see
  [tools](mcp-server.md#tools) and [a package's tests](authoring.md#test). It needs the developer role and the switch,
  as a write does.
