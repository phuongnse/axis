# AI authoring in Studio

This page describes how an author works with [authoring requests](authoring-requests.md) in Studio:
sending a request, approving its plan, reading its diff and accepting or dropping it, the setting that
lets an agent accept, and how revisions show. The rest of Studio is in [Studio](studio.md).

## The request panel

_(planned for M35)_

The request panel is where an author sends requests and follows them.

- **Where.** An Ask AI button on the application's [band](studio-workspace.md#band) opens a panel
  beside the editor. It shows only when `Ai:DefaultBinding` is set and the person holds the author
  role.
- **Sending.** The author writes what they want in words, in any language, and sends it. A check box,
  "Accept when checks and review pass", asks for auto-accept, and shows only when the application
  [allows it](#auto-accept). The panel shows the tenant's requests left this month when the tenant has
  a quota.
- **The list.** The panel lists the application's requests, newest first, with their author, state and
  time. Each opens its page in the panel.
- **Progress.** A request's page shows its state in words, as
  [authoring requests](authoring-requests.md#states) name the states, with the current step and the fix
  round. It updates by polling, as the publish status does.

## The plan

_(planned for M35)_

A request waits for its author's approval of the plan before any change is made.

- In `awaitingApproval` the page shows the plan in plain words: what will change, which modules and
  layers it uses, and which scenarios it adds.
- **Approve plan** starts the work. **Change plan** asks for another plan with the author's comment.
  **Drop** ends the request.
- Only the author of the request approves its plan.

## The diff

_(planned for M35)_

When a request is `ready`, its page shows what the draft changes and how its checks went, so the
author can decide.

- The page shows the draft's diff as a [revision's diff](#revisions) shows it, with a summary in words
  for each resource. A layer shows the module resource with the layer applied, marked as the
  [customization layers](customization-layers.md#in-studio) editor marks it.
- The checks and the review show under the diff: diagnostics, each scenario with its result, and the
  reviewer's notes.
- **Accept** asks for an accept and shows its status. **Drop** ends the request and deletes the draft.
- **A conflict** shows the resources that changed since the draft read them, and who changed them. The
  author drops the request, or sends it again so that the agent works on the folder as it is now.
- A `failed` request shows why, and its draft's diff, so the author can read what was done.

## Auto-accept

_(planned for M35)_

Auto-accept lets the agent accept its own drafts in an application, as an automatic merge does.

- **The setting.** "Let AI accept its own drafts" is a switch on the application's Settings page,
  beside the manifest. It saves on its own, apart from the manifest's Save, through
  [the authoring API](authoring.md#application-settings). It is off by default.
- **Who changes it.** Only a person with the publisher role. An author without it sees the value, with
  the switch disabled. Each change writes an audit record with the person, the application and the new
  value.
- **Where it is kept.** In the tenant database, with the application, not in a resource file, so no
  tool and no draft can change it, and an agent cannot give itself the right to accept.
- **What it allows.** _(planned for M35)_ The agent accepts a draft only under the rules of
  [accepting](authoring-requests.md#accepting). An accepted draft is still not live until a person
  publishes.

## Revisions

_(planned for M35)_

Studio shows the application's [revisions](revisions-and-drafts.md#revisions), so an author sees who
changed what and can undo it.

- **History.** The History entry of the sidebar lists the application's revisions, newest first: the
  summary, who, when, and through which client. A revision the server made, such as the start of
  history, shows System as its author and no client. A revision's summary opens its diff as a page of
  its own, at `&view=history&revision=<id>`, with History still marked in the sidebar. The page shows
  each resource the revision changed with **Added**, **Changed** or **Removed**, and its JSON before
  and after side by side.
- **Undo.** Each revision but the oldest, the start of history, has **Undo**, under the rules of
  [undo](revisions-and-drafts.md#undo). It runs at once, and the undo is itself a revision that can
  be undone. A revision that cannot be undone alone names the later revisions that block it, each with
  its summary, author and time, and nothing is undone.
- **Publishing.** _(planned for M35)_ The publish dialog shows the whole diff from the live release,
  with each change marked by the request or the person that made it.

## Connected apps

_(planned for M35)_

A tenant administrator manages outside MCP clients, see
[client registration](mcp-server.md#client-registration). Studio shows authors the address of the MCP
server, `https://<tenant host>/mcp`, so they can connect a client when the administrator allows it.
