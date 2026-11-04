# Design review 2026-11-03-studio-settings: the owner's rules

Topic: Studio's Settings page and the Off-standard tag on Releases.

The prototype starts from the tag `design-review/2026-11-01-studio-editors` (b046699d), so the owner keeps seeing D128
and D129, with `main` (b2c03324) merged in for module contracts and off-standard releases. Its own diff starts at the
merge commit e7e2a2c5. Each entry is one agreed change: what the owner asked, in English, and what was built, for the
docs change at the end of the review.

## Merge of `main` (510fc01e)

`main` is merged in for its latest logic beside the review. In a conflict the prototype stays: the 59 conflicted files
under `web/` are the prototype's as they stood, and `en.json`, `vi.json` and `StudioEndpoints.cs` keep its side of each
conflict. The new identity pages of `main`, their tests and `shellSidebar.tsx` are left out, because nothing here uses
them. `ImportDialog.tsx` has `main`'s quota refusal and the prototype's footer.

`main` is the standard for behaviour, so the prototype follows its logic and `main`'s code is not changed for it:

- `scripts/dev.sh` sets the tenant's `Legal` block to example links, and the realm sync writes the links and the terms
  step. `development-legal.mjs` keeps only the email theme, Mailpit, the company theme and the axis client's address.
- The shell's footer reads the legal links from the API, and `developmentLegal` is gone.
- `studio.ts`, `StudioEditor.tsx`, `StudioPublishing.tsx` and `FormWidget.tsx` show the quota title as `main` does.
- `HomePage.tsx` draws the declined card at `/?signIn=declined`.
- `frame.ftl` prefers a client's `http://` address for the licences link, prototype only, because the sync writes
  `https://{host}/licences` and the SPA answers over http on its own port under `scripts/dev.sh`.

- The licences page is `main`'s, so the prototype's expanding rows with each package's licence text are gone. Under
  `scripts/dev.sh`, `write-licences.mjs` writes the public list into `web/public` and the list with versions beside the
  server.

## Carried over from the studio-editors review

- **Off-standard on Releases.** On the Releases list, the Off-standard tag sits beside the release number and opens a
  popover with each relaxed point: the module's label, the point's name and the reason. The Relaxed points column goes.
  Point names show as readable labels, never technical names.

## Agreed changes
- **Release page, not a popover.** A release can relax many points, across modules, each with a reason of up to 500
  characters, so the owner asked whether a popover fits. Agreed: no. The Releases list keeps the Off-standard tag beside
  the number, as Live is, and drops the Relaxed points column; the number is the link that opens the release's own
  page (`&view=release&release=N`, under the Releases entry). The page heads with "Release N" and its tags, its label
  under them, then Published, Published by and Note as a description list, then, for an off-standard release, a
  Relaxed points table: Module (its label), Point (its label, with a muted line saying what kind of point it is and
  on which entity) and Reason. Activate… is the page's own action, on the right of its heading. A number the history
  lacks says so in one line with a link to every release.
- **Points have labels (option A).** The owner chose readable labels over words pieced together from names. A
  contract's states, transitions, validations and effects take a `label` text reference; a ceiling slot uses its
  module role's label, and the ceiling of roles mapped to no slot is "Other roles". Studio reads the words from the
  module files the release stores, so an old release keeps the words it was published with. This is a configuration
  change, so it needs a decision.

## Opening things and dialog sizes

The owner asked to standardise how a table opens a record and how large a dialog is. Agreed, as three levels chosen
by what the person does, never by taste:

- **Opening a record is a page.** Every thing with an identity of its own (a record, a Studio resource, a release, a
  user, a process instance) opens from its table's identifying cell, a real link, as a page with its own address, so a
  link can be shared, reloaded, gone back to and opened in a new tab, and the page has room to grow. Industry
  practice: Polaris resource index and details, SAP Fiori list report and object page, GitHub issues, Salesforce
  record pages.
- **A side panel goes with a page and never opens a record.** It is for what accompanies the page: the problems
  panel, filters, help. A quick look ("peek", as Notion, Linear and Jira offer) would be an addition to the page, with
  the same content and Open full page, never in its place; Axis has none for now. The users page, which opens a user
  in a 480 px drawer, moves to a user page.
- **A modal dialog is for a short task that must end before going on:** confirming (Activate…, Delete), asking for a
  little (a reason, a name, an invitation, a publish's label and note) or choosing one thing (Choose…). Never a
  record's detail or a long form, and never a dialog from a dialog. What needs much scrolling, or to be come back to,
  is a page.
- **A dialog's body scrolls at the dialog's edge.** The owner saw a focused field's sides cut thin and a scrollbar set in
  from the edge. Both came from the body scrolling inside the frame's 24 px padding. Agreed, as Material, Fluent and
  Carbon do: the body reaches the frame's edges and keeps the padding inside it, so the scrollbar runs at the dialog's
  right edge from under the title to above the buttons, which stay, and a field's focus ring has room; the scrollbar
  is the thin one, in the page's mode.
- **Three dialog sizes, by what the dialog holds:** small, about 400 px, for a confirmation or one question; medium,
  about 560 px, for a short form of two to six fields; large, about 800 px, for content that needs the width, such as a
  list to choose from or an import's preview. No larger size on a desktop: what needs more is a page. Below 992 px every
  dialog takes the width less the page margin. A dialog is as tall as its content, at most the window less its
  margins; its body scrolls while its title and buttons stay. The size is a token that a dialog names, never a width of
  its own.
- **A fuller release page.** The owner found the page too thin and asked where the label was. Agreed: the facts are
  Label, Status (live since, or replaced on), Published, Published by, and Note on a line of its own; then Relaxed
  points, Changes from the release numbered just below it (named from the release that holds each file, not links,
  as the folder may have moved on), Modules and packages (version, and Added or "Was 1.1.0" since the previous
  release), and Activations (when, by whom, the release it replaced, live until). A new
  `GET .../releases/{number}` gives the changes, modules and packages.
- **Activate… says up front when it cannot.** Activating an older release that lacks a module the live release pins,
  or pins it at a version without some of its entities or fields, was accepted and then refused by the publisher
  (AXC0213). Agreed: the release endpoint runs the same module check on the two stored releases; the release page
  shows "Release 1 cannot replace release 2" with each module and the rule, and Activate… is off; the dialog opened
  from the list checks first, titles itself with the same sentence and offers Close alone. What depends on the tenant
  at that moment, such as its bindings, is still checked only on activation. This changes an API, so it needs a
  decision.

## Showing what is empty

The owner proposed that information always shows, with Not set or no data, rather than being hidden. Agreed, by
industry practice (GitHub's issue sidebar, Salesforce and Jira record pages, Fiori object pages):

- **A field the object always has always shows.** Empty, it reads "Not set" on a form or a detail and the muted dash
  in a table cell, as D96 has it.
- **A list that is a standing part of the object always shows.** Empty, it says in one line what that means, such as
  "This release uses no modules or packages." or "None. This release follows every contract of its modules."
- **Hide only what does not apply or is not allowed.** A part that does not apply to the object's kind or state, such
  as relaxed points for a release that pins no module, or a field the person may not read, is left out.
- **Loading and failing are never empty.** A part that is still loading or failed to load says so, never Not set.
- **The band says off-standard in words.** The owner found the band's Off-standard tag a repeat of the heading's and
  asked why it led the band, against the standard. It broke D129's rule that the band's states are words with no icon
  or tag, and it marked the folder, not the release. Agreed: the band reads "Release 3 is live, off-standard" in
  textMuted; the Off-standard tag stays only beside a release's number, in the list and on its page.
- **One change list, modules and packages included.** The owner did not understand the "Since release 2" column, then
  found a table for the changes unlike the Overview's list of the same thing. Agreed: Studio has one change list
  component, the Overview's three aligned columns (name, kind, change, D129), used by the Overview's changes since the
  live release and by a release's changes from the one before it. Its rows are the changed resources and the modules
  and packages that differ ("Upgraded from 1.1.0 to 1.2.0", "Added at 1.0.0", "Removed, was 1.0.0"); with none, one
  line says so. Modules and packages lists Name, Type and Version only.
- **Changes compare with the live release.** The owner pointed out that a release is opened to decide whether to
  switch to it, so its changes should be against the live release, not the one before it. Agreed, as Argo CD shows the
  diff from live before a sync: a release that is not live lists what activating it would change, titled "If you
  activate it, compared with live release 4" (Added is what activating adds, Removed what it takes away, a module
  "Removed, was 1.0.0"); the live release lists what it brought, titled "Changes from release 3"; the first live
  release says there is nothing to compare with. The title always names the release it compares with. Choosing any
  release to compare with goes with the later comparison page.
- **Later: a change opens a comparison.** On a release page a changed resource is text, not a link, as the folder may
  have moved on. Opening a comparison of the two releases' versions (as GitHub shows a commit's file) is left for later,
  while the editors are still being built on main.
- **An empty table keeps its header.** The owner noted that the standard empty table, as the artifact's Table card
  shows, keeps its header with the empty text in its body. Agreed for every table, the release page's included. The
  empty text is textMuted: Ant Design drew it at a quarter of the text colour, 1.8:1, which fails 4.5:1; fixed in
  AxisTable for every table.
- **Packages have a label.** A package's manifest gains `label`, as a module's has, so Studio never names a package by
  its id. A configuration change, so it needs a decision; the issue makes it required and adds it to every package.
- **An icon button's tooltip and a dialog.** Opening a dialog from an icon button logged React's flushSync warning:
  Ant Design's focus trigger closes the tooltip with flushSync when the dialog takes focus mid-render. The tooltip now
  listens to hover alone and the button opens it on keyboard focus itself.

## The resource form standard

The owner noted that many Studio views are the same schema form as Settings, so its rules are the standard for every
resource without a designer of its own (application, role, policy, rule, event, trigger, sequence, data source,
migration, webhook) and for the schema part of the editors that have one. Agreed, from GitHub's, Vercel's and
Shopify's settings pages:

- **Sections.** The root properties sit in the kind's sections, each a panel on surface, as the Overview's blocks,
  with a heading and a line that says what it holds. Settings: General (name, title, description, time zone),
  Modules, Packages, File storage. A property no section names goes to a last section with no heading. A section that
  holds one field names it in its heading, so the field has no label of its own; one that holds one object shows the
  object's fields itself.
- **Fields named in words.** Every field has a label and may have a help text, the texts
  `shell.studio.field.<kind>.<path>` and `….help`, drawn as the form widget draws them: the label in medium textMuted,
  the help in small textMuted between the label and the input. No `formatVersion`, `textKey` or `modules 1`.
- **The file's identity is hidden.** `id`, `kind` and `formatVersion` are not shown; `name` is, with its help.
- **A label is one text box, in the author's language.** The owner asked whether a box per language is the standard;
  it is not, as it grows with every language. Agreed, as Contentful, Sanity and Webflow edit one language at a time and
  Shopify, Salesforce and WPML translate on a screen of their own: one box in the author's language, or the
  application's first when it lacks that one; Translations…, the translate icon (A and a glyph, as Google Translate and Material draw it), opens every language of the label
  in a medium dialog (Done keeps, Cancel puts back), the icon sitting inside the box at its end, as a password
  box holds its eye, so the box keeps the width and the 32 px height of every field; a muted line under the box names the languages it lacks, "Missing
  in Vietnamese". Many labels at once are translated in the text resources' table. The key is Studio's own, made from
  the resource and the path, and never shown. The texts are part of the resource's one Save: no Write texts button.
- **One save bar.** While there are unsaved changes, a bar stays at the foot of the window across the content:
  "Unsaved changes" on the left, Discard and Save on the right. With nothing to save it is not drawn. Save writes the
  label texts first, then the resource.
- **A list of objects is a table.** One column per property, named by its field text; a value of a known kind by its
  label (a module or package by the label of its manifest), a nested list or map by its count. Add… (such as Add
  module…) is the table's main action; each row has Edit… and Remove as icons. Edit… and Add… open the item in a
  dialog, medium for plain values and large for an item holding a list or a map, titled by the verb of the button that
  opened it: "Add module", as it adds an installed module and creates none (the owner found New module wrong), and
  "Edit module". The dialog edits its own copy: Done
  writes it to the list as one step to undo and waits for the item's required fields; Cancel leaves the list as it was,
  so Add… adds no row until Done (the owner saw an empty row appear behind the dialog). Choosing a module or package
  fills an empty version range with its newest version, "^1.2.0"; a chosen module with no roles or connectors says so. Inside the dialog a nested list is rows of inputs with Add row, as a dialog never opens another.
- **A field that knows what it holds has its control.** A module or package is chosen from those installed, by label,
  from a new `GET /api/studio/catalog`; a module's role and connector maps are one row per slot of the chosen module,
  its label, then the application's role or connector to choose. No "Edit this value in the JSON view" for them.
- **Danger zone.** Delete… is the last section of a resource's page, framed in dangerText, with a line that says what
  deleting does; its dialog still asks. The application has none.
- **No file line under a resource's heading.** The owner agreed to drop the muted file and path line D129 put there: a
  technical name, which the JSON view shows to whoever needs it. Amends D129's "Every page has one heading".
- **Relaxed points in Settings are read-only (option A).** The owner asked whether B was set aside only for its extra
  work. It was not: a relax is a decision that needs the point's context, which the contract page shows, and each
  thing is edited in one place, as GitHub edits a branch rule on its own page. Settings lists a module's relaxed
  points in words, each linked to its contract page, with its reason and a line saying where to relax or undo; a new
  `GET .../relaxed` gives them, and each point carries its contract's id. A page of every relaxed point, should bulk
  review be needed, is the place to edit many at once, never a dialog.
- **Uses and Used by by name.** Two panels after the form and before the Danger zone, each Studio's one change list:
  the other resource by its label as the link that opens it (a Used by entry at the path of the reference), its kind,
  and how the one uses the other in words ("Grants to it", "Assigns tasks to it", one text per relation of the
  compiler). No file, path or relation code; a label's text is left out of Uses, as the form shows the label; the
  same resource used the same way shows once; with none, one line says so. Entries no longer expand a level: the link
  opens the resource, whose page has its own lists.

- **A time zone is chosen, not typed.** The owner asked why the time zone was a text box. Agreed, as calendars do: a
  searchable select of the time zones the server knows (the catalog's `timeZones`, so every choice passes AXC0277),
  each "(UTC+07:00) Asia/Ho Chi Minh", by offset then id; cleared, the application's dates are UTC. A schedule
  trigger's time zone is the same control. Selects take the field's full width, as every field does.
- **A section's one list puts Add… in the heading.** The owner did not want Add module… on a row of its own: a section
  that holds one list puts its main action on the right of the section's heading, and the table has no toolbar row.
- **A cell shows content, never a bare count.** The owner found counts such as Roles 2 or Relaxed points 4 of no use.
  Agreed: a nested list or map shows what it holds in words. A module's roles are one line per slot, "Budget approver:
  Department manager", a slot no role plays reading "Not mapped" in warningText; its relaxed points are the Off-standard
  tag, as the Releases list marks a release, the points being in its dialog; a column that would be empty in every
  row, such as Connectors when no module has any, is left out, while the dialog keeps every field. Short fixed-form
  columns (a version range, a tag) are as wide as their content, so the free text has the room.

## How lists are ordered

The owner asked what the tables sort by and whether there is a standard. There was none: each followed its file or its
server. Agreed, three rules chosen by what a list is:

- **An order the author gives keeps it.** Process steps, wizard steps, a table's columns, a form's fields, navigation:
  as written, and the author moves them. Sorting them would change their meaning.
- **Records in time are newest first.** Releases, activations, history.
- **A set sorts by name.** Modules, packages, relaxed points, Uses and Used by, the changes: by the name a person reads,
  with the page language's collation (`Intl.Collator`, so Đ follows D in Vietnamese). A list that mixes kinds groups
  them first in the sidebar's order (the application, Data, Pages, Logic, Access, Integration, Texts, Tests, then
  modules and packages), then by name; relaxed points by module, then by point. The order of a module's pins in
  `application.json` only decides which entry AXC0306 reports, so it is no author's order.
- **Sorting is for display only.** The file keeps its order, so adding a module adds one line to its diff. Studio's
  small tables have no sort by column, as their order is already the one to expect; the `table` widget keeps D96's rules.

## Undo rather than confirm

The owner asked why Remove asks nothing, and noted that Discard drops every change, not one. Agreed, as Nielsen Norman
Group and Material advise, and as mail and documents do:

- **What can be taken back is undone, not confirmed.** Removing a row changes only the draft, so it asks nothing; a
  notice names it, "Organization removed.", with Undo, for ten seconds.
- **Every editor keeps a history.** Undo and Redo, icons in the save bar with Ctrl+Z and Ctrl+Shift+Z, step through
  every change of the draft, a label's texts among them, on every resource kind, no longer only on the designers. The
  bar stays while a step can be redone, reading "No unsaved changes" once all are undone.
- **What cannot be taken back is confirmed.** Discard drops every unsaved change at once and no Undo brings them back,
  so a small dialog asks "Discard all unsaved changes?" with a red Discard; deleting a resource keeps its dialog.

## Surfaces and empty states

- **A dialog draws on the surface token.** The owner saw a table's header sink inside a dialog. In dark mode the dialog
  drew on Ant Design's own elevated grey, #1f1f1f, which is no Axis token, while tables and inputs drew on `surface`,
  #141414, so they sat in a darker patch. Agreed, as D129 has every design value a token: the theme gives dialogs the
  `surface` colour, as panels have, so every part inside sits on the colour it was made for and the mask behind sets
  the dialog apart. One theme setting, no part fixed on its own.
- **One empty state.** The owner found every place showing emptiness its own way, and chose the simple one: Ant
  Design's simple empty picture with one line in textMuted. `EmptyState` draws it for every empty table (through
  AxisTable) and list (Uses, Used by, a release's changes). Its line is "No data yet." unless the emptiness says
  something the reader needs, such as a release following every contract or matching the live release. A field with
  no value keeps Not set, a cell the dash.
- **A version range is chosen, not typed.** The owner asked why the version range was typed. A range has three forms
  only (`1.2.0`, `~1.2.0`, `^1.2.0`), so it is two selects, as package managers split them: Version, from the installed
  versions newest first ("1.2.0 (newest)"), and Updates in words, "Compatible updates (1.x)" for `^`, the default,
  "Fixes only (1.2.x)" for `~`, "This version only" for exact. The file keeps the range as written, so no
  configuration changes. A table shows it in words, "1.2.0 and later 1.x", "1.2.0 only". A version no longer installed
  stays shown, with "0.9.0 is not installed. Choose an installed version." in warningText under it.
- **A label is above its input, everywhere.** The owner set the convention: every field's label sits above its input,
  as the form widget has it (D96), never beside it. A module's role and connector maps follow it: each slot is a field
  of its own, its label (the field label's style, tied to the select) above its select. A group's fields sit in from
  the group's label behind a thin 2 px rule in borderSubtle, so they read as its fields and not its peers, as GitHub
  and Linear set sub-fields apart.
- **A binding is chosen, never typed.** The owner asked what File storage is and what its box takes. It takes the name
  of the tenant's storage binding, which decides where the bytes of file fields go (today only `database`; none keeps
  them in the tenant's database). Agreed: a new `GET /api/studio/bindings` answers the request tenant's binding names
  by kind, url, secret and storage, and never a value; `BindingPicker` chooses from the names of the kind the place
  needs, for file storage ("The tenant's database, with no binding" when none, clearing the whole entry), a webhook's
  URL and secret, and a connector's base URL, token URL and secrets. A name the tenant lacks stays shown with "The
  tenant has no … binding named X, so activation will refuse it." This changes an API and makes `studio.md`'s "not a
  pick from a list, because no route lists a tenant's binding names" false, so the docs change rewrites it.
- **A list's row opens from its identifying cell.** The owner noted that the standard table opens a row from a link,
  so an Edit icon is redundant. Agreed, as D96 and D129 have it: the first column, the module or package by its label,
  is the link that opens the item's Edit dialog, a button styled as a link as it goes to no address; each row keeps
  Remove alone.
- **Uses and Used by keep the form's rhythm.** The owner saw Uses half as far from the form as the sections are from
  each other: the editor set its parts 8 px apart. They are spacing-md apart now, as the form's sections are; and Uses
  names modules and packages by their labels, as the catalog now reaches it.
- **Uses leaves out what the form shows; Used by only where it can apply.** On Settings, Uses repeated the Modules and
  Packages tables above it, and Used by was always empty, as nothing in Axis refers to the application. Agreed, by the
  rules already set, not as an exception: each kind declares the references its form already shows (every kind its
  labels' texts; the application its modules and packages), which Uses leaves out, and a kind nothing can refer to
  (the application) has no Used by, as it never applies; a panel with nothing left is left out. Settings therefore
  ends at File storage; every other kind keeps both panels.

## Form and JSON

The owner asked whether both tabs stay and whether they stay in step. They do: both edit one draft, so a change in
either shows in the other at once, and Undo, Redo, Discard and Save act on both. Agreed to keep both, as VS Code keeps
its settings UI beside `settings.json` and Azure a JSON view beside its forms, and to close two gaps:

- **While the JSON has an error, the form is read-only.** It shows the last valid version under a warning, "The JSON
  has an error, so the form shows the last valid version and cannot be edited. Fix the JSON, or Undo.", with Go to
  JSON; its inputs and its lists' actions are off, so no edit in the form overwrites the JSON being typed, as VS Code's
  settings UI refuses edits while `settings.json` is broken. Save is off.
- **The JSON view is a code editor.** CodeMirror, which Studio already has, with `@codemirror/lang-json`: JSON
  highlighting, line numbers, and two checks as one types, each marked in the gutter and underlined where it is: a
  syntax error ("This is not valid JSON here.") and every place the content breaks the kind's schema, in words ("Add
  version.", "colour is not a field of this resource."), checked with Ajv, which moves from the dev dependencies to
  the dependencies.
- **The JSON view looks like a part of Axis.** The owner found it ugly: a thin, broken border, a wide gap below and
  red text. Agreed: its border is a field's, `border` at 1 px with `radius-base`, and the gutter stays inside it. It
  reaches down to `spacing-md` above the footer at any window height, at least 240 px, and scrolls inside. Its text keeps
  `spacing-sm` from the border. Its colours are tokens, never CodeMirror's: names `primaryText`, strings `text`, numbers
  `infoText`, `true`, `false` and `null` `warningText`, punctuation `textMuted`.
- **JSON that does not parse is an unsaved change.** The save bar shows while it lasts, with Undo and Discard, and Save
  is off. A change that leaves the data as it was, such as a space or Format, shows no bar, because nothing can be saved.
- **A tab bar sits spacing-md under what precedes it.** The owner found the gap between a page's heading and its tabs
  too wide: Ant Design pads a tab's label 12 px above it. The theme starts a tab's label at the top of its bar, the
  underline keeping its 12 px under it, for every tab bar.
