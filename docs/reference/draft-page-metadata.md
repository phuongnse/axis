# Draft page metadata

The page metadata routes answer what Studio draws on the canvas of its
[page designer](studio.md#page-designer) and [form designer](form-designer.md): the metadata that a page or a
form would have, compiled from a draft that is not saved yet, or from a module's page. They are part
of the [authoring API](authoring.md), and nothing they do is written.

| Route                                                                               | Answers                                                        |
| ----------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| `POST /api/studio/apps/{application}/resources/{id}/page-metadata`                  | A [draft page](#a-draft-page) or a [draft form](#a-draft-form) |
| `GET /api/studio/apps/{application}/modules/{module}/resources/{id}/page-metadata`  | A [module page](#a-module-page) as it is saved                 |
| `POST /api/studio/apps/{application}/modules/{module}/resources/{id}/page-metadata` | A [module form draft](#a-module-form-draft)                    |

## A draft page

`POST .../resources/{id}/page-metadata` answers the metadata that an unsaved page would have, so the
canvas can draw the draft. The `{id}` names the saved page.

- **The body.** The full draft page, as the file would hold it. Its `id` and `kind` must equal those
  of the saved file, or the answer is `400`.
- **The compile.** The server compiles the saved folder in memory with the draft in place of the saved
  file. It takes no lock, touches no database and reads no records, so nothing is written.
- **The answer.** `{ page, diagnostics }`. `page` has the shape of
  [`GET /api/sites/{path}/pages/{page}`](frontend.md), with every widget kept. Policies play no part,
  as in every authoring route, so `access` lists every action the target accepts and every field a user
  may write. The record paths are as the compiled page has them, but the canvas answers the widgets'
  data calls from placeholder data.
- **Widget paths.** Each widget also carries `path`, its JSON Pointer in the draft, such as
  `/widgets/0/columns/1/widgets/0`.
- **Diagnostics.** Only those whose `resourceId` is the page, shaped as in
  [diagnostics](authoring.md#diagnostics). They are those of the draft as sent, so they match what a
  save would show.

## A draft with errors

A draft with errors still answers `200`, so the canvas can draw what compiles and show the rest as
placeholders.

- **Placeholders.** A widget that does not compile is `{ type, path, placeholder: true }`, and the rest
  of the page is drawn as usual. Only an error turns a widget into a placeholder: a warning leaves it
  drawn. A placeholder's `type` is the draft's `type`, or null when that is not a string.
- **Containers.** A container whose widgets all fail is one placeholder. A column or tab whose widgets
  all fail stays in its container, holding placeholders.
- **No page.** A page that cannot be read at all answers `page: null` with its diagnostics. That is the
  case when a file other than the page has an error, when the page has an error outside its widgets
  (its name, title or parameters, or an empty `widgets` list), and when no top-level widget compiles.

## A draft form

The same route answers an unsaved form, for the form designer. The body is the full draft form with
the saved `id` and `kind`, and the answer is `{ widget, diagnostics }`.

- **The widget.** `widget` has the shape of a `form` widget that names this form, see form metadata in
  [the form widget](form-widget.md): `entity` with all its fields, `form`, `actions: []`, and an
  `access` that lists every action and writable field. It has no expressions.
- **Paths.** Each section, field and step also carries `path`, such as `/sections/0`,
  `/sections/0/fields/1` and `/steps/0`.
- **Errors.** The diagnostics are those of the form. Any error in the compile, in the form or in
  another file, answers `widget: null`.

## A module page

`GET .../modules/{module}/resources/{id}/page-metadata` answers `{ page, diagnostics }` for a page of
the module version that the application pins, with the application's
[customization layer](customization-layers.md#in-studio) applied, so Studio can draw the saved module
page on the canvas.

- **Saved only.** No route compiles a draft of a module page, so the canvas does not show its unsaved
  edits. It shows a change after Save or a reset.
- **The id.** The `{id}` is the id written in the module's file, as in the
  [module resource route](authoring.md#module-resources), not the id derived for the application.
- **The answer.** `page` has the shape of [a draft page](#a-draft-page), and each widget carries
  `path`. The route compiles the application's folder as it is, so it answers `page: null` when that
  compile has any error, and Studio then shows its "cannot draw" alert. `diagnostics` are those of the
  module's file.

## A module form draft

`POST .../modules/{module}/resources/{id}/page-metadata` answers `{ widget, diagnostics }` for an
unsaved form of the module version that the application pins, so the form designer can draw it.

- **The id and the body.** The `{id}` is the id written in the module's file, as in the module resource
  route. The body is the whole edited form with the module file's `id` and `kind`.
- **The layer in memory.** The server compares the draft with the module's file as the
  [module resource save](module-resource-saves.md) does and builds the application's layer in memory,
  in place of any saved layer. A draft equal to the module's file has no layer. It compiles the folder
  with that layer, so nothing is written, locked or read from the database.
- **The answer.** `widget` has the shape of [a draft form](#a-draft-form), with the form named
  `<module>/<name>`, and each section, field and step carries `path` in the draft. It is null when the
  compile has any error. `diagnostics` are those of the module's form file and of the layer file.

## Order of checks

The routes check a caller first, so that a denied caller learns nothing about which applications or
resources exist. A caller with no session gets `401`. A caller with no author role, with Studio off or,
on a `POST`, with no CSRF header gets `403`, before any lookup. Then each route checks in this order,
and the first failure is the response:

| Route              | Checks after the caller                                                                                                                                                                                                                                                                                            |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Draft page or form | `404` for an unknown application, an unknown resource, or a resource that is neither a page nor a form; `415` for a body that is not `application/json`; `400` for a body that is not a JSON object or whose `id` or `kind` differs from the saved file                                                            |
| Module page        | `404` for an unknown application, a module that the application does not pin, an unknown resource, or a resource that is not a page                                                                                                                                                                                |
| Module form draft  | `404` for an unknown application, a module that the application does not pin, an unknown resource, or a resource that is not a form; `400` for a body that is not an object, an `id` or `kind` that differs, an edit that cannot be saved as a layer, or a form whose name another resource of the module also has |

None of these routes takes a lock, so none answers `503`.

## Speed

A draft page answers within 500 ms on a typical application.
`StudioPageMetadataPerformanceTests` fails when the best of three attempts of any timed post
takes 500 ms or more.
