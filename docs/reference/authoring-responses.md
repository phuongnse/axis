# Responses in the authoring API

The answers that the routes of the [authoring API](authoring.md) share.

Every error is problem details (`application/problem+json`). The titles are fixed and never contain
text from the request.

Every route answers `401` with no session and `403` with no author role or with Studio off, and every
`POST`, `PUT`, `PATCH` and `DELETE` also answers `403` with no CSRF header. `publish`, `activate` and
the settings `PUT` also answer `403` with no publisher role, titled "You do not hold the publisher role.", and creating
an application answers `403` with no `Studio:ApplicationsPath`. Listing the applications and the
templates answers no `404` and no `409`. The other routes differ:

| Route                                       | `404`                                                                                       | `409`                                                                                                    |
| ------------------------------------------- | ------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Create application                          | Unknown template                                                                            | The name or the folder already exists                                                                    |
| List resources                              | Unknown application                                                                         | Not used                                                                                                 |
| Read                                        | Unknown application or resource                                                             | Not used                                                                                                 |
| Create                                      | Unknown application                                                                         | The `id` or the target file already exists                                                               |
| Patch                                       | Unknown application or resource                                                             | Stale `If-Match` version                                                                                 |
| Delete                                      | Unknown application or resource                                                             | Stale `If-Match` version                                                                                 |
| Changes                                     | Unknown application, or a patch that names an unknown resource                              | Any stale version, a patched resource that is gone, or a create whose `id` or target file already exists |
| Package files list                          | Unknown application                                                                         | Not used                                                                                                 |
| Package file read                           | Unknown application, or a path that names no package file                                   | Not used                                                                                                 |
| Package file save                           | Unknown application                                                                         | A stale `If-Match` version, or `If-None-Match: *` over a file that exists                                |
| Package file delete                         | Unknown application, or a path that names no package file                                   | A stale `If-Match` version                                                                               |
| Module resource                             | Unknown application, module or resource                                                     | Not used                                                                                                 |
| List revisions, Revision diff               | Unknown application, or for a diff a revision                                               | Not used                                                                                                 |
| Undo                                        | Unknown application or revision                                                             | The start of history, or a later revision changed the same files                                         |
| Module resource save, Module resource reset | Unknown application, module or resource, or for a reset a list or key that names no element | A stale `If-Match` version, `If-None-Match` over an existing layer, or a reset with no layer             |
| Resource references                         | Unknown application or resource                                                             | Not used                                                                                                 |
| Text key references                         | Unknown application, or a key that no text resource defines and no label uses               | Not used                                                                                                 |
| Diagnostics                                 | Unknown application                                                                         | Not used                                                                                                 |
| Names                                       | Unknown application                                                                         | Not used                                                                                                 |
| Policy matrix                               | Unknown application                                                                         | Not used                                                                                                 |
| Text write                                  | Unknown application                                                                         | A text resource whose `texts` is not an object                                                           |
| Expression check                            | Unknown application, or an unknown `resourceId`                                             | Not used                                                                                                 |
| Draft page metadata                         | Unknown application, or a resource that is neither a page nor a form                        | Not used                                                                                                 |
| Module page metadata                        | Unknown application, module or resource, or a resource that is not a page                   | Not used                                                                                                 |
| Module form metadata                        | Unknown application, module or resource, or a resource that is not a form                   | Not used                                                                                                 |
| Settings read                               | Unknown application                                                                         | Not used                                                                                                 |
| Settings change                             | Unknown application                                                                         | The manifest has no UUID `id`                                                                            |
| Validate                                    | Unknown application                                                                         | The manifest has no UUID `id`                                                                            |
| Publish                                     | Unknown application                                                                         | The manifest has no UUID `id`                                                                            |
| Preview                                     | Unknown application                                                                         | This tenant has no served preview tenant, or the manifest has no UUID `id`                               |
| Activate                                    | Unknown application, or a release not in the tenant's history                               | Not used                                                                                                 |
| Request status                              | Unknown application or request                                                              | Not used                                                                                                 |
| Release history                             | Unknown application                                                                         | Not used                                                                                                 |

Some answers have titles of their own:

| Answer                                                | Title                                                   |
| ----------------------------------------------------- | ------------------------------------------------------- |
| `409`, a manifest with no UUID `id`                   | "The application manifest has no valid id."             |
| `409`, a tenant with no served preview tenant         | "This tenant has no preview tenant." It names no tenant |
| `400`, a bad `publish` body                           | "The body is not a valid publish request."              |
| `400`, a bad settings body                            | "The body must be an object with a boolean autoAccept." |
| `409`, a text resource whose `texts` is not an object | "A text resource has no texts object."                  |
| `403`, no `Studio:ApplicationsPath` on a create       | "Studio cannot create applications here."               |
| `403`, no developer role on a package file write      | "You do not hold the developer role."                   |
| `403`, the tenant's application packages are off      | "Application packages are off in this tenant."          |
| `409`, a stale package file version                   | "The package file has changed since this version, or it exists already. Read it again." |

`400` is a bad body, `415` a body of the wrong content type, `428` a missing `If-Match` and `503` a
write lock that cannot be taken, as [Order of checks](authoring.md#order-of-checks) says. A successful `validate`,
`publish`, `preview`, `test` or `activate` answers `202`, see [Publishing](authoring-publishing.md#publishing).

