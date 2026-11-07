---
name: axis-design
description: Runbook for the owner's local agent session that keeps the Axis Design System artifact in Claude and the repository in step. Use when the owner asks for a design change in words, reviews the design one change at a time, continues a review or works on a design/ branch, or asks to sync or publish the design system, or after a merged change to the tokens or a widget. Not for agents running inside the NexKit pipeline.
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
  [Widget previews](../../../docs/reference/widget-previews.md).

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

1. Take the review's design issue, or agree a short topic with the owner, such
   as `field-look`, and open a design issue with the `on-hold` label for it. A
   review covers one screen or one group of screens. If an
   earlier review's issues have not built what it touches, say so and stop: the
   review waits for them. Create the prototype branch from `main`, named as
   [writing on GitHub](../../../docs/delivery.md#writing-on-github) says:
   `git switch -c design/<yyyy-mm-dd>-<topic> main`, then
   `git push -u origin design/<yyyy-mm-dd>-<topic>`. Work only on that branch,
   never only in a temporary folder. Never open a pull request from it, and
   never give it a NexKit branch name. No push to a branch starts CI, and the
   prototype never gets `/nexkit review`, because it is never merged.
2. Create the review's docs branch from `main`, `docs/<topic>-review`, and push
   it. The review's rules go there as they are agreed.
3. Link both branches on the design issue, and keep its state there: what is
   done, what waits for the owner and what comes next, so the review can go on
   from another machine.

### Each change

Take one change at a time, and do not start the next until the owner has
seen this one. Build only what the design needs to be seen and agreed: no test
changes and no server changes. What the screen needs from an API is a fixture
here and a contract in the docs. What the build must do that the docs do not
say is a comment on the design issue. What you find outside the review's topic
becomes an issue.

1. Restate the change in Axis terms: the tokens, the widgets and the
   previews it touches, by their names in
   [Widget previews](../../../docs/reference/widget-previews.md). Ask the
   owner when the request is ambiguous.
   Then search for what already does the job (the shared widget, its context,
   the route that feeds it) and say which one the change extends. A change
   that would add a second way, a copy or a caller-side workaround is made in
   the shared place instead, as the "One concern has one mechanism" rule in
   [AGENTS.md](../../../AGENTS.md#working-rules) says. When the change shows a
   second mechanism already there, tell the owner and merge the two.
2. Make it in the real code under `web/src/platform/` and
   `web/src/designSystem/`, so the artifact shows the real widgets. Mark each
   new behaviour with a short `Proposed:` comment, so the issue that builds
   it can find every one.
3. When the change adds a state that changes how a widget looks, add it to
   the widget's preview in the same step.
4. Run `scripts/design-system.sh`, which writes
   `artifacts/design-system/axis-design-system.js` and checks it.
5. Check every changed card as the artifact shows it (see below).
6. Commit the change on the branch with a message that says what it shows,
   so the branch history lists the review's changes in order. Push the branch.
7. Tell the owner what changed and which preview to open, and ask for a verdict.
   The owner looks at the previews; take no screenshots for them. Keep, change
   or drop the change as they say. Write each rule they agree where it lives in
   the docs, on the docs branch, in English as all committed text is, and push
   it. Bring the design issue's state up to date.

The artifact does not change during a review. It is published once, at the end.

### Bringing `main` into the prototype

Use this when `main` has changed code the prototype touches. The rule is in
[Design changes](../../../docs/delivery.md#design-changes).

1. `git fetch origin`, then `git merge origin/main`. Never rebase a pushed
   prototype.
2. Resolve each conflict on `main`'s API, data shape, texts and logic, and put
   the prototype's look back on them. Never change `main`'s logic to suit the
   prototype.
3. Check it: `tsc -b --noEmit`, `oxlint --deny-warnings --report-unused-disable-directives` and `prettier --check` in
   `web/`, and `scripts/design-system.sh`. Then push the branch.

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

0. Audit the prototype for one concern with two mechanisms: have an agent read `web/src` and the server for copies, forks
   and second ways to the same result, and put what it finds in a comment on the design issue, so the build merges each
   into the shared one. The rule is in [AGENTS.md](../../../AGENTS.md#working-rules).
1. When the owner has agreed every change, read the docs branch through: it
   holds each agreed rule where it lives, in the reference pages, and deletes
   what it replaces, with what a screen needs from an API as a contract. Add a
   decision only for a choice between real alternatives, never one that lists
   the review's rules. Follow [writing the docs](../../../docs/writing-docs.md),
   and mark what is not built yet. Open it as a hand-made pull request for the
   owner to merge.
2. Tag the prototype's last commit and push the tag, so
   NexKit's agents can read it and no link to it ever breaks:
   `git tag -a design-review/<yyyy-mm-dd>-<topic> -m "<what it shows>"` and
   `git push origin design-review/<yyyy-mm-dd>-<topic>`. No push to a branch other than
   `main` starts a workflow, so the tag starts no run; check with `gh run list`. Link
   the tag's comparison with the commit the branch
   started from, `compare/<base>...design-review/<yyyy-mm-dd>-<topic>`, where
   `<base>` is `git merge-base design/<yyyy-mm-dd>-<topic> main`, so the diff stays the
   same as `main` moves on. The tag is never deleted. Once it is pushed,
   delete the branch, locally and with `git push origin --delete
   design/<yyyy-mm-dd>-<topic>`.
3. Once the docs pull request is merged, so links point at `main`, hand over to
   the build, as [Design changes](../../../docs/delivery.md#design-changes) says:
   - With issues that wait for the design issue, correct each so it links the
     docs and the tag, and takes the build's comments on the design issue.
   - Without any, write one NexKit issue that builds the whole review, as
     [Writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) says. It links
     the docs sections, the artifact and the tag, names the tokens, widgets and
     previews it changes, and takes those comments. Do not split it: NexKit
     splits it if it needs to.
   - Then close the design issue, which starts the issues that wait for it.
4. After the build is merged, publish the artifact as [A sync](#a-sync) says,
   with no need to ask when every difference comes from what merged. The sync
   keeps the card order and names, which `scripts/design-system.sh` writes as
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
5. Show that list to the owner. Publish only after they confirm, unless the
   sync ends a review and every difference comes from what merged.
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
