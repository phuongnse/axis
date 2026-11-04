# D132. Rules from the Studio settings review

Status: **Agreed**

The owner reviewed Studio's Settings page, its release page and the platform's dialogs in the running prototype, one
change at a time, and agreed these rules between 2026-11-03 and 2026-11-04. They change some rules of
[D129](d129-rules-from-the-studio-editors-review.md) (the line under a heading, the band's words, the Releases list and
the Overview's changes) and add to [D96](d96-rules-from-the-design-system-review.md). None of it is built yet _(planned
for M31)_, and it builds on D129's issue. The prototype is the tag `design-review/2026-11-03-studio-settings`, whose own
changes start at the commit of `main` it last merged.

## Configuration

- **A contract's points have labels.** A contract's states, transitions, core validations and effects each need a
  `label` text reference, so a point reads as words, never as a name pieced together. A point without one, or a label
  whose key no text resource holds, is a compile error. A ceiling slot uses its module role's label, and the ceiling of
  the roles mapped to no slot reads "Other roles". Studio reads the words from the module files the release stores, so
  an old release keeps the words it was published with, and a part published before this rule falls back to its name. See
  [named points](../reference/module-contracts.md#named-points).
- **A package has a label.** An extension package's manifest gains a required `label`, as a module's has, so Studio
  never names a package by its id. Every package adds one. See [manifest](../reference/extension-packages.md#manifest).

## APIs

- **`GET .../releases/{number}` gives a release's page.** It answers what the release changes, what it pins and what
  stops it from replacing the live release, see [release page](../reference/studio-workspace.md#release-page).
- **The relaxed points carry their words.** `relaxed` in the release history gains each point's module title, part,
  entity and label, and `GET .../relaxed` lists the folder's points with their contract, see
  [Studio API](../reference/studio-resource-form.md#studio-api).
- **`GET /api/studio/catalog` gives what is installed.** It answers the modules, the packages and the time zones, so
  each is chosen rather than typed, see [Studio API](../reference/studio-resource-form.md#studio-api).
- **`GET /api/studio/bindings` gives a tenant's binding names.** It answers the names by kind and never a value, so a
  binding is chosen from a list, see [Studio API](../reference/studio-resource-form.md#studio-api).

## Releases

- **A release has its own page.** A release can relax many points across modules, each with a long reason, so a popover
  does not fit. The number on the Releases list is the link that opens the page, and the Relaxed points column goes. See
  [release page](../reference/studio-workspace.md#release-page).
- **Changes compare with the live release.** A release is opened to decide whether to switch to it, so a release that is
  not live lists what activating it would change, and the title always names the release it compares with. See
  [release page](../reference/studio-workspace.md#release-page).
- **Activate… says up front when it cannot.** The release route runs the module check of activation on the two stored
  releases, so the page and the dialog say so before a publisher tries. What depends on the tenant at that moment is
  still checked only on activation. See [release page](../reference/studio-workspace.md#release-page).
- **The band says off-standard in words.** It reads "Release 3 is live, off-standard". The tag stays beside a release's
  number alone, as D129 has the band's states in words. See [band](../reference/studio-workspace.md#band).
- **One change list.** The Overview and a release's page draw changes with one component, modules and packages included.
  See [change list](../reference/studio-workspace.md#change-list).

## The resource form

- **One standard for every resource.** The rules of Settings hold for every resource without a designer of its own and
  for the schema part of those that have one: sections, fields named in words, one label box with Translations…, tables
  with a dialog that edits a copy, controls that know what they hold, a Danger zone, and Uses and Used by by name. See
  [Studio resource form](../reference/studio-resource-form.md).
- **No file line under a heading.** A file and a path are technical names, which the JSON view shows to whoever needs
  them. This amends D129's "Every page has one heading". See
  [page heading](../reference/studio-workspace.md#page-heading).
- **Relaxed points in Settings are read-only.** A relax needs the point's context, and each thing is edited in one
  place. See [relaxed points](../reference/studio-resource-form.md#relaxed-points-in-settings).
- **Form and JSON stay in step.** While the JSON has an error, the form is read-only. The JSON view is a code editor in
  the tokens, with its own actions, and the expression editor draws in the same colours. See [form and JSON](../reference/studio-resource-form.md#form-and-json) and
  [JSON view](../reference/studio-resource-form.md#json-view).

## The platform

- **Three levels of opening things.** A thing with an identity of its own opens as a page, a side panel goes with a page
  and never opens a record, and a dialog is for a short task. The users page moves a user to a page of their own. See
  [opening things](../reference/actions-and-controls.md#opening-things).
- **Three dialog sizes, by what a dialog holds.** Small, medium and large are tokens that a dialog names. Its body scrolls
  at its edge, and it draws on `surface`. See [dialogs](../reference/actions-and-controls.md#dialogs).
- **Information shows, even empty.** A field an object always has, and a list that is a standing part of it, always show.
  Only what does not apply or is not allowed is left out. See
  [showing what is empty](../reference/actions-and-controls.md#showing-what-is-empty).
- **Lists have three orders.** An author's order is kept, records in time are newest first and a set sorts by name, for
  display only. See [list order](../reference/actions-and-controls.md#list-order).
- **Undo rather than confirm.** What can be taken back is undone, what cannot is confirmed, and every editor keeps a
  history. See [undo rather than confirm](../reference/actions-and-controls.md#undo-rather-than-confirm).
- **A label is above its input, everywhere.** See [fields](../reference/actions-and-controls.md#fields).
- **A tab bar sits `spacing-md` under what precedes it.** See
  [tab bars](../reference/actions-and-controls.md#tab-bars).

_Why:_ each rule came from something the owner saw in the prototype: a popover too small for what it held, a form of raw
property names, a box to type what Axis already knows, a table that opened its rows three ways, and emptiness shown
differently on every page. One standard for a resource and one rule for each of these keeps a person from learning
Studio page by page.
