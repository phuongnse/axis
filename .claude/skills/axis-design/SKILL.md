---
name: axis-design
description: Runbook for the owner's local agent session that keeps the Axis Design System artifact in Claude and the repository in step. Use when the owner asks for a design change in words, or asks to sync or publish the design system, or after a merged change to the tokens or a widget. Not for agents running inside the NexKit pipeline.
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
