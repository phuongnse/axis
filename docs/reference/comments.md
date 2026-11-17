# Comments

_(planned for M43)_

People discuss a record or a task in comments. A comment may be internal or shared, mention people, carry files and keep every version. This page covers comments, mentions, comment files and the record timeline. The record API is in [record-api.md](record-api.md), and the history of a record in [record-audit.md](record-audit.md).

| Route | What it does |
| --- | --- |
| `POST /api/apps/{app}/entities/{entity}/records/{id}/comments` _(planned for M43)_ | Posts a comment on a record, see [record comments](#record-comments) |
| `POST /api/apps/{app}/entities/{entity}/records/{id}/comments/files` _(planned for M43)_ | Uploads a file for a record comment, see [files](#files) |
| `GET /api/apps/{app}/entities/{entity}/records/{id}/timeline?kinds=&page=&pageSize=` _(planned for M43)_ | Lists the comments, changes, process steps and emails of a record, see [the timeline](#the-timeline) |
| `POST /api/apps/{app}/entities/{module}/{entity}/records/{id}/comments` _(planned for M43)_ | Posts a comment on a record of a module entity, see [routes](record-api.md#routes) |
| `POST /api/apps/{app}/entities/{module}/{entity}/records/{id}/comments/files` _(planned for M43)_ | Uploads a file for a comment on a record of a module entity, see [files](#files) |
| `GET /api/apps/{app}/entities/{module}/{entity}/records/{id}/timeline?kinds=&page=&pageSize=` _(planned for M43)_ | Lists the timeline of a record of a module entity, see [the timeline](#the-timeline) |
| `GET /api/apps/{app}/tasks/{id}/comments?page=&pageSize=` _(planned for M43)_ | Lists the comments of a task, see [task comments](#task-comments) |
| `POST /api/apps/{app}/tasks/{id}/comments` _(planned for M43)_ | Posts a comment on a task, see [task comments](#task-comments) |
| `POST /api/apps/{app}/tasks/{id}/comments/files` _(planned for M43)_ | Uploads a file for a task comment, see [files](#files) |
| `PATCH /api/apps/{app}/comments/{commentId}` _(planned for M43)_ | Edits the author's own comment, see [editing and deleting](#editing-and-deleting) |
| `DELETE /api/apps/{app}/comments/{commentId}` _(planned for M43)_ | Deletes the author's own comment, see [editing and deleting](#editing-and-deleting) |
| `GET /api/apps/{app}/comments/{commentId}/versions` _(planned for M43)_ | Lists the versions of a comment, see [editing and deleting](#editing-and-deleting) |

## Record comments

_(planned for M43)_

A person comments on a record they may read. A comment is either internal, seen by people with the internal comments grant, or shared, seen by people with the record's read access.

- **Body.** A `POST` takes `text`, `visibility`, `mentions` and `files`. For example:

  ```json
  {
    "text": "Please check the supplier's address again.",
    "visibility": "internal",
    "mentions": ["0192f4a9-6c1e-7d2b-8a3f-5e6d7c8b9a0b"],
    "files": []
  }
  ```

- **Limits.** `text` has 1 to 10,000 characters. `mentions` lists at most 20 user ids, and `files` at most 10 file ids from [files](#files).
- **Visibility.** `visibility` is `internal` or `shared`, and it is required. A missing value is a `400`, so a comment never reaches people by default.
- **Grants.** Each entity has two new actions, which go in [roles and policies](roles-and-policies.md#policies):
  - `comment` posts a shared comment on a record that the person may read, and that matches the grant's filter.
  - `internalComments` reads and posts internal comments. It alone is enough to do both.
- **Answers.** The checks run in the order of the [record API's errors](record-api.md#errors). `401` is for no session, and `403` for a missing [CSRF header](record-api.md#create-update-and-delete) or a missing grant for the visibility. `404` is for a record that does not exist or cannot be read, and `400` for a bad body. A successful post answers `201` with the comment.
- **Purge.** When a record is purged, its comments, their versions and their files go with it.

## Task comments

_(planned for M43)_

Comments on a task are for the people who may act on it. Their discussion stays with the task.

- **Who.** The people who may act on a task, as [who may act](task-api.md#who-may-act) says, read and post its comments while the task is open. Anyone else gets the answers that [task API errors](task-api.md#task-api-errors) give.
- **No visibility.** A task comment has no `visibility`, and no internal or shared choice. It never shows on the record's timeline, because a person who reads the record may have no right to the discussion of a task about it.
- **Body.** A `POST` takes `text`, `mentions` and `files`, with the same limits as a [record comment](#record-comments).

## Mentions

_(planned for M43)_

A mention names a person in a comment, and tells them about it. Only people who may read the comment are told.

- **Who is told.** A mention tells a person only when they may read the comment. Axis checks their roles the way [sharing role tasks](task-api.md#sharing-role-tasks) reads the holders of a role, from their [user profile](notifications.md#user-profiles) and the [roles given to them](user-administration.md#roles).
- **Who is not told.** A mention of a person who may not read the comment stays as text in the comment, and tells that person nothing.
- **The notice.** The notice has the source `mention`, and follows a person's [notification preferences](notifications.md#notification-preferences). It names the author and opens the record or the task page. It never carries the text of the comment, so a forwarded email or a later loss of access reveals nothing.
- **Edits.** An edit tells only the people it newly mentions.

## Files

_(planned for M43)_

A comment can carry files, which use the upload and download of [file fields](file-fields.md). The checks and the storage are the same.

- **Upload.** A comment's upload route takes one `file` part, as a file field's route does. It runs the checks of [upload](file-fields.md#upload), with `Files:MaxSize` and the media type table. The file is stored as `pending`, for the uploader and the record or task it is for, until a post attaches it.
- **Clean-up.** A `pending` file that no post attaches is purged after 24 hours, as [storage and clean-up](file-fields.md#storage-and-clean-up) says.
- **Download.** A file of a comment is downloaded from the file route, `GET /api/apps/{app}/files/{id}`. A person who may not read the comment gets the same `404` as for any file they may not read.

## Editing and deleting

_(planned for M43)_

A person may change a comment for a short time after posting it. Every version stays, so a change is never lost.

- **Who.** Only the author may edit or delete a comment. The author must do it within `Comments:EditWindow`, a [host setting](host-settings.md#comment-and-live-page-settings), counted from the post. The author must also still hold the grant for the comment's visibility. Otherwise the answer is a `403`.
- **Edit.** `PATCH` takes `text`, `mentions`, `files` and `version`, where `version` is the version the person edited. A stale `version` is a `409`, as [the record API](record-api.md#concurrency) answers a stale write.
- **Visibility.** An edit never changes `visibility`. Making an internal comment shared would publish it, so the person posts a new comment instead.
- **Delete.** `DELETE` answers `204` and hides the comment from the timeline. Its versions stay readable, see below.
- **Versions.** `GET .../versions` lists every version of a comment, newest first, to anyone who may read the comment. A deleted comment keeps its versions.

## Storage

_(planned for M43)_

A comment lives in the tenant database, with its earlier versions and its files.

- **Comments.** `axis.comments` holds each comment. `axis.comment_versions` holds each earlier version, and the last version that a delete leaves.
- **Files.** A comment's files are rows of `axis.files`, whose owner is the comment, as [storage and clean-up](file-fields.md#storage-and-clean-up) describes.

## The timeline

_(planned for M43)_

The timeline shows one record's comments, changes, process steps and emails in one list. It replaces the history panel of the [form widget](form-widget.md#history) with this list.

- **Access.** The timeline needs the same access, `404` rules and paging as the [history route](record-audit.md#the-history-route).
- **Kinds.** `kinds` is a comma list of `comment`, `change`, `step` and `email`. All four show when `kinds` is left out. An unknown kind is a `400` keyed `kinds`.
- **Comments.** A `comment` entry is a record comment that the person may read. Internal comments need the `internalComments` grant, and deleted comments are left out.
- **Changes and steps.** A `change` entry is an audit record of a write, and a `step` entry is a process step, as the history route lists them.
- **Emails.** An `email` entry shows the time, the sending step and the send state. It never shows the address, the subject or the body. A subject can hold values of fields that the reader may not read.
- **Task comments.** Task comments never show on the timeline, see [task comments](#task-comments).
