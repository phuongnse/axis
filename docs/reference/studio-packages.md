# Packages in Studio

This page describes how Studio lists, edits and compares the files of an application's own
[packages](package-runners.md#an-applications-packages), and what the authoring API checks when a
file is written. It is the screen and API side of those packages. How they run, are counted and are
switched on is in [extension packages](package-runners.md#an-applications-packages), and the rest of
Studio is in [Studio](studio.md) and [Studio workspace](studio-workspace.md).

## The Packages entry

The Packages entry gives an author the application's packages. It ends the sidebar's Integration group,
see [the workspace](studio-workspace.md#workspace). It is kept in the address as `&view=packages`, and a
package's workspace as `&view=packages&package=<id>`.

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

An open package is a workspace that shows its files in a code editor, and a developer saves, builds and tests them
there. Running them, so that a package can be run in the browser, is planned, see the marked bullets below.

- **The workspace.** An open package is one workspace. It fills the window down to the footer: the explorer on the
  left, the open files as tabs with the code editor under them, the status line at the editor's foot, and the output
  panel under it. The workspace follows [rules, not boxes](shell.md#layout): rules between its regions and no box.
- **The output panel.** The output panel under the status line has a Problems tab, which lists the problems of the
  package's files, see Problems below, an Output tab, which shows the last build or test, see Build and Tests below,
  and a Tests tab, see Tests below. For a developer it also has a Run tab, see Run below. The tab row is a line tab row,
  in the workspace's one tab-row look.
- **The output panel's handles** _(planned for M37)_. A handle on the explorer's end rule sets its width, from a least width to half the workspace, and a handle on the
  output panel's top rule sets its height, both drawn as the one [handle on a rule](shell.md#layout). A rule runs
  under the heading from the sidebar across.
- **The heading.** The package's label, with its kind and version under it. The heading has no tag of a change,
  because the band's Changes not live tells of every change, so the workspace reads no diff against the live release.
- **The heading's actions.** Build, Run tests and Run… are on the right as one secondary
  [group of equal weight](actions-and-controls.md#buttons), each button as wide as the widest, named Build and test
  for assistive technology. They show only with the developer role. Build and Run tests are off while a save, a build or a
  test runs and while Studio reconnects, as one build or test runs at a time. Run… opens the Run tab.
- **The explorer.** A tree of the package's folders and files, drawn by Ant Design's `Tree`. Folders come first, with
  a chevron on a folder's left and a folder icon, and a file has a file icon. A file with unsaved changes has a dot. A
  chosen or hovered row is marked across its whole width, a chosen one in the tint of the [navigation's](shell.md#layout)
  current entry, and each level steps in by 12 px. Files is its title row.
- **The explorer's actions** _(planned for M37)_. A file keeps the chevron's place only in a tree that has a folder, so
  without one the files start at the explorer's edge. Files has New, which offers New file and New folder, and
  Refresh. A node's actions are on a right click, Shift+F10 or the menu key: New file, New folder, Rename (F2), Delete
  and Copy path. A file or folder dragged onto a folder moves into it, and onto the explorer's free space moves to the
  package's root. The folder holds only files, so a new folder lives in the explorer until a file is in it, as git
  keeps folders.
- **The files and the editor.** An open file is a tab with a dot while it is unsaved, and closing it asks before it
  discards. The tabs are line tabs in the one tab look, and their rule is level with the rule under the explorer's
  title row. The editor is one CodeMirror 6 editor, the one the [JSON view](studio-resource-form.md#json-view) is, in
  `codeEditorTheme.ts`'s colours and with the highlighting of the file's language, so no editor draws in
  CodeMirror's own. It highlights C# (`.cs`), TypeScript (`.ts`), JavaScript (`.js`, `.mjs`, `.cjs`), Python (`.py`)
  and JSON (`.json`), and any other file shows as plain text. Comments are `textMuted` in italics and type names
  `successText`. It folds, matches and closes brackets, indents on input, completes the words of the text and
  marks the current line, in `fillSubtle`, and a matching bracket has the thin `border` outline of a search match. A
  language without a syntax tree, such as C#, folds by indentation.
- **The editor's marks and keys** _(planned for M37)_. A build problem is marked at its line. The editor's fold column
  sits `spacing-sm` from the line numbers and from the gutter's rule, and shows an open fold's chevron while the
  pointer is over the gutter and a folded one always, as VS Code does. Ctrl+P opens a file by its name.
- **Saving.** One save bar, or Ctrl+S (Cmd+S on macOS), saves every changed file, one after another. Each save uses
  the version check of [versions](authoring.md#versions), and a save over a stale version answers `409`, as it does for
  a resource. That file keeps its text and its dot, and its tab shows the notice with Reload, while the other files
  still save. A quota refusal shows as it does for a resource.
- **The status line.** At the editor's foot, right above the output panel, it tells of the open
  file: the cursor's line and column, the file's language and what is unsaved.
- **Save before a build, a test or a run.** Build, Run tests and Run… save every changed file first, as a code editor saves before it
  runs a task, so each takes the files the editor shows. A file that cannot be saved, such as one changed elsewhere, or
  one already in conflict, stops it with that file's notice: its tab opens with Reload and nothing is sent.
- **Build.** A build runs only when asked, from Build or Ctrl+Shift+B (Cmd+Shift+B on macOS). Each save still answers
  the folder's diagnostics. Once every file is saved, Build sends `POST .../build` with the package and follows the
  request until it ends, see [the authoring API](#the-authoring-api). The Output tab opens when it starts. Its title
  row reads Build, with the build's state as a status tag: queued, building, built or failed. Under it are the lines
  the host wrote, with no box of their own. A build that cannot be sent or followed shows the request alert.
- **Problems.** The Problems tab lists the folder's diagnostics whose file is under `packages/<id>/`, errors then
  warnings, with the list of the [Problems panel](studio-workspace.md#problems-panel), or says "No problems.". After a
  save it shows the problems that the save answered. A problem's file is a link, named by the file and, when the
  diagnostic has a line, by ", line N". It opens the file in the workspace, or switches to its tab, with the cursor at
  that line, or on line 1 when the diagnostic has none. A package's file shows in the band's Problems panel as plain
  muted text.
- **Problems from a build or a test.** The diagnostics of a build, or of a test whose copy of the package the host
  refused, show in Problems like any other diagnostic, each opening its file at its line. They stay until the next
  build or test starts.
- **A problem on the row** _(planned for M37)_. A problem's file at its line follows its message on the row when the
  row has room.
- **Tests.** Once every file is saved, Run tests sends `POST .../test` with the package and follows the request until
  it ends. The Tests tab opens when it starts. It shows one row a test, with its result, name, duration and message.
  A failed test's place, its file at its line, follows its message and opens there, as a problem's file does. The
  Output tab's title row then reads Tests, with the test's state as a status tag: queued, testing, passed or failed.
  Under it are the lines the host wrote. A test that cannot be sent or followed shows the request alert.
- **Run.** Run… shows two panes parted by a rule that runs the panel's height. The Input pane is
  one block under its title row, in the order a person works: Operation, then Saved input, then the Form and JSON
  tabs and the values, with no rule between them. Run sits in the pane's action bar at its foot, outlined as the
  pane's main action and lined up with the fields. The JSON shows each field the input lacks as `null`. The Output
  pane has the Output title row, with the outcome as a status tag beside the title and Duration on the row's right,
  then the output as read-only JSON, then a rule and the Log, wrapped. Each pane scrolls once from top to bottom, see
  [scroll areas](actions-and-controls.md#scroll-areas). A run's CPU time is not shown, because one run's CPU time
  tells the author nothing they act on, and it counts in the tenant's
  [package run time](tenant-quotas.md#package-run-time-a-day).
- **Saved inputs.** A run's named inputs are kept with the application and shared by its
  developers, as AWS Lambda's shareable test events are, so a team runs the same cases. Saved input's list ends with
  Manage saved inputs…, which opens the [dialog that keeps saved items](actions-and-controls.md#fields).
- **Without the developer role.** The editor is read-only, with an info alert. The workspace shows no save bar, no
  Build, Run tests and Run… group and no Run tab, Ctrl+S saves nothing and Ctrl+Shift+B builds nothing.
- **Without the developer role, the explorer** _(planned for M37)_. The explorer offers only the files, Copy path and
  Refresh.

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

| Route                                                                     | Response                                                                                                                                                                                                                    |
| ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `GET /api/studio/apps/{application}/package-files`                        | `200` with `{ items: [{ package, path, version }] }`, every file under `packages/` in a package folder, sorted by file                                                                                                      |
| `GET /api/studio/apps/{application}/packages/{package}/files/{**path}`    | `200` with `{ package, path, version, content }` and the version as an `ETag`, `content` being the file's text                                                                                                              |
| `PUT /api/studio/apps/{application}/packages/{package}/files/{**path}`    | `200` with `{ version, diagnostics }` and an `ETag`, both for a create and for a replace. The body is `{ content }` as `application/json`, with `If-None-Match: *` to create or `If-Match` of the file's version to replace |
| `DELETE /api/studio/apps/{application}/packages/{package}/files/{**path}` | `200` with `{ diagnostics }`. It needs `If-Match` of the file's version                                                                                                                                                     |

Studio's package workspace needs these routes besides, see [a package's files](#a-packages-files). Each has the
developer role and the switch as a write has, and answers `403` otherwise.

| Route                                                              | Response                                                                                                                                                                                       |
| ------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `POST /api/studio/apps/{application}/build`                        | `202` with the request and a `Location` header for `.../requests/{id}`. The body is `{ package }`. The request ends with `diagnostics` and `output`, see A build without a publish below       |
| `POST /api/studio/apps/{application}/test`                         | `202` as above. The body is `{ package }`, as the MCP server's `test_package` takes, and the request ends with `package`, `packageTests` and `output`, see A package's tests from Studio below |
| `POST /api/studio/apps/{application}/run` _(planned for M37)_      | `202` as above. The body is `{ package, operation, input }`, and the request ends with `run`, see A run of one operation below                                                                 |
| `GET /api/studio/apps/{application}/packages/{package}/run-inputs` | `200` with `{ items: [{ name, operation, input }] }`. The version is the `ETag`, which comes once the inputs are saved, see Saved run inputs below                                             |
| `PUT /api/studio/apps/{application}/packages/{package}/run-inputs` | `200` with `{ version }` and an `ETag`. The body is `{ items }`, with `If-None-Match: *` for the first save or `If-Match` of the version, and a stale version answers `409`                    |
| `POST /api/studio/apps/{application}/packages/{package}/move`      | `200` with `{ diagnostics }`. The body is `{ moves: [{ from, to, version }] }`, see Moves below                                                                                                |

- **The checks.** A write to a package's files is under the [versions](authoring.md#versions) and the write
  lock, the storage quota of a save, see [refusals](tenant-quotas.md#refusals), and the
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
- **Drafts.** Each route takes an optional `?draft=<draft id>`. The list, the read, the write, the delete and the move
  then work on the files of the signed-in author's [draft](revisions-and-drafts.md#drafts), and leave the
  working folder and `main` as they are. The checks, the roles, the switch and the storage quota of a save are those
  of the working folder. `If-Match` and `If-None-Match` are checked against the file in the draft. A draft
  that is another person's, or does not exist, answers `404`, as does any server that keeps no revisions.
  Each write is one commit on the draft's branch, named as a revision is, and the answer's diagnostics are
  those of the draft. A read, a write or a delete records the file's version on `main` by its path, and
  the [accept](revisions-and-drafts.md#accepting-a-draft) of the draft fails with `AXC0377` when that
  file has changed on `main` since.
- **Publishing.** _(planned for M37)_ A publish of an application that holds a package needs the publisher
  role only, and builds and scans each package that Axis runs, see
  [an application's packages](package-runners.md#an-applications-packages).
- **The tools.** The MCP server's `list_package_files`, `read_package_file`, `write_package_file` and
  `delete_package_file` list, read, write and delete package files, in the person's own draft for a write, see
  [tools](mcp-server.md#tools). A write or a delete needs the developer role and the switch, as the routes do, takes
  no version and does not check the storage quota, as `write_resource` does not.
- **Testing a package.** The MCP server's `test_package` tests a package's files in a draft, see
  [tools](mcp-server.md#tools) and [a package's tests](authoring-publishing.md#test). It needs the developer role and the switch,
  as a write does.
- **A build without a publish.** `POST .../build` queues a `build` request that the publisher
  sends to `application-package-host`, as a publish's build is sent, and it releases nothing and writes no audit
  record. The body is exactly `{ package }`, a string, and anything else answers `400`. A package with no file in
  the working folder answers `404`. The route checks no quota, as a test does not. Studio polls `GET .../requests/{id}`
  as for any request. The request ends `succeeded` when the host built the package and `failed` when the host refused
  it. It ends with each problem as a [diagnostic](authoring.md#diagnostics) at its file, with `line` and `column`,
  which only a package file's diagnostic has, and with `output`, the lines the host wrote. Its `output` is an empty
  list until it ends, and `null` for every other kind of request.
- **A Node.js package's TypeScript** _(planned for M37)_. A Node.js package's build compiles its TypeScript with its
  `tsconfig.json` before the checks, so an author never compiles on their own machine.
- **A package's tests from Studio.** `POST .../test` with `{ package }` queues a `test` request for that package's
  working files, from a snapshot of the folder, as `test_package` does. Its answer has `package`, `packageTests` and
  `output`. Each of `packageTests` has `name`, `outcome`, `message` and `durationMs`, and a failed test has `file`,
  `line` and `column`, where it failed in the package. `output` holds the lines the host wrote: what the tests
  printed, or what the build wrote when the host refused the package. The body is `{ package }` with a string, or
  `{ scenarios }` alone, and a body with both, or a `package` that is not a string, answers `400`. A package with no
  file in the folder answers `404`. Only a body with `package` needs the developer role and the switch, and the
  route answers `403` otherwise, checked once the body is read. No quota is checked. Without `package` the request
  is the application's scenario test, which needs the author role only, see
  [test](authoring-publishing.md#test).
- **A run of one operation** _(planned for M37)_. `POST .../run` with `{ package, operation, input }` queues a `run`
  request. The host builds the package's working files and calls the operation with the folder's bindings for its
  capabilities. The request ends with `run`, which has `operation`, `outcome`, `output`, `error` (`code` and
  `message`) and `durationMs`. The run's CPU seconds count in the tenant's
  [package run time](tenant-quotas.md#package-run-time-a-day), and the answer carries no CPU time.
- **Saved run inputs.** `GET` and `PUT .../packages/{package}/run-inputs` read and replace the application's named
  inputs for the package, `{ items: [{ name, operation, input }] }`, under the [versions](authoring.md#versions) as a
  file is. The package must be a plain folder name, but it needs no files. The inputs are the file
  `run-inputs/<package>.json` at the folder root. It is outside `packages/`, so no build holds it, and the loader
  skips the `run-inputs/` folder, so no compile, release or content hash reads it and it is no change that waits for a
  publish. Before the first save `GET` answers `200` with `{ items: [] }` and no `ETag`, so the client sends
  `If-None-Match: *` to create. A save needs `If-None-Match: *` or the current `If-Match`, and a stale version or a
  second create answers `409` and writes nothing. The body is an object with only `items`. Each item has a non-empty
  string `name`, a non-empty string `operation` and an `input` of any JSON value, and no name appears twice, or the
  answer is `400`. The route does not check the input against the operation. Both routes check the session, the
  author role, the developer role, the switch, the application, the package, and for `PUT` the precondition headers
  (`428` for none, `400` for both), the content type (`415`), the body and the version. In a cell `PUT` also checks the
  [storage quota](tenant-quotas.md#refusals). Each save is a revision, "Create run inputs of package x" or "Change
  run inputs of package x", and [undo](revisions-and-drafts.md#undo) puts the earlier inputs back.
- **Moves.** `POST .../packages/{package}/move` with `{ moves: [{ from, to, version }] }` moves files as one
  revision, so one [undo](revisions-and-drafts.md#undo) puts them all back. A folder moves by listing each of its
  files. `from` and `to` are paths inside the package of the URL, by the rules of the path above, so a move never
  carries a file into another package. `version` is the bare hex that the list and the read give. The body is an
  object with only a non-empty `moves` array of objects with exactly the non-empty strings `from`, `to` and
  `version`. No `from` or `to` may appear twice, no `from` may equal its `to`, and no `to` may be a folder of another
  `to`, or the answer is `400`. A `to` may be another move's `from`, so a chain or a swap works, because the `to`
  paths are free once every `from` has left. A missing or changed `from`, a `to` that holds a file that stays, a
  folder, or a file in the way, answers `409`. A refused path answers `400`. Nothing is written on a refusal. The
  route checks the session, the author role, the developer role, the switch, the application, the package (`400`),
  the content type (`415`), the body (`400`) and each path (`400`), then the write lock (`503`), the draft (`404`)
  and the files (`409`). It never checks the storage quota, because a move adds no bytes. The revision is "Move
  package file packages/x/a.ts to packages/x/b.ts" for one file and "Move 2 package files in packages/x" for more,
  and a draft commit has the same name. With `?draft`, a missing `from` answers `409` too. When every file leaves a
  folder, the empty folder stays on disk: the list never shows it, but a file at its path answers `409`.
