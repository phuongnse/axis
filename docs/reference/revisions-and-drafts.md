# Revisions and drafts

Each application folder has a history of revisions, and authoring requests work on drafts apart
from it. Both live in an internal git repository for each folder.
[D119](../decisions/d119-ai-authoring-in-studio.md) agrees the idea, and
[D125](../decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) the shapes below.
Everything here is _(planned for M35)_. The conventions of [architecture.md](../architecture.md)
apply.

## The repository

- **One per application folder.** The server keeps a git repository for each folder that Studio
  edits, with LibGit2Sharp. It lives outside the folder, under `Studio:RevisionsPath`, as
  `<folder name>.git`, so an application folder that is itself in a git checkout, such as a sample,
  gets no nested repository.
- **Never served.** No route serves the repository, and no git protocol listens. People and agents
  see revisions and drafts only through the authoring API and Studio.
- **In a cell.** `Studio:RevisionsPath` is under the tenant's Studio folder,
  `<Studio:ApplicationsPath>/<tenant id>/.revisions/`, so the tenant's backup, export and deletion
  cover it, see [tenant lifecycle](tenant-lifecycle.md).
- **The working folder stays the source.** Files on disk are what the compiler, the publisher and
  Studio read, as today. The repository records them and never replaces them.
- **Created on first use.** The first write to a folder that has no repository commits the folder
  as it is, as the revision "Start of history".

## Revisions

A revision is one commit on the repository's `main` branch.

- **What makes one.** Each save in Studio, each `changes` call, each text write, each create and
  each delete through the [authoring API](authoring.md#writes), and each accept. The commit is made
  under the same platform lock as the write, after the files are written, so the history follows the
  folder in the same order.
- **What it names.** The author, as their user id and display name at the time, the time, the
  client (`studio`, the agent host or an outside client id) and, for an accept, the authoring
  request. The commit message is a short summary of the change, such as "Change process
  RequestApproval".
- **Outside changes.** A change made on disk outside the authoring API, such as a hand edit under
  `scripts/dev.sh`, is committed as "Outside change" before the next write, so the next revision
  holds only its own change.
- **Versions.** A resource's `version` stays what the authoring API gives today. A draft records it
  for each resource it reads.

## Drafts

- **A branch.** A draft is a branch `drafts/<draft id>` that starts at the `main` commit of the time
  it was created. The working folder is never checked out to it.
- **Writes.** Each tool write commits to the branch from memory, as a tree of the changed files, so
  writing a draft never touches the working folder or another draft.
- **What it records.** For each resource it reads or changes, the version that resource had on
  `main` when the draft first used it.
- **Diff.** The diff of a draft is the difference between its branch and the `main` commit it
  started from, resource by resource, see `diff_draft` in [the MCP server](mcp-server.md#tools).
- **Ending.** An accepted or dropped draft's branch is deleted. Its accept stays as a revision.

## Accepting a draft

The publisher runs an accept as a publish request of the kind `accept`, see
[accepting](authoring-requests.md#accepting).

1. It takes the application's write lock.
2. It compares each version the draft recorded with the version on `main` now. Any difference is a
   conflict, and nothing is written.
3. It applies the draft's changes to a scratch copy of the working folder and runs the compiler and
   the scenarios on it, as a validate and a test request do.
4. On success it writes the changed files to the working folder, as the authoring API writes them,
   and commits them as one revision.
5. On any failure the working folder and `main` are unchanged, and the request answers the failure.

## Undo

- **A revert.** Undoing a revision commits a new revision that reverts it. History never shrinks.
- **Only when clean.** A revision whose files a later revision changed again cannot be undone alone.
  Studio lists the later revisions that touch the same files, and the author undoes those first.
- **Checks.** An undo runs the compiler like any write. Its diagnostics show as for a save. It never
  publishes.
- **Who.** A person with the author role. An agent has no undo tool.

## Limits

- **Size.** A revision holds only JSON files under the folder, as the authoring API reads them.
- **Retention.** Revisions are kept as long as the application. A deleted application's repository
  is deleted with its folder.
