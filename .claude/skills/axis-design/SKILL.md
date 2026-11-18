---
name: axis-design
description: Runbook for the owner's local agent session that keeps the Axis Design System artifact in Claude and the repository in step. Use when the owner asks for a design change in words, reviews the design one change at a time, continues a review or works on a design/ branch, or asks to sync or publish the design system, or after a merged change to the tokens or a widget. Not for agents running inside the NexKit pipeline.
---

# Keeping the Axis design system in step

The rules are in [Design changes](../../../docs/delivery.md#design-changes) and
[D57](../../../docs/decisions/d57-a-design-system-published-from-the-repository.md).
This runbook gives the steps and commands.

- **The artifact's link** is in Design changes. Look it up there each time.
- **The artifact's file layout and how to publish** are in the Design System
  type's own instructions in Claude. Follow them each time.
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
4. Add a change note to the artifact that names the issue by number and link.
   The note is the last one, so a later sync can find the issue.

## A design review

Use this when the owner reviews the design in words, one change after
another. The rules are under "Several changes are a design review" in Design changes.

### Start

1. Take the review's design issue, or agree a short topic with the owner, such
   as `field-look`, and open a design issue for it with the `on-hold` label. Its
   body states the scope and the build notes, and says the review ends
   in a docs change that NexKit writes from the rule comments. Set the body with
   `gh issue edit <n> --body-file <file>`. If an earlier review's issues have not
   built what it touches, say so and stop.
2. Put the prototype branch on the current `origin/main`, named as
   [writing on GitHub](../../../docs/delivery.md#writing-on-github) says. Work
   only on that branch. It gets no pull request, no CI and no `/nexkit review`,
   as Design changes says.
   - If the branch exists, as when a review goes on in a new session: run
     `git fetch origin`, `git switch design/<yyyy-mm-dd>-<topic>`, then follow
     [merging-main.md](merging-main.md).
   - Otherwise: run `git fetch origin`,
     `git switch -c design/<yyyy-mm-dd>-<topic> origin/main`, then
     `git push -u origin design/<yyyy-mm-dd>-<topic>`.
3. Post one state comment with `gh issue comment <n> --body-file <file>`. It
   names the branch, the `main` commit it stands on, what is done, what waits for
   the owner, what comes next and each change's parts list. Its id is the
   `#issuecomment-<id>` at the end of the URL. Edit it from then on:
   `gh api --method PATCH repos/{owner}/{repo}/issues/comments/<id> -F body=@<file>`.

### Each change

Take one change at a time. Start the next only after the owner has seen this
one. What the build must do beyond the docs goes in the design
issue's body, with `gh issue edit`. What you find outside the review's topic
becomes an issue.

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
   in this step. Open no issue for other missing fixtures.
5. Run `scripts/design-system.sh`, which writes
   `artifacts/design-system/axis-design-system.js` and checks it.
6. Check every changed card as [checking-a-card.md](checking-a-card.md) says.
7. Commit with a message that says what it shows, and push the branch.
8. Tell the owner what changed and which preview to open, and ask for a
   verdict: keep, change or drop it. Write the rules agreed in this step as one
   comment, as [Design changes](../../../docs/delivery.md#design-changes) says.
   Post it, or edit the screen's rule comment if it has one, with the commands
   in Start step 3. Then edit the state comment in place, with this change's
   parts list from step 2.

### End

When the owner has agreed every change, follow
[ending-a-review.md](ending-a-review.md).

## A sync

Use this after a merged change to the tokens or a widget, or when the owner
asks. The rule is "A sync follows the repository" in
[Design changes](../../../docs/delivery.md#design-changes).

1. Run `scripts/design-system.sh`, which builds the bundle from source. If
   `web/node_modules` is missing, run `scripts/setup.sh` first.
2. Read the tokens from `web/src/platform/designTokens.ts`.
3. For each issue the change notes name, run
   `gh issue view <number> --json state,title`. Keep the changes of open issues.
4. Compare the artifact with the repository. List each other difference the
   sync would overwrite, and each token the code no longer defines.
5. Show that list to the owner and publish only after they confirm, unless the
   sync ends a review and every difference comes from what merged.
6. Publish the previews and tokens to the same artifact, keeping open issues'
   changes. Add a change note with the commit you synced from
   (`git rev-parse --short HEAD`) and the open issues kept.

## When something fails

See [when-something-fails.md](when-something-fails.md).
