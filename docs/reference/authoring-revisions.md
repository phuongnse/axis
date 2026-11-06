# Revisions in the authoring API

Three routes of the [authoring API](authoring.md) read and undo the history that
[revisions](revisions-and-drafts.md#revisions) keep. A revision is named by its full 40-character commit id.
A server with no `Studio:RevisionsPath`, or a folder with no history yet, lists no revisions, and the
diff and the undo answer `404`.

| Route                                                           | What it does                                  |
| --------------------------------------------------------------- | --------------------------------------------- |
| `GET /api/studio/apps/{application}/revisions`                  | Lists the revisions, see [the list](#list)    |
| `GET /api/studio/apps/{application}/revisions/{revision}`       | Shows the files of one, see [the diff](#diff) |
| `POST /api/studio/apps/{application}/revisions/{revision}/undo` | Undoes one, see [the undo](#undo)             |

## List

`200` with `{ items: [{ id, summary, author, userId, client, request, time }] }`, newest first and not
paged. `userId` and `client` are `null` for a revision the server made, and `request` is `null`.

## Diff

`200` with `{ revision, resources: [{ file, change, id, kind, name, before, after }] }`, sorted by
`file`. `revision` has the fields of a list item. `change` is `added`, `changed` or `removed`. `before`
and `after` are the file's JSON text, and `null` on the side where the file does not exist. `id`, `kind`
and `name` are read from the file, and are `null` when it does not parse. A file moved by hand shows as
one removed and one added.

## Undo

`200` with `{ id, diagnostics }`, where `id` is the new revision and `diagnostics` are the folder's, as a
save gives them. The rules are in [undo](revisions-and-drafts.md#undo). In a cell it checks the tenant's
storage quota like a save. Two `409` answers have fixed titles:

| Answer                                         | Title                                                     |
| ---------------------------------------------- | --------------------------------------------------------- |
| `409`, the start of history                    | "The start of history cannot be undone."                  |
| `409`, a later revision changed the same files | "A later revision changed the same files. Undo it first." |

The second has a `revisions` extension that lists those revisions as the list gives them, newest first.
An unknown revision answers `404`, titled "The application has no revision with this id."

## Order of checks

After checks 2, 3 and 5 of the [authoring API](authoring.md#order-of-checks) for the list and the diff,
and checks 1, 2, 3 and 5 for the undo, the first failure is the response:

1. A `{revision}` that is not 40 hexadecimal characters, or not a commit on `main`, `404`.
2. For the undo, the write lock cannot be taken, `503`.
3. For the undo, under the lock: the start of history, or later revisions that changed the same files,
   `409`, and a file of the revision that is not a safe path in the folder, `400`.
