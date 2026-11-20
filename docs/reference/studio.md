# Studio

This page describes what Studio's editors, designers and buttons do: creating applications and
resources, editing a resource, its references and checks, publishing, releases and tests. How Studio is laid out, with its sidebar, band, Overview and Releases, is in
[Studio workspace](studio-workspace.md), and the form that edits one resource is in
[Studio resource form](studio-resource-form.md). The seed grid, the endpoint rows and the form steps are in
[Studio form editors](studio-form-editors.md). The [entity designer](entity-designer.md), the
[form designer](form-designer.md) and the [process designer](process-designer.md) have pages of their
own. The editor that each kind opens in, such as the page designer or the connector editor, is in
[Studio editors by kind](studio-editors-by-kind.md). Studio calls the [authoring API](authoring.md), its settings are in
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
  `&view=policies` the [policy matrix](#policy-matrix), `&view=packages` the [Packages entry](studio-packages.md#the-packages-entry), `&view=packages&package=<id>` one package's files,
  `&view=publish` the [publish page](#publishing) and `&view=requests&request=<id>` one [AI request's page](ai-authoring-in-studio.md#a-requests-page).
- **The AI requests table** _(planned for M35)_. `&view=requests` shows the [AI requests](ai-authoring-in-studio.md#the-request-panel) table.
- **A field.** `&field=<name>` opens a field's [drawer](entity-designer.md#field-drawer) on an entity's page. Opening and
  closing add a history entry, a rename replaces the name, and a name no field has opens nothing and is dropped.
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
  [editors by kind](studio-editors-by-kind.md#editors-by-kind), and keeps the JSON view as the second view of the same draft. A
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
  [table](actions-and-controls.md#tables), under the table's prefix too, such as `fields.filter`, or the `field` of an
  open [field drawer](entity-designer.md#field-drawer), is not asked about either, because the editor stays on screen.

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
[publishing routes](authoring-publishing.md#publishing), and a status line under the band follows each request to
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
  [release history](authoring-publishing.md#release-history). Publish sends the request with only the filled
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
  row opens as a [test failure](studio-running-tests.md#running-tests) does, and a skipped row shows its relaxed point and
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
  [publishing](authoring-publishing.md#publishing), and the status line follows the request as it follows a
  publish. A failed activation puts its diagnostics in the Problems panel, and the list still marks
  the previous release Live. Activate… is off while a request runs and while the reconnect message
  shows.
- **Loading.** The history loads when the application opens, and again after a request ends, a save, a
  text write, a delete or a reconnect, because the folder may then differ from the live release.

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
- **Open.** A chosen name that is a resource of the folder or of a module has an Open icon button joined
  after the select, named "Open" and the property's name. It covers every kind that the names list with
  an id, roles and events included. The button is a real link to the resource's Studio address, so
  Ctrl+click opens a new tab. An empty value, an unknown value, or a field, step, column, parameter,
  operation, outcome or text key has no button. A picker outside Studio's editor has none.
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
