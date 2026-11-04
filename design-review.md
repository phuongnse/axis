# Design review 2026-11-04-entities: the owner's rules

This is an open design review. Work here follows the `axis-design` skill: one change at a time, only what the design needs,
no test changes on this branch, and logic left to the review's issue.

Topic: Studio's Entities page.

The prototype starts from the tag `design-review/2026-11-03-studio-settings`, so the owner keeps seeing the workspace and
the resource form that `main` has not built yet, with `main` (624ab6e1) merged in. Its own diff starts at that merge.
Each entry is one agreed change: what the owner asked, in English, and what was built, for the docs change at the end
of the review.

In a merge of `main`, a conflict keeps the prototype. `main` is still the standard for behaviour, so its logic is never
changed to suit the prototype, and any of it that keeping the prototype loses is brought into the prototype's files.

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
  drawer. The owner wanted the details on the page, and the shared sections keep every form alike.
- **For the issue:** a diagnostic inside a field opens at its row; it is to open the drawer at the setting. Reordering by drag
  is still to come.

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
