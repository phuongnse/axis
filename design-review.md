# Design review 2026-11-04-entities: the owner's rules

This is an open design review. Work here follows the `axis-design` skill: one change at a time, only what the design needs,
no test changes on this branch, and logic left to the review's issue.

Topic: Studio's Entities page.

The prototype starts from the tag `design-review/2026-11-03-studio-settings`, so the owner keeps seeing the workspace and
the resource form that `main` has not built yet, with `main` merged in, first at 624ab6e1, then at fd1bf541 and at 7d171005 (2026-11-05). Its own diff starts at the first merge.
Each entry is one agreed change: what the owner asked, in English, and what was built, for the docs change at the end
of the review.

In a merge of `main`, `main` is the standard for behaviour and the prototype for look. Where both built the same thing,
`main`'s API, data shape and logic are kept and the prototype's look and agreed additions are put back on them. A conflict
never keeps the prototype's logic over `main`'s. What the prototype's files lose is brought back (see "Lost in merges").
After a merge, run the type check, lint, and the tests of the shared widgets, and write the merge down below.

## State and how to continue

Last updated 2026-11-05 (merged `main` at 7d171005; one action row; one language rule for labels). The review is open; the
owner is reviewing it one step at a time.

- **Done:** the kind lists (one table, standard table steps 1 to 3, a table with too little room); the band (too little
  room, its actions); the entity designer's sections, `AxisField` with required help, the required mark, the Publish
  dialog's fields, the relaxed points in the Modules table, step 2, the field drawer in groups with the rules at the field;
  "A picker names by the label" for every `ReferencePicker`; and New resource, checked on click with every required field
  marked.
- **Waiting:** the owner's verdict on step 2, the field drawer.
- **Next, in order:**
  1. Step 3: Add field opens a choice of type first, a grid of the types, each with an icon and one line, then the drawer.
  2. Step 4: the entity's Validations section as `AxisField` boxes like the drawer's rules, with a Field column or label,
     and Indexes the same way.
  3. "A picker names by the label", left: the multi-choice of fields in Indexes.
  4. Then the review's End, as the `axis-design` skill says: one docs change, the tag, one NexKit issue.
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

### The band's actions

- **Asked:** the band's actions are one row of the same kind of button, whoever the author is.
- **Built:** New resource…, Validate, Preview and Run tests are text buttons, `AxisButton` in an `AxisActionRow`, the same gap
  apart, and Publish… is the one solid button. The row is the same with or without Publish…, for an author who may not
  publish. The folded "…" is `AxisButton`'s `icon` role. The row does not wrap (`AxisActionRow`'s `wrap`, Proposed), so the
  band can measure whether it fits.

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
  file's order, with Name, Label, Type ("Reference to Supplier", "Enum", see "A type in words" below), Required, Unique and Properties (tags for
  Sequence, Creator, Computed, Default, Searchable and Sensitive), a search, Add field in the section's heading, and Move up,
  Move down and Remove in each row's menu. A field's name opens its settings in a 560 px drawer, with the table still in sight.
  The drawer's name, label, type, Required, Unique and help have help texts, and its label and help are typed as texts, a box
  per locale, as the entity's label is. The other settings are still the old list, for step 2.
- **A type in words:** the Type column read "Reference → organization/Department" and "Enum 3 values", a shorthand of
  this review's own, with technical names and a count that leads nowhere. It now reads as Salesforce's Data Type column
  does, in words and by label: the type's name, and for a reference, references or child collection the entity it points
  to, by its label, as "Reference to Department" or "Child collection of Line item", from
  `shell.studio.entity.typeOf.<type>`. The label comes from `referenceOptions` and `useTextOfKey`, as the drawer's target
  picker gives it, and the name shows only where the entity has no label. Enum and every other type is its name alone.
- **Required and Unique read Yes or No:** they are columns because every field has them and an author compares them down
  the table, which fields must be filled and which tell records apart; a setting only some fields have is a tag under
  Properties. A cell showed a check for yes and the dash for no, but the dash means a value that is not set (D96), and a
  No is a value. Both answers now show as words, through `booleanText` in `platform/formatValue.ts`, the words a record's
  boolean reads in every table and read-only form, which the schema form's read-only value takes too. Words are read the
  same by a screen reader and are not mistaken for an empty cell.
- **Properties:** the tags say where a field's value comes from, at most one of Computed, Sequence, Creator, Matched
  (`match`), User (`userId`) and Default, then Searchable and Sensitive. User and Matched were missing, so a user field and
  a matched reference looked like any other.
- **The target is a link:** in the Type cell, the entity a reference, references or child collection points to is a
  link that opens it, a module's entity at its module view. Only the label is the link; the field's name stays the row's
  button that opens the drawer. The address comes from `studioResourceAddress` in `platform/studio.ts`, which Uses, Used by
  and the kind tables take too, in place of three addresses built by hand.
- **Leave without saving:** Studio dropped an unsaved draft on any link. Now one guard, `LeaveGuardProvider` around the
  shell in `PlatformShellRoute`, asks "Leave without saving?" with Stay and Leave before any link in the shell is followed,
  the header, the sidebar, Uses and the Type cell among them, and before a navigation the workspace starts by itself
  (`useGuard`: open a reference or a problem, a policy, an application, a sidebar entry). An editor counts itself in through
  `useUnsavedChanges`, which `SaveBar` calls, so every editor with a save bar is guarded. A reload or closing the tab asks
  the browser's own question. A link opened in a new tab is not asked about.
- **Back is the browser's:** Studio has no Back or Next of its own. Every view has its own address and each move is a
  history entry (the search replaces its entry), so the browser's Back and Forward, Alt+Left and the mouse's back button step
  through it. A second Back would be one concern with two mechanisms, and people would have to guess whether the two differ.
  Desktop IDEs such as Mendix Studio Pro and VS Code have their own because they have no browser around them; web builders
  such as Salesforce Setup and Power Apps leave it to the browser. Where the author is comes from the sidebar's marked entry,
  which also leads up to the list, and the page heading, so Studio keeps no breadcrumb.
- **For the issue, so Back works well:** the SPA moves to a data router, `createBrowserRouter`, so `useBlocker` guards
  Back and Forward with the same "Leave without saving?" and `<ScrollRestoration>` returns the author to where they were in a
  long page such as the Fields table. A field's drawer gets an address, `&field=<name>`, so Back reopens it and a link can
  open a field. The module editors and the contract pages have no save
  bar, so they are not guarded yet. A module entity's page heading shows `organization/Department`, its name, not its label.
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
  fields with a heading and help, where they had only a placeholder. Every required field is marked, in every form and
  dialog, even where every field is required, such as New resource. Only the sign-in page marks none, as everyone knows
  both its fields are needed. (Changed on 2026-11-05: a short form whose every field was required used to mark none.)
- **Not translated:** under a text typed in the author's language, a muted line says which languages lack it, as before,
  but only once the text has words in the author's language, so an empty optional text such as a help says nothing. It
  names up to two languages, "Not translated into Vietnamese", and counts more, "Not translated into 3 languages", so it
  stays one short line however many languages the application has; Translations lists them all. `LabelTexts` draws it, so
  every label of Studio follows it.
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
- **Where the value comes from:** the drawer's Value group showed every way at once, a default, an expression, a sequence
  and Creator, though the compiler refuses two together (AXC0013), and Required beside a value the server sets. It is now
  one choice, "The value comes from", of the ways the type takes: Entered by people, Computed, Sequence, Creator, and
  Matched for a reference, each with one line of help, then only the chosen way's settings: a default and Holds a user
  (`userId`) for an entered value, the expression, the sequence, or Find by and From for a match. Required shows only for an
  entered value. Choosing a way rewrites the field once, through `withValueSource` in `platform/entityFields.ts`, which drops
  the other ways' keys and those only an entered value may have. Dataverse's column Behavior (Simple, Calculated, Rollup) is
  the same idea. `match` and `userId` were edited only in the JSON tab before.
- **Value before Validation:** the drawer's groups are General, the type's settings, Value, Validation, then Search and
  privacy. What has an effect comes before what it affects: the type decides which settings there are, and where the value
  comes from decides whether Required applies.
- **Maximum length is the type's:** a sequence field showed Maximum length alone under Validation, as the compiler refuses
  `minLength`, `min` and `max` on a value the server sets, yet keeps `maxLength`, the size of the stored text. So Maximum
  length is a setting of the type, under Text settings, as precision and scale are of a number, and as Salesforce's
  `Text(80)` and Dataverse's "Maximum character count" put it. Validation keeps only checks on an entered value: Required,
  Unique, Minimum length, the range and the rules.
- **Matched:** Find by offers only the target entity's unique fields, so `GET .../names` answers `unique` with each field
  (Proposed), and From only this entity's fields of the same type that the compiler allows (AXC0362).
- **One choice control:** `AxisChoiceField` in `platform/AxisField.tsx` is a choice among a few ways, each with its help, and
  the money field's currency source takes it too, in place of its own radio group.
- **Found, for the issue:** an empty condition, as a new validation or rule has, is sent to `expressions/check`, which
  answers `400`; `main` does it too. The editor is to wait until there is text to check.
- **Help text, for the issue:** every field of the platform's own screens has help text, enforced three ways. `AxisField`
  requires `help`. A test fails when a `shell.studio.field.*` text has no `.help` in every locale; the texts under that prefix
  that are not fields, such as `modules.addTitle`, `modules.add`, `modules.empty` and `modules.item`, move out of it. The
  frontend reference states the rule. An application's own forms keep `help` optional, as their author decides. The 13 other
  editors that draw their own labels, such as ConnectorEditor, ProcessEditor, PageEditor and FormSteps, move to `AxisField`.
- **For the issue:** a diagnostic inside a field opens at its row; it is to open the drawer at the setting. Reordering by drag
  is still to come.

### A field is checked on click

- **Asked:** New resource's fields had no `*` and no help, and Create was disabled until both had a value. The owner asked
  whether an action should be disabled or should show what is wrong when clicked.
- **Rule:** a finish button is never disabled because the input is not valid. A click checks the fields, shows under each one
  that is wrong what to do, such as "Enter a name.", and focuses the first. A message goes once the field is put right. A button
  is disabled only while its action runs. This is the form widget's Save already (D96), and one rule for every form and dialog.
- **Why:** a disabled button says nothing of what is missing, has low contrast and cannot be focused, so a screen reader
  user does not find it. GOV.UK's design system, SAP Fiori and Atlassian advise against it.
- **Built:** `AxisField` takes `error` (Proposed), `null` while the field has none. Ant Design's form item draws the message,
  so it is the form widget's message: 4px under the input, in `dangerText`, with the input's border in it, and the group is
  described by it. New resource is built to the rule: Kind and Name (or Locale) are `AxisField`s, marked required, with help;
  a role's name has its own help; Create is always enabled, and its click checks and focuses.
- **For the issue:** the other dialogs that disable their finish button move to the rule: New application
  (`StudioNewApplication`), the schema form's dialog (`SchemaForm`), Relax, Add state and Add transition
  (`StudioModuleContract`), and the entity migration's Fill nulls or Add conversion (`EntityMigrationQuestions`).

### A picker names by the label

- **Asked:** wherever a person picks a field or a resource, the choices show its label as text, as the tables do, not its
  name. The name shows only where it is needed: instead of a missing label, and in textMuted after the label of options
  that share one. The search finds either, and the file keeps the name.
- **Built:** one mechanism. `GET .../names` answers a `labelKey` with each name it lists that has a label or title (entity,
  field, process, step, data source, rule, connector, event, role, page, policy and the module resources), in
  `DefinedNames`. `referenceOptions` gives each choice as `{ name, labelKey }`, and `ReferencePicker` resolves the key
  through `useTextOfKey`, the one way Studio turns a text key into words, which the entity designer's label text uses too.
  A module's label resolves too, as the names route's texts hold the module texts merged into the application's.
- **One language rule:** a label shows in the author's language, else in `en`, as a resource's title does (`resourceTitle`).
  `textIn` in `platform/studio.ts` is that rule, and `resourceTitle`, `useTextOfKey` and a relaxed point's words take it, so
  a table and a picker never name one resource two ways. The label editor's own rule, a box in the author's language or else
  the application's first, is about which language is edited and stays as it is.
  Every picker gets the rule with no change of its own, and the slot pickers of `SchemaForm` use `ReferencePicker` again in
  place of their own labelled select.
- **For the issue:** the names route gains `labelKey`, so `docs/reference` for the route says so. A choice the picker is given
  as a plain name has no label and shows the name. Columns, parameters and operations have no label in their format.

### Writing an expression, step 1

- **Asked:** an author writes expressions in about twelve places, rules, computed fields, defaults, process conditions,
  form conditions, policy filters, endpoint values and more, and needs the guide and the context to write one right.
  The owner asked how other products do it. Salesforce's advanced formula editor has Insert Field, Insert Function and
  Check Syntax; Power Fx shows a function's description and arguments as you type, with errors inline; Notion's editor
  previews the result on the row it was opened from, with a list of fields and functions to click; Airtable completes
  fields and functions with their parameters. An author needs to know what the place needs, what there is to use, and
  whether what they wrote is right.
- **Built, in `ExpressionEditor`, the one editor every place draws, so every place has it:**
  - a line under the box says what the place needs, "Needs to give yes or no", until the expression gives a type, then
    "Gives yes or no", in the words the designer names types by; `expressionExpectation` in `expressionScope.ts` says what
    a place needs (a validation, a condition or a filter a boolean, a computed field or a default the field's type);
  - an error reads first in words, its code after it, small;
  - completion names each choice: a field by its label and type, a rule by its signature, a function by its signature,
    with what it does and an example beside the list;
  - inside a call, a line shows the function's signature with the parameter at the cursor in bold, and what it does;
  - Expand, an icon beside the box, opens the expression in a large dialog with a panel of every field, rule and function
    allowed there, in groups (Fields, Rules, Text, Numbers, Dates, Logic, Lists, User), searchable, where a click inserts
    the name at the cursor;
  - the box wraps long lines and grows with them; an empty expression is no longer sent to be checked.
- **One list of functions:** `expressionFunctions.ts` holds each function's parameters, result, group and example, and its
  words are `shell.studio.expression.fn.<name>`. It replaces the names `expressionScope` kept, and the scope offers only
  those allowed at the place: no aggregate in a default, no `now()`, `today()` or `toDate()` in a computed field
  (AXC0086), `currentUser()` only in a policy grant's or a data source's filter (AXC0095).
- **One wrapper:** `ExpressionField.tsx` holds `Expression`, which the entity designer and the process editor each had a
  copy of.
- **Step 1 missed the language's own words.** The owner searched for `is`, `null` and `or` and found nothing: the
  editor offered fields and functions as long lists of technical names, and none of the operators a condition is made of.
  An expression is a formula that gives a value of a type, yes or no only one of them, so a condition builder alone would
  serve half the places; the code editor stays the one way, made easy for both.
- **The language's words, built:** `expressionOperators` in `expressionFunctions.ts` lists the comparisons, `is null` and
  `is not null`, `and`, `or`, `not`, `in (…)`, the arithmetic and the values (`true`, `false`, `null`, `'text'`,
  `date('…')`), each with what it does, `shell.studio.expression.op.<id>`, and an example. The panel has three tabs,
  Fields, Operators and Functions, and a search looks in all three and lists what it finds best first: a name that is the
  words, then one that starts with them, then one that holds them, then one whose description does.
- **Completion knows where the cursor stands, built:** after a value it offers the operators that value's type takes, in
  the language's order, the comparisons first, and after a clause that already compares, only `and` and `or`; inside
  quotes after an enum field, that field's values in their order, closing the quote; elsewhere the fields, the values,
  `not`, the rules and the functions, those that give the type the place needs first. It opens by itself after a space
  that follows a value and after a quote that follows an enum field. `totalAmount is null or totalAmount > 0` is typed
  with completion alone.
- **Next, agreed:** a line that reads the expression back in words with labels, then starting points by the type the place
  needs. A condition builder, a mode for simple yes-or-no expressions only, is to be weighed after.
- **For the issue:** the server answers the scope of a place, its fields with labels and types, the functions allowed with
  their words, and the type it needs, from `ExpressionFunctions`, so the client keeps no list. The check takes the draft,
  as a rule not yet saved is answered 400 today ("The path holds no expression"). Step 2 previews the result on a sample
  record, as Notion does, through `ExpressionInterpreter`. A "Did you mean" for an unknown name. `ExpressionEditor`'s
  tests change: the type reads in words, the code follows the message, and Tab stops at Expand before it leaves.

### Vietnamese texts of the entities area

- **Asked:** the owner found the Vietnamese texts poor. `docs/reference/ui-writing.md` and its glossary, merged in #2971,
  are the standard, and the Vietnamese texts are rewritten to it area by area, the entities area first, for the owner to
  review before the rest follow as NexKit issues. #2972 is the check that keeps them to it.
- **Rewritten:** the texts of the entity page, the field drawer, the expression editor and the entity kind, 113 in `vi`:
  "đối tượng" for entity, "trường", "quy tắc kiểm tra" for a validation, "Không trùng", "Hướng dẫn", "Lựa chọn" for an
  enum, "Kiểm tra lỗi" and "phát hành" for Validate and publish; no "field", "entity", "unique", "Policy", "Ràng buộc" or
  "thực thể" left; "Biểu thức tính" in place of "Tính từ"; one spelling, "thỏa"; "Thêm đối tượng…" verb first.
- **English too:** the field drawer's validations read "Validations", "Add validation" and "Delete validation", where they
  said "Rules", which names another concept.
- **Labels, by rule, in every area:** the owner's review gave three rules for every screen, in `ui-writing.md` by #2974: a
  label or a column title does not repeat what holds it ("Tên", not "Tên trường", in the Fields table), a setting is
  named by what it means ("Tên hiển thị" for a label, "Kiểu dữ liệu" for a type), and a short Vietnamese label that reads
  two ways is rewritten. Applied to every text they touch, 33 in `vi` and 8 in English: the Fields table's columns, a
  process step's and parameter's settings, a scenario step's type, the page editor's labels, a release's label, and the
  identity provider's button text. No "nhãn", "kiểu trường" or "kiểu tham số" is left.
- **Capitals of a word put in a sentence:** "Thiết lập Văn bản" capitalized a type's name in the middle of a phrase.
  `withWord` in `platform/texts.ts` puts a word Axis defines in place of a placeholder with the sentence's capitals, a
  capital where the placeholder starts the text and lower case elsewhere: "Text settings", "Thiết lập văn bản". The
  drawer's type group, the expression editor's "Gives" and "Needs to give" lines and its completion's type detail take
  it, in place of the editor's own lower-casing. A name an author gave keeps its capitals. The rule is in #2974.
- **A child collection reads "Bảng con gồm Dòng hàng"**, so the entity is what the rows are, not who owns the table.
- **"Người dùng", not "mọi người":** Studio's help said "mọi người" where English says "people", which reads as a crowd.
  The people who use an application are left out where the sentence needs no subject, "Thông báo hiện ở trường này khi
  điều kiện không thỏa.", and are "người dùng" where it needs one; the rule and the glossary term are in #2974. 15 texts
  in every area, and the last four old tone marks, "xoá" to "xóa".
- **For the issue:** the rewrite of the other areas, one NexKit issue each, after the owner's review of this one.

### Uses and Used by

- **Asked:** the owner found the relation words poor, "Sửa bản ghi của nó", "Cấp quyền trên nó", and the flat list hard to
  read. The headings "Sử dụng" and "Được dùng bởi" stay: they read clearly.
- **Built:** each panel is the standard table, `AxisTable`, grouped by kind in the sidebar's order: a group row with the
  kind's name, its other cells blank through the table's `blankRow`, on the canvas colour, then its entries, with the
  columns Name, the other resource by its label as the link that opens it, and How it is used. No count. The kind is the
  group's, so no row repeats it. A first try drew grouped lists of its own; the owner asked why not the table, which
  groups as well, so the one table does it. `StudioChangeList` stays for the change lists of a release and of the
  Overview.
- **Words:** each relation is a verb and its object, with no "nó": "Sửa bản ghi", "Đọc bản ghi", "Cấp quyền", "Gọi",
  "Khởi chạy", "Hiện dữ liệu". The English relations stay. The rule, no "nó" for "it", is in #2974, and the other 39
  texts that said "nó" are rewritten, with "phát hành", "trang web", "thành phần", "cam kết" and "bộ kết nối" where they
  still had older words.
- **To decide:** "phiên bản" names a release, and a module's version is also called "phiên bản"; the module blocker now
  says "một bản" for the module's version. The glossary is to name a module version apart.

### Second mechanisms found by the audit of 2026-11-05

Each is one concern with more than one way to do it. The issue merges each into the first one named, as the rule "One
concern has one mechanism" says. Found by reading, not yet run.

1. Name of a kind: `platform/studioKinds.ts` (`kindName`, `kindSingular`) against `pages/studioViews.ts` (`kindName`, other
   text keys) and `kindLabel` in `StudioPage.tsx`.
2. A localized title with its fallback: `localized()` in `platform/site.ts` against the copies in `platform/studio.ts` and
   `SchemaForm.tsx`, which fall back differently (first locale, or `en`). Studio's labels and resource titles now take one,
   `textIn` (see "A picker names by the label"); the application's title and description, and the site's, still fall back
   to the first locale.
3. Sorting names: `nameCollator` in `pages/studioViews.ts` against inline collators and bare `localeCompare` calls. It moves
   to `platform/`.
4. The label of an option: done in this review, see "A picker names by the label". `StudioResourceTitle` stays for the
   places that are not pickers.
5. A status as a tag: raw colour names in `UsersPage`, `ConsolePublishing`, `StudioReleases`, `StudioTestResults` and
   `StudioPolicyMatrix`, against the colour tokens.
6. Empty states drawn with Ant Design's `Empty` in `HomePage`, `ProcessInstancePage` and `RecordHistory`, against
   `EmptyState`.
7. Tables that do not use `AxisTable`, such as `UsersPage`, `LicencesPage`, `TextTable`, `AccountPage` and the console
   lists. `DeadLetterList` and `ConsoleDeadLetters` look like two lists of one thing. Some are editable grids.
8. Search boxes and filters apart from `TableFilter`, and literal input widths, against the size tokens.
9. Raw `Button` next to `AxisButton`, mostly in `PageEditor`, `ProcessEditor`, `EntityDesigner` and `ConnectorEditor`.
10. `Alert` drawn by hand in about 20 places, with the same text (`referencesFailed`) twice.

### Merge of `main` at fd1bf541, 2026-11-05

`main` built, since the first merge: the size tokens and `AxisTable` (D129), the kind lists, the Releases list, the band, the
Problems panel, Studio's sidebar entries, and the layer editors for module resources. How each conflict went:

- **`main`'s kept as the base:** `AxisTable` and its API (`fixed` and `freeText` kinds, `dataSource`, `onRefresh`, `below`,
  `failure`, `rowActions` with `configured` and `actions`, `rowAttributes`), `TableWidget`, `Shell` and the sidebar claim
  (`ShellSidebar`, `shellSidebarContext`), `antTheme`, `designTokens`, `AxisButton` (`AxisActionRow`, `plain`, `caret`),
  `resourceTitle` (a file name never shows where the resource has a kind), `StudioResourceTitles` and `StudioReleaseChanges` on
  the server, the history answer's shape, and the texts where both gave a key a value.
- **The prototype's put back on it:** `AxisTable`'s `priority`, `searchText`, `columns` slot, move-under-row, `testId`, the
  range text and the empty state; `dialogSizeTokens`; the Tabs, Modal and expanded-row theme; `NamedIconButton`; `contentPadding` in the sidebar context; the descriptions and the activity route.
- **The prototype's workspace kept:** `StudioPage` with `PageLayout`, `StudioNavigation`, `StudioStatus`, the Overview, the kind
  tables and the release pages, with `main`'s module editors, layers (`onModuleWritten`, `diagnostics`) and manifest handling
  wired in, and the entity designer with `main`'s layer marks and Reset to module.
- **On the branch and not used by the prototype page:** `main`'s `StudioBand`, `StudioSidebar` and `studioNavigation`. The
  issue replaces the prototype's parts with them or the other way round; it decides.
- **Replaced, not kept:** `main`'s `StudioProblems` (a sticky column beside the page, `onSelect`) is not on the branch: the
  file holds the prototype's `StudioProblemsPanel`, a drawer that links each problem through `addressOf`. `main`'s
  `StudioBand` still names `problemsPanelId` in `aria-controls`, which no element carries now. The issue takes `main`'s
  component as the base and puts the prototype's look on it, or keeps the drawer and moves the id to it.
- **Texts:** `main`'s wording wins where a key has both, such as `shell.studio.activate` (now "Activate release") and the
  `{release}` placeholder of its dialog.
- **Known differences from `main`'s tests:** a tooltip on an icon button opens on keyboard focus only when the browser says
  `:focus-visible`, which jsdom does not, so the two tests that focus such a button by keyboard fail.

### Merge of `main` at 7d171005, 2026-11-05

`main` built, since fd1bf541: the review's process change (#2947, the same text as this branch's), New resource… on the band
that asks for a kind and New entity… on a kind's list that asks for none (#2847), the built-in Approvals application in
place of the request-approval template, and the cell's DNS check of tenant hosts. How each conflict went:

- **`StudioNewResource`:** `main`'s file is the base: its `kind` prop, its kind choice from `studioKinds`, and its texts
  `shell.studio.newResource.kindButton.<kind>` and `kindTitle.<kind>`. The prototype's `inBand`, `buttonOf` and `titleOf`
  are gone, as `main`'s role follows `kind` alone. The prototype's look is back on it: the dialog is `dialogWidth('medium')`.
  The kind list's heading shows the button only for a kind with a starter, as `main`'s caller does.
- **`StudioPage`:** the prototype's workspace is kept; `main`'s new kind list heading is the prototype's already.
- **`docs/development.md` and the texts:** `main`'s.
- **One action row:** the prototype's `ActionRow` is gone, as it was a second way to `AxisActionRow`. `AxisActionRow` takes
  `wrap` (Proposed), false on the band, whose fold measures whether its actions overflow. `FormWidget`'s footer and the entity
  migration dialogs are `main`'s again (`AxisActionRow`, and `FooterRow` where a form has no solid button).

### Lost in merges

`main` stays the standard for behaviour, so what keeping the prototype lost is back in the prototype's files:

- `StudioApplicationTexts.Read` names the applications of the list again.
- Studio no longer opens the only application by itself, which `main` removed in 88f74fb2.
- `shell.users.invite.send`, the name of the invite dialog's button.
- A site's pages keep the checkbox and radio alignment and error border, in `PageTheme`.
- Initials are of the first and last words, not the first two.
- The entity migration dialogs use `AxisButton` and `AxisActionRow` again, so Drop data is in `dangerText`.
- `FormWidget`'s footer is `main`'s `AxisActionRow` or `FooterRow` again, where the prototype had its own `ActionRow`.
- `main`'s `StudioProblems`, see "Replaced, not kept" under the merge at fd1bf541.
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
