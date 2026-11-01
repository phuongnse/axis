# Design review 2026-11-01-studio-editors: the owner's rules

The prototype starts from the tag `design-review/2026-10-30-studio` (6f8913ac), so its own diff starts there. Each
entry is one agreed change: what the owner asked, in English, and what was built, for the docs change at the end of
the review.

- **Application menu.** The owner asked to drop All applications: Studio in the header's app switcher already leads
  back to the list. Built: the menu holds the applications and New application… only. Amends D128's "The application
  menu leads back to every application".
- **Band on a list or a view.** The band's title repeated exactly what the sidebar shows, so the owner asked whether
  it was needed and what would be more useful. Chosen (recommended): the application's state, errors, warnings and
  changes not live, with the title kept as a visually hidden heading.
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
- **Overview band.** The Overview's band was empty while the description sat alone below it, so the owner asked to
  move the description into the band, on one line ending in an ellipsis. Built, with the whole text in a tooltip.
