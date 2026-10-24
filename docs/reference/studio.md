# Studio

Detailed reference for the Studio page, where authors edit, check, preview, test and publish an
application. Its API is in [authoring.md](authoring.md), and its settings are in
[host-settings.md](host-settings.md#studio-settings).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

Studio is where authors edit an application's resources. It uses the
[authoring API](authoring.md) and writes the same JSON files that git keeps, as
[D38](../decisions/d38-studio-edits-application-folders-through-an-authoring-api.md)
agrees. The route, the link, the application picker, New application, New resource, the resource list with its
search, the editor, the reference pickers, the Uses and Used by lists, the delete
button and its warning, the diagnostics panel, Validate, Preview and Publish with
the request status and the preview links, the Releases list with Roll back, Run tests,
Run and the Test results list, the read-only view of a module entity, and the reconnect message are built.

- **Route.** `/studio` is a platform route, outside every site. The header
  shows a Studio link only when `GET /api/me` answers `studio: true`. A person
  who opens the route without the author role gets a `403` from the API and
  sees a forbidden page. A signed-out person is sent to sign in.
- **Address.** The picked application and the selected resource are in the
  address, as `/studio?app=PurchaseRequests&resource=<id>`, so a reload keeps
  them. A Used by entry or a diagnostic that opens a resource at a path adds
  `&path=<path>`. The id of a module entity opens it too, as `&resource=<id>`,
  when the id is not one of the folder's own resources.
- **New application.** A **New application** button shows beside the picker
  when `GET /api/me` answers `studioCreate: true`. It also shows when Studio
  lists no application. The dialog asks for a name and checks it with the API's
  rule before it sends anything. It offers Blank or a template from
  `GET /api/studio/templates`, with the title and description in the UI locale
  and `en` as the fallback. A `409` or a `400` shows next to the name. A success
  adds the application to the picker, picks it, and loads its resources and
  diagnostics. See
  [creating an application](authoring.md#creating-an-application) and
  [D51](../decisions/d51-creating-applications-and-resources-from-studio.md).
- **New resource.** A **New resource** button above the search box opens a
  dialog that asks for the kind and the name, or the locale for a `text`
  resource. The kind picker lists the 18 kinds that the create route accepts,
  which is every kind except the manifest. The author can type to narrow it.
  - It sends the kind's starter with an id from `crypto.randomUUID()`. A starter
    is the smallest content that passes the kind's schema. A reference the
    author must still choose is an empty string, so it shows up as a diagnostic.
    A new `site` gets the kebab-case name as its `path`, by the same rule as
    file names, because a path is a value and not a reference.
  - The starters live in the SPA, in `web/src/platform/studioStarters.json`.
    Each lists the pointers of its empty references. A Vitest test checks each
    one against its bundled schema, so a schema file without a starter fails,
    and every error must be at a listed pointer. A server test reads the same
    file, sends each starter through the create route, and checks that its
    diagnostics are at the listed pointers only.
  - A new text resource copies every key and text of the default locale of the
    first site in file order, or `en` when there is no site, so every label still
    resolves. The author then translates. Its name is `Texts` and the locale,
    such as `TextsVi` for `vi` and `TextsPtBr` for `pt-BR`.
  - A `409`, when the id or the file exists, and a `400`, when the name or the
    locale is refused, show under the field and keep the dialog open.
  - After a create, the new resource joins the list, the diagnostics panel shows
    the diagnostics that the create answered, and the editor opens the resource.
  - Its texts are platform texts under `shell.studio.*`, in `en` and `vi`.
- **Application picker.** The first step picks one of the applications that
  `GET /api/studio/apps` lists. With only one application, it is picked at
  once.
- **Resource list.** The list groups the resources by kind. The kind shows as
  written in the files, such as `entity`, without translation. A search box
  filters it by name and `id`, ignoring letter case. Selecting a resource shows
  its name, kind, `id` and file, and opens it in the editor below. A **Module
  entities** group below the kinds lists each entity of the modules the application
  uses, by its `<module>/<name>` name. It is built from the application's names,
  and the search box filters it too.
- **Editor.** The editor shows one resource in two views of the same draft.
  - A form is built from the JSON Schema of the resource's kind. The SPA
    bundles the schemas in `src/Axis.Configuration/Schemas`, the files the
    server embeds, and loads the one it needs when a resource is selected. The
    form shows text, number, boolean and enum values, nested objects, and
    arrays with add and remove. `id`, `kind` and fixed values such as
    `formatVersion` are read-only. Labels are the property names as written in
    the schema, without translation. A part the form cannot show, such as
    `oneOf`, `anyOf` or a map, points to the JSON view. A map whose keys carry a
    reference mark is the exception, see **Reference pickers** below. A text resource's
    `texts` map shows in the Text editor, see **Editors by kind** below. An
    `allOf` whose items hold only `if`, `then` and `else` shows its
    properties, and the form does not show the conditions.
    A label, an object whose only property is `textKey`, has its own editor, see
    Label texts below. Until the names load, a label shows as a reference picker
    over the application's text keys, and a key that the application does not
    define is tagged unknown.
  - A JSON view shows the draft as text. A valid edit there updates the form.
    Invalid JSON shows an error and turns Save off.
  - Save sends only the changes, as a JSON Patch with the version the editor
    loaded in `If-Match`. It stays off while nothing has changed. An array
    whose length changed is replaced as a whole, and an array of the same
    length is compared item by item. Clearing an optional value removes its
    key. After a save the editor carries on from the new version.
  - A stale version is a `409`. The editor keeps the author's draft on screen
    and offers Reload. Reload reads the resource again and drops the draft.
    Selecting another resource also drops an unsaved draft, without a warning.
  - A kind with a typed editor shows it in place of the form, and keeps the
    JSON view as the second view of the same draft. The typed editor saves
    through the same JSON Patch, with the same `If-Match` rules. It never drops
    a value it does not show, so unknown keys stay as they are. See
    **Editors by kind** below. The seed editor, the scenario editor, the
    connector editor, the page editor, the endpoint mapping rows, the text
    editor, the process editor's step list, with its decision, end, task and operation
    panels, and the entity designer are built.
- **Uses and Used by.** Each resource shows a **Uses** list and a **Used by**
  list, from the [references routes](authoring.md#references). Each entry shows
  its relation, the other resource, and the file and path of the reference.
  - Selecting a **Uses** entry opens the other resource at its top.
  - Selecting a **Used by** entry opens the other resource at the path of the
    reference. The path is in the address and the details, and the editor
    scrolls to the form field for that path. Nothing is highlighted. A path
    the form cannot show, such as one inside a map, opens the JSON view instead.
  - An expand control on a resource or text key entry loads one more step, the
    entry's own lists. It loads them once. Collapsing and expanding again shows
    the same lists.
  - A package or a module entry is plain text. It cannot be opened or expanded.
  - Uses and Used by stay lists. The **Process graph** below is the only
    drawn graph.
- **Text keys.** A text resource's **Uses** list holds its keys, each as an
  entry. Expanding a key lists the labels that use it and the locales that
  define it, as its **Used by** list.
- **Delete.** The editor has a **Delete** button, which is hidden for the
  application manifest. It always opens a confirm dialog first. When a
  resource's `usedBy` count is above 0, the dialog warns and names the count. The
  author can still confirm. The delete sends the loaded version in `If-Match`. A
  stale version shows the conflict message, and nothing is deleted. After a
  delete, the resource leaves the list and the diagnostics panel shows the
  diagnostics that the delete answered, which report what broke.
- **Stale counts.** The `usedBy` counts and the lists load with the selected
  resource. A save does not reload them, so a count can be out of date until the
  page is reloaded. The names, and so the counts that labels show, load again
  after a save and after a text write.
- **Diagnostics panel.** The panel lists the diagnostics of the folder, with
  each one's code, severity, message, file and `path`. It loads them from
  `GET /api/studio/apps/{application}/diagnostics` when an application is
  picked. After a save, it shows the diagnostics that the save answered
  instead. A diagnostic that names a resource selects it, clears the search, and
  opens the resource at the diagnostic's `path`, as a Used by entry does. A
  diagnostic with an empty path opens the resource at its top. A diagnostic that
  names no resource cannot be selected. A diagnostic never blocks a save.
- **Publishing.** Up to three buttons sit under the application picker. They use the
  [publishing routes](authoring.md#publishing).
  - **Validate** sends a validate request. **Publish** opens a dialog with an
    optional label, up to 100 characters, and an optional note, up to 2000.
    Confirming sends the publish request. Empty values are left out of the body.
  - **Publish** shows only when `GET /api/me` answers `studioPublisher: true`.
    When that answer is `false` or fails, **Publish** does not show.
  - **Preview** sits next to **Validate** and shows only when `GET /api/me`
    answers a `studioPreviewHost` that is not null. It does not need
    `studioPublisher`. It sends `POST .../preview` at once, with no dialog, and
    the status line shows **queued**, **running** and the outcome, as for Publish.
    A succeeded preview shows **Previewed** and a link to each of the
    application's sites on the preview host. Studio builds a link from the page's
    protocol and port, the `studioPreviewHost` and the site's `path`, and it reads the
    paths from the folder's `site` resources when the preview succeeds. A site
    renamed after the request was sent can give a wrong link. The preview host
    must be served on the same port and protocol as the page. A failed preview
    shows **Preview failed** and puts its diagnostics in the diagnostics panel.
    A preview shows no release number and does not change the Releases list,
    because its release belongs to the preview tenant's own history.
  - Studio then checks `GET .../requests/{id}` every second until the status is
    `succeeded` or `failed`, with no time limit. A status line shows **queued** or
    **running** meanwhile. It shows the outcome only once the publisher has finished
    the request.
  - A succeeded publish or rollback shows its release number. Studio reads it from
    `GET .../releases`, by matching the request's `releaseId`. A failed publish shows
    the failure and no release number, even when the request names a release, so a
    failure never looks like a success. A rollback shows its own texts, **Rolled back**
    and **Rollback failed**.
  - When a request ends, the diagnostics panel shows the request's diagnostics in
    place of the loaded ones, for a failed request and for a succeeded one that holds
    warnings. They open at their paths as before.
  - The buttons are off while a request runs and while the reconnect message
    shows. If the server does not answer during a request, Studio checks it again
    after the reconnect.
- **Running tests.** Studio runs the application's
  [test scenarios](test-scenarios.md) through the
  [test route](authoring.md#test), as
  [D55](../decisions/d55-running-test-scenarios-from-studio.md) agrees.
  - **Run tests** sits next to **Validate**. It sends `POST .../test` with no
    body, so every scenario runs.
  - **Run** sits in the details of a selected `scenario` resource. It sends
    `{ "scenarios": ["<name>"] }`, so only that scenario runs.
  - Both run the saved files, not an unsaved draft in the editor, as Validate does.
  - Both show only when `GET /api/me` answers `studioTest: true`. When that
    answer is `false` or fails, they do not show. They are off while a request
    runs and while the reconnect message shows.
  - The status line works as for Validate. It shows **queued**, **running** and
    the outcome, and Studio checks `GET .../requests/{id}` every second until the
    status is `succeeded` or `failed`. A test shows **Tests passed** or
    **Tests failed**, and never a release number.
  - The **Test results** list shows each scenario of the request's `scenarios`
    field, with **passed** or **failed**, its time and, for a failure, the failing
    step and its message. Steps count from 1, as the step cards of the editor do.
    Selecting a failure opens the scenario resource at the item's `path`, as a
    diagnostic does.
  - While the request is queued or running, the list shows a note instead of rows,
    so an empty list never reads as a pass. A failed run with no results says that
    no scenario ran, and points to the diagnostics. Compile errors, a missing
    setting and an unknown scenario name fail a run this way.
  - The list is not kept across a page reload. The request's diagnostics go in the
    diagnostics panel, as before.
  - The buttons are off while a request runs and while the reconnect message
    shows.
- **Releases.** A list under the publishing buttons shows the open application's
  release history from `GET .../releases`.
  - Each row is one activation: its release number, label, note, who activated it
    and when. Rows are sorted by time, newest first.
  - Exactly one row is marked **Active**. A release that was activated more than
    once has a row for each activation, and only its newest row is marked.
  - **Roll back** shows only when `GET /api/me` answers `studioPublisher: true`. It
    sits on every row of a release that is not the active one, and is off while a
    request runs and while the reconnect message shows.
  - **Roll back** opens a dialog that names the release number and label. Confirming
    sends `POST .../releases/{id}/rollback`. The status line then shows **queued**,
    **running** and the outcome, as for Publish. A failed rollback puts its
    diagnostics in the diagnostics panel, and the list still marks the previous
    release **Active**.
  - A warning shows when the folder's content hash differs from the active release,
    for example after a save or a rollback. It does not show without an active
    release, or while the folder has errors, because the server then returns no
    content hash for the folder.
  - The list loads when the application is picked, and again after a request ends,
    after a save, a text write or a delete, and after a reconnect.
- **Reconnect.** Under `scripts/dev.sh` a saved file restarts the server.
  Studio shows a reconnect message at the top of the page when a request gets
  no answer, or answers `502`, `503` or `504`. The Vite dev proxy answers `502`
  while the server is down.
  - Studio then calls `GET /api/me` with a back-off of 0.5, 1, 2 and then 4
    seconds, with no limit, until the server answers with any other status.
  - Then the resource list, the diagnostics, the open resource and its Uses
    and Used by lists load again, and the message goes away. The names load again
    too.
  - An unsaved draft stays on screen, with the version it was based on. If the
    file changed in the meantime, the next save gets a `409`. Studio does not
    resend a save that got no answer, so the author clicks Save again.
  - Save and Delete are off while the message shows.
  - A `401` from a reload starts sign-in, as elsewhere.
- **Reference pickers.** A property that the schema marks with
  [`x-axis-reference`](resource-files.md#schema-keywords) shows as a select over
  the names that the [names route](authoring.md#names) lists, in place of a text
  box.
  - The select is searchable, and it matches a typed name without regard to
    letter case. A field or step part lists the fields of the entity, or the
    steps of the process, that the draft names. A parameter or column part
    lists the parameters or columns of the data source that the draft names,
    and an operation part lists the operations of the connector it names. An
    outcome part lists the outcomes of the task steps that have the step name
    the draft gives, in every process, without repeats.
  - A map whose keys carry a mark, such as a trigger's `parameters`, shows one
    row for each entry, in the order of the file. A row has a key select, a
    value box and **Remove**. The key select offers the names that no other
    row uses. **Add** appends the first name that is not used and is off when
    none is left. A parameter's value box follows its type: a number box for
    `integer` and `decimal`, a checkbox for `boolean` and a text box for the
    rest. Picking another key for a row keeps its value when the value fits
    the new type. A `null` value is edited in the JSON view.
  - A value that is not one of the names stays in the select as the first
    option, tagged **unknown**, so nothing is lost. The select takes only the
    listed names. To set a name that is not listed, the author edits the value
    in the JSON view.
  - Until the names load, the property shows a text box.
  - The names load with the application. They load again after a save, a text
    write, a delete and a reconnect.
- **Label texts.** A label is an object whose only property is `textKey`. It
  shows the key and one text box for each locale of the names, in place of the
  key alone. The **Write texts** button writes every locale in one call through
  the [text write route](authoring.md#text-writes). The label shows the texts
  of the key from the names, and the boxes load again when the author types
  another known key. Until the names load, the label keeps the reference picker.
  - A new label gets the key
    `<kind folder in camelCase>.<name in camelCase>.<path segments without the final textKey, joined with '.'>`,
    such as `pages.myTasks.title`. It adds `2`, `3` and so on when the key is
    taken. The author can change it. An empty `textKey`, as a new resource's
    starter leaves it, counts as no key and gets the same suggestion.
  - The text is written at once, when the author clicks **Write texts**, and
    not on Save. Then the key goes into the draft and is saved with the next
    Save. An unused key does no harm. A write that fails shows a message and
    leaves the draft as it was. The button is off while it writes, while the
    reconnect message shows, and while the key is empty or holds `/`.
  - A text write counts as a change of the folder, as a save does. The
    diagnostics panel shows the diagnostics that it answered, and the names and
    the Releases list load again.
  - A key that other labels use shows how many, from `usedBy` in the names
    answer, so the author knows the edit changes them too.
- **Expression editor.** A string property that the schema marks with
  `x-axis-expression` shows a code editor in place of a text box.
  - It is CodeMirror 6, about 100 KB gzipped, in a chunk that loads only when
    Studio first shows an expression. It highlights the
    [expression grammar](expressions.md#grammar).
  - It checks the text 300 ms after the last keystroke, through the
    [expression check route](authoring.md#expression-check). Only the latest
    answer is shown. It underlines the range from `start` to `end`, shows the
    error code and message under the editor, and shows the result `type` when
    there is no error.
  - **Completion.** Typing a word, or Ctrl+Space, opens a list of the names in
    scope. The arrow keys and Enter pick an option. The names come from the
    [names route](authoring.md#names), loaded with the application and again
    after a save, a delete and a reconnect.
    - The fields of the entity at the place: the entity being edited, a policy
      grant's `entity`, or a data source's or process's `entity`.
    - A data source filter offers its parameters, and a rule offers its own.
    - The application's rules, with module rules as `<module>:<name>`, and the
      functions and aggregates of the [expression language](expressions.md#functions).
    - `currentUser()` only in a policy grant's filter. Other expressions, such
      as triggers, migrations and endpoints, offer the rules and the functions only.
    - A function or rule inserts with its brackets and puts the cursor inside
      them. `now()` and `currentUser()` put the cursor after them.
    - When the names do not load, the list offers the functions only. Typing and
      Save keep working.
  - The check uses the saved resource, so a draft path that is not saved yet may
    have no answer. A property that the saved object lacks has one: a new
    `requiredWhen` on a saved form field is checked before the first save. A
    place inside a new field or item that the file does not hold has none.
    When there is no answer, or the check answers `400` or
    `401`, the last result stays on screen. A `401` does not start sign-in, so
    the draft is kept. The editor never blocks Save.
- **Entity designer** _(built)_. An `entity` resource opens in a designer on
  the Form tab in place of the generic form. The JSON tab stays and edits the
  same draft. See
  [D54](../decisions/d54-the-entity-designer-and-the-policy-matrix.md).
  - The header shows the entity's name, its label with a text box per locale,
    and a display field picker. The picker lists the field names of the draft.
    It does not show `id`, `kind` or `formatVersion`.
  - The fields are a table. Its columns are name, label, type, required,
    unique and a settings column. A field's label is a picker of the existing
    text keys. The settings column shows only what the row's type takes:
    `maxLength`, `precision` and `scale`, `values`, `target`, `expression`,
    `sequence`, `creator` and `sensitive`. The settings follow the
    [field types](resource-files.md#entity-field-types-and-constraints), so a
    setting a type does not take cannot be edited.
  - Changing a field's type drops the settings the new type does not take and
    keeps the rest. It does not add an empty `values` or `target`, and Validate
    reports the missing setting.
  - Clearing a field's label, the display field, `maxLength`, `precision`,
    `scale`, `sequence`, `expression` or the last enum value removes the key.
  - A computed field's `expression` and each validation's expression use the
    [expression editor](../decisions/d52-reference-pickers-label-texts-and-the-expression-editor.md).
    A validation also has a message key picker and a field picker, and can be
    added and removed.
  - Fields can be added, removed and moved up or down. Each of these replaces
    the `fields` array.
  - Every input keeps its `studio-field-<pointer>` id, and each row has the id
    of its field, so a diagnostic opens at its input, or at the row when the
    row does not show the setting.
  - A module entity, such as `departments/Department`, opens in the designer
    read-only, from the [module resource route](authoring.md#routes). Every input is
    off, and it has no Add field, Remove, Move, Add validation, Save or Delete
    button, no JSON tab and no Uses or Used by lists. A label shows its text key.
- **Migrations from the designer** _(built)_. The designer compares
  with the entity as Studio loaded it. "Loaded" means as Studio read the
  entity, not the active release. It asks a question for a change to a loaded
  field and adds the migration change that goes with the answer:

  | Change to a loaded field                       | Question                                             | Migration added                                           |
  | ---------------------------------------------- | ---------------------------------------------------- | --------------------------------------------------------- |
  | Rename                                         | Keep its data?                                       | `renameField` with `from` and `to`.                       |
  | Remove                                         | Drop its data?                                       | `dropField`. Answering No keeps the field.                |
  | Type change                                    | A `convert` expression                               | `convert` with the author's expression.                   |
  | Narrower `maxLength`                           | A `convert` expression                               | `convert`.                                                |
  | Removed enum values                            | A `convert` expression that maps them to kept values | `convert`.                                                |
  | Now required, or a new required field          | Fill the nulls? A `fill` expression                  | `fill`.                                                   |
  | To or from `enum`, or `unique` added           | None                                                 | None. The designer warns, and Validate reports `AXC0016`. |
  | Rename or remove of a `child-collection` field | None                                                 | None, because it has no column.                           |
  - The migration resource is named `<Entity>Change<UTC yyyyMMddHHmmss>`, for
    example `PurchaseRequestChange20261020093000` in
    `migrations/purchase-request-change20261020093000.json`. It holds the
    changes of one save in edit order. It never edits an existing migration.
  - The save sends the entity patch and the migration create in one
    [`changes` call](authoring.md#changes). A `409` keeps the draft, as for a
    single save.
  - Validate still runs every activation check, so a missing migration shows
    before a publish.
  - The questions come when the author clicks Save, one at a time, in the
    order in which the author first edited each loaded field. A chain of
    renames, such as a to b to c, counts as one rename from a to c. A field
    added in the same session asks nothing, except a `fill` question when it is
    required. Those questions come after the loaded fields, in row order. A new
    row with the name of a removed loaded field counts as that loaded field.
  - A rename asks Keep data, Drop data or Cancel. Keep data adds
    `renameField`, and Drop data adds `dropField` for the old name. Cancel
    stops the whole save and keeps the draft. A removal asks Drop data or Keep
    field. Keep field puts the field back where it was, and the save goes on
    with no migration for it.
  - The questions of one field stay together: its rename or removal, then its
    `convert`, then its `fill`. A `convert` or `fill` after a rename uses the
    new name, and several edits of one field ask once. When a rename is
    answered Drop data, the `convert` question is skipped, and a required field
    counts as new, so its `fill` is still asked. When a required field is
    renamed and the rename keeps its data, no `fill` is asked.
  - A `convert` or `fill` question asks for the expression in a plain text
    box. It offers the button that adds it, a Skip button that adds nothing for
    that field, and Cancel, which stops the whole save and keeps the draft.
    Studio does not check the expression. The save's diagnostics report a bad
    value at `/changes/{i}/value`.
  - A loaded field that switches to or from `enum`, or gains `unique`, shows a
    warning in its row as soon as the author edits it. The save adds no
    migration for it.
  - A rename typed in the JSON view reads as a removal plus a new field,
    because that view gives no row identity. The removal question still
    guards the data.
  - See [migrations](migrations.md#migrations) and the
    [resource file shape](resource-files.md#resource-file-shape).

- **Policy matrix** _(built)_. One view per application shows who
  may do what.
  - Roles are the rows. The columns are the entities without child entities,
    the data sources, the processes and the endpoints.
  - A cell shows the OR of the role's actions from all its policies, as in
    [combining grants](security.md#combining-grants).
  - A cell opens its grants. Each shows its actions, its filter in the
    expression editor and its fields in the field picker.
  - An edit writes to `<Role>Access`, the PascalCase role name plus
    `Access`. So `department-manager` writes `DepartmentManagerAccess`. The
    policy is created when it is missing, with `roles` set to that role only.
  - A grant from a policy that names several roles shows as shared and opens
    that policy. So does a grant from another policy of this role. A
    `<Role>Access` that names other roles counts as shared too. Its row is
    read-only, with a message.
  - Only the actions a target takes can be chosen. An entity takes `read`,
    `create`, `update` and `delete`. A data source takes `read`. A process
    takes `start`, `inspect`, `cancel`, `retry` and `resolve`. An endpoint
    takes `invoke`. Filter and fields are only on entity grants.
  - The view reads the [policy matrix route](authoring.md#policy-matrix).
  - A Resources / Policy matrix switch sits under the application picker. The
    matrix view is kept in the address as `&view=policies`, so a reload or a
    shared link reopens it. The diagnostics panel shows in both views.
  - Selecting a cell opens its grants under the table. A cell lists the
    role's combined actions. A data source cell that lacks the `read` of its
    root entity warns and names that entity. A read-only row shows its reason.
    A shared grant is tagged Shared, and its Open policy link opens that policy
    in the Resources view.
  - In an editable row, the cell's own grants are editable. They are the
    grants of `<Role>Access` that name exactly this target. The panel reads
    that policy with the resource route, so every edit fits the file that a
    save patches.
    - Each grant has checkboxes for the actions its target takes, and nothing
      else. An action the target does not take stays in the file and shows as
      a warning tag, and only Validate reports it.
    - An entity grant also edits its record filter in the expression editor
      and its fields in a field picker. An empty field list means all fields,
      so clearing the picker removes the `fields` key.
    - Add grant adds a grant for the cell's target. Remove deletes a grant,
      but not the policy's last one.
    - Save is off while a grant has no ticked action, and while nothing
      changed.
  - A save writes one file. It sends one patch with the version the panel
    loaded, and the patch changes only what the author edited. When the role
    has no `<Role>Access`, Save creates `policies/<role>-access.json` with
    `roles` set to that role only. A stale version shows a conflict message,
    keeps the draft and offers Reload, which discards it.
  - After a save, the diagnostics panel, the matrix and the names load again,
    and the selected cell stays open. Selecting another cell, or a matrix that
    loads again after another write, discards an unsaved draft.
  - A shared grant, a grant of a read-only row and a grant in any other policy
    stay read-only. Splitting a shared policy is not done here.
  - See [roles and policies](roles-and-policies.md).
- **Editors by kind.** Studio uses a typed editor for the kinds listed here, the entity
  designer for `entity`, and the generic form for every other kind. The seed editor,
  the connector editor, the scenario editor, the page editor, the endpoint mapping rows, the form steps panel, the text
  editor, the process editor's step list, with its decision, end, task and operation panels, and the entity designer are
  built. The last column lists what still needs the JSON view, because the form cannot show it.

  | Kind          | Editor                                                        | Still needs the JSON view                                             |
  | ------------- | ------------------------------------------------------------- | --------------------------------------------------------------------- |
  | `application` | Generic form                                                  | Nothing                                                               |
  | `entity`      | Entity designer                                               | Nothing                                                               |
  | `page`        | Typed editor, Design tab (partly built, see Page designer)    | Nothing                                                               |
  | `form`        | Generic form, typed steps panel, Design tab (planned for M13) | Nothing                                                               |
  | `site`        | Generic form                                                  | Nothing                                                               |
  | `process`     | Typed editor with the process graph                           | Nothing                                                               |
  | `rule`        | Generic form                                                  | Nothing                                                               |
  | `sequence`    | Generic form                                                  | Nothing                                                               |
  | `role`        | Generic form                                                  | Nothing                                                               |
  | `policy`      | Generic form                                                  | Nothing                                                               |
  | `dataSource`  | Generic form                                                  | Nothing                                                               |
  | `connector`   | Typed editor                                                  | Nothing                                                               |
  | `endpoint`    | Generic form, typed rows for `values`                         | Nothing                                                               |
  | `event`       | Generic form                                                  | Nothing                                                               |
  | `trigger`     | Generic form                                                  | Nothing                                                               |
  | `migration`   | Generic form                                                  | The `changes` list, because each change's fields depend on its `type` |
  | `seed`        | Typed editor                                                  | Nothing                                                               |
  | `scenario`    | Typed editor                                                  | Nothing                                                               |
  | `text`        | Typed editor                                                  | Nothing                                                               |

  The generic form sends a part to the JSON view when it holds `oneOf` or
  `anyOf`, when it holds `allOf` with an item that has more than `if`, `then`
  and `else`, is an object with no `properties`, such as a map, or has no type. A map whose keys carry a reference
  mark is the exception. The last column follows that rule. A new kind or property that the
  form cannot show goes to the JSON view until an editor covers it.

  - **Page editor** _(built)_. The Form tab shows the page's name, its title
    with a text box for each locale, and a list of its widgets, each with its
    type and a summary such as `table · PurchaseRequestList`. A panel per
    widget type, `table`, `form`, `taskInbox`, `processInspector`, `kpi`,
    `chart`, `detail`, `columns`, `tabs` and `section`, shows the properties the widget has today. The `columns`, `tabs` and `section` panels edit the width of each column, the label of each tab, and the title and collapsed state of a section. A `table` panel has entity and data source
    pickers, then a `rowActions` list of navigate or delete actions and a
    `toolbarActions` list of navigate actions. A delete action has a label and
    an optional `visibleWhen` expression. A `form` panel has entity and form
    pickers and its `actions`, each a `startProcess` action with a process
    picker and a label, or a navigate action. A
    `taskInbox` panel has no properties. A `processInspector` panel has a
    process picker. A `detail` panel has the entity and form pickers and the
    actions of a form, and a record picker between them. The widget is described
    in [detail, chart and KPI widgets](detail-chart-kpi-widgets.md), per
    [D64](../decisions/d64-detail-chart-and-kpi-widgets.md). The record picker
    lists the page's parameters that are required and of type `reference`. When
    the widget names an entity, it lists only the parameters whose `target` is
    that entity, ignoring letter case. When it names a form, it lists every such
    parameter. Picking another entity keeps the record, and the picker shows a
    record that matches no listed parameter as unknown. The compiler reports it
    after the save. The JSON view stays as a second view of the same draft.
    - **Type change.** Choosing another type removes the known properties the
      new type does not take, because the compiler rejects them. A switch to
      `processInspector` adds an empty `process`, which the schema requires. A
      switch to `detail` adds an empty `record`, which the schema also requires.
      A switch to `kpi` or `chart` adds the empty keys its type requires. A new
      chart starts with the `bar` kind and one measure with an empty name and
      label, because the schema accepts only `bar`, `line` or `pie` and needs
      at least one measure.
    - **KPI and chart panels.** A `kpi` panel has a data source picker, a
      measure picker and a label with a text box for each locale. Add
      comparison adds a comparison with its own measure picker and label, and
      Add navigate action adds a navigate action with the fields above. Each
      has a Remove button. A `chart` panel has a data source picker, a kind
      picker over `bar`, `line` and `pie`, a category picker, a title, and a
      list of measures, each with a picker and a label, with Add measure and
      Remove measure. Remove measure is disabled when one is left. The data
      source picker of a KPI lists only data sources with a total row, and the
      one of a chart lists only data sources with exactly one group field,
      which are the only choices the compiler accepts. The column pickers read
      the chosen data source from the names. The category lists its group
      field, and the measure pickers list its measures. A chart's measure
      pickers list only measures of type `integer` or `decimal`, because the
      names give a `count` the type `integer` and a `sum`, `min` or `max` the
      type of its field. Picking another data source keeps the names already
      set, and a name the new data source does not project shows as unknown.
      The compiler reports it after the save. While the names have not loaded,
      the column pickers are plain boxes. Both panels also have a Parameters
      list, with one picker for each parameter of the data source. Each picker
      lists the page parameters of the same type. Clearing a picker removes its
      key, and a key the list does not show stays as it is. Clearing the last
      one leaves an empty `parameters` object. A binding to a missing or
      wrong-type page parameter shows as unknown. The compiler checks the
      target, the enum values and the required flag after the save. The editor
      does not check that a `pie` has exactly one measure. See [widget
      binding](data-sources.md#widget-binding) and
      [D64](../decisions/d64-detail-chart-and-kpi-widgets.md).
    - **Exclusive bindings.** The compiler accepts exactly one of a table's
      entity and data source, and one of a form's or a detail's entity and form.
      Picking one clears the other.
    - **Navigate actions.** A navigate action
      ([D63](../decisions/d63-navigate-actions-and-page-parameters.md)) has a
      page picker, a label with a text box for each locale, an optional
      `record` expression and parameter expressions. The page picker lists
      every page, including the current one, because a page may open itself
      with other parameters. A table offers only navigate actions. A form
      offers Add action for `startProcess` and Add navigate action. An empty
      Record box removes the `record` key.
      - **Parameter expressions.** When the names list the target page, the
        action shows one expression box for each parameter the page declares,
        with the parameter's type and `required` beside it. An empty box
        leaves the parameter out. A value the page does not declare stays
        visible with a note and Remove, because the compiler refuses it.
        Picking another page removes the values the new page does not declare,
        ignoring letter case. While the names have not loaded, or when the page
        is not in them, the action keeps rows where the author types the
        parameter name, and a wrong name shows as a diagnostic after the save.
    - **Pickers.** The action's process picker lists every process. It does
      not check what the choice holds, so a wrong choice shows as a diagnostic
      after the save.
    - **Page parameters.** A Parameters list sits between the title and the
      widgets. Each parameter has a name, a type picker over `text`,
      `integer`, `decimal`, `boolean`, `date`, `date-time`, `enum` and
      `reference`, a Required switch and Remove. Add parameter appends a `text`
      parameter with an empty name. A `reference` parameter also has a target
      entity picker and an `enum` parameter has its values. A type change
      removes the `target` or the `values` that the new type does not take.
      Switching Required off removes the `required` key. The names answer lists
      each page's parameters, which is how a navigate action knows them
      ([D53](../decisions/d53-typed-editors-and-the-process-graph-in-studio.md)).
      `parameters` that is not a list points to the JSON view.
    - **Widget tree** _(built)_. The editor shows the page's widgets as
      nested rows. A container shows its columns, tabs or section body as
      nested lists, and a row opens that widget's panel at its own pointer,
      such as `/widgets/0/columns/1/widgets/0`. Every list has an Add widget
      picker with all ten types. A new `columns` starts with two columns of
      width 6, a new `tabs` with one tab and a new `section` empty. Every row
      has Move up, Move down and Remove, which replace only the list that holds
      the widget, and a Move to picker. Move to lists every other widget list
      as a path such as `Page › columns 1 › Column 2`, leaves out the widget's
      own list and the lists inside it, and replaces the page's `widgets` in
      one change. A container's type is fixed, and its panel edits the rest. The
      editor does not check the depth limit or an empty list, so the
      compiler reports them after the save. A widget keeps every key the editor
      does not know when it moves.
    - **Container panels** _(built)_. Each container type has a panel under
      its fixed type. A `columns` panel shows one width box from 1 to 12 for
      each column. The editor does not check that the widths add up to 12, so
      the compiler reports it after the save. Add column adds a column that
      takes what is left of 12, or 1 when nothing is left, and keeps the other
      widths. Remove column drops a column with the widgets in it. A `tabs`
      panel shows each tab's label as a key with one text box per locale, and
      has Add tab and Remove tab. A `section` panel shows the title the same
      way, and a Collapsed switch. Switching Collapsed off removes the
      `collapsed` key, because a section starts expanded. Remove column and
      Remove tab are disabled when one is left, because the schema needs at
      least one. A key the panels do not show, on a column, a tab or a section,
      stays in the saved file. A container whose parts are malformed, such as
      `columns` that is not a list, points to the JSON view. See [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).
    - **Unknown keys.** A key the editor does not show, on the page or on a
      widget, is saved unchanged. A widget of a type with no panel shows its
      type and points to the JSON view.
  - **Process step editor.** The Form tab opens the process with its name,
    entity and optional start condition, and its start step. The JSON view
    stays as a second view of the same draft.
    - **Step list** _(built)_. The steps show as cards in file order. Each card
      shows the step's name and type, with move up, move down and remove. Add
      step appends a step of the chosen type, with an empty name.
    - **Type change** _(built)_. Changing a step's type removes the keys the
      new type forbids and adds the keys it requires, empty. The other keys,
      including unknown keys, stay.
    - **Decision and end panels** _(built)_. A decision shows its branches,
      each with a `when` expression and a target, and its `otherwise`. A branch
      can be added and removed. An end step shows only its name and type.
    - **Transitions** _(built)_. The start step, `next`, a branch's `next`,
      `otherwise` and an outcome's `next` pick their target from the step names
      of the unsaved draft. A target that is not a step stays visible and is
      tagged unknown. Removing a step does not rewrite the transitions that
      name it, so the compiler reports them.
    - **Task panel** _(built)_. A task shows its label and its form as pickers
      over the application's text keys and forms. The assignee has a User and
      Role switch. User shows the expression editor and Role shows a role
      picker. A switch removes the other key, so the step never holds both.
      `dueIn` is a text box, and clearing it removes the key. The outcomes are
      rows of name, label and target step, with add and remove. A key the
      panel does not show stays as it is. A diagnostic path inside a task's
      label, assignee, form, `dueIn` or outcomes opens the Form tab at that
      field.
    - **Operation panel** _(built)_. An `operation` step shows an operation
      choice, `updateRecord`, `callConnector`, `raiseEvent` or **Package
      operation**, above its transition. Changing the operation removes only
      the keys the new one forbids. It adds no empty key, so the compiler
      reports what is still missing. Every other key, including unknown keys,
      stays.
      - `updateRecord` shows `set` as rows of a field of the process's entity
        and an expression. `callConnector` shows the connector and its
        operation as pickers, `input` as rows of a typed input name and an
        expression, `output` as rows of a typed connector output name and an
        entity field, and `retry`. `raiseEvent` shows the event as a picker
        and `fields` as rows of a payload field of that event and an
        expression.
      - A package operation is typed in a text box as
        `<package>/<operation>`, because no route lists the installed
        operations. It shows `input`, `output` and `retry` as `callConnector`
        does. The names route lists no connector input or output names, so
        those are typed too.
      - `retry` is attempts and backoff. Clearing both removes `retry`.
      - While the application's names are not loaded, every picker is a name
        box. A key the step's operation forbids is not shown as rows. It says
        to edit it in the JSON view, and a diagnostic path inside it opens the
        JSON view.
  - **Connector editor** _(built)_. The Form tab shows the base URL binding,
    the auth type with its secret binding, `timeout`, `idempotency`, and the
    operations as a list. The JSON view stays as a second view of the same
    draft.
    - **Bindings.** A binding is a name box, labelled as a URL or a secret
      binding. It is not a pick from a list, because no route lists a tenant's
      binding names. No value of a binding is shown or stored: the editor
      reads and writes only the resource and schema routes, and the values stay
      in tenant settings.
    - **Auth.** Choosing a type removes the keys the new type forbids, such as
      `token` for `header`, and adds the keys it requires, with an empty name.
      Clearing the type removes `auth`.
    - **Operations.** A card per operation shows its name, method, path and
      `lookup`, which is picked from the connector's other operations. Its
      input and output fields are rows of name, type and required. Operations
      and rows can be added and removed. A `GET` with input rows keeps them and
      shows a hint, and the compiler reports the error.
    - **Unknown keys.** A key the editor does not show, in the file, in an
      operation or in `auth`, is saved unchanged.
  - **Scenario editor** _(built)_. The Form tab shows the scenario's name and
    `now`, then its users with their roles, its stubs, and its steps as a list
    with a panel per step type. The JSON view stays as a second view of the
    same draft.
    - **Steps.** Each step has a type. Changing the type keeps the properties
      the new type takes and drops the others. Keys the editor does not show
      stay in the file. A step can be removed or moved up or down.
    - **Pickers.** Entities, processes, roles and connectors are picked by
      name. A `record` is picked from the aliases that earlier `createRecord`
      steps save. A `step` and its `outcome` come from the processes that the
      record started.
    - **Values.** The fields of the step's entity show as typed cells. A value
      the entity does not name stays as a text row. A step whose entity is not
      in the folder edits its `values` as a JSON object, and the text is saved
      only when it is one.
  - **Seed editor** _(built)_. The Form tab shows the seed's name, entity and
    `sync`, then its records as a grid, with a row per record and a column per
    field of the seed's `entity`. The JSON view stays as a second view of the
    same draft.
    - **Columns.** The input fits the field type: a text box for `text`,
      `date`, `date-time` and `reference`, a text box for `integer` and
      `decimal` that stores a number, a true/false choice for `boolean` and a
      choice of `values` for `enum`. A `reference` is a text box that checks
      the UUID form, and the record picker is separate work. Each input follows
      the [record API's value rules](record-api.md#request-bodies-and-values).
    - **No column.** A computed field, a `sequence` field, a `creator` field
      and a `child-collection` field get no column, because seeding rejects a
      value for the first three, and child collections stay as the file has
      them. A value the grid does not show, including an unknown key, is saved
      unchanged.
    - **Rows.** **Add record** adds a row with a new record id, and **Remove**
      deletes the record from the draft. Clearing a cell removes its key from
      the record's `values`. The grid never writes `null`.
    - **Save.** A cell whose value would fail seeding, because it has the wrong
      type, is out of bounds or is missing a required value, shows an error
      and turns Save off, also when the value comes from the JSON view.
    - **Fallback.** The entity is found by name among the folder's `entity`
      resources, ignoring letter case. When it is not there, for example a
      module entity or a typo, the seed uses the generic form and the JSON view.
  - **Text editor** _(built)_. A `text` resource opens as one table of keys
    with a column per locale, and a search box.
    - **Columns.** One row per key, sorted by key, and one column per locale in
      the order of the folder's `text` resources. Only the open resource's
      column is editable. The other locales show beside it as read-only text,
      read from their files. To edit another locale, open its `text` resource.
    - **Missing keys.** A key that the open locale lacks shows an empty cell
      marked missing. Typing in it adds the key to this locale. A locale that
      the file lacks the key in shows a missing mark in place of the text.
    - **Search.** The box keeps the rows whose key, or text in any locale,
      contains the search, ignoring letter case.
    - **Cells.** A cell edit goes into the draft, and **Save** sends one JSON
      Patch for the open resource only. Clearing a cell saves an empty string
      and keeps the key, because every locale holds the same keys
      (`AXC0027`).
    - **Add key.** The row below the table takes a key and a text for each
      locale. It writes the key to every locale through the
      [texts route](authoring.md#text-writes) at once. The key cannot be empty,
      cannot contain `/`, and cannot already be in the table. The editor takes
      the open file's new version from the route's answer and adds the key to
      its loaded copy and draft, so unsaved cell edits stay and the next Save
      does not meet a stale version. The diagnostics and the names reload.
    - **JSON view.** It stays as the second view of the same draft.
    - **Removing a key** is not built. No route removes a key from every
      locale yet.
  - **Endpoint `values`** _(built)_. The endpoint keeps the generic form for
    its entity, process, input and output. Its `values` show as rows of a field
    and an expression in the expression editor. The field is picked from the
    fields of the endpoint's entity, found by name among the application's
    names, and is typed while those are unknown. **Add mapping** lists the
    fields that have no row. Renaming a row's field keeps its place in the
    file, and a value that is not an object is left to the JSON view.
  - **Form steps** _(built)_. The form keeps the generic form for its entity,
    sections and fields, which includes an expression editor for each
    `visibleWhen`, `requiredWhen` and `readOnlyWhen`. Its `steps` show as cards
    with a title, a section picker, **Move up**, **Move down** and **Remove**.
    **Add step** appends a card. The picker lists the sections that have a
    `name`, so an unnamed section is not offered. Removing the last step removes
    `steps`, and the form goes back to one page. A condition offers the fields
    that the form lists, because a condition reads only those. A `steps` value
    that is not a list is left to the JSON view.

- **Process graph** _(built)_. A process shows a
  drawn graph of its steps and transitions above its editor, see
  [D53](../decisions/d53-typed-editors-and-the-process-graph-in-studio.md).
  It is read-only. The author edits steps in the list.
  - Dagre, `@dagrejs/dagre`, lays out the graph, and React Flow,
    `@xyflow/react`, draws it. Their sizes are about 30 KB and 50 KB min+gzip.
    The SPA loads them in their own chunk when a process opens. M14's process
    designer builds on both.
  - The graph and the step editor stay in step. Both read the same unsaved
    draft, so an edit in the list or the JSON view redraws the graph at once.
  - A node opens its step at `/steps/{i}`, and the editor scrolls to that step's
    card. An edge opens its transition: the outcome, the branch, `otherwise` or
    `next`.
  - The node of the open path is current, and it has `aria-current`. Its
    highlight follows the open path, not the focus in the step list.
  - Nodes are reached with Tab, in step order, and opened with Enter. Each node
    has a text label with the step's name and type. Edges are reached with the
    pointer only. A keyboard user reaches each transition through the pickers
    in the step list.
  - A transition to a step that does not exist draws no edge. The graph never
    invents a node.
  - A step of the open process is marked when a diagnostic of the open resource
    names its path, `/steps/{i}` or a path inside it. A marked node has an
    error-coloured border, "has diagnostics" in its text label, and
    `data-marked`. The marks come from the panel's last loaded list, so they
    change on a load, a save or a request, not on every edit.
- **Page designer** _(partly built, planned for M13)_. The page editor gets a Design tab
  that builds a page by drag and drop, per
  [D65](../decisions/d65-a-visual-page-designer-on-a-canvas-drawn-by-the-sites-widgets.md).
  The form designer is partly built and planned for M13, see
  [D66](../decisions/d66-a-visual-form-designer-for-form-resources.md), and the
  process designer for M14, see
  [D46](../decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md).
  The process graph is read-only, and not a designer.
  - **Design tab.** It sits next to the Form and JSON tabs and is the default
    tab. The three tabs edit one draft. _Partly built:_ the tab is there, but
    Form stays the default tab until the designer is complete. The canvas is
    mounted only while the Design tab is active, so it asks again when the
    author returns.
  - **Canvas.** It draws the unsaved draft with the site's own widget
    components. Tables, charts and other data widgets show placeholder rows and
    values made from the field types, as the widget previews do with fixtures.
    Actions run nothing. The canvas asks
    [the draft page metadata route](authoring.md#draft-page-metadata) 300 ms
    after the last edit and redraws from its answer. _Partly built:_ the canvas
    draws the page, with labels from the application's texts and no request to
    `/api/apps/`. It cancels every click and form submit, so a navigate action
    or a link leaves Studio where it is. A page parameter gets a placeholder
    value made from its type. A page the route cannot draw shows a short notice
    instead of widgets.
  - **Placeholders and diagnostic marks.** A widget that does not compile draws
    as a placeholder with its type and a line for each of its diagnostics, with
    the code and the message. A widget is marked when a diagnostic of the page
    names its `path` or a path inside it, of any severity. So a container is
    marked too when a widget inside it has a diagnostic, even in a closed section
    or an inactive tab. A marked widget has an error-coloured border, "has
    diagnostics" in its text label, and `data-marked`, as a node of the process
    graph has. The marks come from the route's latest answer, so they follow the
    unsaved draft.
  - **Selection.** A click on the canvas or a pick in the widget tree selects a
    widget in both. The selected widget has `aria-current`. _Partly built:_ a
    click selects the innermost widget under the pointer, and a click on the
    empty area of a container selects the container. A pick in the tree outlines
    the widget on the canvas in the primary colour, and the selected frame has
    `aria-current`. Every frame is a Tab stop, and Enter or Space selects it. A
    frame's name is its type and summary, such as `table · PurchaseRequestList`,
    with "has diagnostics" when it is marked. The selection is kept across the
    Design and Form tabs while the resource stays open. It is not in the URL's
    `path`, because `path` makes the editor open the Form tab.
  - **Property panel.** It sits beside the canvas and shows the widget's
    existing panel. An edit redraws the canvas. Each action draws as a button
    on the canvas, and selecting it opens its action in the panel, including a
    `delete` action. _Partly built:_ the side pane of the Design tab holds the
    whole widget tree and the panel of the selected widget, the same as the Form
    tab. An edit redraws the canvas through the 300 ms draft post. On the
    canvas, every action of a table (row and toolbar), a form, a detail and a
    KPI draws as a button, also one the site would hide, such as an action whose
    `visibleWhen` fails on placeholder data or a form action on an unsaved
    record. A click on the button, or Enter or Space on it, selects the action
    and not its widget. The selection is the JSON Pointer of the action, such as
    `/widgets/0/rowActions/1`, or `/widgets/0/navigate` for a KPI. The button has
    `aria-current` and an outline in the primary colour, and the widget tree
    keeps its widget selected. The panel then shows the whole widget panel with
    the action's card marked by `aria-current` and scrolled into view. Adding a
    navigate, `startProcess` or `delete` action in the panel selects the new
    action, and removing an action selects its widget. The button runs nothing,
    so it does not navigate, start a process, open a delete confirmation or call
    `/api/apps/`. On the canvas a KPI's navigate action is a link-style button
    on the label line, and the tile is not a link. At phone width, a table's row
    actions fold into a menu that the canvas cannot open, because it cancels the
    click. There the author selects a row action from the tree and the panel.
  - **Palette.** It lists the ten widget types. The author drags one onto the
    canvas, or picks one and inserts it after the selected widget or into the
    selected container. A type is disabled, with the reason, when the page
    cannot take it under a limit of
    [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md):
    a second table, a second form, task inbox or process inspector, or a
    container past 4 levels. _Partly built:_ the side pane of the Design tab
    lists the ten types above the widget tree. Enter, Space or a click inserts a
    new widget after the selected widget, at the end of the selected container's
    first list, or at the end of the page's list on an empty page. The new
    widget is selected, and a live region names the list and the position where
    it landed. A type the page cannot take is disabled and shows its reason.
    The author can also drag a type with the pointer onto the canvas. A drop on
    the upper half of a widget inserts before it, and a drop on its lower half
    inserts after it. A drop on a widget list, which is the page's own list or
    the list of a column, a tab or a section, empty or not, appends to that
    list, and a drop between two widgets lands in the list, so it appends. The
    innermost target wins. A container that does not compile yet, such as a new
    empty section, is drawn as a placeholder with a drop area for each list its
    draft has. A drop that a page limit forbids inserts nothing, and the live
    region gives the same reason that is shown beside the disabled type. An
    allowed drop selects the new widget, and the live region names the list and
    the position where it landed. A type that is disabled cannot be dragged. The
    library's own spoken messages are off, so the live region is the only voice,
    and a keyboard user inserts with Enter or Space as above. Dragging an
    existing widget is not built yet.
  - **Moves.** The author drags a widget to reorder it or to move it into or out
    of a container. The keyboard sensor does the same, and a screen reader
    hears where the widget lands. The Move to picker stays.
  - **Data panel.** It lists the entities and the data sources. Dropping an
    entity offers a table, a form or a detail. Dropping a data source offers a
    table, and a chart with one group field or a KPI with a total row.
  - **Width switch.** It draws the canvas at desktop width, which fills the
    canvas area, or at phone width, which is 375 px. At phone width the columns
    stack to full width and tables use their phone layout, even in a wide
    browser window. The choice goes back to desktop when the author returns to
    the Design tab. The live site keeps its own rule, which is the window width.
  - **Undo.** Undo and redo buttons, Ctrl+Z and Ctrl+Shift+Z, or Cmd on macOS,
    cover every edit of the draft in the three tabs. The history is kept in
    memory, up to 100 steps, with one step for each box. It is cleared by a
    reload, a conflict reload or opening another resource. A save keeps it.
    Studio's undo replaces the browser's own inside the editor's boxes.
    _Built:_ it covers pages. The buttons sit beside Save and are off when
    there is nothing to undo or redo, and while a save runs. A step ends when
    the author edits another property or leaves the box, and a pick in a
    dropdown or a button action is always a step of its own. The keys also work
    when nothing has focus, but not when the focus is outside the editor. A
    reload after a reconnect clears the history too. Other kinds of resource
    have no undo yet.
  - **Library.** Drag and drop uses `@dnd-kit/core`, `@dnd-kit/sortable` and
    `@dnd-kit/utilities`. The core package, which brings `@dnd-kit/accessibility`
    and `@dnd-kit/utilities` with it, measures 12.7 KB gzipped in the lazy chunk
    `StudioPageDesign`. The chunk holds the canvas, the palette and the library,
    and loads when the Design tab opens. The build fails when the chunk is
    missing or is part of the main bundle. `@dnd-kit/sortable` comes with the
    moves of existing widgets.
  - **Format.** The designer writes only the order of the widget tree and the
    properties the format has. It writes no pixels, and widths stay in
    twelfths.
- **Form designer** _(partly built, planned for M13)_. The form editor gets a Design tab that
  lays out a form by drag and drop, per
  [D66](../decisions/d66-a-visual-form-designer-for-form-resources.md). It sits
  next to the Form and JSON tabs, is the default tab, and the three tabs edit
  one draft. It reuses the canvas, property panel, undo (100 steps, cleared by
  a reload) and `@dnd-kit` drag and drop of the page designer.
  - **Canvas.** It draws the unsaved draft with the site's form widget, in its
    sections and steps. Fields get placeholder values made from the field
    types, and a child collection gets placeholder rows. The Save button does
    nothing. The canvas asks
    [the draft page metadata route](authoring.md#draft-page-metadata) 300 ms
    after the last edit. For a form it answers the metadata of a form widget,
    with a `path` on each section, field and step.
    _Partly built:_ the Design tab is there, and Form stays the default tab
    until the designer is complete. The canvas opens the placeholder record, so
    each field shows a placeholder value, and no request goes to `/api/apps/`.
    Save, Next, Back and Cancel do nothing, because the canvas cancels every
    click and form submit. A click only selects. A form the route cannot draw
    shows a short notice. The tab shows the Used on list above the step switch
    and the canvas, with a property panel beside the canvas, and no palette or
    width switch.
    The canvas is mounted only while the Design tab is active.
  - **Step switch.** It lists the steps in order and shows the sections of one
    step. A form without `steps` has no switch.
    _Partly built:_ the switch is a segmented control above the canvas, with
    the step titles in order. A step that a diagnostic names is marked on its
    entry. The wizard bar and the Next and Back buttons of the form widget still
    draw, but do nothing.
  - **Condition marks.** No condition is evaluated, because the canvas has no
    record. Every field and section shows. An element with `visibleWhen`,
    `requiredWhen` or `readOnlyWhen` carries a mark that names each condition
    and shows its text from the draft. A diagnostic of the form marks an
    element as in the page designer: an error-coloured border, "has
    diagnostics" in the text label, and `data-marked`. _Partly built:_ a
    section or field has a frame with that border, label and `data-marked`. A
    mark shows the name of each condition and its text, such as "Visible when:
    status == 'resolved'".
  - **Field palette.** It lists the entity's fields that no section lists,
    child collections included. The author drags one into a section, or picks
    one and inserts it into the selected section by keyboard. Removing a field
    puts it back in the palette. The palette never offers a field already on
    the form, and a drop never copies one.
  - **Moves.** The author drags fields within and between sections, and
    sections within and between steps. To move a section to another step, drop
    it on that step in the switch, or use the Move to step picker. Every move
    has a keyboard way, and a screen reader hears where the element lands.
  - **Sections and steps.** The palette adds a section, with a unique `name`
    and a new title key that the label editor opens, and a step. A new section
    joins the selected step. Adding the first step names every unnamed section
    and puts all sections into it, so the form stays valid (`AXC0230`).
    Removing the last step removes `steps`.
  - **Property panel.** A field shows `readOnly` and its three conditions in
    the expression editor, which offers only the fields the form lists. A field
    that the entity marks `required` has no `visibleWhen` box. A computed,
    sequence or creator field shows `readOnly` on and locked. A section shows
    its title and `visibleWhen`. A step shows its title.
    _Partly built:_ a click, Enter or Space selects a field or a section. A
    click on a step selects it and shows its sections. The selected element has
    `aria-current` and an outline in the primary colour. The panel asks the
    author to select something when nothing is selected. The selection is kept
    across the Design, Form and JSON tabs while the resource stays open. The
    panel reads the entity flags from the canvas's latest answer, so it shows
    nothing about a field until the form is drawn. A required field that
    already has a `visibleWhen` shows no box for it. Remove it in the Form or
    JSON tab.
  - **Used on.** A list of the pages and process steps that use the form. It is
    the Used by list filtered to the `widget form` and `task form` relations,
    loads with the resource, and can be stale like the `usedBy` counts. An
    entry opens the resource at its path. One edit changes every one of them.
    _Built:_ the list sits above the canvas, loads once when the form opens,
    and shows each entry's relation, name and path.
- **Texts.** Its texts, including those of the lists above, are the platform
  texts under `shell.studio.*`, in `en` and `vi`, like the other shell texts.
