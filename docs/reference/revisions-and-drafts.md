# Revisions and drafts

Each application folder has a history of revisions, so every change can be traced and undone, and
authoring requests work on drafts apart from it, so an agent's change never touches the folder before
it is accepted. Both live in an internal git repository for each folder, because git already does
diffs, history, branches, reverts and all-or-nothing updates. The reasons are in
[D125](../decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md).

## The repository

The server keeps one git repository for each application folder that Studio edits, with
LibGit2Sharp.

- **Outside the folder.** The repository lives under `Studio:RevisionsPath`, as `<folder name>.git`,
  so an application folder that is itself in a git checkout, such as a sample, gets no nested
  repository. Where the folder is, its default and the rule in a cell are in
  [Studio settings](host-settings.md#studio-settings). A server with no such folder keeps no
  revisions, and its writes work as before.
- **In a cell.** The repositories are under the tenant's Studio folder, so the tenant's backup,
  export and deletion cover them, see [tenant lifecycle](tenant-lifecycle.md).
- **Never served.** No route serves the repository, and no git protocol listens. People and agents see
  revisions and drafts only through the authoring API and Studio.
- **The working folder stays the source.** The files on disk are what the compiler, the publisher and
  Studio read. The repository records them and never replaces them.
- **Created on first use.** The first write to a folder that has no repository commits the folder as
  it is, as the revision "Start of history". Creating an application makes no revision, so its first
  write records the template as "Start of history".
- **Built in memory.** A revision is built from the folder's JSON files, as the authoring API reads
  them, into a bare repository. Git never checks anything out, so no `.git` file ever appears in the
  folder.
- **Loaded at start.** The server loads the git library at start, so an installation without it fails
  at once, not on the first save.

## Revisions

A revision is one commit on the repository's `main` branch, and every change to the folder makes one.

- **What makes one.** Each save in Studio, each `changes` call, each text write, each create, each
  delete, each module layer save or reset and each undo through the [authoring API](authoring.md#writes). The
  commit is made under the same platform lock as the write, after the files are written, so the
  history follows the folder in the same order. A write that leaves every file byte for byte the same
  makes no revision, and a refused write makes none.
- **Accepts.** _(planned for M35)_ Each accept makes one revision, see
  [accepting a draft](#accepting-a-draft).
- **What it names.** The git author and committer are the user's display name at the time. The commit
  message is a short summary of the change, such as "Change process RequestApproval", then the
  trailers `Axis-User-Id` with the user id and `Axis-Client` with the client, which is `studio`.
  The commit time is the time of the write.
- **Agent clients.** _(planned for M35)_ The client can also be the agent host or an outside client id,
  and a revision from an accept names the authoring request.
- **Outside changes.** A change made on disk outside the authoring API, such as a hand edit under
  `scripts/dev.sh`, is committed as "Outside change" before the next write, so the next revision holds
  only its own change. "Start of history" and "Outside change" name the server (`Axis`) as author,
  with no user id and no client, because nobody known made those changes.
- **A failed revision.** When recording a revision fails, the server logs a warning and the write
  still succeeds, because its files are already written. The next write records the missed change as
  "Outside change".
- **Versions.** A resource's `version` is the one the authoring API gives, see
  [versions](authoring.md#versions). A draft records it for each resource it reads or changes.

## Drafts

A draft is a branch where an authoring request makes its change, apart from the working folder and
from every other draft.

- **A branch.** A draft is a branch `drafts/<draft id>` that starts at the `main` commit of the time it
  was created. Creating a draft first records any outside change in the folder, so the draft starts at
  what the folder holds. The working folder is never checked out to it.
- **Writes.** Each tool write commits to the branch from memory, as a tree of the changed files, so
  writing a draft never touches the working folder or another draft. A write holds the full content of
  a resource. It replaces the file of the resource the draft has, and a new resource goes where a
  create puts it. The manifest cannot be deleted.
- **What it records.** For each resource it reads or changes, the version that resource had on `main`
  when the draft first used it. A resource `main` lacks is recorded as absent. Listing resources
  records nothing, so an unrelated change on `main` is not a conflict.
- **The record.** A draft's record holds its owner, the commit it started at, its creation time and
  the recorded versions. It is kept in the same repository as the branch, under `refs/axis/drafts/<draft id>`.
- **The owner.** A draft belongs to the person who made it. For anyone else it does not exist and
  answers "not found", as an unknown id does.
- **Diff.** _(planned for M35)_ The diff of a draft is the difference between its branch and the `main` commit it started
  from, resource by resource, see `diff_draft` in [the MCP server](mcp-server.md#tools).
- **Dropping.** Dropping a draft deletes its branch and its record.
- **Ending by accept.** _(planned for M35)_ An accepted draft's branch is deleted, and its accept stays
  as a revision.

## Accepting a draft

_(planned for M35)_

The publisher runs an accept as a publish request of the kind `accept`, see
[accepting](authoring-requests.md#accepting), in these steps:

1. It takes the application's write lock.
2. It compares each version the draft recorded with the version on `main` now. Any difference is a
   conflict, and nothing is written.
3. It applies the draft's changes to a scratch copy of the working folder and runs the compiler and
   the scenarios on it, as a validate and a test request do.
4. On success it writes the changed files to the working folder, as the authoring API writes them,
   and commits them as one revision.
5. On any failure the working folder and `main` are unchanged, and the request answers the failure.

## Undo

Undoing a revision commits a new revision that reverts it, so history never shrinks. The revision
is named "Undo <summary>", such as "Undo Change rule FinanceThreshold". The
[authoring API](authoring-revisions.md) lists the revisions, shows one and undoes it.

- **Only when clean.** A revision whose files a later revision changed again cannot be undone alone.
  The undo is refused and names the later revisions that touch the same files, newest first, and the
  author undoes those first. Nothing is written when it is refused.
- **Not the start.** The first revision, "Start of history", cannot be undone, because that would
  delete every file in the folder.
- **Checks.** An undo runs the compiler like any write, and its diagnostics show as for a save. It
  never publishes.
- **Who.** A person with the author role. An agent has no undo tool.

## Limits

What a repository holds and how long it keeps it.

- **Size.** A revision holds only JSON files under the folder, as the authoring API reads them.
- **Retention.** Revisions are kept as long as the application. A deleted application's repository is
  deleted with its folder.
