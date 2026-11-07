# Studio workspace

This page describes how Studio is laid out around the work: the list of applications, the workspace
that an open application takes, and the band, headings, Overview, Releases and History that every
application shares. The form that edits one resource is in
[Studio resource form](studio-resource-form.md), and what each editor and designer does is in
[Studio](studio.md). Shared controls, such as buttons and tables, follow
[actions and controls](actions-and-controls.md).

## Applications

`/studio` with no application open lists the applications that `GET /api/studio/apps` gives, as tiles
in the home page's grid, see [platform home page](shell.md#platform-home-page). Each tile shows the
application's mark, title and description in the UI locale, with `en` as the fallback and the
application's `name` when it has no title. An application shows no sites here.

The page's heading is Studio, and New application… is its main action, on the heading's right. It
shows when `GET /api/me` answers `studioCreate: true`, also when Studio lists no application. The list
has no band and no breadcrumb, because the header's app switcher already leads home.

## Workspace

An open application takes the shell's sidebar for its own navigation, in place of a site's, see
[layout](shell.md#layout). Its content is compact: `spacing-md` round it, a compact band and compact
sidebar entries.

The sidebar starts with the application's row, at the height of the band beside it, so their lines
meet. The row is a menu of every application, with the open one marked, and New application… after
them. It has no entry back to the list, because Studio in the header's app switcher leads there. The
row and the band stay in place while the page scrolls, and only the sidebar's entries scroll, under
the row. Beside a sidebar the footer sits under the content column only, so the sidebar always runs to
the bottom of the window.

Under the row come Overview, Releases, History and Settings, which concern the whole application.
History lists the application's revisions, see [revisions](ai-authoring-in-studio.md#revisions). Then
come the resource kinds in groups by what an author does with them:

| Group       | Kinds                                                  |
| ----------- | ------------------------------------------------------ |
| Data        | entity, seed, sequence, data source, migration         |
| Pages       | site, page, form                                       |
| Logic       | process, rule, event, trigger                          |
| Access      | role, policy, identity provider, then the policy matrix |
| Integration | connector, endpoint, webhook                           |
| Texts       | text                                                   |
| Tests       | scenario                                               |

A kind outside these groups shows under Other. Settings opens the application's manifest. The sidebar
leaves out a kind the application has no resource of, since the Overview introduces those, and no
entry shows a count. An open entry shows its label alone and a collapsed one its icon alone, with the
label as its tooltip; collapsed, a group's title becomes a thin rule.

A kind's entry opens its list, at `&kind=<kind>`, under a heading with the kind's name; for a kind that
modules hold, the module's resources follow. A resource open in an editor marks its kind's entry, and
the address keeps the view, as in `/studio?app=PurchaseRequests&resource=<id>`.

## Band

Every page of an open application has one compact band, of the `bar` height, under the shell's header.
It holds the application's state on the left and its actions on the right, and nothing else: no
breadcrumb, kind or title, because the sidebar leads back to a kind's list and the page's heading names
the page. Studio's list of applications has no band. The band stays in place while the page scrolls.

The state is words, with no icon:

- The application's errors and warnings are one text button, such as "2 errors, 1 warning", with the
  errors in `dangerText` and the warnings in `warningText`. It opens the
  [problems panel](#problems-panel).
- After it, in `textMuted` and never a link, comes one of: "Changes not live" when the folder differs
  from the live release, "Not published yet" when there is no live release, "Release 7 is live" when
  nothing needs attention, or "Release 7 is live, off-standard" when the live release is
  [off-standard](module-contracts.md#off-standard-releases). A quiet band never looks as if it is
  still loading. The off-standard words mark a release, never the folder, so a relax entry that is
  not live shows nothing in the band. When the release history fails it reads "Releases did not load",
  in `warningText`. While the history loads, only the problems show.

The actions are New resource…, Validate, Preview and Run tests as text buttons with no icon, then
Publish… as the band's one solid button, see [buttons](actions-and-controls.md#buttons). New resource…
comes first, so it is never next to Publish….

- **Ask AI** _(planned for M35)_. Ask AI is a text button just before Publish…, and it opens the
  request panel of [AI authoring in Studio](ai-authoring-in-studio.md).

## Problems panel

The band's errors and warnings open a Problems panel on the right, under the header and the band. It is
not modal, so the page stays usable while it is open. It lists every error and then every warning, each
with its message and where it is. Opening one opens its resource at the place it names and leaves the
panel open, so an author can fix one problem after another.

## Page heading

Every Studio page heads its content with one level-4 heading, a page padding below the band, as the
platform's other pages do. It names what the page is about: the application's title on the Overview,
the entry's name on another sidebar entry, such as Settings or Policy matrix, the resource's
[name](#names) on an open resource, and Studio on the list of applications.

An optional line under the heading, in `textMuted`, says more: the application's description on the
Overview, or the module an open resource comes from. It never names a file or a path, which only the
JSON view shows. The page's own actions, such as a scenario's Run scenario, sit on the heading's right.

## Names

Studio shows words, never an internal code, see [text](actions-and-controls.md#text).

- **A resource** is named by its label in the author's language: an entity, a role or an identity
  provider by its `label`, a page or a site by its `title`. A text resource is named by its language.
  A resource that only authors see, and so has no label, is named by the name its author gave.
  `GET .../resources` gives each resource's `titles` by locale, and a text resource's `locale`.
- **A kind** has a name for its list, such as Entities, and one for a single resource, such as Entity,
  in the author's language.
- **A person**, such as who activated a release, is named by their name. The server's activation at
  startup reads "System", the platform operator "The platform operator", and an id with no name left
  "Someone no longer known". The console names people the same way.

## New resource

New resource… on the band asks for a kind, by its singular name, in the sidebar's groups and order,
and then for a name, or for a locale when the kind is text. A kind's list also has its own button on
its heading's right, named for the kind, such as New entity…, whose dialog asks for the name alone.

Every kind an author may create is offered wherever kinds are offered: the band's dialog, the sidebar
and More you can add. That includes migrations, which an author may write by hand although the
[entity designer](entity-designer.md) usually writes them.

## Overview

The Overview is the first page of an open application. It says what to do next rather than what the
application holds, so it shows no counts and repeats nothing that the band says; in particular it lists
no problems, which the band counts and the [problems panel](#problems-panel) lists. Under the heading
and the application's description it shows, in one column:

1. **Get started**, for an application with no entity: four steps, each a link to where it is done.
   Describe your data with an entity, show it on a page, put the page on the site, then try it and
   publish.
2. **Live**, once the application has a live release: each site of the live release, from
   `GET /api/sites`, as a tile of the [platform home page](shell.md#platform-home-page), in its grid
   and order. A tile opens the running site in a new tab, so Studio stays open, and its accessible name
   says so, as in "Purchasing, opens in a new tab". A live release with no site says so.
3. **Changes since the live release**: the files that differ from it, from the `changes` of
   [`GET .../releases`](authoring.md#release-history), in the [change list](#change-list). With no
   change, one line says that everything is live; with no live release, one line says that Publish
   makes the application live. A link to every release follows. A row opens the resource as it is now,
   not the [diff](ai-authoring-in-studio.md#the-diff) of AI authoring.
4. **More you can add**: each kind the application has no resource of, migrations included, in the
   sidebar's groups. Each kind is a link to its list, with a line on what it is for and, in
   `textMuted`, when one is needed, such as "Handy for lists that must be ready on the first day".
   Contracts and layers are not offered, and neither are the kinds that Get started leads to while it
   shows. Once the application has every kind, the block is gone and nothing takes its place.

## Change list

The Overview's changes and a release's changes share one list.

- **Three aligned columns:** the resource's name as a link that opens it, its kind in the singular,
  such as Entity, and Added, Changed or Removed. A removed file is named from the live release and is
  not a link, as nothing is left to open. On a [release page](#release-page) a changed resource is plain
  text, because the folder may have moved on since that release.
- **Modules and packages** _(planned for M31)_. The list also shows each module and package that
  differs, as "Upgraded from 1.1.0 to 1.2.0", "Added at 1.0.0" or "Removed, was 1.0.0". With no
  difference, one line says so.
- **Order:** by kind in the sidebar's order, then by name, see
  [list order](actions-and-controls.md#list-order).

## Releases

The Releases entry lists the application's releases from
[`GET .../releases`](authoring.md#release-history) on the
[shared table](actions-and-controls.md#tables), one row per release, the highest number first: a publish
gives a release its label and note, and activating goes to whichever release is wanted.

| Column       | Shows                                                                                                                                                       |
| ------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Release      | The number, with the Live tag beside the release that runs and the Off-standard tag beside an [off-standard release](module-contracts.md#off-standard-releases) |
| Label        | The label the publish gave, or the dash                                                                                                                     |
| Published    | When it was published                                                                                                                                       |
| Published by | Who published it, see [names](#names)                                                                                                                       |
| Note         | The note the publish gave, or the dash                                                                                                                      |

There is no Status column, because a release that is not live has no state of its own.

Every row but the live one has Activate…, an icon button named as in "Activate release 6", shown to a
publisher only. It activates that release, back to an older one or forward to a newer one, and its
dialog says which release replaces the live one. [D130](../decisions/d130-activate-a-release-replaces-roll-back.md)
agrees the same word for the concept, the route and the request kind.

- **The number opens the release page.** The number in the Release column is the
  link to the [release page](#release-page).
- **Activate… checks first.** The dialog loads the release when it opens and shows a spinner meanwhile.
  When the release
  [cannot replace the live one](#release-page), the dialog titles itself with the same sentence as the
  release page and offers Close alone. When the release does not load, the dialog falls back to its usual
  sentence and Activate.

## Release page

A release opens as a page of its own, at `&view=release&release=N` under the Releases entry, because it
can relax many points across modules, each with a long reason, which a popover cannot hold. A number
the history lacks says so in one line, with a link to every release.

The page shows, in order:

1. **The heading** "Release N" with its tags, and Activate… as the page's action on the heading's right.
2. **Facts:** Label; Status, live since or replaced on; Published; Published by; and Note, on a line of
   its own.
3. **Relaxed points**, for a release that pins a module: the module's label, the point's label with a
   muted line that says what kind of point it is and on which entity, and the reason. With none, one
   line says that the release follows every contract of its modules. Point labels are described in
   [named points](module-contracts.md#named-points).
4. **Changes**, in the [change list](#change-list). A release is opened to decide whether to switch to
   it, so a release that is not live lists what activating it would change, under "If you activate it, compared with live release 4": Added is what activating
   adds and Removed what it takes away. The live release lists what it brought, under "Changes from
   release 3", and the first live release says there is nothing to compare with. The title always names
   the release it compares with.
5. **Modules and packages** that the release pins: name, type and version.
6. **Activations:** when, by whom, the release it replaced and until when it was live.

- **Cannot replace the live one.** An older release that lacks a module that the live
  release pins, or pins it at a version without some of its entities or fields, cannot replace the live
  release, as `AXC0213` says. The release route runs the same module check on the two stored releases,
  so the page says, for example, "Release 1 cannot replace release 2", with each module and the rule,
  and Activate… is off. What depends on the tenant at that moment, such as its bindings, is still
  checked only when the release is activated.

## Release route

`GET /api/studio/apps/{application}/releases/{number}` answers an author with
`{ number, previous, previousIsLive, live, changes, partChanges, modules, packages, blockers }`. A
user without the author role is refused, and a number that the history lacks answers 404.

- `previous` is the number of the release it compares with: the live one when this one is not live,
  with `previousIsLive` true, else the one just below it, or `null` for the first. `live` is true when
  this release is the live one.
- `changes` lists the files that differ from `previous`, as in the
  [release history](authoring.md#release-history). It is `null` without `previous`.
- `partChanges` lists the modules and packages that differ, each
  `{ type, id, label, change, version, previousVersion }`, where `change` is `added`, `removed`,
  `upgraded` or `downgraded`. It is `null` without `previous`.
- `modules` and `packages` list what the release pins, each with its `id`, `label`, `version` and
  `previousVersion`.
- `blockers` lists the modules that stop it from replacing the live one, each
  `{ module, label, liveVersion, version }`, where `version` is `null` when the release lacks the
  module. It is empty for the live release and when no release is live.
- A module's `label` is the one in the `module.json` that the release stores, and a package's `label`
  is the one of the installed package of that version. The id stands in when there is none.
