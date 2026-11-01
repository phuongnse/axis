# Studio workspace

Detailed reference for how Studio is laid out: the list of applications, the workspace an open application takes, its
band and its Overview. What each editor, designer and button does is in [Studio](studio.md). The conventions of
[architecture.md](../architecture.md) apply: sections marked _(planned for Mx)_ are not built yet, and Dn refers to
[decisions.md](../decisions.md). The rules come from
[D128](../decisions/d128-rules-from-the-studio-review.md). All of this page is _(planned for M31)_.

## Applications

- **Tiles by title.** `/studio` with no application open lists the applications that `GET /api/studio/apps` gives, as
  the home page lists tools: one tile each with the application's mark, its title and its description in the UI
  locale, `en` as the fallback and the `name` when the application has no title. The tiles take the home page's grid
  of up to three columns, see [platform home page](shell.md#platform-home-page). An application has no sites under it
  here.
- **New application… leads.** It is the list's main action, shown when `GET /api/me` answers `studioCreate: true`,
  and it also shows when Studio lists no application.
- **The API gives titles.** `GET /api/studio/apps` gives each application's `titles` and `descriptions` by locale,
  from the `label` and `description` of its manifest and its text resources, see
  [Studio API](authoring.md). A folder that does not load gives its `name` alone.

## Workspace

- **The application takes the sidebar.** An open application draws its navigation in the shell's sidebar, in place
  of a site's navigation, see [layout](shell.md#layout). A page that claims the sidebar is a workspace: `spacing-md`
  round its content, a compact band and 32 px sidebar entries.
- **The application heads the sidebar.** Its row is 48 px, as the band beside it, so their rules line up. It is a menu
  that lists every application, marks the open one and ends in a caret. All applications, shown when there are
  several, leads back to the list, and New application… follows the choices.
- **Whole-application entries come first.** Overview, Releases and Settings, then the resource kinds in groups by
  what an author does with them: Data (entity, seed, sequence, data source), Pages (site, page, form), Logic (process,
  rule), Access (role, policy, identity provider), Integration (connector, endpoint), Texts and Tests (scenario). A kind
  outside these groups shows under Other. The Access group ends in the policy matrix. Settings opens the
  application's manifest.
- **Only kinds with resources.** The sidebar leaves out a kind the application has no resource of. The Overview
  introduces those kinds, see [Overview](#overview). No entry shows a count.
- **Open and collapsed.** An open entry is its label alone and a collapsed one its icon alone, with the label as its
  tooltip. Collapsed, a group's title gives way to a thin rule.
- **A resource belongs to its kind.** A resource open in the editor marks its kind's entry, and the address keeps the
  view, as in `/studio?app=PurchaseRequests&resource=<id>`.
- **New resource… starts with the list's kind.** On a kind's list it opens the dialog with that kind chosen.

## Band

- **One row.** Every Studio page has one compact band, 48 px high: the kind as a link to its list, then the title of
  the open resource or view as the page's heading, then the actions. The band names no Studio or application step,
  because the sidebar already shows both.
- **Publish… is the one solid button.** Validate, Preview and Run tests are text buttons with no icon, see
  [buttons](actions-and-controls.md#buttons). Publish… ends in an ellipsis, as it asks for more.
- **It stays in place.** The band stays under the shell's header while the page scrolls, see
  [layout](shell.md#layout).

## Overview

The Overview is the first view of an open application. It says what to do next rather than what the application
holds, and shows no counts. Under the application's description it shows, in order:

- **Get started**, for an application with no entity: four steps, each a link to where it is done. Describe your data
  with an entity, show it on a page, put the page on the site, then try it and publish.
- **Needs attention**: the folder's errors first, with the line that they stop Publish, then its warnings. Each opens
  the resource at the place it names. With none, one line says that nothing needs attention.
- **Live release**: the release's number, who published it and when, and a link to every release. One line says
  when the folder has changes the live release does not have yet, and nothing is said when it has them all. An
  application never published says that Publish makes it live.
- **More you can add**: each kind the application has no resource of, in the sidebar's groups, as a link to its list
  with one line on what it is for.
