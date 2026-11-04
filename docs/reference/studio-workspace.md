# Studio workspace

Detailed reference for how Studio is laid out: the list of applications, the workspace an open application takes, its
band, its page headings, its Overview, its Releases list and a release's page. The form that edits a resource is in
[Studio resource form](studio-resource-form.md). What each editor, designer and button does is in [Studio](studio.md). The conventions of [architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_
are not built yet, and Dn refers to [decisions.md](../decisions.md). The rules come from
[D128](../decisions/d128-rules-from-the-studio-review.md) and
[D129](../decisions/d129-rules-from-the-studio-editors-review.md) and
[D132](../decisions/d132-rules-from-the-studio-settings-review.md). The Applications, Workspace and Releases sections are built, except where marked. The rest of this page is _(planned for M31)_.

## Applications

- **Tiles by title.** `/studio` with no application open lists the applications that `GET /api/studio/apps` gives, as
  the home page lists tools: one tile each with the application's mark, its title and its description in the UI
  locale, `en` as the fallback and the `name` when the application has no title. The tiles take the home page's grid
  of up to three columns, see [platform home page](shell.md#platform-home-page). An application has no sites under it
  here.
- **New application… leads.** It is the list's main action, on the right of the page's heading, Studio, shown when
  `GET /api/me` answers `studioCreate: true`, and it also shows when Studio lists no application. The list has no band
  and no breadcrumb, as the header's app switcher leads home.
- **The API gives titles.** `GET /api/studio/apps` gives each application's `titles` and `descriptions` by locale,
  from the `label` and `description` of its manifest and its text resources, see
  [Studio API](authoring.md). A folder that does not load gives its `name` alone.

## Workspace

- **The application takes the sidebar.** An open application draws its navigation in the shell's sidebar, in place
  of a site's navigation, see [layout](shell.md#layout). A page that claims the sidebar is a workspace: `spacing-md`
  round its content, a compact band and 32 px sidebar entries.
- **The application heads the sidebar.** Its row is the `bar` size, 48 px, as the band beside it, so their rules line
  up. It is a menu that lists every application, marks the open one and ends in a caret, and New application…
  follows the choices. It has no entry back to the list: Studio in the header's app switcher leads there.
- **The row stays.** The application row stays in place, as the band beside it does, and only the sidebar's entries
  scroll under it. Beside a sidebar the footer sits under the content column only, so the sidebar runs to the bottom
  of the window and is never pushed up by the footer, see [layout](shell.md#layout).
- **Whole-application entries come first.** Overview, Releases and Settings, then the resource kinds in groups by
  what an author does with them: Data (entity, seed, sequence, data source, migration), Pages (site, page, form),
  Logic (process, rule, event, trigger), Access (role, policy, identity provider), Integration (connector, endpoint,
  webhook), Texts and Tests (scenario). A kind outside these groups shows under Other, which no kind of the platform
  needs. The Access group ends in the policy matrix. Settings opens the
  application's manifest.
- **Only kinds with resources.** The sidebar leaves out a kind the application has no resource of. The Overview
  introduces those kinds, see [Overview](#overview). No entry shows a count.
- **Open and collapsed.** An open entry is its label alone and a collapsed one its icon alone, with the label as its
  tooltip. Collapsed, a group's title gives way to a thin rule.
- **A resource belongs to its kind.** A resource open in the editor marks its kind's entry, and the address keeps the
  view, as in `/studio?app=PurchaseRequests&resource=<id>`.


## Band

- **State and actions alone.** Every Studio page of an open application has one compact band, 48 px high, the `bar`
  size. It holds the application's state on the left and its actions on the right, nothing else: no breadcrumb, no
  kind and no title, as the sidebar already leads back to a kind's list and the page's heading names the page. A page
  with nothing for the band has none: Studio's list of applications has no band.
- **The state in words.** The application's errors and warnings are one text button, "2 errors, 1 warning", the errors
  in `dangerText` and the warnings in `warningText`, that opens the [problems panel](#problems-panel). After it, in
  `textMuted` and never a link: "Changes not live" when the folder differs from the live release, "Not published yet"
  when there is no live release, "Release 7 is live" when nothing needs attention, so a quiet band never reads as
  still loading, and "Releases did not load" in `warningText` when the history fails. A live release that is
  off-standard reads "Release 3 is live, off-standard", as the Off-standard tag marks a release, never the folder, and
  stays beside a release's number. No state has an icon, as its
  words already say it. While the history loads, only the problems show.
- **The actions.** New resource…, Validate, Preview and Run tests are text buttons with no icon, the same gap apart,
  and Publish… is the band's one solid button, see [buttons](actions-and-controls.md#buttons). New resource… heads
  them and is never beside Publish…, see [new resource](#new-resource).
- **It stays in place.** The band stays under the shell's header while the page scrolls, see
  [layout](shell.md#layout).

## Problems panel

- **Beside the page.** The band's errors and warnings open a Problems panel on the right, under the header and the
  band. It is not modal: the page stays usable while it is open.
- **Work to do.** It lists every error, then every warning, each with its message and where it is. Opening one opens
  its resource at the place it names, and the panel stays open, so the author fixes one problem after another.

## Page heading

- **One heading on every page.** Every Studio page heads its content with a level-4 heading with no margin of its
  own, a page padding below the band, as the platform's other pages head theirs. It names what the page is about: the
  Overview says the application's title; another sidebar entry says its name, such as Settings, Entities, Releases or
  Policy matrix; an open resource says its name, see [names](#names); Studio's list of applications says Studio.
- **A line under it.** An optional line in `textMuted`, at the theme's font size, says more: the application's
  description on the Overview, or the module an open resource comes from. It never names a file or a path, which are
  technical names the JSON view shows to whoever needs them.
- **The page's actions on its right.** A page's own actions, such as a scenario's Run scenario or the list's New
  application…, sit on the right of its heading.

## Names

What Studio shows is a text, never an internal code, see [text](actions-and-controls.md#text).

- **A resource by its label.** Studio names a resource that has a label by it, in the author's language: an entity,
  a role or an identity provider by its `label`, a page or a site by its `title`. A text resource is named by its
  language. A resource that only authors see has no label and is named by the name its author gave.
  `GET .../resources` gives each resource's `titles` per locale, and a text resource's `locale`.
- **A kind by its name.** Every kind has a name for its list, such as Entities, and one for a single resource, such
  as Entity, in the author's language.
- **A person by their name.** Who activated a release is the person's name, "System" for the server's activation at
  startup, "The platform operator", or "Someone no longer known" for an id with no name left, in Studio and the
  console.

## New resource

- **On the band.** New resource…, on every page of an open application, asks for a kind, by its name for one
  resource, in the sidebar's groups and order, then for a name, or for a locale when the kind is text.
- **On a kind's list.** A kind's list has its own button in its toolbar, named for the kind, such as New entity….
  Its dialog, New entity, asks for the name alone, or the locale for a text resource.
- **One list of kinds.** A kind an author may create is offered everywhere kinds are offered: the band's dialog, the
  sidebar and More you can add. Migrations are offered too, as [D54](../decisions/d54-the-entity-designer-and-the-policy-matrix.md)
  lets an author write one by hand, though the entity designer usually writes it.

## Overview

The Overview is the first view of an open application. It says what to do next rather than what the application
holds, shows no counts and repeats nothing the band says. Under its heading and the application's description it
shows, in one column and in order:

- **Get started**, for an application with no entity: four steps, each a link to where it is done. Describe your data
  with an entity, show it on a page, put the page on the site, then try it and publish.
- **Live**, once the application has a live release: each live site as the home page shows it, its mark, its title as
  the link and its description, in the home page's grid and order, see
  [platform home page](shell.md#platform-home-page). The sites are those of the live release, from `GET /api/sites`,
  so a site added since is not there. A tile opens the running site in a new tab, so Studio stays open, and its
  accessible name says so, such as "Purchasing, opens in a new tab". A live release with no site says so.
- **Changes since the live release**: each file that differs from the live release, from the `changes` of
  [`GET .../releases`](authoring.md#release-history), in the [change list](#change-list). With no change, one line says that everything is
  live; with no live release, one line says that Publish makes the application live. Then a link to every release.
  A row opens the resource as it is now: the diff view of
  [AI authoring in Studio](ai-authoring-in-studio.md#the-diff) is not planned here.
- **More you can add**: each kind the application has no resource of, in the sidebar's groups, migrations included,
  each on lines of its own: the kind as a link to its list, what it is for, and in `textMuted` when one is needed, in
  natural words, such as "Handy for lists that must be ready on the first day". The kinds Get started leads to are
  not repeated while it shows. Once the application has every kind there is no block, and nothing takes its place.

The Overview lists no problems: the band says how many, and the [problems panel](#problems-panel) lists them on every
page.

## Change list

Studio has one change list, used by the Overview's changes since the live release and by a release's changes.

- **Three aligned columns.** The resource's name, as the link that opens it; its kind in the singular, such as Entity;
  and Added, Changed or Removed. A removed file is named from the live release and is not a link, as nothing is left to
  open. On a [release page](#release-page) a changed resource is text, as the folder may have moved on.
- **Modules and packages too.** Its rows are the changed resources and the modules and packages that differ, as
  "Upgraded from 1.1.0 to 1.2.0", "Added at 1.0.0" or "Removed, was 1.0.0". With none, one line says so.
- **Order.** It groups the kinds in the sidebar's order, then sorts by name, see
  [list order](actions-and-controls.md#list-order).

## Releases

The Releases entry lists the application's releases from [`GET .../releases`](authoring.md#release-history), on the
[shared table](actions-and-controls.md#tables).

- **One row per release,** the highest number first, as a publish gives a release its label and note and activating
  goes to the release one wants.
- **Columns.** Release, the number, as the link that opens the [release page](#release-page) _(planned for M31)_, with the Live tag beside the one that runs, as the band says, and the
  Off-standard tag beside an [off-standard release](module-contracts.md#off-standard-releases); Label; Published, when
  it was published; Published by, who published it, see [names](#names); and Note. Label and Note show the dash when
  the publish gave none. There is no Status column: a release that is not live has no state of its own to name.
- **Activate….** Every row but the live one has Activate…, an icon button named "Activate release 6", shown to a
  publisher only. It activates the release either way, back to an older release or forward to a newer one, see
  [D130](../decisions/d130-activate-a-release-replaces-roll-back.md). Its dialog says which release replaces the live
  one. It checks first and, when the release [cannot replace the live one](#release-page), titles itself with the same
  sentence as the page and offers Close alone _(planned for M31)_. Until then it sends the existing rollback route.

## Release page

A release opens as a page of its own, `&view=release&release=N`, under the Releases entry, because a release can relax
many points across modules, each with a long reason. A number the history lacks says so in one line, with a link to
every release. On a page, in order:

- **Heading.** "Release N" and its tags, with Activate… as the page's own action on its right.
- **Facts.** Label; Status, live since or replaced on; Published; Published by; and Note, on a line of its own.
- **Relaxed points**, for a release that pins a module: Module, its label; Point, its label with a muted line that says
  what kind of point it is and on which entity; and Reason. With none, one line says the release follows every contract
  of its modules. Point labels are in [named points](module-contracts.md#named-points).
- **Changes**, in the [change list](#change-list). A release that is not live lists what activating it would change,
  titled "If you activate it, compared with live release 4": Added is what activating adds and Removed what it takes
  away. The live release lists what it brought, titled "Changes from release 3". The first live release says there is
  nothing to compare with. The title always names the release it compares with. Choosing any release to compare with,
  and opening a change as a comparison of the two versions, are left for later.
- **Modules and packages**: Name, Type and Version.
- **Activations**: when, by whom, the release it replaced and until when it was live.

**Activate… says up front when it cannot.** An older release that lacks a module the live release pins, or pins it at a
version without some of its entities or fields, cannot replace the live release, see `AXC0213`. The route runs the same
module check on the two stored releases, so the page says "Release 1 cannot replace release 2" with each module and the
rule, and Activate… is off. What depends on the tenant at that moment, such as its bindings, is still checked only on
activation.

`GET /api/studio/apps/{application}/releases/{number}` answers an author with
`{ number, previous, previousIsLive, live, changes, partChanges, modules, packages, blockers }`:

- `previous` is the release it compares with: the live one when this one is not live, and `previousIsLive` is then true,
  else the one just below it, or null for the first.
- `changes` lists the files that differ from it, as in the [release history](authoring.md#release-history).
- `partChanges` lists the modules and packages that differ, `{ type, id, label, change, version, previousVersion }`, with
  `change` `added`, `removed`, `upgraded` or `downgraded`.
- `modules` and `packages` list what the release pins, each with its `id`, `label`, `version` and `previousVersion`.
- `blockers` lists the modules that stop it from replacing the live one, `{ module, label, liveVersion, version }`.
