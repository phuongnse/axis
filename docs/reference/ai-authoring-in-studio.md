# AI authoring in Studio

This page describes how an author works with [authoring requests](authoring-requests.md) in Studio:
sending a request, approving its plan, reading its diff and accepting or dropping it, the setting that
lets an agent accept, and how revisions show. The rest of Studio is in [Studio](studio.md).

## The request panel

_(planned for M35)_

The request panel is where an author asks AI for a change in words. Ask AI opens the panel, and each sent
request opens its own page.

- **Where.** Ask AI on the application's [band](studio-workspace.md#band) opens and closes the panel. The
  panel is the shared [side panel](shell.md#layout) `StudioSidePanel`, which the Problems panel also uses.
  It shows only when `studioAi` is true, the field that [`GET /api/me`](security.md#roles-and-the-current-user) answers.
- **Sending.** The author writes what they want in words, in any language, and sends it. A check box,
  "Accept when checks and review pass", asks for auto-accept, and shows only when the application
  [allows it](#auto-accept). Send has no count beside it, because Axis counts no requests, see
  [D150](../decisions/d150-each-authors-own-ai-with-axis-counting-no-requests.md).
- **The request's page.** After a send, the main area opens the request's page at
  `/studio?app=<application>&view=requests&request=<id>`, and the plan shows there once AI has written it.
  The panel stays open and empty for the next request. The notices of AI requests open the same address,
  see [tenant notices](notifications.md#tenant-notices).
- **No list.** The panel lists no requests. The [bell](notifications.md#the-bell) tells an author when a
  request of theirs waits for them or ends, and the sidebar's AI requests entry, below, leads to every request.
- **Before a key.** Until the author has connected their own AI, the panel shows two ways to ask AI, in
  place of the form. Whether the author has connected one comes from the [AI status](authoring-requests.md#routes) route.
  - **With your own API key.** "Add your AI provider's API key, and the requests you send here run on it.
    Your account pays for what you ask." The key works as [a person's own key](model-bindings.md#a-persons-own-key)
    describes. "Connect your AI" opens the [account page](platform-pages.md#account-page).
  - **With an AI app you already pay for.** The second way follows under a rule. "An AI app that supports
    MCP works on your applications with its own subscription, once your administrator allows it. Give it
    this address:" The [address](#connected-apps) follows, with its copy button.
- **Progress.** A request's page shows its state in words, as
  [authoring requests](authoring-requests.md#states) name the states, with the current step and the fix
  round. It updates by polling, as the publish status does.
- **The AI requests page** _(planned for M35)_. The page lists every request of the application, newest
  first, in one [table](actions-and-controls.md#tables) with four columns:
  - **Request** is the request's name, as the link to its page. Until AI names it, the name is the first
    sentence of its words, cut at 80 characters with an ellipsis. It has no sort.
  - **State** is a [status tag](shared-widgets.md#which-widget) with the state's one word, as
    [authoring requests](authoring-requests.md#states) names them. A fix round reads as Working.
  - **Sent by** is the author's name, or "You" for the reader's own requests.
  - **Sent** is the time the request was sent.

  The table has the full toolbar of a table whose rows grow: Search, which matches a request's name and
  words; Filter on State, Sent by and Sent; Columns; a sort on State, Sent by and Sent, where State sorts in
  the order a request goes through its states; paging such as "1–7 of 7", with a page size; and Refresh.
  Search, filters, sort and paging all live in the address. The page has no control of its own, so Ask AI
  stays on the band.
- **The sidebar's AI requests entry** _(planned for M35)_. AI requests is a parent entry in the sidebar,
  right after History, which opens and folds. Under it are two views of the same table, each a filter kept
  in the address:
  - **All requests** shows every request.
  - **Needs action** shows the requests that wait for the reader: Sent by is you and State is Awaiting
    approval or Ready. The table shows this filter as chips, which the reader may change.

  The sidebar marks Needs action only while the table shows exactly that filter, and All requests otherwise,
  a request's page included. The entry opens by itself while one of its views is open, and a person's choice
  to open or fold it holds until the page changes.

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

- **The setting.** "Let AI accept its own drafts" is a switch that is the last part of the application's
  Settings form, after File storage. It saves on its own, apart from the manifest's Save, through
  [the authoring API](authoring.md#application-settings). It is off by default.
- **Who changes it.** Only a person with the publisher role. An author without it sees the value, with
  the switch disabled. Each change writes an audit record with the person, the application and the new
  value.
- **Where it is kept.** In the tenant database, with the application, not in a resource file, so no
  tool and no draft can change it, and an agent cannot give itself the right to accept.
- **What it allows.** The agent accepts a draft only under the rules of
  [accepting](authoring-requests.md#accepting). An accepted draft is still not live until a person
  publishes.

## Revisions

_(planned for M35)_

Studio shows the application's [revisions](revisions-and-drafts.md#revisions), so an author sees who
changed what and can undo it.

- **History.** The History entry of the sidebar lists the application's revisions, newest first: the
  summary, who, when, and through which client. A revision the server made, such as the start of
  history, shows System as its author and no client. The list is a [table](actions-and-controls.md#tables)
  paged on the server, [20 revisions at a time](authoring-revisions.md#list), with Search, Filter, Columns,
  Refresh, a sort on Who, When and Through and a pager with a page size. All of them live in the address. A revision's summary opens its diff as a page of
  its own, at `&view=history&revision=<id>`, with History still marked in the sidebar. The page shows
  each resource the revision changed with **Added**, **Changed** or **Removed**, and its JSON before
  and after side by side. A loaded revision has no link back, because the sidebar's marked History
  entry and the browser's Back lead back. A revision that does not exist shows one line with a link to History.
- **One diff** _(planned for M35)_. Every diff in Studio is one changes view, whether it is a revision's,
  a request's draft or the publish page's. Each file is a block with its own border. Its header holds a fold
  button, named "Hide changes" or "Show changes" with the file's name, then the resource's name in bold, its
  kind, whether it was Added, Changed or Removed, and its line counts, such as "+8 −3", which assistive
  technology reads in full. A resource that only a draft adds takes the name its change carries. The file's
  summary in words comes under the header, then its diff. A file is open at first, unless its page folds it.
- **A file's diff** _(planned for M35)_. The JSON is shown as one column of lines with line numbers. An added
  line is on an added tint with a mark in the gutter, a removed line on a removed tint with its own mark, and
  the changed words of a line on a deeper tint. The JSON is coloured as the code editor colours it. A run of
  unchanged lines beyond three lines of context folds to "Show `<n>` unchanged lines". An added file is all
  added, and a removed file is all removed. The view is read-only.
- **Undo.** Each revision but the start of history, which the server names on every page and under any
  filter, has **Undo**, under the rules of
  [undo](revisions-and-drafts.md#undo). It runs at once, and the undo is itself a revision that can
  be undone. A revision that cannot be undone alone names the later revisions that block it, each with
  its summary, author and time, and nothing is undone.
- **Publishing.** _(planned for M35)_ The publish page lists every file that differs from the live release,
  under "`<n>` files changed since the live release". Each file is a block, folded at first, with "Changed by"
  and each person who changed it since the release, in the order of their first change. The reader is "you",
  and an AI request is "the AI request “`<name>`”", a link to its page. The list comes from the
  [live diff](authoring-revisions.md#live-diff). The page is described in [publishing](studio.md#publishing).

## Connected apps

_(planned for M35)_

A tenant administrator manages outside MCP clients, see
[client registration](mcp-server.md#client-registration). The request panel ends, under a rule, with
**Use your own AI app**. Under it, in `textMuted`, comes the sentence "An AI app that supports MCP can work
on your applications once your administrator allows it. Give it this address:". The address, in code style,
`https://<tenant host>/mcp`, with a copy button, ends that line, right after "Give it this address:". Before an author has connected
their own AI, the panel shows this address once, in its second way, see [the request panel](#the-request-panel).
