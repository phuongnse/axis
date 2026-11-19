# Studio

This page describes what Studio's editors, designers and buttons do: creating applications and
resources, editing a resource, its references and checks, publishing, releases and tests, and the
editor of each kind. How Studio is laid out, with its sidebar, band, Overview and Releases, is in
[Studio workspace](studio-workspace.md), and the form that edits one resource is in
[Studio resource form](studio-resource-form.md). The seed grid, the endpoint rows and the form steps are in
[Studio form editors](studio-form-editors.md). The [entity designer](entity-designer.md), the
[form designer](form-designer.md) and the [process designer](process-designer.md) have pages of their
own. Studio calls the [authoring API](authoring.md), its settings are in
[Studio settings](host-settings.md#studio-settings), and its texts are platform texts under
`shell.studio.*`, in `en` and `vi`.

## Route and address

Studio is a platform route, and its address keeps what is open, so that a reload or a shared link
opens the same view.

- **The route.** `/studio` lies outside every site. The header lists Studio in the app switcher only
  when `GET /api/me` answers `studio: true`. A person without the author role who opens the route gets
  a `403` from the API and sees a forbidden page, and a signed-out person is sent to sign in.
- **A resource.** The address holds the open application and resource, as
  `/studio?app=PurchaseRequests&resource=<id>`. Picking a resource from a list opens it as
  `&resource=<id>`, without the kind. The id of a module resource opens it the same way when the id is
  not one of the folder's own resources.
- **A path.** A Used by entry or a diagnostic that opens a resource at a path adds `&path=<path>`.
- **A view.** `&kind=<kind>` shows that kind's list, `&view=releases` the Releases entry,
  `&view=release&release=<number>` a [release page](studio-workspace.md#release-page) under it,
  `&view=policies` the [policy matrix](#policy-matrix), `&view=publish` the [publish page](#publishing)
  and `&view=requests&request=<id>` one [AI request's page](ai-authoring-in-studio.md#a-requests-page).
- **The AI requests table** _(planned for M35)_. `&view=requests` shows the [AI requests](ai-authoring-in-studio.md#the-request-panel) table.
- **A field** _(planned)_. `&field=<name>` opens a field's
  [drawer](entity-designer.md#field-drawer) on an entity's page.
- **Back is the browser's.** Studio has no Back or Next of its own and no breadcrumb.
  Every view has its own address and each move is a history entry, except that a search replaces its
  entry, so the browser's Back and Forward step through it. A second Back would be one concern with two
  mechanisms. Where the author is comes from the sidebar's marked entry, which also leads up to the list, and the
  [page heading](studio-workspace.md#page-heading).
- **Scroll position.** Going Back returns the author to where they were on a long page, see [routing](frontend.md#routing).

## New application

New application… creates an application folder, blank or from a template, as
[creating an application](authoring-applications.md#creating-an-application) describes. Where its button shows is
in [applications](studio-workspace.md#applications) and [workspace](studio-workspace.md#workspace).

The dialog asks for a name. It offers Blank or a template from `GET /api/studio/templates`, with the
title and description in the UI locale and `en` as the fallback, and a built-in template first, with a
"Built in" mark. Name is marked required and both fields have help. Create stays on, and a click
[checks the fields](actions-and-controls.md#fields), including the name against the API's rule, and
sends nothing while one is wrong. A `409` or a `400` shows under the name. A success adds the application to the list and to the application row's menu, opens
it, and loads its resources and diagnostics.

- **One field to start from**. `StartFromField` draws the choice, as the
  [sign-up page](sign-up.md#start-from) does: Blank first, then the templates. When the list does not
  load, its warning status says under the choices that Blank is still there.

## New resource

New resource… creates a resource from its kind's starter, so a new resource opens with as few
diagnostics as its kind allows. Where the dialogs open, and which kinds they offer, is in
[new resource](studio-workspace.md#new-resource).

- **The dialog.** The kind picker names each kind of the sidebar's groups by its singular name, and the
  author can type to narrow it. The dialog then asks for a name, or for a locale when the kind is
  `text`. A `409`, when the id or the file exists, or a `400`, when the name or the locale is refused,
  shows under the field and keeps the dialog open.
- **Its fields.** Kind and Name, or Locale, are marked required and have help. Create
  stays on, and a click [checks the fields](actions-and-controls.md#fields).
- **The starter.** Studio sends the kind's starter, the smallest content that passes the kind's
  schema except at the references the author must still choose, with an id from
  `crypto.randomUUID()`. A reference the author must still choose is an empty string,
  so it shows as a diagnostic. A new `site` gets the kebab-case name as its `path`, by the rule of file
  names, because a path is a value and not a reference. A new `identityProvider` starts with type
  `accounts`, in `identity-providers/<name>.json`. A new trigger starts with an empty `event`, and
  Studio removes the key only when the author picks an event and then clears it.
- **Tested starters.** The starters live in `web/src/platform/studioStarters.json`, each with the
  pointers of its empty references. A Vitest test checks each against its bundled schema, so a schema
  with no starter fails, the manifest, the contract and the layer aside, and every error must be at a
  listed pointer. A server test sends each starter through the create route and checks that its
  diagnostics are at the listed pointers only.
- **A new text resource** copies every key and text of the default locale of the first site in file
  order, or of `en` when there is no site, so every label still resolves while the author translates.
  Its name is `Texts` and the locale, such as `TextsVi` for `vi` and `TextsPtBr` for `pt-BR`.
- **After a create,** the resource joins its list, the Problems panel shows the diagnostics that the
  create answered, and the editor opens the resource.

## Kind lists and module resources

A kind's sidebar entry opens its list, see [workspace](studio-workspace.md#workspace), and picking a
resource opens it alone, under its [name](studio-workspace.md#names), with its editor below.

- **One list for every kind**. A kind's list is the
  [standard table](actions-and-controls.md#tables), with the same columns for every kind: Name,
  Description, Status, Last modified by and Last modified at. Every column shows unless Columns hides it, and a
  cell with no value shows the dash. Name is a link. A list does not show the inside of a resource, such as its
  fields or references, a file name, or a count that leads nowhere.
- **The server builds the rows**. The [kind list route](studio-resource-form.md#kind-list) answers one page with
  its names in the UI locale. A failed load shows one error above the table.
- **A module's resources**. The resources of the modules that the application uses are in the same list. Each is
  named by its label, else without the `<module>/` prefix, and has a neutral tag of its module's label, else the
  module's id. There is no Module column. A kind with no label, such as a policy, keeps its name. A text resource
  shows its language, such as Vietnamese. A module's resource has a status, as a new module version or a layer changes it.
- **Status**. New, Changed or Published, as the [resource activity](studio-resource-form.md#resource-activity) reads
  it, and none for a scenario. The whole list loads at once, so the cell never says it is loading.
- **Its controls**. Search, Filter, Columns, Refresh, a pager that says "1–20 of 45" with page sizes of 10, 20, 50
  and 100, and a sort on Name, Status, Last modified by and Last modified at, all kept in the address and done on the
  server. With no sort the rows are in name order. Description does not sort, because it is long text. Search looks in
  the name, the description and the module, and Status is a filter, not a search term. The module, a tag and not a
  column, is not a filter.

A module resource opens in its kind's editor with the application's layer applied, as
[customization layers in Studio](customization-layers.md#in-studio) describes. A module data source, rule, trigger,
sequence, event, seed or entity draws the same [parts](studio-resource-form.md#sections) as a local resource's Form tab, with
its Customized tag and Reset all above them. A module contract opens in its contract editor, with Relax and Undo relax,
as [module contracts in Studio](module-contracts.md#in-studio) describes, and draws its lifecycle as a state diagram
that marks the initial and deletable states and labels each arrow with its transition's name and whether it has a
guard. A module scenario opens read-only, as formatted JSON under a notice in the alert role.

## Editor

The editor shows one resource in two views of the same draft, a form and the JSON view, and saves only
what changed.

- **The generic form** is built from the JSON Schema of the resource's kind. The SPA bundles the
  schemas of `src/Axis.Configuration/Schemas`, the files the server embeds, and loads the one it needs
  when a resource opens. The form shows text, number, boolean and enum values, nested objects, and
  arrays with add and remove. An array that the author adds an entry to and then empties stays in the
  file as an empty array, as a data source's `groupBy` of a total does.
- **What the form leaves to the JSON view.** A part that holds `oneOf` or `anyOf`, an `allOf` with an
  item that holds more than `if`, `then` and `else`, an object with no `properties`, such as a map, and
  a part with no type point to the JSON view. A `oneOf` whose value is text, or empty, shows its text
  alternative, such as a `groupBy` name, while one with an object value, such as a `groupBy` entry with
  a `bucket`, points to the JSON view. An `allOf` of only `if`, `then` and `else` shows its properties
  and not the conditions. A map whose keys carry a reference mark shows as rows, see
  [reference pickers](#reference-pickers). A new kind or property that the form cannot show goes to the
  JSON view until an editor covers it.
- **Typed editors.** A kind with a typed editor or a designer shows it in place of the generic form, see
  [editors by kind](#editors-by-kind), and keeps the JSON view as the second view of the same draft. A
  typed editor saves through the same JSON Patch and never drops a value it does not show, so unknown
  keys stay as they are.
- **The form's layout.** The generic form and the typed editors that use [Studio resource form](studio-resource-form.md)
  share its sections, field names, labels, lists and save bar.
- **Every form's layout.** The entity designer follows it too, see
  [sections](entity-designer.md#sections).
- **A description.** Every kind that may have a
  [`description`](resource-files.md#resource-file-shape) shows it in the form, and each editor of its own
  shows it after the name and the label.
- **The JSON view** shows the draft as text, and a valid edit there updates the form. Invalid JSON
  shows an error, and a click on Save opens the JSON view at the first problem and sends nothing. Its
  look and its actions are in [JSON view](studio-resource-form.md#json-view).
- **Save** sends only the changes, as a JSON Patch with the version the editor loaded in `If-Match`,
  and stays off while nothing changed. An array whose length changed is replaced whole, and an array of
  the same length is compared item by item. Clearing an optional value removes its key. After a save
  the editor carries on from the new version.
- **A stale version** is a `409`. The editor keeps the author's draft on screen and offers Reload,
  which reads the resource again and drops the draft.
- **Leave without saving.** One guard around the shell asks "Leave without saving?", with
  Stay and Leave, before a link in the shell is followed and before a move that the workspace starts by
  itself, such as opening a reference or a problem. It guards Back and Forward too, through the
  [data router](frontend.md#routing). An editor counts itself in through `useUnsavedChanges`, which
  `SaveBar` calls, so every editor with a save bar is guarded, and the module editors and contract pages
  join it. A reload or closing the tab asks the browser's own question. A link opened in a new tab is not
  asked about, and neither is the move that Studio makes after a delete, because the draft went with the resource.
  A move that only changes the `search`, `filter`, `sort`, `page` or `pageSize` of a
  [table](actions-and-controls.md#tables), under the table's prefix too, such as `fields.filter`, is not asked about
  either, because the editor stays on screen.

## References

Each resource but the application has a Used by tab, from the [references routes](authoring.md#references),
so an author sees what a change or a delete touches. How it shows is in [Used by](studio-resource-form.md#used-by).

- **Opening an entry.** A Used by entry opens the other resource at the path of the reference: the path
  goes into the address, and the editor scrolls to the form field for that path, with nothing
  highlighted. A path that the form cannot show, such as one inside a map, opens the JSON view. A path
  inside an item of a [list of objects](studio-resource-form.md#lists-of-objects) lands on the list that
  holds the item, and the author opens the row to reach the field.
- **Counts can be stale.** The Used by list loads when its tab opens, and the `usedBy` counts load with
  the resources. A save does not reload either, so a count or the list can be out of date until the page
  reloads. The names, and so the counts that labels show, load again after a save and after a text write.
- **A list, not a graph.** Used by stays a list. The process graph on a process's Design tab is the only
  drawn graph.

## Delete

Delete removes a resource's file, and asks first, because other resources may use it.

- **In a "…" menu.** Delete… is in a "…" menu at the right of the resource's
  [heading](studio-workspace.md#page-heading), so no tab shows it apart from the others.
- **It asks.** Delete always opens a confirm dialog, which warns and names the count when the
  resource's `usedBy` count is above 0. The author can still confirm. The application manifest has no Delete.
- **A link to Used by.** The dialog for a resource with uses also offers "See what
  uses it", which opens the Used by tab.
- **It checks the version.** The delete sends the loaded version in `If-Match`. A stale version shows
  the conflict message, and nothing is deleted.
- **After a delete,** the resource leaves its list, and the Problems panel shows the diagnostics that
  the delete answered, which report what broke.

## Problems panel

The [problems panel](studio-workspace.md#problems-panel) lists the folder's diagnostics. This section
says where they come from and what opening one does.

- **Loading.** The panel loads `GET /api/studio/apps/{application}/diagnostics` when an application
  opens. A save, a create, a delete and a text write replace the list with the diagnostics they
  answered, and a request that ends replaces it as [publishing](#publishing) says.
- **Opening one.** A diagnostic that names a resource opens it at the diagnostic's `path`, as a Used by
  entry does, or at its top when the path is empty. A diagnostic that names no resource opens nothing.
- **Never blocking.** A diagnostic never blocks a save.

## Publishing

Validate, Preview and Publish… on the [band](studio-workspace.md#band) send requests through the
[publishing routes](authoring.md#publishing), and a status line under the band follows each request to
its end.

- **Validate** sends a validate request.
- **Publish…** shows only when `GET /api/me` answers `studioPublisher: true`, and not when it answers
  `false` or fails. It opens the publish page and is hidden there, where the page has its own Publish.
- **The publish page.** Publish… opens `/studio?app=<application>&view=publish`, which marks no sidebar entry and
  shows the Overview without `studioPublisher`. Its heading is "Publish this application", with the line "Everything
  below goes live as a new release, in place of release `<n>`.", "Nothing is live yet. Everything below goes live as
  the first release." or, with nothing to publish, "Everything is live, so there is nothing to publish." Under it,
  "The release" holds Label, up to 100 characters, with the help "A short name for the release, such as Spring
  update.", and Note, up to 2,000, with the help "What changed and why, for whoever reads the release list."
- **The changed files.** After "The release", the page lists the files that differ from the [live release](authoring-revisions.md#live-diff), or none with no live release or no revision history.
- **The action bar** says "Goes live as release `<n>`" and holds Cancel and Publish. `<n>` is the number of an
  earlier release with the folder's content hash, else the highest number plus one, or 1, see
  [release history](authoring.md#release-history). Publish sends the request with only the filled
  values. It and Cancel replace the page's history entry with the page Publish… was opened from, at its top, or
  the Overview after a link, and the status line follows the request there. Publish is off while a request runs,
  the reconnect shows, the history or the changed files load or nothing is left to publish. A failed history drops `<n>` and the line, and a folder that is all live drops `<n>`.
- **Preview** shows only when `GET /api/me` answers a `studioPreviewHost` that is not null, and needs
  no `studioPublisher`. It sends `POST .../preview` at once, with no dialog. A succeeded preview shows
  Previewed and a link to each of the application's sites on the preview host, built from the page's
  protocol and port, the `studioPreviewHost` and the site's `path`, which Studio reads from the folder's
  `site` resources when the preview succeeds. So the preview host must be served on the page's port and
  protocol, and a site renamed after the request was sent can give a wrong link. A failed preview shows
  Preview failed. A preview shows no release number and leaves the Releases list as it is, because its
  release belongs to the preview tenant's own history.
- **Preview's status line** _(planned for M35)_. Its status line ends with "The preview is ready. Open <site>" and a link
  for each site, as [Preview the draft](ai-authoring-in-studio.md#preview-the-draft) does.
- **The status line.** Studio checks `GET .../requests/{id}` every second, with no time limit, until
  the status is `succeeded` or `failed`, and shows queued or running meanwhile. It shows the outcome
  only once the publisher has finished the request.
- **The release number.** A succeeded publish or activation shows its release number, which Studio
  finds in `GET .../releases` by the request's `releaseId`. A failed publish shows the failure and no
  release number, even when the request names a release, so a failure never looks like a success. An
  activation shows its own texts, Activated and Activation failed, in the words of
  [D130](../decisions/d130-activate-a-release-replaces-roll-back.md).
- **Diagnostics.** When a request ends, the Problems panel shows its diagnostics in place of the loaded
  ones, for a failed request and for a succeeded one that holds warnings.
- **Contract scenarios.** A publish, validate or preview request that stored contract scenarios lists
  them under the status line, see [module contracts in Studio](module-contracts.md#in-studio). A failed
  row opens as a [test failure](#running-tests) does, and a skipped row shows its relaxed point and
  opens nothing.
- **While a request runs.** The buttons are off while a request runs and while the reconnect message
  shows. When the server does not answer during a request, Studio checks the request again after the
  reconnect.

## Releases

The Releases entry lists the releases from `GET .../release-list`, as
[releases](studio-workspace.md#releases) describes, and Activate… makes another release live.

- **A row's facts.** A release's label, note, time and publisher are those of its first activation,
  which is the publish. The list is paged on the server, and its view is kept in the address.
- **Activate….** Confirming its dialog sends `POST .../releases/{id}/activate`, see
  [publishing](authoring.md#publishing), and the status line follows the request as it follows a
  publish. A failed activation puts its diagnostics in the Problems panel, and the list still marks
  the previous release Live. Activate… is off while a request runs and while the reconnect message
  shows.
- **Loading.** The history loads when the application opens, and again after a request ends, a save, a
  text write, a delete or a reconnect, because the folder may then differ from the live release.

## Running tests

Studio runs the application's [test scenarios](test-scenarios.md) through the
[test route](authoring.md#test) and lists each scenario's result.

- **Two buttons.** Run tests, on the band, sends `POST .../test` with no body, so every scenario runs.
  Run, on an open `scenario` resource, sends `{ "scenarios": ["<name>"] }`, so only that scenario runs.
  Both run the saved files, not an unsaved draft, as Validate does.
- **Who sees them.** Both show only when `GET /api/me` answers `studioTest: true`, and not when it
  answers `false` or fails. They are off while a request runs and while the reconnect message shows.
- **The status line** shows queued, running and the outcome, as for Validate: Tests passed or Tests
  failed, and never a release number.
- **Test results.** _(planned for M35)_ Each result reads as [a scenario's result](ai-authoring-in-studio.md#scenario-results)
  does. Each scenario of the request's `scenarios` field shows a `StatusTag` that reads passed (`success`), failed (`danger`)
  or skipped (`muted`), its time and, for a failure, the failing step and its message. Steps count from 1, as the scenario
  editor's step cards do. The name is the only link, with its kind muted after it. A failure opens at the item's `path`, a pass at its top.
- **No empty pass.** While the request is queued or running, the list shows a note instead of rows, so
  an empty list never reads as a pass. A failed run with no results says that no scenario ran and
  points to the diagnostics: compile errors, a missing setting and an unknown scenario name fail a run
  this way.
- **Not kept.** The list is not kept across a reload, and the request's diagnostics go in the Problems
  panel.

## Reconnect

Under `scripts/dev.sh` a saved file restarts the server, so Studio waits for a server that is briefly
gone and then loads its data again.

- **The message.** Studio shows a reconnect message at the top of the page when a request gets no
  answer, or answers `502`, `503` or `504`. The Vite dev proxy answers `502` while the server is down.
- **Waiting.** Studio then calls `GET /api/me` with a back-off of 0.5, 1, 2 and then 4 seconds, with
  no limit, until the server answers with any other status. Then the resources, the diagnostics, the
  names, the open resource and its Used by list load again, and the message goes away.
- **The draft stays.** An unsaved draft stays on screen with the version it was based on, so when the
  file changed meanwhile the next save gets a `409`. Studio does not resend a save that got no answer,
  so the author clicks Save again.
- **Meanwhile.** Save and Delete are off while the message shows. A `401` from a reload shows the
  sign-in prompt, as elsewhere.

## Reference pickers

A property that the schema marks with [`x-axis-reference`](resource-files.md#schema-keywords) is chosen
from the names that the [names route](authoring.md#names) lists, in place of a text box, so an author
picks what exists.

- **The select** is searchable, ignoring letter case. A module process, page or policy shows as
  `<module id>/<name>`, so a site's navigation can pick a module page and a `startProcess` action a
  module process.
- **A choice shows its label**. `ReferencePicker` names each choice by its label, from
  `labelKey` in the [names](authoring.md#names), as the tables do. The name shows only instead of a missing
  label, and in `textMuted` after the label of options that share one. The search finds either, and the
  file keeps the name. Columns, parameters and operations have no label in their format, so they show
  their name. Every picker takes this, the slot pickers of the [resource form](studio-resource-form.md)
  among them.
- **Parts of a resource.** A field or step part lists the fields of the entity, or the steps of the
  process, that the draft names. A parameter or column part lists those of the data source that the
  draft names, and an operation part the operations of the connector it names. An outcome part lists the
  outcomes of the task steps that have the step name the draft gives, in every process, without repeats.
- **Unknown values.** A value that is not one of the names stays in the select as the first option,
  tagged unknown, so nothing is lost. The select takes only the listed names, so a name that is not
  listed is set in the JSON view.
- **Loading.** Until the names load, the property is a text box. The names load with the application,
  and again after a save, a text write, a delete and a reconnect.
- **Maps with marked keys.** A map whose keys carry a mark, such as a trigger's `parameters`, shows one
  row for each entry, in the order of the file: a key select that offers the names no other row uses, a
  value box and Remove. Add appends the first name that is not used and is off when none is left. A
  parameter's value box follows its type: a number box for `integer` and `decimal`, a checkbox for
  `boolean` and a text box for the rest. Picking another key keeps the value when it fits the new type.
  A `null` value is edited in the JSON view.
- **A trigger's parameters** take their keys from the process's [parameters](authoring.md#names) for a
  scheduled job without a data source, and for a data change trigger whose `on` holds `delete`. A
  scheduled job's values are literals of each parameter's type, and a delete trigger's values are
  expressions over the deleted record, in the [expression editor](#expression-editor). A delete trigger
  does not show `fields` unless the file already has the list, so the author can remove it. Changing
  `type` does not remove the keys the new type forbids, and the compiler reports them.

## Label texts

A label is an object whose only property is `textKey`. Studio edits its texts in every locale, as
[labels](studio-resource-form.md#labels) shows, and Save writes them through the
[text write route](authoring-text-writes.md). Typing a label's texts is an unsaved change of the
resource, and Undo steps through it.

- **The key.** A new label gets a key made of the kind's folder and the resource's name in camelCase,
  then the path's segments without the final `textKey`, joined with `.`, such as
  `pages.myTasks.title`. Studio adds `2`, `3` and so on when the key is taken. An empty `textKey`, as a
  starter leaves it, counts as no key and gets the same key.
- **Texts before the patch.** Save writes the texts of each changed key before the patch that puts the
  key into the saved resource, so a resource never names a key with no text, and an unused key does no
  harm. A write that fails shows the save error, sends no patch and keeps the draft.
- **A text write changes the folder,** as a save does: the Problems panel shows the diagnostics that it
  answered, and the names and the release history load again.
- **A shared key.** A label whose key other labels use says how many, from `usedBy` in the names
  answer, so the author knows that the edit changes them too.
- **One language rule**. A label shows in the author's language, else in `en`, as a
  resource's title does, so a table and a picker never name one resource two ways. The label editor's own
  rule, a box in the author's language or else the application's first, is about which language is
  edited, and stays.

## Expression editor

A string property that the schema marks with `x-axis-expression` is a code editor that highlights the
[expression grammar](expressions.md#grammar), checks the text as the author types and completes names.
It is CodeMirror 6, in a chunk that loads only when Studio first shows an expression or the JSON view.

- **Checking.** The editor checks its text once when it first draws, and 300 ms after each keystroke it
  sends the text to the [expression check route](authoring.md#expression-check), and shows only the
  latest answer. An empty text, required or not, is not sent. It underlines the range from `start` to
  `end` and shows the message and then its error code under the editor. From its first draw or a keystroke
  until the answer to its latest text arrives or the check fails, and while its code loads, the editor has
  `aria-busy="true"`. An empty text leaves it not busy. Tests wait on this busy state before they click on
  the Form tab, see [flaky tests](../development.md#flaky-tests).
- **No answer.** The check places the text in the saved resource, so a place inside a field or an item
  that is not saved yet has no answer, while a new property of a saved object has one, see
  [the place](authoring.md#expression-check). With no answer, or an answer of `400` or `401`, the last
  result stays on screen. A `401` does not show the sign-in prompt, so the draft is kept. The editor
  never blocks Save.
- **One line under the box.** An empty box shows what the place needs, as "Needs to give yes or no". Once
  the expression reads back, the reading takes the place of "Gives …", since a type that does not match is
  already an error. A validation, a condition or a filter needs a boolean, and a computed field or a default
  the field's type. A place that takes any type keeps "Gives …" under the reading.
- **Reads back in words.** While the expression has no error, an "In words:" line under the box reads it back,
  in every place, because every place uses this editor. A field reads as its label in bold, or as its name when
  it has no label, and a path as the labels of its fields, such as "Name of Supplier". An enum value reads as
  its value label, `true` and `false` as yes and no, and `null` as no value. Numbers and dates read in the
  author's language. A text value reads between single quotes, such as 'PR-'. A comparison reads in words: "is
  at least", "has no value", and "is one of" with the list joined by "or". A comparison of dates reads as before
  or after. A yes-or-no field on its own reads as being yes, and after `not` as being no. Arithmetic keeps ×,
  ÷ and −, so `amount * 1.1` reads "Amount × 1.1". With an error the line is hidden, and the error shows. The
  reading comes from the [expression check](authoring.md#expression-check), and its words are texts in every
  locale, so a change of language needs no new check.
- **Functions read in words.** Each function has words of its own, such as "the days from … to …" for
  `daysBetween`. `addDays(today(), 7)` reads "today plus 7 days".
- **Groups read as the expression groups them.** A group of only `and`, or only `or`, reads on one line, joined
  in the author's language. A group that holds a group reads as a list under "All of these:" or "At least one
  of these:", so `and` binding tighter than `or` shows. A `not` group always reads as a list, under "Not all of
  these:" for `and`, and under "None of these:" for `or`.
- **The box is one line, with Expand inside it.** The box starts one line high, like a text
  field. Expand is a small icon button inside the box, at the end of its first line, and each line keeps room at
  its end, so the text never runs under it. The box in the Expand dialog shows at least eight lines.
- **An error in words.** An error reads first in words, with its code after it, small. An
  empty expression is not sent to be checked. The box wraps long lines and grows with them.
- **A function's signature.** In a call to an allowed function, a line shows `contains(text, text)` and what it does,
  with the parameter at the cursor in bold, counted by the commas at the call's own level. Outside a call, none.
- **Expand.** Expand, next in Tab order after the box, opens the expression in a large dialog, with an editor
  that checks and saves like the box, and a panel of tabs Fields, Operators and Functions, with every rule
  allowed there. A click inserts the name at the cursor. A search looks in all three, best first: a name that
  is the words, then one that starts with them, then one that holds them, then one whose description does.
  The dialog's body scrolls each tab's list, which has no scroll of its own, as [scroll areas](actions-and-controls.md#scroll-areas) say.
- **Completion that explains.** Each choice has a short detail and an info panel while it is selected. A field
  shows its label and type in words, a function its signature, what it does and an example, and a rule its
  signature and the type it gives. An operator or value shows its words and an example.
- **Completion knows where the cursor stands.** After a value it offers that type's operators, the comparisons
  first, and after a clause that already compares, only `and` and `or`. Inside quotes after an enum field it
  offers the field's values in their order and closes the quote. Elsewhere it offers the fields, the values,
  `not`, the rules and the functions, those that give the type the place needs first. It opens by itself after
  a space that follows a value and after a quote that follows an enum field.
- **The language's own words.** Completion and the panel offer the comparisons, `is null`,
  `is not null`, `and`, `or`, `not`, `in (…)`, the arithmetic and the values `true`, `false`, `null`,
  `'text'` and `date('…')`, each with what it does and an example. A code editor stays the one way to
  write an expression, because an expression gives a value of any type, not only yes or no.
- **Only what the place allows.** The server answers
  [the scope of a place](authoring.md#expression-scope), so the client keeps no list of functions: no
  aggregate in a default, and no `now()`, `today()` or `toDate()` in a computed field (`AXC0086`).
- **Completion.** Typing a word, or Ctrl+Space, opens a list of what fits at the cursor, and the arrow keys
  and Enter pick one. The fields, parameters and rules come from the [names route](authoring.md#names),
  loaded with the application and again after a save, a delete and a reconnect. The functions come from
  the [expression scope](authoring.md#expression-scope), loaded once per place with the draft, and again with the
  next check answer while the scope has no answer. Without names the list offers the language's words and the
  functions only, and typing and Save keep working. A rule
  and a function with parameters go in with the cursor inside their brackets, and `now()` after them.
- **What completion offers:**
  - the fields of the entity at the place: the entity being edited, a policy grant's `entity`, or the
    `entity` of a data source, a process or a data change trigger;
  - `id`, the record's reference, before the fields, in a process with an entity and in a data change
    trigger, offered as a parameter and not as a field, because a path cannot start from it;
  - a data source filter's parameters, a rule's own parameters, and a process's `parameters` when it
    has no subject, with no field;
  - the application's rules, with module rules as `<module>:<name>`;
  - the functions and aggregates of the [expression language](expressions.md#functions) that the scope
    answer allows at the place, such as `currentUser()` only in a grant's or a data source's filter.

## Policy matrix

The policy matrix shows in one view who may do what in an application, and edits each role's own
access policy. It reads the [policy matrix route](authoring-policy-matrix.md), is the last entry of
the sidebar's Access group, and is kept in the address as `&view=policies`.

- **The table.** Roles are the rows. The columns are the entities without child entities, the data
  sources, the processes and the endpoints. A cell shows the OR of the role's actions from all its
  policies, as in [combining grants](security.md#combining-grants). A data source cell that lacks the
  `read` of its root entity warns and names that entity.
- **Filter.** The table has a [Filter](actions-and-controls.md#tables) on the Role and on every target. A target filters
  as text over the cell's actions, so "contains update" finds the roles that may update there. The filter is kept in
  the address as `matrix.filter`, and every role shows on one page with no pager.
- **A cell's grants.** Selecting a cell opens its grants under the table, each with its actions, its filter in the
  [expression editor](#expression-editor) and its fields in a field picker. The panel says the cell has no grant only
  when it shows none. An editable row whose cell has only shared grants lists them and offers Add grant.
- **Where an edit goes.** An edit writes to `<Role>Access`, the PascalCase role name plus `Access`, so
  `department-manager` writes `DepartmentManagerAccess`. When the role has none, Save creates
  `policies/<role>-access.json` with `roles` set to that role only.
- **Read-only grants.** A grant from a policy that names several roles, or from another policy of the role, is tagged
  Shared, and its Open policy link opens that policy. A `<Role>Access` that names other roles counts as shared too, so
  its row is read-only and shows its reason. A module policy's grant, in the row of its slot's role, is tagged with the
  module and has no link. Splitting a shared policy is not done here.
- **Editable grants.** In an editable row, the cell's own grants, those of `<Role>Access` that name
  exactly this target, are editable. The panel reads that policy with the resource route, so every
  edit fits the file that a save patches. While the policy loads, the panel says so.
  - Each grant has an **Actions** group with help, on a line of its own, that holds a checkbox for
    each action its target takes, as [roles and policies](roles-and-policies.md) lists them, which
    Studio reads from `policy.schema.json`. An action the target does not take stays in the file and
    shows as a warning tag, and only Validate reports it.
  - An entity grant, and no other, also edits its record filter and its fields. The **Fields** picker's
    help says an empty list grants every field, so clearing it removes the `fields` key.
  - Add grant adds a grant named after the cell's target with the first free number in the policy,
    ignoring letter case, such as `purchaseRequest1`. A grant shows its name read-only.
  - **Delete grant.** It deletes a grant, but not the policy's last one.
- **Saving.** Save is off while nothing changed. A click with a grant that has no ticked action says so under its
  actions, focuses them and sends nothing. It writes one file, with one patch that carries the version the panel
  loaded and changes only what the author edited. A stale version shows a conflict message, keeps the draft and offers
  Reload, which discards it. After a save the Problems panel, the matrix and the names load again, and the cell stays
  open. Opening another cell, or a matrix that loads again after another write, discards an unsaved draft.

## Editors by kind

Each kind opens in the editor that fits it: a designer, a typed editor or the [generic form](#editor).

| Kind               | Editor                                                                                                               |
| ------------------ | -------------------------------------------------------------------------------------------------------------------- |
| `entity`           | The [entity designer](entity-designer.md)                                                                            |
| `page`             | The [page editor](#page-editor) and the [page designer](#page-designer)                                              |
| `form`             | The generic form with the [form steps](studio-form-editors.md#form-steps), and the [form designer](form-designer.md) |
| `process`          | The [process editor](#process-editor) and the [process designer](process-designer.md)                                |
| `site`             | The generic form, with the [sign-in](#a-sites-sign-in) picker                                                        |
| `endpoint`         | The generic form, with [rows](studio-form-editors.md#endpoint-rows) for its `values` and its `event`'s `fields`      |
| `connector`        | The [connector editor](#connector-editor)                                                                            |
| `identityProvider` | The [identity provider editor](#identity-provider-editor)                                                            |
| `scenario`         | The [scenario editor](#scenario-editor)                                                                              |
| `seed`             | The [seed editor](studio-form-editors.md#seed-editor)                                                                |
| `text`             | The [text editor](text-editor.md)                                                                                    |

Every other kind, `application`, `rule`, `sequence`, `role`, `policy`, `dataSource`, `event`,
`trigger`, `webhook` and `migration`, opens in the generic form. Every editor shows all of its kind's
content but a migration's `changes` list, which stays in the JSON view because each change's fields
depend on its `type`.

## Page editor

The page editor's Form tab edits a page's title, parameters and widgets, with a panel for each widget,
and the JSON view stays as the second view of the same draft. The [page designer](#page-designer)
edits the same draft on a canvas.

- **The page.** The Form tab shows three [sections](studio-resource-form.md#sections): General, with the
  page's name, its [title](studio-resource-form.md#labels) and its [description](#editor); the
  [parameters](#page-parameters); and Widgets, with the [widget tree](#widget-tree). Each page field and
  each widget name picker, and each field of every widget panel and of their actions, has a label and help,
  named by its path in the page. The page editor draws no label of its own.
- **Widget panels.** Each widget type has a panel that shows the widget's properties: `table`, `form`,
  `taskInbox`, `processInspector`, `kpi`, `chart`, `detail`, `columns`, `tabs` and `section`. A widget
  of a type with no panel shows its type and points to the JSON view.
  - A `table` panel has entity and data source pickers, then a `rowActions` list of navigate or delete
    actions and a `toolbarActions` list of navigate actions. A delete action has a label and an
    optional `visibleWhen` expression.
  - A `form` panel has entity and form pickers and its `actions`, each a `startProcess` action with a
    process picker and a label, or a navigate action.
  - A `taskInbox` panel has no properties, and a `processInspector` panel has a process picker.
  - A `detail` panel has the entity and form pickers and the actions of a form, with a record picker
    between them, see [detail, chart and KPI widgets](detail-chart-kpi-widgets.md). The record picker
    lists the page's required parameters of type `reference`: those whose `target` is the widget's
    entity, ignoring letter case, or every such parameter when the widget names a form. Picking another
    entity keeps the record, a record that matches no listed parameter shows as unknown, and the
    compiler reports it after the save.
  - The [KPI and chart panels](#kpi-and-chart-panels) and the
    [container panels](#container-panels) follow.
- **Exclusive bindings.** The compiler accepts exactly one of a table's entity and data source, and one
  of a form's or a detail's entity and form, so picking one clears the other.
- **Type change.** Choosing another type removes the known properties that the new type does not take,
  because the compiler rejects them. A switch to `processInspector`, `detail`, `kpi` or `chart` adds
  the empty keys the schema requires, such as `process` or `record`, and a new chart starts with the
  `bar` kind and one empty measure, which the schema requires.
- **Process pickers.** An action's process picker lists every process and does not check the choice,
  so a wrong one shows as a diagnostic after the save.
- **Unknown keys.** A key the editor does not show, on the page or on a widget, is saved unchanged,
  also when the widget moves.
- **Parts in words**. A widget's row, a canvas frame's name and a page parameter's row join their parts with a
  comma, as in "table, PurchaseRequestList", see [text](actions-and-controls.md#text).

### KPI and chart panels

A `kpi` panel has a data source picker, a measure picker and a [label](studio-resource-form.md#labels).
Add comparison adds a comparison with its own measure picker and label, and Add navigate action adds a
[navigate action](#navigate-actions), each with Remove. A `chart` panel has a data source picker, a
kind picker over `bar`, `line` and `pie`, a category picker, a title, and a list of measures, each with
a picker and a label, with Add measure and Remove measure, which is off when one measure is left.
Every field and group of both panels shows its label and help. A chart's title and a section's title
share one text, Title, with one help.

- **Data sources.** A KPI's data source picker lists only data sources with a total row, and a chart's
  only those with exactly one group field, which are the only choices the compiler accepts.
- **Columns.** The column pickers read the chosen data source from the names. The category lists its
  group field and the measure pickers its measures. A chart's measure pickers list only measures of
  type `integer` or `decimal`, because the names give a `count` the type `integer` and a `sum`, `min`
  or `max` the type of its field. Picking another data source keeps the names already set, and a name
  that the new data source does not project shows as unknown, which the compiler reports after the
  save. While the names have not loaded, the column pickers are plain boxes.
- **Parameters.** Both panels have a Page parameters list, with a picker for each parameter of the data
  source that lists the page parameters of the same type. Clearing a picker removes its key, a key the
  list does not show stays as it is, and clearing the last one leaves an empty `parameters` object. A
  binding to a missing or wrong-type page parameter shows as unknown. The compiler checks the target,
  the enum values and the required flag after the save, see
  [widget binding](data-sources.md#widget-binding). The editor does not check that a `pie` has exactly
  one measure.

### Navigate actions

A navigate action has a page picker, a [label](studio-resource-form.md#labels), an optional `record`
expression and parameter expressions. The page picker, label, record and parameters each show their
label and help. The page picker lists every page, the current one included, because a page may open
itself with other parameters.

- **Adding one.** A table offers only Add navigate action, and a form also Add action for
  `startProcess`. A new action is named `<action type><n>`, such as `navigate1`, with the first free
  `n` in its list. A KPI's `navigate` has no name, because the schema refuses one.
- **Record.** An empty Record box removes the `record` key.
- **Parameters.** When the names list the target page, the action shows an expression box for each
  parameter the page declares, with the parameter's type and whether it is required. An empty box
  leaves the parameter out. A value the page does not declare stays visible with a note and Remove,
  because the compiler refuses it. Picking another page removes the values that the new page does not
  declare, ignoring letter case. While the names have not loaded, or when the page is not in them, the
  action keeps rows where the author types the parameter's name, and a wrong name shows as a
  diagnostic after the save.

### Page parameters

The Parameters section sits between General and Widgets. Its Add parameter button sits on the right of the
heading, and a page with no parameters says so in one line. Each parameter has a name, a type picker over
`text`, `integer`, `decimal`, `boolean`, `date`, `date-time`, `enum` and `reference`, a Required switch
and Remove. The name, type and Required switch sit one to a row. Add parameter appends a `text` parameter
with an empty name. A `reference` parameter also has a target entity picker, and an `enum` parameter its
values. A type change removes the `target` or the `values` that the new type does not take, and switching Required off removes the `required` key.
The names answer lists each page's parameters, which is how a navigate action knows them. A
`parameters` that is not a list points to the JSON view.

### Widget tree

The editor shows the page's widgets as nested rows. A container shows its columns, tabs or section
body as nested lists, and a row opens its widget's panel at its own pointer, such as
`/widgets/0/columns/1/widgets/0`.

- **Adding.** Every list has an Add widget picker with all ten types. A new widget is named
  `<type><n>`, such as `table1`, with the first `n` free on the page at any depth, ignoring letter case.
  A new `columns` starts with the columns `column1` and `column2` of width 6, a new `tabs` with `tab1`,
  and a new `section` empty.
- **Moving.** Every row has Move up, Move down and Remove, which replace only the list that holds the
  widget, and a Move to picker. Move to lists every other widget list as a path, such as
  `Page › columns 1 › Column 2`, leaves out the widget's own list and the lists inside it, and replaces
  the page's `widgets` in one change. A type change or a move keeps the widget's name.
- **Fixed containers.** A container's type is fixed. The compiler reports the depth limit and an empty
  list after the save.

### Container panels

Each container type has a panel under its fixed type. The type shows in a read-only box labelled Widget
type, and every field of the panel shows its label and help. A key the panels do not show, on a column, a
tab or a section, stays in the saved file. A container whose parts are malformed, such as `columns`
that is not a list, points to the JSON view.

- **Columns.** A `columns` panel shows a width box from 1 to 12 for each column, and the compiler
  reports widths that do not add up to 12 after the save. Add column adds a column that takes what is
  left of 12, or 1 when nothing is left, and keeps the other widths. Remove column drops a column with
  its widgets.
- **Tabs.** A `tabs` panel shows each tab's [label](studio-resource-form.md#labels), with Add tab
  and Remove tab.
- **Names.** An added column or tab is named with the first free `column<n>` or `tab<n>` among its
  siblings, ignoring letter case.
- **Section.** A `section` panel shows the title as a tab's label shows, and a Collapsed switch.
  Switching Collapsed off removes the `collapsed` key, because a section starts expanded.
- **The last one stays.** Remove column and Remove tab are off when one is left, because the schema
  needs at least one.

## Page designer

The page designer is the page editor's Design tab, where an author builds a page by drag and drop on a
canvas that the site's own widgets draw. It sits next to the Form and JSON tabs, a page opens on it,
and the three tabs edit one draft. The form designer and the process designer reuse its property
panel, its undo and its drag and drop.

- **Canvas.** The canvas draws the unsaved draft with the site's own widget components, with labels
  from the application's texts, and sends no request to `/api/apps/`. Tables, charts and the other data
  widgets show placeholder rows and values made from the field types, and a page parameter gets a
  placeholder value made from its type. The canvas cancels every click and form submit, so an action
  runs nothing and a navigate action or a link leaves Studio where it is. A page the route cannot draw
  shows a short notice. The canvas is mounted only while the Design tab is active.
- **Redraw.** The canvas posts the draft to [the draft page metadata route](draft-page-metadata.md)
  300 ms after the last edit and redraws from its answer. A widget redraws only from a new answer, so an
  edit redraws it once, after the post, and a drag over the same target redraws none. The canvas also
  posts the draft after each save. Save writes the texts of new label keys, which the route compiles
  with, and the draft stays the same. From an edit or a save until it draws the answer, the canvas has
  `aria-busy`, which the tests wait on before a drag.
- **Placeholders and marks.** A widget that does not compile draws as a placeholder with its type and a
  line for each of its diagnostics, with the code and the message. A widget is marked when a diagnostic
  of the page, of any severity, names its `path` or a path inside it, so a container is marked too when
  a widget inside it has one, even in a closed section or an inactive tab. A marked widget has a border
  in the `danger` colour, its text label, and `data-marked`. The marks follow the
  route's latest answer, and so the unsaved draft.
- **Marked names.** A marked widget's text label reads "{name}, has problems", where the name is its frame's
  name: the widget's type and the name it binds, so a marked table reads "table, PurchaseRequestList, has problems".
- **Selection.** A click on the canvas or a pick in the widget tree selects a widget in both. A click
  selects the innermost widget under the pointer, and a click on the empty area of a container selects
  the container. The selected widget has `aria-current` and an outline in the primary colour. Every
  frame is a Tab stop, and Enter or Space selects it. A frame's name is its widget's type and the name
  it binds, such as `table` and `PurchaseRequestList`, and reads as the marks above say when it is marked. The
  selection is kept across the Design and Form tabs while the resource stays open, and is not in the
  URL's `path`, which opens the Form tab.
- **Property panel.** The side pane holds the whole widget tree and the panel of the selected widget,
  the same as the Form tab. An edit redraws the canvas through the draft post.
- **Actions on the canvas.** Every action of a table, row and toolbar, of a form, a detail and a KPI
  draws as a button, also one that the site would hide, such as an action whose `visibleWhen` fails on
  placeholder data or a form action on an unsaved record. A click, Enter or Space on the button selects
  the action, by its JSON Pointer, such as `/widgets/0/rowActions/1`, or `/widgets/0/navigate` for a
  KPI, and not its widget. The button has `aria-current` and an outline in the primary colour, the
  widget tree keeps its widget selected, and the panel shows the widget's panel with the action's card
  marked by `aria-current` and scrolled into view. Adding a navigate, `startProcess` or `delete` action
  in the panel selects the new action, and removing an action selects its widget. The button runs
  nothing: it does not navigate, start a process, open a delete confirmation or call `/api/apps/`.
- **Actions that fold.** A KPI's navigate action is a link-style button on the label line, and the
  tile is not a link. A table's first `navigate` row action draws as the link of its identifying
  column, which selects the action as a button does. At narrow width a table's row actions fold into a
  menu, and three or more other row actions fold into the "…" menu at any width, see
  [frontend](frontend.md). The canvas cannot open that menu, because it cancels the click, so the
  author selects such an action from the tree and the panel.
- **Palette.** The side pane lists the ten widget types above the widget tree. Enter, Space or a click
  inserts a new widget after the selected widget, at the end of the selected container's first list,
  or at the end of the page's list on an empty page. The author can also drag a type onto the canvas.
  A drop on the upper half of a widget inserts before it and one on its lower half after it, and a drop
  on a widget list, the page's own or that of a column, a tab or a section, or between two widgets,
  appends to that list. The innermost target wins. A container that does not compile yet, such as a
  new empty section, draws as a placeholder with a drop area for each list its draft has.
- **Page limits.** A type that the page cannot take, under the limits of a page's
  [widget tree](resource-files.md#resource-file-shape), is off, cannot be dragged, and shows its
  reason: a second table, a second form, task inbox or process inspector, or a container past 4
  levels. A drop that a limit forbids inserts nothing, and the live region gives the same reason.
- **Data panel.** Between the palette and the widget tree, the side pane lists every entity and every data
  source, module ones too. Each list has a title and a help line, and a list with no names says so in one
  line. A drop lands where a palette drop lands, and opens a dialog with one button for each widget that
  can show the name: a table, a form or a detail for an entity, and a table, a chart with one group field
  or a KPI with a total row for a data source. A widget that a page limit forbids is off with the palette's
  reason. Enter, Space or a click on a name opens the same dialog, and the widget goes after the selected
  widget. A detail takes as its record the first required `reference` parameter that targets the entity, or
  none, because the designer adds no page parameter. A chart takes the group field as its category, the
  `bar` kind and the first numeric measure, and a KPI the first measure. A measure has no label, so a chart
  and a KPI start with empty text keys and draw as placeholders until the author writes them.
- **Moves.** The author can drag any widget of the canvas, and the innermost widget under the pointer
  drags, so a press on a KPI in a column moves the KPI. It drops on the same targets as a palette type.
  Alt+ArrowDown and Alt+ArrowUp move the selected widget to the next or previous allowed place,
  skipping refused ones, and focus follows it. The Move to picker of the widget tree stays. A move the
  format refuses, a widget inside itself or a container whose deepest list would land past level 4, is
  flagged while it is dragged: the drag overlay and the live region give the reason, and a drop there
  changes nothing. A drop on the widget itself changes nothing either. A move keeps all the widget's
  keys.
- **What the live region says.** After an insert, a drop or a move the new or moved widget is selected, and a live
  region reads one sentence of the agreed texts, with the type, the list and the position filled in, such as
  "Inserted table in Page, position 2." or "Moved kpi to Page › section 2, position 1." A move that cannot go
  further says "The widget cannot move further." The drag and drop library's own spoken messages are off, so the
  live region is the only voice.
- **Width switch.** It draws the canvas at desktop width, which fills the canvas area, or at phone
  width, where the columns stack to full width and tables use their narrow layout, even in a wide
  browser window. The choice goes back to desktop when the author returns to the Design tab. The live
  site keeps its own rule, the window's width.
- **Undo.** Undo and Redo on the [save bar](studio-resource-form.md#save-bar), and Ctrl+Z and Ctrl+Shift+Z, or Cmd on macOS, cover
  every edit of the draft in the three tabs of a page, a form and a process. The history is kept in
  memory, up to 100 steps, and a reload, also one after a reconnect, a conflict reload or opening
  another resource clears it, while a save keeps it. A step ends when the author edits another property
  or leaves the box, and a pick in a dropdown, a button action and a move are always a step of their
  own. The icons are off when there is nothing to undo or redo, and while a save
  runs. The keys also work when nothing has focus, but not when the focus is outside the editor, and
  Studio's undo replaces the browser's own inside the editor's boxes.
- **Undo for every kind.** Every kind's draft has undo and redo, on the
  [save bar](studio-resource-form.md#save-bar).
- **Custom widgets** _(planned for M22)_. The palette offers custom widgets, as
  [custom widgets](custom-widgets.md#designer-palette) describes.
- **Code.** Drag and drop uses `@dnd-kit/core`, `@dnd-kit/sortable` and `@dnd-kit/utilities`, in the
  lazy chunk `assets/DragAndDrop-*.js` that the designers share; the form outline's moves use
  `@dnd-kit/sortable`. Each designer's canvas, palette and, for a page, data panel are a lazy chunk of
  their own, `StudioPageDesign`, `StudioFormDesign` and `StudioProcessDesign`, which load when the
  Design tab opens. The build fails when the drag and drop chunk, the page chunk or the process design
  chunk is missing or is part of the main bundle.
- **Format.** The designer writes only the order of the widget tree and the properties the format has.
  It writes no pixels, and widths stay in twelfths.

## Process editor

The process editor's Form tab edits a process as a list of step cards, and the JSON view stays as the
second view of the same draft. The [process designer](process-designer.md) edits the same draft as a
graph, with the card of the selected step beside it, and describes the panels of the timer,
sub-process and wait for event steps and of a task's deadline and escalations.

- **The process.** The Form tab has the General, Subject and Steps [sections](studio-resource-form.md#sections),
  and each field has a [field text](studio-resource-form.md#fields).
- **Subject.** A choice between Entity, which shows the entity picker, and Parameters, which shows rows
  of a name and a scalar type, `text`, `integer`, `decimal`, `boolean`, `date` or `date-time`, with Add
  and Remove. Switching replaces the draft in one change, so one undo brings it back: the other key is
  removed, and the new key, empty, takes its place in the file. See
  [processes without a subject](process-flow.md#processes-without-a-subject).
- **Steps.** The steps show as cards in file order, each with the step's name and type, Move up, Move
  down and Remove. Add step appends a step of the chosen type, with an empty name. A type change works
  as the [step panel](process-designer.md#step-panel) says.
- **Decision and end.** A decision shows its branches, each with a `when` expression and a target, and
  its `otherwise`. A branch can be added and removed, and a new one is named `branch1`, `branch2` and so
  on. An end step shows only its name and type.
- **Transitions.** The start step, `next`, a branch's `next`, `otherwise` and an outcome's `next` pick
  their target from the step names of the unsaved draft. A target that is not a step stays visible and
  is tagged unknown. Removing a step does not rewrite the transitions that name it, so the compiler
  reports them.
- **Task.** A task shows its label and its form as pickers over the application's text keys and forms.
  The assignee has a User and Role switch: User shows the expression editor and Role a role picker, and
  a switch removes the other key, so the step never holds both. `dueIn` is a text box, and clearing it
  removes the key. The outcomes are rows of name, label and target step, with add and remove. A key the
  card does not show stays as it is. A diagnostic path inside a task's label, assignee, form, `dueIn` or
  outcomes opens the Form tab at that field.
- **Operation.** An `operation` step shows an operation choice, `updateRecord`, `callConnector`,
  `raiseEvent`, `createRecord`, `notify` or Package operation, above its transition. Changing the
  operation removes only the keys the new one forbids and adds no empty key, so the compiler reports
  what is still missing, and every other key, unknown keys included, stays.
  - `updateRecord` shows `set` as rows of a field of the process's entity and an expression.
  - `callConnector` shows the connector and its operation as pickers, its
    [`input`](process-designer.md#connector-input), `output` as rows of a typed connector output name
    and an entity field, and `retry`.
  - `raiseEvent` shows the event as a picker and `fields` as rows of a payload field of that event and
    an expression.
  - `createRecord` has the panel that [createRecord](process-designer.md#createrecord) describes. Its
    entity picker lists every entity, child entities included, because the names do not say which are
    child entities, and the compiler reports one.
  - `notify` has the panel that [notify](process-designer.md#notify) describes.
  - A package operation is typed in a text box as `<package>/<operation>`, because no route lists the
    installed operations. It shows `input`, `output` and `retry` as `callConnector` does, with typed
    names, but its `input` stays scalar rows.
  - `retry` is attempts and backoff, and clearing both removes `retry`.
- **Without names.** While the application's names are not loaded, every picker is a name box. A key
  that the step's operation forbids is not shown as rows: the card says to edit it in the JSON view,
  and a diagnostic path inside it opens the JSON view.

## Connector editor

The connector editor's Form tab shows the connector's fields in the four
[shared sections](studio-resource-form.md#sections) General, Service, Authentication and Operations,
each field with its label and [help](frontend.md#platform-field-help). Add operation sits on the
Operations heading, and the JSON view stays as the second view of the same draft.

- **No values.** A binding is a name, and its help says what it holds and that the file keeps only its name. No
  value of a binding is shown or stored: the editor reads the resource and schema routes and the
  binding names, and writes only the resource, and the values stay in tenant settings.
- **Bindings from the tenant.** A binding is chosen from the tenant's names of the kind it needs, see
  [controls that know what they hold](studio-resource-form.md#controls-that-know-what-they-hold).
- **Auth.** Choosing a type removes the keys the new type forbids, such as `token` for `header`, and
  adds the keys it requires, with an empty name, and clearing the type removes `auth`. `oauth2` shows a
  Token URL, a URL binding, a Client ID and a Client secret, both secret bindings, and Scope and
  Audience boxes that remove their key when empty, see [OAuth2 auth](integration-resources.md#oauth2-auth).
- **Operations.** A card per operation shows its name, method, path and `lookup`, which is picked from
  the connector's other operations. Its input and output fields are rows of name, type and required,
  one field to a line, and the schema's required keys carry the shared required mark. Operations and rows
  can be added and deleted. A `GET` with a body row keeps it and shows a hint, and the compiler reports the error.
- **Placement and nesting.** A scalar input has a Sent in choice of `body`, `path`, `query` or
  `header`, which shows `body` when the file has no `in` key and removes the key when `body` is chosen.
  `object` and `list` inputs show nested field rows instead, down to four levels with the input as
  level 1. Choosing one adds `fields` and removes `in`, and a scalar type removes `fields`. The compiler
  reports placement and path errors after the save, see
  [structured payloads](integration-resources.md#structured-payloads).
- **Read from.** An optional Read from box holds a JSON Pointer into the response, and an empty box
  removes `source`.
- **Unknown keys.** A key the editor does not show, in the file, in an operation or in `auth`, is saved
  unchanged.

## Identity provider editor

The identity provider editor's Form tab shows the provider's fields in the five
[shared sections](studio-resource-form.md#sections) General, Sign-in, Claims, Roles and Assurance, each
field with its label and [help](frontend.md#platform-field-help), see
[identity provider resource](identity-provider.md#identity-provider-resource). Sign-in and Claims show
only for `oidc` and `saml`. General holds the name, `type` and description. Add assurance level sits on
the Assurance heading, and the JSON view stays as the second view of the same draft.

- **Type.** Choosing `accounts`, `oidc` or `saml` removes the keys the new type forbids and adds the
  bindings it requires, with an empty name. An `accounts` provider shows only General, Roles and Assurance.
- **Bindings.** A binding is a name box, and its help says what it holds and that only the name is
  stored. No value is shown or stored. `oidc` shows the issuer, the client ID, and a choice of client
  credential: a client secret, or a client key for `private_key_jwt`, each option with its own help.
  `saml` shows the metadata. Name and Type carry the shared required mark.
- **Label.** The "Name on the sign-in button" field sits in Sign-in, after the bindings, and has its texts in every locale, see [label texts](#label-texts).
- **Claims.** The display name, email and locale claims are name boxes that show their defaults,
  `name`, `email` and `locale`, until the author types another name.
- **Roles.** The roles claim is a name box that shows `roles` by default. `values` are rows of a claim
  value and a role picked from the application's roles, one field to a line, with Add claim value and Delete. With no rows, a claim
  value gives the role of the same name, and the editor says so.
- **Assurance.** `acr` is a list of text rows, each with Delete below, and with none the key is removed.
- **Unknown keys.** A key the editor does not show is saved unchanged.

### A site's sign-in

On a `site`, `signIn` is a list of the application's identity providers, picked by name, with add and
remove. An empty list removes `signIn`, so the site allows every provider, and the form says so.

## Scenario editor

The scenario editor's Form tab has four sections: General, with the scenario's name, its [description](#editor)
and `now`; Users, with their roles; Stubs; and Steps, a list with a panel for each step type. Add user,
Add stub and Add step sit on their section's heading. The JSON view stays as the second view of the same
draft. Scenarios themselves are in [test scenarios](test-scenarios.md).

- **Field names.** Every field, stub and step property shows a label and help text in the app's
  languages, so a person sees "Moves time by" and not the property name `by`.
- **Steps.** Each step has a type. Changing the type keeps the properties the new type takes and drops
  the others, and keys the editor does not show stay in the file. A step can be removed or moved up or
  down.
- **Pickers.** Entities, processes, roles and connectors are picked by name. A `record` is picked from
  the aliases that earlier `createRecord` steps save, and a `step` and its `outcome` come from the
  processes that the record started.
- **Values.** The fields of the step's entity show as typed cells, and a value the entity does not name
  stays as a text row. A money value is a number box that stores a number. A child collection's rows
  cannot be added in the form, so the JSON view writes them. A step whose entity is not in the folder edits its `values` as a JSON object,
  and the text is saved only when it is one.
- **Notifications.** An `expectNotification` step picks its recipient kind, a user, a role or an email
  address, and a channel, `inApp`, `email` or none, which reads both channels. Its expected values are
  key and value rows, like a stub's output.
