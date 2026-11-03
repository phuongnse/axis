# D129. Rules from the Studio editors review

Status: **Agreed**

The owner reviewed Studio's band, Overview, Releases list and the platform's tables in the running prototype, one
change at a time, and agreed these rules between 2026-11-01 and 2026-11-03. They change some rules of
[D128](d128-rules-from-the-studio-review.md) (the application menu, the band and the Overview),
[D96](d96-rules-from-the-design-system-review.md) (the table's row height and where its toolbar holds) and
[D59](d59-table-row-and-toolbar-actions.md) (bulk actions, as a Proposed rule). None of it is built yet _(planned for
M31)_, and it builds on D128's issue. The prototype is the tag `design-review/2026-11-01-studio-editors`, whose own
changes start at the tag `design-review/2026-10-30-studio`.

## The workspace

- **The application menu holds the applications.** It lists them and New application…, and drops D128's All
  applications, as Studio in the header's app switcher already leads back to the list. See
  [workspace](../reference/studio-workspace.md#workspace).
- **The band holds the application's state and actions alone.** No Studio page has a breadcrumb, and the band no
  longer names the kind and the title, as D128 had it: the sidebar leads back to a kind's list. See
  [band](../reference/studio-workspace.md#band).
- **The band's state is words.** Errors and warnings are one text button, "2 errors, 1 warning", that opens a
  Problems panel beside the page; the other states, "Changes not live", "Not published yet", "Release 7 is live" and
  "Releases did not load", are text in `textMuted`, never links, with no icons. A quiet band still says that the
  release is live, so it never reads as loading. See [band](../reference/studio-workspace.md#band).
- **Every page has one heading.** A level-4 heading heads the content of every Studio page, made the same way on every
  page, with an optional line in `textMuted` under it and the page's own actions on its right. See
  [page heading](../reference/studio-workspace.md#page-heading).
- **The sidebar's application row stays.** It stays in place with the band, only the sidebar's entries scroll, and
  beside a sidebar the footer sits under the content column only. See
  [workspace](../reference/studio-workspace.md#workspace) and [layout](../reference/shell.md#layout).
- **Studio names what it shows.** A resource with a label is named by it in the author's language, a text resource by
  its language, and a resource only authors see by the name its author gave. Every kind has a name and a sidebar
  group, and who activated a release is said in words. See
  [names](../reference/studio-workspace.md#names).
- **New resource… is on the band, New entity… on a kind's list.** The band's button asks for the kind by its name,
  in the sidebar's groups, then for a name. A kind's list has its own button, named for the kind, which asks for the
  name alone. Every kind an author may create is offered wherever kinds are offered, migrations too. See
  [new resource](../reference/studio-workspace.md#new-resource).

## The Overview

- **Live, then Changes since the live release, then More you can add.** One column. The Live block shows each live
  site as the home page's tile, which opens the running site in a new tab. Changes since the live release lists
  each file that differs from the live release in three aligned columns, its resource, its kind in the singular and
  what changed; a removed file is named from the live release. More you can add says in natural words when each kind
  is needed, and has no block once the application has every kind. The Overview lists no problems and repeats
  nothing the band says. See [Overview](../reference/studio-workspace.md#overview).
- **`GET .../releases` gives the changes.** A `changes` list names the files that differ from the live release, with
  a removed file's kind, name and label from the live release. The owner agreed it with the rest on 2026-11-03, as
  the Overview's changes need it. See
  [release history](../reference/authoring.md#release-history).
- **Seeing a change waits for its diff.** A row of the changes opens its resource as it is now. The diff view that
  [D119](d119-ai-authoring-in-studio.md) and [D125](d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md)
  agree is left to that plan; whether the Overview links to it is decided when it is built.

## Releases

- **One row per release.** The highest number first: Release, with Live beside the one that runs, then Label,
  Published, Published by and Note, and Activate… as an icon on every other row. No Status column, as a release that
  is not live has no state of its own to name. Studio's button says Activate…, either way;
  [D130](d130-activate-a-release-replaces-roll-back.md) proposes the same word for the concept and the route. See
  [releases](../reference/studio-workspace.md#releases).

## Words, values and tables

- **No middle dot as a separator.** Peers join with a comma, a state is a sentence, and facts of different kinds take
  a column or a line of their own. See [text](../reference/actions-and-controls.md#text).
- **No technical name in the UI.** What the UI shows is a text, never an internal code or a file path. See
  [text](../reference/actions-and-controls.md#text).
- **A cell with no value shows the dash.** Every table cell with no value shows the platform's em dash, as the
  `table` widget does, never nothing and never N/A. See [empty values](../reference/frontend.md).
- **Every design value is a token.** No size, space, type, radius or colour is a number of its own in a component.
  The size tokens are `bar`, 48 px, `smallControl`, 24 px, and `tableRow`, 40 px. See
  [design tokens](../reference/frontend.md).
- **One table for the platform.** Every table, the `table` widget, Studio's, the console's and the users page's, is
  drawn by one shared table on Ant Design's, and a lint rule refuses Ant Design's table anywhere else. Its toolbar is
  D96's, now for every table; a column of a fixed form fits its content and the columns of free text share the width
  left in equal parts; every row and the header are 40 px, where D96 had 48 px; row actions are icons for what the
  platform knows and text for what an author configured, folded into one menu from three on or below 992 px. See
  [tables](../reference/actions-and-controls.md#tables).
- **Selecting rows is for bulk actions** _(Proposed)_. A table shows checkboxes only when it has a bulk action, and a
  selection shows a bar that holds them. An action on one row stays on the row. See
  [tables](../reference/actions-and-controls.md#tables).

_Why:_ each rule came from something the owner saw in the prototype: a breadcrumb that led nowhere new, a state that
repeated itself on one page, a column that left empty width, tables that each went their own way. One place for each
fact, words people read and one shared table keep Studio and the platform consistent and keep a rule changed once
changed everywhere.
