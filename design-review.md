# Design review 2026-11-04-entities: the owner's rules

This is an open design review. Work here follows the `axis-design` skill: one change at a time, only what the design needs,
no test changes on this branch, and logic left to the review's issue.

Topic: Studio's Entities page.

The prototype starts from the tag `design-review/2026-11-03-studio-settings`, so the owner keeps seeing the workspace and
the resource form that `main` has not built yet, with `main` (624ab6e1) merged in. Its own diff starts at that merge.
Each entry is one agreed change: what the owner asked, in English, and what was built, for the docs change at the end
of the review.

In a merge of `main`, a conflict keeps the prototype. `main` is still the standard for behaviour, so its logic is never
changed to suit the prototype, and any of it that keeping the prototype loses is brought into the prototype's files.

## Agreed changes

### One list for every kind

- **Asked:** a kind's list is a real table and the standard for every kind, not one kind's patch. Its columns are Name,
  Description, Status, Last modified by and Last modified at. Every column always shows, and a cell with no value
  shows the dash. A list does not show the inside of a resource, such as its fields or references, a file name, or a count
  that leads nowhere. A kind has no columns of its own.
- **Built:** `StudioResourceTable` on `AxisTable` replaces `StudioResourceRows`, sorted by name, with Name a real link. A
  module's resource shows its name without the `<module>/` prefix and has a status, as a new module version or a layer
  changes it. The
  list has no Module column: a module's resource has a neutral tag with the module's label beside its name.
- **Module resources:** the activity route also answers each one's label and description by locale, from the module's own
  texts, and the built-in and sample modules' entities, contracts, processes, policies and pages have descriptions. A kind
  with no label in its format, such as a policy, keeps its name.
- **Description:** every kind but `text` and `layer` may have an optional `description`, a text reference on `Resource`,
  which the compiler checks like a label (`AXC0028`). The schema form shows it, and each editor of its own shows it after
  the name and label. `GET .../resources` answers `descriptions` by locale, and the search also matches it. The sample
  entities have one, under the key `<entity>.resourceDescription`.
- **Status:** `GET .../resource-activity` answers each resource's `status` (`new`, `changed` or `published`). It is a route
  of its own so the resource list stays free of the database. While it loads the cell says so, and when it fails it says
  Unavailable.
- **Last modified by and at:** the columns are in the design and show the dash. Nothing records them: that is logic, for an
  issue of its own. It is to store who wrote a resource through Studio and when, with the version the write left, so a file
  changed in git shows the dash rather than the wrong person. A module's layer write is to count too.
- **To change in the docs:** D39 (the list shows `usedBy`, which it no longer does; the API still answers it),
  "Reads and diagnostics touch no database" in the authoring API, the resource file formats, and D132's rule on sorting if a paged list is to have a sort by column.

### Lost in merges

`main` stays the standard for behaviour, so what keeping the prototype lost is back in the prototype's files:

- `StudioApplicationTexts.Read` names the applications of the list again.
- Studio no longer opens the only application by itself, which `main` removed in 88f74fb2.
- `shell.users.invite.send`, the name of the invite dialog's button.
- A site's pages keep the checkbox and radio alignment and error border, in `PageTheme`.
- Initials are of the first and last words, not the first two.
- The entity migration dialogs use `AxisButton` again, so Drop data is in `dangerText`.
- No inline `theme` on the workspace menu and no literal font in the JSON editor's find bar.

### Found by the previews

- The Studio card crashed: its fixtures lacked what `main` added, the names' `contracts`, the releases' `relaxed` and the
  manifests. They have them now, and the Entities card's data comes from the local stack.
- A label field in an application with no text resource crashed. It now says a text resource is needed.
- A status tag's words take the text tokens, as Ant Design's own fall to 3.37:1 in light mode. The Live tag of a release and
  other `Tag color` uses have the same problem, left as they are for the issue.
- At 992 px the table is wider than its frame, so Status and the last modified columns need a sideways scroll, as the table
  rules say. The owner is to say whether that holds.

### For the issue

The web tests are `main`'s and about 100 of them fail on this prototype, as a prototype changes widgets without their
tests. The issue brings them to the agreed design. Nothing in this review touches a test.
