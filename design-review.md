# Design review 2026-11-03-studio-settings: the owner's rules

Topic: Studio's Settings page and the Off-standard tag on Releases.

The prototype starts from the tag `design-review/2026-11-01-studio-editors` (b046699d), so the owner keeps seeing D128
and D129, with `main` (b2c03324) merged in for module contracts and off-standard releases. Its own diff starts at the
merge commit e7e2a2c5. Each entry is one agreed change: what the owner asked, in English, and what was built, for the
docs change at the end of the review.

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
- **Three dialog sizes, by what the dialog holds:** small, about 400 px, for a confirmation or one question; medium,
  about 560 px, for a short form of two to six fields; large, about 800 px, for content that needs the width, such as a
  list to choose from or an import's preview. No larger size on a desktop: what needs more is a page. Below 992 px every
  dialog takes the width less the page margin. A dialog is as tall as its content, at most the window less its
  margins; its body scrolls while its title and buttons stay. The size is a token that a dialog names, never a width of
  its own.
