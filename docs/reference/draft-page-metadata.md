# Draft page metadata

Detailed reference for the routes that answer the page metadata Studio draws on its page canvas: the
draft page and form route, and the module page route. The rest of the authoring API is in
[authoring.md](authoring.md). The conventions of [architecture.md](../architecture.md) apply, and Dn
refers to [decisions.md](../decisions.md).

`POST .../resources/{id}/page-metadata` answers the metadata
that an unsaved page would have, so the page designer's canvas can draw the
draft ([D65](../decisions/d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md)).
It also answers an unsaved form, see **A draft form**.
The `{id}` names the saved page.

- **The body.** The full draft page, as the file would hold it. Its `id` and
  `kind` must equal those of the saved file, or the answer is `400`.
- **The compile.** The server compiles the saved folder in memory with the
  draft in place of the saved file. It takes no lock, touches no database and
  reads no records, so nothing is written.
- **The answer.** `{ page, diagnostics }`. `page` has the shape of
  [`GET /api/sites/{path}/pages/{page}`](frontend.md), with every widget kept.
  Policies play no part, as in every authoring route. `access` lists every
  action the target accepts and every field a user may write. The record paths
  are as the compiled page has them, but the canvas answers the widgets' data
  calls from placeholder data.
- **A draft form.** A form body is the full draft form with the saved `id` and
  `kind`
  ([D66](../decisions/d66-a-visual-form-designer-for-form-resources.md)). The
  answer is `{ widget, diagnostics }`. `widget` has the shape of a `form`
  widget that names this form, see **Form metadata** in
  [form-widget.md](form-widget.md): `entity` with all its fields, `form`,
  `actions: []`, and an `access` that lists every action and writable field.
  Each section, field and step also carries `path`, such as `/sections/0`,
  `/sections/0/fields/1` and `/steps/0`. It has no expressions, and its
  diagnostics are those of the form. Any error in the compile, in the form
  or in another file, answers `widget: null`.
- **A module page.** `GET /api/studio/apps/{application}/modules/{module}/resources/{id}/page-metadata`
  answers `{ page, diagnostics }` for a page of the module version that the
  application pins, so Studio can draw it read-only. The `{id}` is the id
  written in the module's file, as in the module resource route, not the id
  derived for the application. `page` has the shape above and each widget
  carries `path`. The route compiles the application's folder as it is, so
  it answers `page: null` when that compile has any error, and Studio then
  shows its "cannot draw" alert. `diagnostics` are those of the module's file.
  A caller with no session gets `401` and one with no author role gets `403`.
  An unknown application, a module that the application does not pin, an
  unknown resource and a resource that is not a page get `404`.
- **Widget paths.** Each widget also carries `path`, its JSON Pointer in the
  draft, such as `/widgets/0/columns/1/widgets/0`.
- **Diagnostics.** Only those whose `resourceId` is the page, shaped as in
  [Diagnostics](authoring.md#diagnostics).
- **A draft with errors.** It still answers `200`. A widget that does not
  compile is `{ type, path, placeholder: true }`, and the rest of the page is
  drawn as usual. Only an error turns a widget into a placeholder: a warning
  leaves it drawn. A placeholder's `type` is the draft's `type`, or null when
  that is not a string. A container whose widgets all fail is one placeholder.
  A column or tab whose widgets all fail stays in its container, holding
  placeholders. A page that cannot be read at all answers `page: null` with
  its diagnostics. That is the case when a file other than the page has an
  error, when the page has an error outside its widgets (its name, title or
  parameters, or an empty `widgets` list) and when no top-level widget compiles.
  The `diagnostics` are those of the draft as sent, so they match what a save
  would show.
- **A denied caller.** A caller with no session gets `401`. A caller with no
  author role, no `Studio:Folders` or no CSRF header gets `403`, before any
  lookup, so the answer says nothing about which applications or resources
  exist. An unknown application, an unknown resource or a resource that is
  neither a page nor a form gets `404`.
- **Speed.** It answers within 500 ms on a typical application. `StudioPageMetadataPerformanceTests` fails when the best of three attempts of any timed post takes 500 ms or more.
