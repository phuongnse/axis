# Design review 2026-11-04-entities: the owner's rules

This is an open design review. Work here follows the `axis-design` skill: one change at a time, only what the design needs,
no test changes on this branch, and logic left to the review's issue.

Topic: Studio's Entities page.

The prototype starts from the tag `design-review/2026-11-03-studio-settings`, so the owner keeps seeing the workspace and
the resource form that `main` has not built yet, with `main` merged in, first at 624ab6e1 and again at fd1bf541 (2026-11-05). Its own diff starts at the first merge.
Each entry is one agreed change: what the owner asked, in English, and what was built, for the docs change at the end
of the review.

In a merge of `main`, `main` is the standard for behaviour and the prototype for look. Where both built the same thing,
`main`'s API, data shape and logic are kept and the prototype's look and agreed additions are put back on them. A conflict
never keeps the prototype's logic over `main`'s. What the prototype's files lose is brought back (see "Lost in merges").
After a merge, run the type check, lint, and the tests of the shared widgets, and write the merge down below.

## State and how to continue

Last updated 2026-11-05 (merged `main` at fd1bf541). The review is open; the owner is reviewing it one step at a time.

- **Done:** the kind lists (one table, standard table steps 1 to 3, a table with too little room); the entity designer's
  sections, `AxisField` with required help, the required mark, the Publish dialog's fields, the relaxed points in the Modules
  table, and step 2, the field drawer in groups with the rules at the field. The last commit before this note is 09b31849.
- **Waiting:** the owner's verdict on step 2, the field drawer.
- **Next, in order:**
  1. Step 3: Add field opens a choice of type first, a grid of the types, each with an icon and one line, then the drawer.
  2. Step 4: the entity's Validations section as `AxisField` boxes like the drawer's rules, with a Field column or label,
     and Indexes the same way.
  3. Then the review's End, as the `axis-design` skill says: one docs change, the tag, one NexKit issue.
- **To pick it up on another machine:**
  - `git fetch && git switch design/2026-11-04-entities`, then `scripts/setup.sh` once and `scripts/dev.sh`.
  - A Keycloak container made before #2794 refuses the realm sync (`axis-fapi`, "DPoP token is disabled") and the server
    stops at start: run `docker compose up -d --force-recreate keycloak` once.
  - A change to `src/Axis.Presentation/Texts/*.json` needs a restart of `scripts/dev.sh`: `dotnet watch` does not embed it
    again.
  - Studio's development user is `author`, password `axis`, and a code from the `otp` secret of `author` in
    `samples/keycloak/axis-realm.json`.
  - To see a relaxed point, relax one on a contract's page; it rewrites the sample's `application.json`, which is never
    committed: `git checkout samples/apps/purchase-requests/application.json` afterwards.
  - The text files have some duplicate keys from an earlier merge, such as `shell.licences.axis`; edit them as lines, as a
    JSON round trip drops the duplicates.

## Agreed changes

### One list for every kind

- **Asked:** a kind's list is a real table and the standard for every kind, not one kind's patch. Its columns are Name,
  Description, Status, Last modified by and Last modified at. Every column always shows, and a cell with no value
  shows the dash. A list does not show the inside of a resource, such as its fields or references, a file name, or a count
  that leads nowhere. A kind has no columns of its own.
- **Built:** `StudioResourceTable` on `AxisTable` replaces `StudioResourceRows`, sorted by name, with Name a real link. A
  module's resource shows its name without the `<module>/` prefix and has a status, as a new module version or a layer
  changes it. The
  list has no Module column: a module's resource has a neutral tag with the module's label beside its name.
- **Module resources:** the activity route also answers each one's label and description by locale, from the module's own
  texts, and the built-in and sample modules' entities, contracts, processes, policies and pages have descriptions. A kind
  with no label in its format, such as a policy, keeps its name.
- **Description:** every kind but `text` and `layer` may have an optional `description`, a text reference on `Resource`,
  which the compiler checks like a label (`AXC0028`). The schema form shows it, and each editor of its own shows it after
  the name and label. `GET .../resources` answers `descriptions` by locale, and the search also matches it. The sample
  entities have one, under the key `<entity>.resourceDescription`.
- **Status:** `GET .../resource-activity` answers each resource's `status` (`new`, `changed` or `published`). It is a route
  of its own so the resource list stays free of the database. While it loads the cell says so, and when it fails it says
  Unavailable.
- **Last modified by and at:** the columns are in the design and show the dash. Nothing records them: that is logic, for an
  issue of its own. It is to store who wrote a resource through Studio and when, with the version the write left, so a file
  changed in git shows the dash rather than the wrong person. A module's layer write is to count too.
- **To change in the docs:** D39 (the list shows `usedBy`, which it no longer does; the API still answers it),
  "Reads and diagnostics touch no database" in the authoring API, the resource file formats, and D132's rule on sorting if a paged list is to have a sort by column.

### The standard table, step by step

- **Agreed direction:** one `AxisTable` with every control, and each view turns on what it needs. Filters have two levels: a
  simple one with chips, and a builder of two levels of And and Or groups. There is one place to filter, so a column header only
  sorts. Search, filter, sort and page live in the address. Saved views and per-person columns are logic, for the issue.
- **Step 1, built:** a paged table says what it shows of how many, as "1–20 of 143". A kind's list has Refresh, page sizes of
  10, 20, 50 and 100, sorts by Name and Status from their headers, with Status in the order New, Changed, Published, and keeps its
  search, sort and page in the address. This changes D132's rule that Studio's small tables have no sort by column.

- **Sort:** a column sorts when its values have an order a person uses: names, people, statuses, dates and numbers. Long text,
  files, lists and actions do not. A kind's list sorts by Name, Status, Last modified by and Last modified at.
- **Search:** a column is searchable when it says so, through `searchText`, as a field marked `searchable` is in the record
  API. Search looks in all of them. A kind's list searches its name, description, module and status, no longer its `id`, and the
  box says Search.
- **Step 2, built:** `TableFilter` and `tableControls` in `web/src/platform/`: Filter opens a builder of conditions in groups of All or Any, with
  no limit on depth, and every column can be filtered on, so the module, a tag and not a column, is not a filter. Conditions follow the kind of value: text, enum, number and date, each
  with empty and not empty. Apply puts it in force, and a condition with no value is left out. A chip stands for each condition
  and group, with Clear all, and the filter lives in the address. A kind's list filters in the browser. For a record table,
  filtering is logic for the issue: the record API needs a filter, which refuses a field the person may not read.

- **Step 3, built:** `TableColumns` in `web/src/platform/`: Columns opens a dialog that shows, hides and orders the columns, with
  Reset. The identifying column stays first and always shows. The choice lives in the address, and a hidden column is still
  searched and filtered on. Keeping the choice for each person is logic, for the issue.

- **Toolbar:** Filter and Columns are icons with tooltips, as Refresh is, and Filter carries the count of conditions in force.
  New entity… moves to the page's heading, as New application… and a release's Activate… do, since it acts on the application.
  This changes D129's "on a kind's list" and the toolbar's main action on Studio's lists. The `table` widget keeps its toolbar
  actions, which an author configures.
- **Tooltip:** a tooltip names the control with no "…", so Filter and Columns, in place of Filter… and Columns…. The "…" stays
  on a button whose words show and that opens a dialog, such as New entity….

### A table with too little room

- **Asked:** one rule for every table, in place of the sideways scroll that hid columns such as Status.
- **Built, in `AxisTable`:** a table measures its own width. Its text columns narrow to 160 px first. Then columns move under
  their row one at a time, from the right, or by a column's `priority` (`high` last, `low` first), as "Title: value" on the
  row's own surface, with no line between the row and them. The identifying column, the first, never moves. A moved cell with
  no value is left out, while a column that fits still shows its dash. Only when what stays does not fit does the table scroll
  sideways, with the first column fixed.
- **Why:** SAP Fiori's pop-in keeps the data in sight, and moves columns with no setting, so every table gains at once.
- **For the issue:** the `table` widget's columns are to take `priority` in the file format, and a column chooser is left out.

### A band with too little room

- **Asked:** the band's actions no longer push the page wider than the window.
- **Built:** the band measures itself. When its actions do not fit, Validate, Preview and Run tests fold into one "…" menu, in
  that order, and New resource… and Publish… stay. They unfold when the room comes back.

### The entity designer, step by step

- **Asked:** the entity designer rendered every setting of every field inside its table row, so a field of many settings
  stood about 400 px high and an entity of 13 fields about 7,200 px. The owner asked how long-standing products do it.
  Salesforce's Object Manager, Dataverse's table designer, Mendix, Strapi and Directus all show a compact list of fields,
  one row a field, and open one field's settings beside the list.
- **Rules the owner set:**
  - Every Studio form is built from the shared parts the application's Settings uses, `SectionPanel` and `FormField`,
    now in `web/src/pages/FormSection.tsx`. A form does not lay its fields out on its own.
  - Every field has its help text under its label. In an application builder, help text is not optional.
  - One field to a row. Two fields share a row only when they truly belong together, such as a range's minimum and maximum.
  - The details are inputs on the page, with no Edit details step.
- **Step 1, built:** the Form tab is sections: General (name, label, description and display field, each with its help),
  Fields, Validations and Indexes, with Uses, Used by and the Danger zone after them as before. Fields is `AxisTable`, in the
  file's order, with Name, Label, Type ("Reference → Supplier", "Enum 3 values"), Required, Unique and Properties (tags for
  Sequence, Creator, Computed, Default, Searchable and Sensitive), a search, Add field in the section's heading, and Move up,
  Move down and Remove in each row's menu. A field's name opens its settings in a 560 px drawer, with the table still in sight.
  The drawer's name, label, type, Required, Unique and help have help texts, and its label and help are typed as texts, a box
  per locale, as the entity's label is. The other settings are still the old list, for step 2.
- **Tried and dropped:** tabs for Fields, Validations and Indexes with a header that summed the entity up and an Edit details
  drawer. The owner wanted the details on the page. Sections suit an entity better than tabs while its parts are small;
  tabs are for parts that are large and apart, as Salesforce's many object parts are. Not every page must be sections.
- **Inputs, built:** one field component, `AxisField` in `web/src/platform/AxisField.tsx`, for Studio and the console:
  label, help text, then the input, one to a row, as a group that its help describes. `help` is a required prop, so a screen
  cannot leave it out. `AxisCheckboxField` is a yes-or-no field with its help under its label, and `AxisFieldRow` puts fields
  that belong together on one row, stacking below 992 px. The schema form draws `AxisFieldHeading`, the same heading, as its
  help comes from the text catalogue. The entity designer and the schema form use them, and Add module and Add package now
  have help for Module, Package, Point and Reason.
- **Required mark:** the platform's own fields follow the form widget's rule of D96: a required field has a `*` in
  `dangerText` after its label, hidden from screen readers, and its input carries `aria-required`. `AxisField` takes
  `required`, and the schema form marks what its schema requires, but not inside an optional object such as `fileStorage`,
  whose fields are needed only once it is there. A field never says "(optional)": the Publish dialog's Label and Note are now
  fields with a heading and help, where they had only a placeholder. A short form whose every field is required, such as
  sign-in, marks none.
- **Nothing between help and input:** a field is its label, its help and its input. Guidance goes into the help, never on
  a line of its own between them.
- **A dialog holds only what it edits:** Add module and Edit module have Module, Version, Roles and Connectors, and no
  read-only list of relaxed points. A table may show what its row's dialog does not edit, as long as the cell leads to
  where it is edited: the Modules table's Relaxed points cell lists each point in two lines, its name as a link to it on its
  contract's page, then its reason, where it showed only an Off-standard tag. Its kind is left to the contract's page, to keep
  the cell short. The Modules section's line says a point
  is relaxed or undone on its contract's page. Each thing is seen in one place and edited in one place.
- **To change in the docs:** "Relaxed points in Settings" in `studio-resource-form.md`, which has Settings list them
  read-only in the module's dialog with a line that says where to relax.
- **Validations and fields:** a validation is a rule on the whole record: its expression may read several fields or count
  child rows, and its `field` only says where its message shows. As Salesforce's validation rules and Dataverse's and
  Directus's field constraints do, the file stays as it is and the designer shows it twice: a field's drawer is to have a
  Validation group with the field's own constraints and the validations shown at it, whose Add rule fills `field`, and the
  entity's Validations section keeps every rule, with a Field column, for those across fields. To build in step 2.
- **Step 2, built:** a field's drawer is in groups, `AxisFieldGroup`, each setting an `AxisField` with its help, and only
  what the field's type takes, so a group with none is left out: General (name, label, type, help text), the type's
  settings, titled "Reference settings" and so on (target entity, values with their labels, currency, precision and scale),
  Validation (Required, Unique, minimum and maximum length, minimum and maximum, a file's size and media types, then the
  rules at this field), Value (default, computed from, sequence, Creator) and Search and privacy (Searchable, Sensitive, and
  Exact lookup once the field is sensitive). Pairs that belong together share a row: minimum with maximum length, minimum with
  maximum, and precision with scale. A rule at the field is a box with its name, Remove rule, its condition and its message,
  typed as texts; Add rule adds one at this field. Value labels are rows of a value and its label typed as texts. The
  migration warning moves to the top of the drawer.
- **Found, for the issue:** an empty condition, as a new validation or rule has, is sent to `expressions/check`, which
  answers `400`; `main` does it too. The editor is to wait until there is text to check.
- **Help text, for the issue:** every field of the platform's own screens has help text, enforced three ways. `AxisField`
  requires `help`. A test fails when a `shell.studio.field.*` text has no `.help` in every locale; the texts under that prefix
  that are not fields, such as `modules.addTitle`, `modules.add`, `modules.empty` and `modules.item`, move out of it. The
  frontend reference states the rule. An application's own forms keep `help` optional, as their author decides. The 13 other
  editors that draw their own labels, such as ConnectorEditor, ProcessEditor, PageEditor and FormSteps, move to `AxisField`.
- **For the issue:** a diagnostic inside a field opens at its row; it is to open the drawer at the setting. Reordering by drag
  is still to come.

### Merge of `main` at fd1bf541, 2026-11-05

`main` built, since the first merge: the size tokens and `AxisTable` (D129), the kind lists, the Releases list, the band, the
Problems panel, Studio's sidebar entries, and the layer editors for module resources. How each conflict went:

- **`main`'s kept as the base:** `AxisTable` and its API (`fixed` and `freeText` kinds, `dataSource`, `onRefresh`, `below`,
  `failure`, `rowActions` with `configured` and `actions`, `rowAttributes`), `TableWidget`, `Shell` and the sidebar claim
  (`ShellSidebar`, `shellSidebarContext`), `antTheme`, `designTokens`, `AxisButton` (`AxisActionRow`, `plain`, `caret`),
  `resourceTitle` (a file name never shows where the resource has a kind), `StudioResourceTitles` and `StudioReleaseChanges` on
  the server, the history answer's shape, and the texts where both gave a key a value.
- **The prototype's put back on it:** `AxisTable`'s `priority`, `searchText`, `columns` slot, move-under-row, `testId`, the
  range text and the empty state; `dialogSizeTokens`; the Tabs, Modal and expanded-row theme; `ActionRow` and
  `NamedIconButton`; `contentPadding` in the sidebar context; the descriptions and the activity route.
- **The prototype's workspace kept:** `StudioPage` with `PageLayout`, `StudioNavigation`, `StudioStatus`, the Overview, the kind
  tables and the release pages, with `main`'s module editors, layers (`onModuleWritten`, `diagnostics`) and manifest handling
  wired in, and the entity designer with `main`'s layer marks and Reset to module.
- **On the branch and not used by the prototype page:** `main`'s `StudioBand`, `StudioSidebar`, `studioNavigation` and
  `StudioProblems`. The issue replaces the prototype's parts with them or the other way round; it decides.
- **Texts:** `main`'s wording wins where a key has both, such as `shell.studio.activate` (now "Activate release") and the
  `{release}` placeholder of its dialog.
- **Known differences from `main`'s tests:** a tooltip on an icon button opens on keyboard focus only when the browser says
  `:focus-visible`, which jsdom does not, so the two tests that focus such a button by keyboard fail.

### Lost in merges

`main` stays the standard for behaviour, so what keeping the prototype lost is back in the prototype's files:

- `StudioApplicationTexts.Read` names the applications of the list again.
- Studio no longer opens the only application by itself, which `main` removed in 88f74fb2.
- `shell.users.invite.send`, the name of the invite dialog's button.
- A site's pages keep the checkbox and radio alignment and error border, in `PageTheme`.
- Initials are of the first and last words, not the first two.
- The entity migration dialogs use `AxisButton` again, so Drop data is in `dangerText`.
- No inline `theme` on the workspace menu and no literal font in the JSON editor's find bar.

### Found by the previews

- The Studio card crashed: its fixtures lacked what `main` added, the names' `contracts`, the releases' `relaxed` and the
  manifests. They have them now, and the Entities card's data comes from the local stack.
- A label field in an application with no text resource crashed. It now says a text resource is needed.
- A status tag's words take the text tokens, as Ant Design's own fall to 3.37:1 in light mode. The Live tag of a release and
  other `Tag color` uses have the same problem, left as they are for the issue.

### For the issue

The web tests are `main`'s and about 100 of them fail on this prototype, as a prototype changes widgets without their
tests. The issue brings them to the agreed design. Nothing in this review touches a test.

Two changes to how tests run go with the issue, as the review found them and they are not design:

- Vitest sets its workers from the memory free when it starts, one for each GB less 1 GB for the machine, from 2 to 6. A
  worker holds about 1 GB, so one per core swaps on a machine that also runs the stack: the full suite took 1030 s with 99
  false timeouts, against 333 s and none with 4 workers.
- `scripts/integration.sh` leaves out the `*PerformanceTests` classes with a filter too, as it does without one.
