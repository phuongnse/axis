# Design review 2026-11-01-studio-editors: the owner's rules

The prototype starts from the tag `design-review/2026-10-30-studio` (6f8913ac), so its own diff starts there. Each
entry is one agreed change: what the owner asked, in English, and what was built, for the docs change at the end of
the review.

- **Application menu.** The owner asked to drop All applications: Studio in the header's app switcher already leads
  back to the list. Built: the menu holds the applications and New application… only. Amends D128's "The application
  menu leads back to every application".
- **Band on a resource.** The owner found "kind › title" useless: the sidebar already leads back to the kind's list,
  and the last step is the page itself and cannot be clicked. Built: no breadcrumb on any Studio page; the band holds
  the application's state and actions alone. Amends D128's "The band names the kind and the title".
- **Not published yet.** The owner agreed to show it when the application has no live release.
- **A quiet band.** The owner asked whether an empty band is fine when nothing needs attention. Recommended and chosen:
  "Release N is live" in textMuted, because an empty band cannot tell "all live" from "still loading" or "failed to
  load"; "Releases did not load" when the history fails.
- **Status words and the problems panel.** The owner asked whether the statuses are links and why each has an icon
  when its words already say it, then noted that a popover is for extra information while a list of problems is work
  to do. Agreed: no icons; errors and warnings are one text button that opens a Problems panel on the right, under the
  header and the band and not modal; opening a problem opens its resource while the panel stays open; the other states
  are textMuted text, not links.
- **Page heading.** The owner's rule: every page has one visible heading, made the same way on every page, never on
  some pages and not others. Agreed: every Studio page heads its content with a level-4 heading with no margin of its
  own, so it sits a page padding below the band and above the content, as the rest of the platform's pages head
  theirs. Its text is what the page is about: the Overview, the application's own page, says the application's title;
  another sidebar entry says its name, such as Settings, Entities, Releases or Policy matrix; an open resource says its
  label; Studio's list of applications says Studio. An optional line in textMuted, at the theme's one font size, under it says more: the
  Overview's application description, or an open resource's file and the path in it, or its module. The
  page's own actions, such as a scenario's Run scenario, sit on the right of the heading. The band holds no heading on
  any page, only the application's state and its actions. A page with nothing for the band has none: Studio's list of
  applications loses its band and its breadcrumb, as the header's app switcher leads home, and its New application…
  sits on the right of its heading.
- **The sidebar's application row.** The owner saw the row scroll away while the band beside it stayed, and asked for
  one behaviour. Recommended and built: both stay in place, and only the sidebar's entries scroll. Beside a sidebar the
  footer sits under the content column only, so the sidebar runs to the bottom of the window, is never pushed up by the
  footer and leaves no gap above it; the owner chose this over a sidebar that shrinks as the footer comes into view.
- **No technical name in the UI.** The owner's rule: what the UI shows is a text, never an internal code, and the
  prototype must show it right so the review is not distracted by it. Discussed: giving every resource kind a label was
  too wide, as only some resources are seen by the application's people, and it ran into D30's frozen migrations.
  Built, in the narrower scope the owner chose for now:
  - Studio names a resource that has a label by it, in the author's language: an entity, a role, an identity provider
    by its `label`, a page or a site by its `title`. A text resource is named by its language. A resource that only
    authors see has no label and is named by the name its author gave.
  - migrations, events, triggers and webhooks have a kind name and a group in the sidebar, so Other is empty.
  - who activated a release is said in words: the person, "System" for the server's activation at startup, or the
    platform operator, and "someone no longer known" for an id with no name left, in Studio and the console.
  - Open, to discuss after the owner's review: how authors' own resources are named.
- **Releases, one row per release.** The owner saw release 3 twice and Roll back on release 4, newer than the live one,
  and asked whether it was a bug. It was not: each row was one activation, as the release history says. The owner's
  rule: a publish takes a label and a note, and activating goes to the release one wants, nothing more. Agreed: one row
  per release, the highest number first, with its label and note, who published it and when, Live on the one that runs
  as the band says, and Activate… on the others. The Overview's live release reads the same label, and when
  and by whom it was published.
- **Activate, not roll back.** The owner asked that the docs, the API and the UI use one word. Recommended and agreed:
  the operation is "activate a release", either way, back to an older release or forward to a newer one, so the
  concept, the route and the UI say activate, and Studio's button is Activate…. For the docs change: a Proposed
  decision that renames the rollback concept and route.
- **Table columns.** The owner's rule, for every table, the `table` widget included: a column whose values have a fixed
  form, such as a number, a date, a date and time, a status tag or the row's icon actions, is as wide as its content and
  never wraps, with spacing-lg on each side so fitted columns do not crowd; free text takes the rest and wraps at
  320 px. Every row is one height whatever it holds, 40 px since "The table's rows" below, icon buttons included: a row of one line is centred in it, and a
  row of more lines grows and reads from the same first line. A table narrower than its columns' content scrolls
  sideways rather than squeezing them, as the console's tables already do: Studio is not used on very small screens.
  Built on the Releases list.
- **Icon actions.** The owner recalled that row actions are icons with a tooltip, and D96 has it: icon-only buttons for
  actions repeated on every row, each with a tooltip and an accessible name. Recommended and agreed: the platform gives
  an icon to an action whose meaning it knows, such as delete, open or activate a release, so every Studio and console
  table uses icons; an action an author configures keeps its text, as configuration never names an icon. D96 stands.
- **Tooltips after an action.** The owner found that an icon button's tooltip stayed after a click, and came back
  when a dialog closed. Built in the shared icon button: a click closes the tooltip until the pointer comes back, and
  focus that a click or a closing dialog gives the button opens none; keyboard focus still shows it, as D96 asks.
- **Every design value is a token.** The owner's rule: no size, space, type, radius or colour is a number of its own in
  a component, or it cannot be controlled. A layout technique, such as a column's 1 % width, needs no token but takes
  its size from one. Built: `sizeTokens` in `designTokens.ts`, `bar` 48 px for the shell's header, a page's band,
  Studio's application row, and `smallControl` 24 px for a small icon button and the line of text
  beside it; the review's own sizes use them. For the issue: the sizes already written as numbers elsewhere, such as
  48 px in the shell and 12 px in forms and menus, move to tokens too.
- **The Overview.** The owner asked how the Overview reads, then which of its problems list and the problems panel to
  keep. Agreed: the panel stays, as it opens from every page and stays open while each problem is fixed; the Overview
  lists no problems, as the band already says how many. The Overview answers what differs from the live release and
  where to go next, in a block called Changes since the live release, as a publish would change just that: under its
  title each file
  that differs from it, its resource's label as the link that opens it, its kind and whether it was added, changed or
  removed, or one line that everything is live; then a link to every release. Its live sites are in a block of their own,
  see "A block for what is live" below. Get started stays
  for an application with no entity. More you can add stays in full, as the owner found it useful: by the sidebar's
  groups, each kind the application has none of as a link to its list, what it is for, and in textMuted when one is
  needed, migrations too (see "One list of kinds" below). For the docs change: a
  Proposed `changes` list on `GET .../releases`, the files that differ from the live release, compared as the
  canonical JSON a release stores.
- **The Overview says nothing twice.** The owner found that the block's line naming the live release, its number and
  who published it and when, repeated the band. Recommended and agreed: the block holds no line naming the live
  release, nor the warning that changes are not live, as the band says both beside Publish…; who published the live
  release and when is on the Releases list, which See all releases opens.
- **A block for what is live.** The owner could not tell whether a site on the Overview opens its resource in Studio
  or the running site. A line of addresses was tried first; the owner then asked for a block of its own, Live, showing
  the sites as the home page does. Built: a Live block above Changes since the live release, once the application has
  a live release, with each live site as the home page's tile, made one shared `SiteTile`: its mark, its title as the
  link and its description in textMuted, in the home page's grid and order. The sites are the sites list's, which
  holds only what is live, so a site added since the live release is not there and the titles are the live release's;
  "The live release has no site yet." when it has none. Each tile opens its site in a new tab, so Studio stays open,
  and its accessible name says so ("Purchasing, opens in a new tab"); no tooltip and no icon, so D128's "text or an
  icon, never both" stands. A site that a tenant serves at a host of its own opens at that host, as the home page's
  tile does.
- **Changes, named as elsewhere.** The owner agreed to two fixes the review found. A change's kind is said in the
  singular, Entity or Role, where the sidebar's list says Entities or Roles: `shell.studio.kindOne.*` texts. A removed
  file, which the folder no longer holds, is named from the live release, its label, else the name its author gave, and
  its kind, as text and not a link, as nothing is left to open; never its path. For the docs change: the Proposed
  `changes` list on `GET .../releases` gives a removed file its `kind`, `name`, `titles` and a text resource's
  `locale` from the live release, and `null` on an added or changed file, which Studio names from the folder.
- **More you can add, in natural words.** The owner found that every "when" line began "Add one when…". Agreed: each
  line says in its own words when the kind is needed, such as "Handy for lists that must be ready on the first day"
  or "For a process that starts on its own, on a schedule or when a record changes", in English and Vietnamese.
- **Tried and dropped: a side column.** To fill the right half, a side column was tried beside the main one, with
  the Live block, how the last full test run went and the modules the application is built with. The owner did not
  find it right and dropped it: the Overview stays one column, the Live block first, then Changes since the live
  release, then More you can add. Its routes, `GET .../tests/latest` and `GET .../dependencies`, and its `aside` and
  `tile` size tokens went with it.
- **No middle dot as a separator.** The owner asked whether the "·" Studio uses everywhere is a convention, and what
  else there is. It is a common way to run short metadata into one line, not a rule, and it glues facts of different
  kinds together with no order, and a screen reader may read it out. Agreed: peers join as words do, with a comma,
  such as "2 errors, 1 warning"; a state is a sentence, "Release 7 is live"; facts of different kinds take a column or
  a line of their own. Built on the band, on Changes since the live release, whose rows are three aligned columns,
  the resource, free text that wraps at 320 px, then its kind and what changed, each as wide as its content with
  spacing-lg between, and on More you can add, where each kind is the link on its own line, what it is for under it
  and when one is needed in textMuted. For the issue: the other dots in Studio, in the form, page and process
  designers, the policy matrix, the references, the problems panel and the Releases list, follow the same rule.
- **More you can add, once everything is there.** The owner asked what the block shows when the application has
  every kind. Recommended and agreed: nothing, no block, as it is help for getting started and its work is done; no
  line that says so, no "add another" links, which New resource… on every list already gives, and no other block to
  fill the space. The Overview is then Live and Changes since the live release alone.
- **Seeing what changed: not in this review.** The owner asked whether a change can be seen, not only listed. It
  cannot yet: a row opens the resource as it is now. D119 and D125 already agree a diff view, planned for M35, which
  the publish dialog shows from the live release. The owner chose to leave it to that plan and not to design a second
  one here; whether the Overview links to it is for when it is built.
- **Where a resource is created.** The owner asked for one place to create a resource of any kind, and for a kind's
  own page to create that kind without asking it. The sidebar's application row was recommended first; the owner
  found it already tight and asked for the band, near Publish…. Agreed, two buttons:
  - The band's New resource…, on every page, heads the band's actions as a text button, the same gap apart as
    Validate, Preview, Run tests and Publish…, and never beside Publish…. A wider gap to set it apart was tried and
    looked stray to the owner; moving it to the band's left was not taken, as the left holds the state. Its dialog asks
    for a kind, by its name for one resource in the sidebar's groups and order, Other last, then for a name.
  - A kind's list has its own button in its toolbar, named for the kind, such as New entity…, whose dialog, New
    entity, asks for the name alone, or the locale for a text resource.
  One button that changed its words and its dialog with the page was not taken: the band's button always does the
  same thing.
- **One list of kinds.** The owner's rule: a kind an author may create shows everywhere kinds are offered, never in
  one place and not another. D54 lets an author write a migration by hand, though the entity designer usually writes
  it, so More you can add offers Migrations as New resource… does, with "Seldom by hand: the entity designer writes
  one whenever a change it saves needs it."
- **The Releases list's columns.** The owner asked whether Published should be two columns, whether the optional label
  and note belong at the front, what an empty cell shows, and why Status only ever says Live. Recommended and agreed:
  - Release, then Label, Published, Published by and Note, then the row's Activate… icon. When and by whom are two
    columns, as facts of different kinds are; the date and time is as wide as its content.
  - The label stays second: when given it is the name people know a release by, as a release's title is. The note,
    the longest text and the most often missing, comes last and takes the width left.
  - No Status column: a release that is not live has no state of its own to name, so Live is a tag beside the number
    of the one that runs, as the band says.
  - A cell with no value shows the platform's `EmptyDash`, the em dash every table cell already uses (a form or a
    detail says "Not set"): never nothing, which cannot tell "not given" from "not loaded" or "failed", and never N/A,
    which says "does not apply" and does not translate. The Releases list drew nothing before.
- **One table for the whole platform.** The owner asked whether row actions or select-then-act is right, and whether
  every table should have the same search, filter, Refresh, Export and Import. The owner's rule: every table is built
  on one shared table, as tables that each go their own way cannot be kept right. Agreed:
  - An action on one row stays on the row, as D59 and D96 have it. Selecting rows is for bulk actions alone: a table
    shows checkboxes only when it has one, and a selection shows an "N selected" bar that holds them. For the docs
    change: a Proposed rule, as D59 left bulk actions for after M12.
  - D96's toolbar holds for every table of the platform, Studio's and the console's too: the view controls, search and
    filters, on the left; Refresh, the secondary actions such as Export and Import, and the main action last, on the
    right. Their places and looks are fixed; whether a table has each depends on what it needs, as a list of a few
    dozen releases needs no search.
  - For the issue: Studio's and the console's tables, the Releases list among them, are built on the same shared
    table as the `table` widget, which draws the toolbar, the row actions and their fold into a menu below 992 px, the
    paging and a cell with no value. The Releases list today lacks the fold and the paging because it is drawn on its
    own.
- **One column takes the width left.** The owner saw Published by far wider than "System", as the three columns of
  free text shared the width left over. Agreed: a table has one column that takes the width the others leave, the
  longest text and the one most worth reading, the note on the Releases list; every other column of free text is as
  wide as its content up to 320 px, where it wraps, with its title on one line and spacing-lg on each side, as a
  fitted column has. Built in `tableColumns.ts` as `freeText`, `fillText` and `textCell`, on the Releases list; the
  `table` widget does not follow these column rules yet, which the shared table of the issue brings to every table.
- **The shared table, built.** The owner wanted to see the shared table before agreeing to it, and found the label
  column too narrow when it holds no value. Built: `AxisTable` in `web/src/platform/`, which the `table` widget and
  the Releases list now draw on, and which replaces `tableColumns.ts`. It draws D96's toolbar (view controls on the
  left; Refresh, the secondary actions and the main action last on the right), the columns by their kind, 48 px rows,
  the row actions as icons for an action the platform knows and text for a configured one, folded into one "…" menu
  from three on or below 992 px, the paging, and sideways scrolling. A column's kind decides its width:
  - `fit`, a value of a fixed form, as wide as its content;
  - `text`, free text, as wide as its content but no narrower than the new `textColumn` size token, 160 px, and no
    wider than 320 px, where it wraps;
  - `fill`, the one column that takes the width the others leave. The Releases list fills its note; a `table` widget
    fills its identifying column, the first, when it is free text, as the one most read; a table that marks none
    fills its last free text column.
  For the issue: Studio's resource lists, the console's tables and the users page move onto `AxisTable` too. Open:
  whether an author may say which column of a `table` widget fills, which is configuration.
- **The table's rows, and only the shared table.** The owner found the table loose and asked for it tighter, by
  tokens alone, meaning the height of its rows, the header's too. Built: every row and the header are the new
  `tableRow` size token high, 40 px, spacing-xs above and below a `smallControl` line, where 48 px, the `bar` size,
  was before; a table's row no longer lines up with the band. The gaps between columns stay spacing-lg on each side.
  The owner agreed that nothing draws Ant Design's `Table` but `AxisTable`. For the issue: a lint rule,
  `no-restricted-imports` in oxlint, refuses `Table` from `antd` anywhere but `AxisTable.tsx`, so no table goes its
  own way again.
