# Revisions and drafts in the authoring API

Three routes of the [authoring API](authoring.md) read and undo the history that
[revisions](revisions-and-drafts.md#revisions) keep, and a fourth reads the diff of a
[draft](revisions-and-drafts.md#drafts). A revision is named by its full 40-character commit id.
A server with no `Studio:RevisionsPath`, or a folder with no history yet, lists no revisions, and the
diff and the undo answer `404`.

| Route                                                           | What it does                                                    |
| --------------------------------------------------------------- | --------------------------------------------------------------- |
| `GET /api/studio/apps/{application}/revisions`                  | Lists the revisions, see [the list](#list)                      |
| `GET /api/studio/apps/{application}/revisions/{revision}`       | Shows the files of one, see [the diff](#diff)                   |
| `POST /api/studio/apps/{application}/revisions/{revision}/undo` | Undoes one, see [the undo](#undo)                               |
| `GET /api/studio/apps/{application}/drafts/{draft}/diff`        | Shows the changes of a draft, see [the draft diff](#draft-diff) |

## List

`200` with `{ items: [{ id, summary, author, userId, client, request, time }], page, pageSize, totalCount,
start }`: one page of the revisions, newest first unless `sort` says otherwise. `userId` and `client` are
`null` for a revision the server made. `request` is the `accept` [publish request](authoring.md#the-request)
that a revision from an accept names, and `null` for any other.

The route takes the record API's `page`, `pageSize`, `sort`, `search` and `filter`, as
[paging and sorting](record-api.md#paging-and-sorting) defines them. The columns are `summary`, `author`
and `client`, which are text, and `time`, which is a date.

- **`sort`.** One column, `-` first for descending order. A `sort` on a time orders by the full time.
  Rows that tie keep the newest-first order. A missing value comes last ascending and first descending.
- **`search`.** Keeps the revisions whose `summary` or `author` contains the text, ignoring letter case.
- **`filter`.** The same groups and operators as the record API. A condition on `time` takes a
  day, `YYYY-MM-DD`, and compares the day of the revision in UTC. A server revision has no `client`, so
  `empty` matches it. The `author` it compares is the stored one, `Axis` for a revision the server made.
- **`totalCount`.** How many revisions match, before the page is cut. A page past the end is `200` with
  empty `items` and the real `totalCount`.
- **`start`.** The id of the start of history, the oldest revision, whatever the page, the search or the
  filter. `null` when there is no revision. It is the one revision that [has no undo](#undo), so a client
  that shows an Undo action on each row hides it on this one.

A `page`, `pageSize`, `sort`, `search` or `filter` that is invalid, including a `sort` or a `filter` on a
column the list does not hold, is `400` with a problem whose `errors` name the parameter, as the record API
does. A server without `Studio:RevisionsPath`, or a folder with no history yet, lists no revisions.
The server reads the folder's whole history for each request and then pages it in memory. The
[shared list query](shared-widgets.md#which-widget) does that work, so Releases and the lists of each kind
can use it.

## Diff

`200` with `{ revision, resources: [{ file, change, id, kind, name, before, after, target }] }`, sorted by
`file`. `revision` has the fields of a list item. `change` is `added`, `changed` or `removed`. `before`
and `after` are the file's text, and `null` on the side where the file does not exist. `id`, `kind`
and `name` are read from the file, and are `null` when it does not parse. `target` is, for a
[customization layer](customization-layers.md) file under `customizations/`, the module resource it changes, such as
`approvals/RequestApproval`, taken from the file's path, and `null` for any other file. A file moved by
hand shows as one removed and one added.

## Draft diff

`200` with `{ start, tip, resources, cursor }`, the changes of the signed-in user's draft since the
`main` commit it started at, see [drafts](revisions-and-drafts.md#drafts). `start` is that commit and
`tip` is the draft commit the page was read at. `resources` has the entries of the revision diff, sorted
by `file`. `cursor` is `null` on the last page, and otherwise is passed back as the `cursor` query to
read the next page.

A cursor that is not one this draft gave answers `400`, titled "The cursor is not one this draft gave."
A draft that does not exist, or that is another person's, answers `404`, titled "The application has no
draft with this id." The write lock is taken to read, so a lock that cannot be taken answers `503`.

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

After checks 2, 3 and 5 of the [authoring API](authoring.md#order-of-checks) for the list, the diff and the
draft diff, and checks 1, 2, 3 and 5 for the undo, the first failure is the response:

1. A `{revision}` that is not 40 hexadecimal characters, or not a commit on `main`, `404`. For the draft
   diff, a `{draft}` that is not the user's draft, `404`.
2. For the undo and the draft diff, the write lock cannot be taken, `503`.
3. For the undo, under the lock: the start of history, or later revisions that changed the same files,
   `409`, and a file of the revision that is not a safe path in the folder, `400`. For the draft diff, under
   the lock: a cursor the draft did not give, `400`.
