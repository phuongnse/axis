# Studio resource form

This page describes the form that edits one Studio resource: its sections and fields, its labels and
lists, the controls that choose what Axis knows, what uses the resource, its save bar and its JSON view. The form is the standard for every resource without a designer of its own: the
application, role, policy, rule, event, trigger, sequence, data source, migration and webhook. The
schema part of the editors that have a designer follows it too. How Studio is laid out around the form
is in [Studio workspace](studio-workspace.md), and what each editor and designer does is in
[Studio](studio.md).

## Sections

_(planned for M31)_

The form puts a resource's root properties in sections, so that an author reads them in groups that
each say what they hold. Each section is a panel on `surface`, as the Overview's blocks are, with a
heading and a line that says what it holds. The application's Settings, for example, has General
(name, title, description and time zone), Modules, Packages and File storage, then the
[auto-accept switch](ai-authoring-in-studio.md#auto-accept). A property that no section names goes to a
last section with no heading.

- **One field in a section.** A section that holds one field names it in its heading, so the field has
  no label of its own. A section that holds one object shows the object's fields itself.
- **One list in a section.** A section that holds one list puts the list's Add… on the right of its
  heading, and the table has no toolbar row.
- **One rhythm.** The sections are `spacing-md` apart.
- **Shared parts.** Every Studio form is built from these sections, with its fields as
  [fields](actions-and-controls.md#fields) says.

## Fields

_(planned for M31)_

Every field is named in words. Its label and its optional help are the texts
`shell.studio.field.<kind>.<path>` and `shell.studio.field.<kind>.<path>.help`, drawn as the form
widget draws them: the label in medium `textMuted`, and the help in small `textMuted` between the label
and the input. Every field has its help, see [platform field help](frontend.md#platform-field-help). A
property's raw name, such as `textKey`, never shows.

The file's identity is hidden: `id`, `kind` and `formatVersion` are not shown, while `name` is, with
its help. A group's fields sit in from the group's label, behind a rule in `borderSubtle`, so they read
as its fields and not as its peers.

## Labels

_(planned for M31)_

A label is one text box in the author's language, or in the application's first language when the
label lacks that one. A box for each language would grow with every language, so the others are a step
away instead.

- **Translations….** The translate icon sits inside the box at its end, so the box keeps the width and
  height of every field. It opens every language of the label in a medium dialog, where Done keeps the
  texts and Cancel puts them back. The text resources' table translates many labels at once.
- **Not translated.** Once a text has words in the author's language, a muted line under the box says
  which languages lack it, so an empty optional text such as a help says nothing. It names up to two, as
  "Not translated into Vietnamese", and counts more, as "Not translated into 3 languages", so it stays one
  short line, and Translations lists them all.
- **The key is Studio's own.** Studio makes it from the resource and the path, and never shows it.
- **One Save.** The texts are part of the resource's one Save and its history, so Undo steps through a
  label's texts too. Save writes the label's texts first, so a resource never names a key with no text.

## Lists of objects

_(planned for M31)_

A list of objects is a table with one column per property, named by its field text. A value of a known
kind shows its label, such as a module or a package by the label of its manifest.

- **A cell shows content, never a bare count.** A nested list or map shows what it holds in words. A
  module's roles are one line for each slot, such as "Budget approver: Department manager", and a slot
  that no role plays reads "Not mapped" in `warningText`. A module's relaxed points show as
  [relaxed points in Settings](#relaxed-points-in-settings) says.
- **Columns.** A column that would be empty on every row, such as Connectors when no module has any, is
  left out, while the dialog keeps every field. Short columns of a fixed form, such as a version range
  or a tag, are as wide as their content.
- **Add… and the row.** Add…, such as Add module…, is the list's main action. The first column is the
  link that opens the item's dialog, a button drawn as a link because it goes to no address. Each row
  keeps Remove alone, as an icon, see
  [undo rather than confirm](actions-and-controls.md#undo-rather-than-confirm).
- **The dialog edits a copy.** It is `medium` for plain values and `large` for an item that holds a list
  or a map. It is titled by the verb that opened it: "Add module", because it adds an installed module
  and creates none, or "Edit module". Done writes the copy to the list as one step to undo, and waits
  for the item's required fields. Cancel leaves the list as it was, so Add… adds no row until Done.
  Inside the dialog a nested list is rows of inputs with Add row, because a dialog never opens another.

## Controls that know what they hold

_(planned for M31)_

A field whose values Axis knows is chosen, never typed, so an author cannot name what does not exist.

| Field             | Control                                                                                                                                                                                                                                                                                                            |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Module or package | Chosen from those installed, by label, from [`GET /api/studio/catalog`](#studio-api). Choosing one fills an empty version range with its newest version, as `^1.2.0`. A chosen module with no roles or connectors says so.                                                                                         |
| A module's slots  | The module's role and connector maps are one field for each slot of the chosen module: the slot's label above a select of the application's roles or connectors.                                                                                                                                                   |
| Version range     | Two selects, because a range has three forms. Version lists the installed versions, newest first, as "1.2.0 (newest)". Updates reads "Compatible updates (1.x)" for `^`, the default, "Fixes only (1.2.x)" for `~` and "This version only" for an exact version.                                                   |
| Time zone         | A searchable select of the time zones the server knows, from the catalog, so every choice passes `AXC0277`. Each reads as "(UTC+07:00) Asia/Ho Chi Minh", by offset then id. Cleared, the application's dates are UTC. A schedule trigger's time zone is the same control.                                         |
| Binding           | Chosen from the names of the kind the place needs, from [`GET /api/studio/bindings`](#studio-api): the application's file storage, a webhook's URL and secret, and a connector's base URL, token URL and secrets. File storage also offers "The tenant's database, with no binding", which clears the whole entry. |

The file keeps a version range as written, and a table shows it in words, as "1.2.0 and later 1.x" or
"1.2.0 only". A version that is no longer installed stays shown, with a line in `warningText` that says
to choose an installed one. A binding name the tenant lacks stays shown, with a line that says
activation will refuse it.

## Relaxed points in Settings

_(planned for M31)_

The Modules table's Relaxed points cell lists each of a module's relaxed points in two lines, from
[`GET .../relaxed`](#studio-api): its name as a link to it on its contract's page, then its reason. Its
kind is left to the contract's page, to keep the cell short. The Modules section's line says that a point
is relaxed or undone on its contract's page. A relax is a decision that needs the point's context, which
the [contract page](module-contracts.md#in-studio) shows, so Add module and Edit module hold only Module,
Version, Roles and Connectors, as [a dialog holds only what it edits](actions-and-controls.md#dialogs).

## Used by

_(planned for M31)_

What uses a resource is a tab of its own, Used by, beside Design, Form and JSON, opened when it is needed,
before a rename or a delete. A kind nothing can use, the application, has no such tab. What a resource uses
is shown by its own editor, and deleting it breaks nothing it uses, so it has no list of its own, see
[D141](../decisions/d141-the-reference-index-and-what-uses-a-resource.md).

The tab is the [standard table](actions-and-controls.md#tables), flat, with search and the kind filter:

1. Name: the other resource by its label, as the link that opens it at the path of the reference.
2. Kind, as wide as its content.
3. How it is used, in words, such as "Grants to it", with one text for each relation of the compiler.

No file, path or relation code shows, and modules and packages show by their labels. It sorts by kind in
the sidebar's order, then by name. The same resource used the same way shows once. With none, the table
says so.

## Save bar

The save bar holds what acts on the whole draft. While there is something to save, it stays at the foot
of the window across the content, drawn over the footer: "Unsaved changes" on the left, Undo and Redo as
icons, then Discard and Save on the right. With nothing to save it is not drawn.

- **While a step can be redone.** The bar stays, reading "No unsaved changes", with Redo on and no
  Discard, as nothing is left to discard. Save is off.
- **What counts.** JSON that does not parse is an unsaved change, so the bar and its Undo and Discard
  are there when they are needed, and Save is off. A change that leaves the data as it was, such as a
  space or Format, shows no bar, because nothing can be saved.
- **Every kind.** Every resource kind has the bar. What Undo, Redo and Discard do is in
  [undo rather than confirm](actions-and-controls.md#undo-rather-than-confirm).

## Form and JSON

Form and JSON are two tabs of one draft, so a change in either shows in the other at once, and Undo,
Redo, Discard and Save act on both. While the JSON has an error, the form is read-only: it shows the
last valid version under a warning that says so, with Go to JSON, and its inputs and its lists' actions
are off, so no edit in the form overwrites the JSON being typed.

## JSON view

The JSON view is a CodeMirror editor with JSON highlighting and line numbers.

- **Checks as the author types.** Two checks run as an author types, each marked
  in the gutter and underlined where it is: a syntax error, "This is not valid JSON here.", and every
  place where the content breaks the kind's schema, in words, such as "Add version.".
- **Its look.** Its border is a field's, `border` with `radius-base`, and the gutter stays inside it.
  Its text keeps `spacing-sm` from the border.
- **Its height.** It reaches down to `spacing-md` above the footer, or above the save bar while that
  shows, at any window height, and scrolls inside.
- **Its colours are tokens.** Names are `primaryText`, strings `text`, numbers `infoText`, `true`,
  `false` and `null` `warningText`, and punctuation `textMuted`. A string stays plain because strings are
  most of what a person reads. The expression editor draws in the same colours, so no editor shows
  CodeMirror's own.
- **Its actions end the tab bar,** so they take no height from it, and show only on the JSON tab.

| Action   | What it does                                                                                                                                                                                                                                                                                                                                                                  |
| -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Problems | "2 problems", a text button in `dangerText`, shows only while there are any, and moves to the next one.                                                                                                                                                                                                                                                                       |
| Find     | An icon, also Ctrl+F or Cmd+F, opens a bar at the top of the view, because the browser's own find cannot see the lines a code editor has not drawn. It counts the matches, as "1 of 22", from where it opened. Enter and Shift+Enter move between them, and Escape closes it and goes back to the text. Each match has a thin outline and the one in focus the selected tint. |
| Format   | An icon that lays the JSON out again. It is off while the text is not JSON.                                                                                                                                                                                                                                                                                                   |
| Copy     | An icon that copies the whole text and says so.                                                                                                                                                                                                                                                                                                                               |

## Studio API

These routes give the form what it chooses from, and answer an author, as every Studio route does. The
release routes are in [release page](studio-workspace.md#release-page).

- `GET /api/studio/catalog` answers `{ modules, packages, timeZones }`. Each module has its `id`,
  `label`, `versions`, newest first, and its `roles` and `connectors` slots, each `{ name, titles }`
  with its label in each locale, read from the module's own texts. Each package has its `id`, `label`
  and `versions`. Each time zone has its IANA `id` and its `offset` from UTC in minutes now, by offset
  then id.
- `GET /api/studio/bindings` answers the names of the request tenant's deployment bindings by kind,
  `{ url, secret, storage }`, sorted by name. It never answers a value, because a release names bindings
  and never holds them, and an author needs only their names.
- `GET /api/studio/apps/{application}/relaxed` answers `{ items }`, the relax entries of the
  application's folder, each as in the [release history](authoring.md#release-history), with the id of
  its contract, `contractId`. Its words come from the installed module version that the folder pins.

### Resource activity

_(planned for M31)_

`GET .../resource-activity` answers `{ items }`, one for each resource of the folder and of the modules the
application uses. It is a route of its own, so the resource list stays free of the database. Studio's
[kind lists](studio.md#kind-lists-and-module-resources) read it.

- **`status`** is `new`, `changed` or `published`, against the live release. A file the release does not
  hold is new, and with no live release every file is. A file it holds that no longer loads is changed. A
  module's resource changes with a new module version or a layer.
- **`titles` and `descriptions`** go with a module's resource: its label and description by locale, from
  the module's own texts.
- **`modifiedBy` and `modifiedAt`.** Studio's writes record who wrote a resource and when, with the version
  the write left, and a module's layer write counts too. The route answers them only while the file still
  has that version, so a file changed in git shows the dash rather than the wrong person.
