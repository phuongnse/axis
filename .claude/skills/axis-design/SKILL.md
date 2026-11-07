---
name: axis-design
description: Runbook for the owner's local agent session that keeps the Axis Design System artifact in Claude and the repository in step. Use when the owner asks for a design change in words, reviews the design one change at a time, continues a review or works on a design/ branch, or asks to sync or publish the design system, or after a merged change to the tokens or a widget. Not for agents running inside the NexKit pipeline.
---

# Keeping the Axis design system in step

The rules are in [Design changes](../../../docs/delivery.md#design-changes) and
[D57](../../../docs/decisions/d57-a-design-system-published-from-the-repository.md).
This runbook gives the steps and commands, so follow its links for the rules.

- **The artifact's link** is in Design changes. Look it up there each time.
- **The artifact's file layout and how to publish** are in the Design System
  type's own instructions in Claude. Read them each time and follow them.
- **Token names and the widget list** are in the
  [frontend reference](../../../docs/reference/frontend.md) under "Design
  tokens and previews". The preview names are in
  [Widget previews](../../../docs/reference/widget-previews.md).

## A requested change

Use this when the owner asks for a change in words, for example "give a
section more space".

1. Restate the request in Axis terms. Name each token, such as `primary` or
   `spacing-md`, and each widget, such as `table`, `form` or `section`. Ask the
   owner when the request is ambiguous.
2. Open the artifact and make the change. Show the owner the result.
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

## A design review

Use this when the owner reviews the design in words, one change after
another. The rules are under "Several changes are a design review" and the
bullets after it in Design changes.

### Start

1. Take the review's design issue, or agree a short topic with the owner, such
   as `field-look`, and open a design issue with the `on-hold` label for it. Its
   body says the review ends in a docs change that NexKit writes from the rule
   comments, and that a comment naming the one it replaces supersedes it. If an
   earlier review's issues have not built what it touches, say so and stop.
2. Create the prototype branch from `main`, named as
   [writing on GitHub](../../../docs/delivery.md#writing-on-github) says:
   `git switch -c design/<yyyy-mm-dd>-<topic> main`, then
   `git push -u origin design/<yyyy-mm-dd>-<topic>`. Work only on that branch.
   It gets no pull request, no CI and no `/nexkit review`, as Design changes
   says.
3. Link the branch on the design issue, and keep its state there: what is
   done, what waits for the owner and what comes next.

### Each change

Take one change at a time, and do not start the next until the owner has seen
this one. What the build must do that the docs do not say is a comment on the
design issue. What you find outside the review's topic becomes an issue.

1. Restate the change in Axis terms: the tokens, the widgets and the previews
   it touches. Then search for what already does the job and say which one the
   change extends, as [AGENTS.md](../../../AGENTS.md#working-rules) says. When
   the change shows a second mechanism already there, tell the owner and merge
   the two.
2. Make it in the real code under `web/src/platform/` and
   `web/src/designSystem/`, so the artifact shows the real widgets. Mark each
   new behaviour with a short `Proposed:` comment, so the issue that builds it
   can find every one.
3. When the change adds a state that changes how a widget looks, add it to the
   widget's preview in the same step.
4. Run `scripts/design-system.sh`, which writes
   `artifacts/design-system/axis-design-system.js` and checks it.
5. Check every changed card as [checking-a-card.md](checking-a-card.md) says.
6. Commit the change with a message that says what it shows. Push the branch.
7. Tell the owner what changed and which preview to open, and ask for a
   verdict. Keep, change or drop the change as they say. Comment each rule they
   agree on the design issue, as [Design changes](../../../docs/delivery.md#design-changes)
   says, with `gh issue comment <n> --body-file <file>`. Then bring the design
   issue's state up to date.

When `main` has changed code the prototype touches, follow
[merging-main.md](merging-main.md) before the next change.

### End

When the owner has agreed every change, follow
[ending-a-review.md](ending-a-review.md).

## A sync

Use this after a merged change to the tokens or a widget, or when the owner
asks. The rule is "A sync follows the repository" in
[Design changes](../../../docs/delivery.md#design-changes).

1. Run `scripts/build.sh` if the build output is missing or old. Then run
   `scripts/design-system.sh`, which writes `artifacts/design-system/`.
2. Read the tokens from `web/src/platform/designTokens.ts`.
3. For each issue the artifact's change notes name, run
   `gh issue view <number> --json state,title`. The changes of open issues are
   kept.
4. Compare the artifact with the repository. List every other difference the
   sync would overwrite, and every token the artifact has that the code no
   longer defines.
5. Show that list to the owner and publish only after they confirm, unless the
   sync ends a review and every difference comes from what merged.
6. Publish the previews and the tokens to the same artifact, keeping the
   changes of open issues. Add a change note that names the commit you synced
   from (`git rev-parse --short HEAD`) and the open issues you kept.

## When something fails

See [when-something-fails.md](when-something-fails.md).
