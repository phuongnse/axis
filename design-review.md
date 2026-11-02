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
  "Live · Release N" in textMuted, because an empty band cannot tell "all live" from "still loading" or "failed to
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
  320 px. Every row is 48 px high whatever it holds, icon buttons included: a row of one line is centred in it, and a
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
  Studio's application row and a table's row, and `smallControl` 24 px for a small icon button and the line of text
  beside it; the review's own sizes use them. For the issue: the sizes already written as numbers elsewhere, such as
  48 px in the shell and 12 px in forms and menus, move to tokens too.
- **The Overview.** The owner asked how the Overview reads, then which of its problems list and the problems panel to
  keep. Agreed: the panel stays, as it opens from every page and stays open while each problem is fixed; the Overview
  lists no problems, as the band already says how many. The Overview answers what differs from the live release and
  where to go next, in a block called Changes since the live release, as a publish would change just that: under its
  title each file
  that differs from it, its resource's label as the link that opens it, its kind and whether it was added, changed or
  removed, or one line that everything is live; then one line "Live at:" with every live site, as an application can have
  several, leaving out a site that is not live yet; then a link to every release. A site that a tenant serves at a host
  of its own opens at that host, so the API has to give Studio each site's address; the prototype shows this host and
  the site's path. Get started stays
  for an application with no entity. More you can add stays in full, as the owner found it useful: by the sidebar's
  groups, each kind the application has none of as a link to its list, what it is for, and in textMuted when one is
  needed, leaving out migrations, which the entity designer writes with the entity (D54). For the docs change: a
  Proposed `changes` list on `GET .../releases`, the files that differ from the live release, compared as the
  canonical JSON a release stores.
- **The Overview says nothing twice.** The owner found that the block's line naming the live release, its number and
  who published it and when, repeated the band. Recommended and agreed: the block holds no line naming the live
  release, nor the warning that changes are not live, as the band says both beside Publish…; who published the live
  release and when is on the Releases list, which See all releases opens.
- **A link that leaves Studio.** The owner could not tell whether a site on the Overview opens its resource in Studio
  or the running site. Recommended and agreed: the line reads "Live at:" and each site is its address, host and path,
  as the link's text, so it reads as a place on the web and can be copied for the application's people; the site's
  resource stays in the sidebar. It opens in a new tab, which its tooltip ("Purchasing, opens in a new tab") and its
  accessible name, which starts with the address, say. No icon, so D128's "text or an icon, never both" stands.
