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
  application's first when it lacks that one; Translations…, a globe icon beside it, opens every language of the label
  in a medium dialog (Done keeps, Cancel puts back); a muted line under the box names the languages it lacks, "Missing
  in Vietnamese". Many labels at once are translated in the text resources' table. The key is Studio's own, made from
  the resource and the path, and never shown. The texts are part of the resource's one Save: no Write texts button.
- **One save bar.** While there are unsaved changes, a bar stays at the foot of the window across the content:
  "Unsaved changes" on the left, Discard and Save on the right. With nothing to save it is not drawn. Save writes the
  label texts first, then the resource.
- **A list of objects is a table.** One column per property, named by its field text; a value of a known kind by its
  label (a module or package by the label of its manifest), a nested list or map by its count. Add… (such as Add
  module…) is the table's main action; each row has Edit… and Remove as icons. Edit… and Add… open the item in a
  dialog, medium for plain values and large for an item holding a list or a map; Done keeps it, Cancel puts it back,
  or removes a new one. Inside the dialog a nested list is rows of inputs with Add row, as a dialog never opens another.
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

