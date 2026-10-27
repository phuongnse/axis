---
name: axis-design
description: Runbook for the owner's local agent session that keeps the Axis Design System artifact in Claude and the repository in step. Use when the owner asks for a design change in words, reviews the design one change at a time, or asks to sync or publish the design system, or after a merged change to the tokens or a widget. Not for agents running inside the NexKit pipeline.
---

# Keeping the Axis design system in step

The rules are in
[Design changes](../../../docs/delivery.md#design-changes) and
[D57](../../../docs/decisions/d57-a-design-system-published-from-the-repository.md).
This runbook says how to apply them. It does not repeat them, so follow its
links.

- **The artifact's link** is in Design changes. Look it up there each time. The
  artifact is private to the owner.
- **The artifact's file layout and how to publish** are in the Design System
  type's own instructions in Claude. Read them each time and follow them. They
  are not described here.
- **Token names and the widget list** are in the
  [frontend reference](../../../docs/reference/frontend.md) under "Design
  tokens and previews". The preview names are in
  [Widget previews](../../../docs/development.md#widget-previews).

Never ask NexKit's agents to read or write the artifact. Only this session
acts on it.

## A requested change

Use this when the owner asks for a change in words, for example "give a
section more space".

1. Restate the request in Axis terms. Name each token, such as `primary` or
   `spacing-md`, and each widget, such as `table`, `form` or `section`. Ask the
   owner when the request is ambiguous.
2. Open the artifact from the link in Design changes and make the change. Show
   the owner the result.
3. When the owner accepts it, write the NexKit issue as
   [Writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) says. The
   issue:
   - links the artifact
   - names the tokens and widgets it changes
   - links the "Design tokens and previews" section of the frontend reference
   - has 2–5 criteria that a test or command verifies, for example the new
     token value in `web/src/platform/designTokens.ts`
4. Add a change note to the artifact that names the issue by its number and
   link. The note is the last one, so a later sync can find the issue.

The artifact now differs from the repository by this change alone, until the
issue is merged.

## A design review

Use this when the owner reviews the design in words, one change after
another, for example by going through every card of the artifact. The rules
are under "Several changes are a design review" in Design changes.

### Start

1. Sync the artifact to `main` first, as [A sync](#a-sync) says, so every
   difference the owner sees comes from the review.
2. Agree a short topic with the owner, such as `field-look`. Create the
   prototype branch from `main`: `git switch -c design/<topic> main`. Work
   only on that branch, never only in a temporary folder. Never open a pull
   request from it, and never give it a NexKit branch name.

### Each change

Take one change at a time, and do not start the next until the owner has
seen this one.

1. Restate the change in Axis terms: the tokens, the widgets and the
   previews it touches, by their names in
   [Widget previews](../../../docs/development.md#widget-previews). Ask the
   owner when the request is ambiguous.
2. Make it in the real code under `web/src/platform/` and
   `web/src/designSystem/`, so the artifact shows the real widgets. Mark each
   new behaviour with a short `Proposed:` comment, so the issue that builds
   it can find every one.
3. When the change adds a state that changes how a widget looks, add it to
   the widget's preview in the same step.
4. Run `scripts/design-system.sh`, which writes
   `artifacts/design-system/axis-design-system.js` and checks it.
5. Check every changed card as the artifact shows it (see below).
6. Publish the bundle to the artifact, as the Design System type's
   instructions say. Add a change note of at most 280 characters, the
   artifact type's limit, that names the review and its branch, such as
   `Design review field-look (design/field-look): required mark and help
   text`.
7. Commit the change on the branch with a message that says what it shows,
   so the branch history lists the review's changes in order. The branch
   stays local: only its tag is pushed, at the end.
8. Show the owner the card and ask for a verdict. Keep, change or drop the
   change as they say. Write down each rule they agree, in their words, for
   the docs change at the end.

### Checking a card

A check passes only when the card behaves as it will in the artifact.

- Draw it in a frame sandboxed without `allow-forms`, about 700 px wide, with
  the card laid out at 992 px and scaled to fit, as the artifact does. A
  form's own submission is blocked there, so check that its buttons still
  work.
- Load IBM Plex Sans and put `<meta charset="utf-8">` in the page, as the
  artifact does.
- Check it in light and in dark mode, with no console error in either.
- Measure the contrast: text at 4.5:1 or more and controls and borders at 3:1
  or more, on `surface` and `canvas`, in both modes.
- Measure the card's content height at 992 px and set its marker's height to
  it, so nothing is cut off and no empty band is left.

### End

1. When the owner has agreed every change, write one docs change on its own
   branch from `main`: the rules in the reference docs that hold their
   detail, and a decision when a rule changes configuration, an API or an
   earlier decision. Follow
   [keeping the docs current](../../../docs/delivery.md#keeping-the-docs-current),
   and mark what is not built yet. Open it as a hand-made pull request for
   the owner to merge.
2. Before the issue, tag the prototype's last commit and push only the tag,
   so NexKit's agents can read it and no link to it ever breaks:
   `git tag -a design-review/<date>-<topic> -m "<what it shows>"` and
   `git push origin design-review/<date>-<topic>`. CI and CodeQL start only on
   branch pushes, so the tag starts no run; check with `gh run list`. Never
   push the branch: the prototype does not update the tests, so CI would only
   fail on it. The issue links the tag's comparison with the commit the branch
   started from, `compare/<base>...design-review/<date>-<topic>`, where
   `<base>` is `git merge-base design/<topic> main`, so the diff stays the
   same as `main` moves on. The tag is never deleted. Delete the local branch
   once it is tagged.
3. Write one NexKit issue that builds the whole review, as
   [Writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) says. It
   links the docs sections, the artifact and the prototype tag, and names
   the tokens, widgets and previews it changes. Open it once the docs pull
   request is merged, so its links point at `main`. Do not split it: NexKit
   splits it if it needs to.
4. Add a change note to the artifact that names the issue, as for a requested
   change.
5. After the issue is merged, sync the artifact again. The sync keeps the
   card groups and their order, which `scripts/design-system.sh` writes as
   the bundle's first line, `/* @ds-bundle: {...} */`.

## A sync

Use this after a merged change to the tokens or a widget, or when the owner
asks.

1. Run `scripts/build.sh` if the build output is missing or old. Then run
   `scripts/design-system.sh`, which writes `artifacts/design-system/`.
2. Read the tokens from `web/src/platform/designTokens.ts`.
3. Read the artifact's change notes. For each issue they name, run
   `gh issue view <number> --json state,title`. The changes of open issues are
   kept.
4. Compare the artifact with the repository. List every other difference the
   sync would overwrite, and every token the artifact has that the code no
   longer defines.
5. Show that list to the owner. Publish only after they confirm.
6. Publish the previews and the tokens to the same artifact, keeping the
   changes of open issues. Add a change note that names the commit you synced
   from (`git rev-parse --short HEAD`) and the open issues you kept.

## When something fails

| Symptom                                           | What to do                                                                                   |
| ------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| The artifact link does not open                   | Ask the owner. The artifact is private to them.                                              |
| `scripts/design-system.sh` fails                  | It needs the Playwright browser from `scripts/setup.sh`. Run that, then try again.           |
| A change note names a closed issue never merged   | Ask the owner whether to drop the change or to open a new issue. Do not drop it by yourself. |
| An issue named in a note cannot be found          | Ask the owner. Keep the change until they answer.                                            |
| Save or Next does nothing in the artifact         | The frame is sandboxed without `allow-forms`. Check the bundle still fires the submit event. |
| A dash shows as garbled characters, such as "â€”"  | The page lacks `<meta charset="utf-8">`. Add it and check the card again.                  |
| A card is cut off or ends in an empty band        | Measure its content height again at 992 px and set the marker's height to it.               |
| A change note is refused                          | It is over 280 characters. Shorten it and keep the review's name and branch.                 |
| A link to the prototype gives a 404              | It points at a branch, which was deleted. Link the review's `design-review/` tag instead.    |
