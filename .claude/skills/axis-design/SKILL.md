---
name: axis-design
description: Runbook for the owner's local agent session that keeps the Axis Design System artifact in Claude and the repository in step. Use when the owner asks for a design change in words, reviews the design one change at a time, continues a review or works on a design/ branch, or asks to sync or publish the design system, or after a merged change to the tokens or a widget. Not for agents running inside the NexKit pipeline.
---

# Keeping the Axis design system in step

The rules are in [Design changes](../../../docs/delivery.md#design-changes) and
[D57](../../../docs/decisions/d57-a-design-system-published-from-the-repository.md).
This runbook gives the steps and commands.

- **The artifact's link** is in Design changes. Look it up there.
- **The artifact's file layout and how to publish** are in the Design System
  type's own instructions in Claude. Follow them.
- **Token names and the widget list** are in the
  [frontend reference](../../../docs/reference/frontend.md) under "Design
  tokens and previews". The preview names are in
  [Widget previews](../../../docs/reference/widget-previews.md).

## A requested change

For a change the owner asks for in words, follow [requested-change.md](requested-change.md).

## A design review

Use this when the owner reviews the design in words, one change after
another. The rules are under "Several changes are a design review" in Design changes.

### Start

1. Take the review's design issue, or agree a short topic with the owner, such
   as `field-look`, and open a design issue labelled `design` and `on-hold` with no milestone, as
   [how issues run](../../../docs/delivery.md#how-issues-run) says. Its title names
   one subject. Its body states the scope and build notes, and says NexKit writes the docs change
   from the rule comments. Set the body with
   `gh issue edit <n> --body-file <file>`. If an earlier review's issues have not
   built what it touches, say so and stop.
2. Check the issue's scope against the one-subject rule in
   [Design changes](../../../docs/delivery.md#design-changes). If it holds more than one subject,
   propose the split to the owner. When the owner agrees, open each other subject with
   `gh issue create ... --label design,on-hold` and no milestone, and mark it *blocked by* this issue
   only when it builds on what this one agrees. Then edit this issue's title and body to its own
   subject with `gh issue edit`.
3. Put the prototype branch on the current `origin/main`, named as
   [writing on GitHub](../../../docs/delivery.md#writing-on-github) says. Work
   only on that branch. It gets no pull request, no CI and no `/nexkit review`,
   as Design changes says.
   - If the branch exists, as when a review goes on in a new session: run
     `git fetch origin`, `git switch design/<yyyy-mm-dd>-<topic>`, then follow
     [merging-main.md](merging-main.md).
   - Otherwise: run `git fetch origin`,
     `git switch -c design/<yyyy-mm-dd>-<topic> origin/main`, then
     `git push -u origin design/<yyyy-mm-dd>-<topic>`.
4. Post one state comment with `gh issue comment <n> --body-file <file>`. It
   names the branch, the `main` commit it stands on, each agreed step with a link
   to its rule comment, what waits for the owner, what comes next and the parts
   list of the step in hand. It holds the present only. Its id is the
   `#issuecomment-<id>` at the end of the URL. Edit it from then on:
   `gh api --method PATCH repos/{owner}/{repo}/issues/comments/<id> -F body=@<file>`.

### Each change

Take one change at a time. Start the next only after the owner has seen this
one. What the build must do beyond the docs goes in the design
issue's body, with `gh issue edit`. Anything a step reaches outside the subject,
not only a new screen or platform concept, becomes its own design issue, held,
and blocked by this one only when it builds on what this one agrees:
`gh issue create ... --label design,on-hold`, with the blocked-by mark as
[Writing an issue](../axis-nexkit/SKILL.md#writing-an-issue) step 4 says. The step
goes on without it.

1. Follow [merging-main.md](merging-main.md), so the prototype stands on the
   current `main`.
2. Restate the change in Axis terms: the tokens, widgets and previews it
   touches. Before building, list each visible part with the shared widget,
   helper or token that draws it, from
   [which widget](../../../docs/reference/shared-widgets.md#which-widget). Mark
   **new** each part no widget draws, with the shared place it goes to. Merge
   any two mechanisms the list shows for one job, and tell the owner.
3. Make it in the real code under `web/src/platform/` and
   `web/src/designSystem/`, so the artifact shows the real widgets. Mark each
   new behaviour with a `Proposed:` comment for its issue to find.
4. Add each look-changing state and each needed fixture to the widget's preview
   in this step. Check each added or changed fixture against the API's reference
   page and the server code that answers it, such as
   `src/Axis.Server/Processes/ProcessInspectionEndpoints.cs`. A value no API gives
   is a field marked `Proposed:`, named in the build notes. Open no issue for other
   missing fixtures.
5. Run `scripts/design-system.sh`, which writes
   `artifacts/design-system/axis-design-system.js` and checks it.
6. Check every changed card as [checking-a-card.md](checking-a-card.md) says,
   including the rules for every screen.
7. Commit with a message that says what it shows, and push the branch.
8. Tell the owner what changed and which preview to open, and ask for a
   verdict: keep, change or drop it. Write the rules agreed in this step as one
   comment, as [Design changes](../../../docs/delivery.md#design-changes) says.
   Post it, or edit the screen's rule comment if it has one, with the commands
   in Start step 4. When the owner agrees, have an agent audit this step's diff,
   `git diff <step's first commit>^..HEAD -- web/src`, for one concern done by two
   mechanisms. Merge what it finds, then commit and push before the next step.
   Then edit the state comment in place: link this step's rule comment and hold
   the present only.

### End

When the owner has agreed every change, follow
[ending-a-review.md](ending-a-review.md).

## A sync

After a merged change to the tokens or a widget, or when the owner asks, follow [sync.md](sync.md).

## When something fails

See [when-something-fails.md](when-something-fails.md).
