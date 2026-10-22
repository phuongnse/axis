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
  which is every kind except the manifest.
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
    `oneOf`, `anyOf` or a map, points to the JSON view. A text resource's
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
    steps of the process, that the draft names.
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
    have no answer. When there is no answer, or the check answers `400` or
    `401`, the last result stays on screen. A `401` does not start sign-in, so
    the draft is kept. The editor never blocks Save.
- **Entity designer** *(built)*. An `entity` resource opens in a designer on
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
- **Migrations from the designer** *(built)*. The designer compares
  with the entity as Studio loaded it. "Loaded" means as Studio read the
  entity, not the active release. It asks a question for a change to a loaded
  field and adds the migration change that goes with the answer:

  | Change to a loaded field | Question | Migration added |
  | --- | --- | --- |
  | Rename | Keep its data? | `renameField` with `from` and `to`. |
  | Remove | Drop its data? | `dropField`. Answering No keeps the field. |
  | Type change | A `convert` expression | `convert` with the author's expression. |
  | Narrower `maxLength` | A `convert` expression | `convert`. |
  | Removed enum values | A `convert` expression that maps them to kept values | `convert`. |
  | Now required, or a new required field | Fill the nulls? A `fill` expression | `fill`. |
  | To or from `enum`, or `unique` added | None | None. The designer warns, and Validate reports `AXC0016`. |
  | Rename or remove of a `child-collection` field | None | None, because it has no column. |

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
- **Policy matrix** *(built)*. One view per application shows who
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
  the connector editor, the scenario editor, the page editor, the endpoint mapping rows, the text
  editor, the process editor's step list, with its decision, end, task and operation panels, and the entity designer are
  built. The last column lists what still needs the JSON view, because the form cannot show it.

  | Kind          | Editor                                | Still needs the JSON view                                             |
  | ------------- | ------------------------------------- | --------------------------------------------------------------------- |
  | `application` | Generic form                          | Nothing                                                               |
  | `entity`      | Entity designer                       | Nothing                                                               |
  | `page`        | Typed editor                          | A widget or layout that M12 adds, until M12 gives each container a panel |
  | `form`        | Generic form                          | Nothing                                                               |
  | `site`        | Generic form                          | Nothing                                                               |
  | `process`     | Typed editor with the process graph   | Nothing                                                               |
  | `rule`        | Generic form                          | Nothing                                                               |
  | `sequence`    | Generic form                          | Nothing                                                               |
  | `role`        | Generic form                          | Nothing                                                               |
  | `policy`      | Generic form                          | Nothing                                                               |
  | `dataSource`  | Generic form                          | Nothing                                                               |
  | `connector`   | Typed editor                          | Nothing                                                               |
  | `endpoint`    | Generic form, typed rows for `values` | Nothing                                                               |
  | `event`       | Generic form                          | Nothing                                                               |
  | `trigger`     | Generic form                          | The `parameters` map of a data source trigger                         |
  | `migration`   | Generic form                          | The `changes` list, because each change's fields depend on its `type` |
  | `seed`        | Typed editor                          | Nothing                                                               |
  | `scenario`    | Typed editor                          | Nothing                                                               |
  | `text`        | Typed editor                          | Nothing                                                               |

  The generic form sends a part to the JSON view when it holds `oneOf` or
  `anyOf`, when it holds `allOf` with an item that has more than `if`, `then`
  and `else`, is an object with no `properties`, such as a map, or has no type. The last column follows that rule. A new kind or property that the
  form cannot show goes to the JSON view until an editor covers it.

  - **Page editor** *(built)*. The Form tab shows the page's name, its title
    with a text box for each locale, and a list of its widgets, each with its
    type and a summary such as `table · PurchaseRequestList`. A panel per
    widget type, `table`, `form`, `taskInbox` and `processInspector`, shows the
    properties the widget has today. A `table` panel has entity, data source
    and form page pickers. A `form` panel has entity and form pickers and its
    `startProcess` actions, each with a process picker and a label. A
    `taskInbox` panel has no properties. A `processInspector` panel has a
    process picker. The JSON view stays as a second view of the same draft.
    - **Type change.** Choosing another type removes the known properties the
      new type does not take, because the compiler rejects them. A switch to
      `processInspector` adds an empty `process`, which the schema requires.
    - **Exclusive bindings.** The compiler accepts exactly one of a table's
      entity and data source, and one of a form's entity and form. Picking one
      clears the other.
    - **Pickers.** The form page picker lists every page except the current
      one. The action's process picker lists every process. Neither checks
      what the choice holds, so a wrong choice shows as a diagnostic after the
      save.
    - **Widgets.** A page holds one widget today, so the list has no add,
      remove or reorder.
    - **Widget tree** *(planned for M12)*. The editor shows the page's widgets
      as nested rows. The author can add and remove a widget, move it up or
      down and move it into a container. Each container type has a panel. A
      `columns` panel edits the column widths. A `tabs` panel edits the tab
      labels, with one text box per locale. A `section` panel edits the title
      and the collapsed state. See [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).
    - **Unknown keys.** A key the editor does not show, on the page or on a
      widget, is saved unchanged. A widget of a type with no panel, such as one
      M12 adds, shows its type and points to the JSON view.
  - **Process step editor.** The Form tab opens the process with its name,
    entity and optional start condition, and its start step. The JSON view
    stays as a second view of the same draft.
    - **Step list** *(built)*. The steps show as cards in file order. Each card
      shows the step's name and type, with move up, move down and remove. Add
      step appends a step of the chosen type, with an empty name.
    - **Type change** *(built)*. Changing a step's type removes the keys the
      new type forbids and adds the keys it requires, empty. The other keys,
      including unknown keys, stay.
    - **Decision and end panels** *(built)*. A decision shows its branches,
      each with a `when` expression and a target, and its `otherwise`. A branch
      can be added and removed. An end step shows only its name and type.
    - **Transitions** *(built)*. The start step, `next`, a branch's `next`,
      `otherwise` and an outcome's `next` pick their target from the step names
      of the unsaved draft. A target that is not a step stays visible and is
      tagged unknown. Removing a step does not rewrite the transitions that
      name it, so the compiler reports them.
    - **Task panel** *(built)*. A task shows its label and its form as pickers
      over the application's text keys and forms. The assignee has a User and
      Role switch. User shows the expression editor and Role shows a role
      picker. A switch removes the other key, so the step never holds both.
      `dueIn` is a text box, and clearing it removes the key. The outcomes are
      rows of name, label and target step, with add and remove. A key the
      panel does not show stays as it is. A diagnostic path inside a task's
      label, assignee, form, `dueIn` or outcomes opens the Form tab at that
      field.
    - **Operation panel** *(built)*. An `operation` step shows an operation
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
  - **Connector editor** *(built)*. The Form tab shows the base URL binding,
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
  - **Scenario editor** *(built)*. The Form tab shows the scenario's name and
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
  - **Seed editor** *(built)*. The Form tab shows the seed's name, entity and
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
  - **Text editor** *(built)*. A `text` resource opens as one table of keys
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
  - **Endpoint `values`** *(built)*. The endpoint keeps the generic form for
    its entity, process, input and output. Its `values` show as rows of a field
    and an expression in the expression editor. The field is picked from the
    fields of the endpoint's entity, found by name among the application's
    names, and is typed while those are unknown. **Add mapping** lists the
    fields that have no row. Renaming a row's field keeps its place in the
    file, and a value that is not an object is left to the JSON view.
  - **Page editor and M12.** M12 adds layouts and widgets while this editor is
    built, see
    [D46](../decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md)
    and [D61](../decisions/d61-widget-trees-in-columns-tabs-and-sections-and-pages-at-phone-width.md).
    The page editor changes again then.
- **Process graph** *(built)*. A process shows a
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
- **Designers.** Visual page, form and process designers are not built yet.
  They are planned for M13 and M14, see
  [D46](../decisions/d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md).
  The process graph is read-only, and not a designer.
- **Texts.** Its texts, including those of the lists above, are the platform
  texts under `shell.studio.*`, in `en` and `vi`, like the other shell texts.
