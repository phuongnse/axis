# AI authoring in Studio

How an author sends an authoring request in Studio, approves its plan, reads its diff and accepts
or drops it, and how revisions show. [D119](../decisions/d119-ai-authoring-in-studio.md) agrees the
idea, and [D125](../decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) the
shapes below. Everything here is _(planned for M35)_. The rest of Studio is in
[Studio](studio.md). The conventions of [architecture.md](../architecture.md) apply.

## The request panel

- **Where.** An **Ask AI** button in the application's header opens a panel beside the editor. It
  shows only when `Ai:DefaultBinding` is set and the person holds the author role.
- **Sending.** The author writes what they want in words, in any language, and sends it. A check box
  "Accept when checks and review pass" asks for auto-accept, and shows only when the application
  allows it. The panel shows the tenant's requests left this month when the tenant has a quota.
- **The list.** The panel lists the application's requests, newest first, with their author, state
  and time. Each opens its page in the panel.
- **Progress.** A request's page shows its state, as [authoring requests](authoring-requests.md#states)
  name them, in words, with the current step and the fix round. It updates by polling, as the
  publish status does.

## The plan

- In `awaitingApproval` the page shows the plan in plain words: what will change, which modules and
  layers it uses, and which scenarios it adds.
- **Approve plan** starts the work. **Change plan** asks for another plan with the author's comment.
  **Drop** ends the request.
- Only the author of the request approves its plan.

## The diff

- In `ready` the page shows the draft's diff resource by resource: each resource with **Added**,
  **Changed** or **Removed**, a summary in words, and the JSON before and after side by side. A
  layer shows the module resource with the layer applied, marked as the
  [customization layers](customization-layers.md#in-studio) editor marks it.
- The checks and the review show under the diff: diagnostics, each scenario with its result, and
  the reviewer's notes.
- **Accept** asks for an accept and shows its status. **Drop** ends the request and deletes the
  draft.
- **A conflict** shows the resources that changed since the draft read them and who changed them.
  The author drops the request, or sends it again so the agent works on the folder as it is now.
- A `failed` request shows why, and its draft's diff, so the author can read what was done.

## Auto-accept

- **The setting.** "Let AI accept its own drafts" is a setting of the application in Studio. It is
  off by default.
- **Who changes it.** Only a person with the publisher role. Each change writes an audit record with
  the person, the application and the new value.
- **Where it is kept.** In the tenant database, with the application, not in a resource file, so no
  tool and no draft can change it.
- **What it allows.** The agent accepts a draft only under the rules of
  [accepting](authoring-requests.md#accepting). An accepted draft is still not live until a person
  publishes.

## Revisions

- **History.** A **History** view of the application lists its revisions, newest first: who, when,
  through which client or request, and a summary. Each opens its diff.
- **Undo.** Each revision has **Undo**, under the rules of [undo](revisions-and-drafts.md#undo). A
  revision that cannot be undone alone names the later revisions that block it.
- **Publishing.** The publish dialog shows the whole diff from the live release, with each change
  marked by the request or the person that made it.

## Connected apps

Outside MCP clients are managed by a tenant administrator, see
[client registration](mcp-server.md#client-registration). Studio shows authors the address of the
MCP server, `https://<tenant host>/mcp`, so they can connect a client when the administrator allows
it.
