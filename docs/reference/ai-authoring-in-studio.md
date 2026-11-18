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
  see [tenant notices](notifications.md#tenant-notices). What the page shows is in [a request's page](#a-requests-page).
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
- **The AI requests page** _(planned for M35)_. The page lists every request of the application, newest
  first, in one [table](actions-and-controls.md#tables) with four columns:
  - **Request** is the request's name, as the link to its page. Until AI names it, the name is the first
    sentence of its words, cut at 80 characters with an ellipsis. It has no sort.
  - **State** is a [status tag](shared-widgets.md#which-widget) with the state's one word, as the
    [states table](#a-requests-page) gives it. A fix round reads as Working.
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

## A request's page

_(planned for M35)_

A request's page shows one request from its name to its end. It is one feed of items, read from the top,
with a bar at its foot for the actions. The author sees where the request stands and what to do next.

### The heading

- The heading is the request's name. Until AI names the request, the name is the first sentence of its
  words, cut at 80 characters with an ellipsis.
- The state sits beside the name as a [status tag](shared-widgets.md#which-widget), with its word from
  [the states](#the-states).
- Under the name, `spacing-xs` apart, come the request's words in full. Then, in `textMuted`, comes the
  sender's line: "Sent by you on <time>, co-authored with Claude Sonnet 5.5", or "Sent by <name> on <time>"
  for a request of another person.
- The co-authors are the AIs that made the change, in the order they first worked. Axis's agent is named
  by the model its author's AI used. An outside app is named by the name it registered with. The line is
  one sentence with no full stop. Nothing else on the page names an AI.
- The page checks the request every second until it waits for its author or ends.
- An unknown request shows "This request does not exist.", with a link to
  [the request panel](#the-request-panel). A failed action shows "That did not work. Try again." in an
  [alert](shared-widgets.md#which-widget) above the feed.

### The states

The page shows one word for the state of a request, the same for every reader. The states are those of
[authoring requests](authoring-requests.md#states). Each word has one tone of a
[status tag](shared-widgets.md#which-widget), which says what the state asks of the reader.

| State              | Word              | Tone        |
| ------------------ | ----------------- | ----------- |
| `planning`         | Planning          | `neutral`   |
| `awaitingApproval` | Awaiting approval | `attention` |
| `working`, `fixing` | Working          | `neutral`   |
| `checking`         | Checking          | `neutral`   |
| `reviewing`        | Reviewing         | `neutral`   |
| `ready`            | Ready             | `attention` |
| `accepted`         | Accepted          | `success`   |
| `dropped`          | Dropped           | `muted`     |
| `failed`           | Failed            | `danger`    |

A fix round reads as Working too. The Work item shows its number.

### The feed

The page is one ordered list of items: Plan, Work, Result and Dropped. An item shows once it has something
to show. The page has no tabs and no stages bar.

- Each item has a mark in a circle of the `smallControl` size token, and a line in `border` down to the
  next item, except after the last. Its title is bold, with its standing in `textMuted` after it. Its body
  sits under the title, and `spacing-md` separates it from the next item.
- The mark says how the item stands:
  - **Done** is a check in `primaryText`.
  - **Now** is a turning spinner in `primaryText`, while AI works on the item.
  - **Waiting** is a clock in `warningText`, while the item waits for a person.
  - **Stopped** is a cross in `danger`, where a failure stopped the request.
  - **Idle** is a dash in `textMuted`, where a dropped or failed request left the item.
- **Plan** is described in [the plan](#the-plan).
- **Work** is described in [the Work item](#the-work-item).
- **Result** shows what the request changes, as [the diff](#the-diff) describes.
- **Dropped** is an idle item with the word Dropped and the time the request was dropped.

### The Work item

The Work item shows once the plan is approved. Its standing is "Fix round <n>" while AI fixes, and nothing
else. Its line says what the work is doing or did.

- While AI works, a spinner and the running step in present words, such as "Checking the draft".
- When the work is done, "<n> steps in <duration>".
- When the work stopped, an alert above the line says "AI could not finish this request.", with the
  failure as its message.
- For an outside app's request, the [app's line](#an-outside-apps-request).

The line ends with **Show the steps** or **Hide the steps**, which folds the History in and out.

### The History

Under Show the steps comes the request's History, a table of its steps, newest first. Its columns are Step,
Result, Started and Duration.

- The table has no paging, search or filter. It is part of one request, which [tables](actions-and-controls.md#tables)
  allows for a table that is part of one thing.
- Duration reads "40 s", "3 min" or "1 h 5 min" in the reader's language. A step that is still running
  shows an em dash.
- The words of a step come from its kind and its outcome, and never from AI. Examples are "Wrote a plan"
  ("Proposed a plan" for an outside app), "Changed the draft", "Checked the draft", "Fixed the draft",
  "Reviewed the draft", "Wrote <resource>", "Asked for another plan: “<comment>”" and
  "Asked for changes: “<comment>”".
- The results read "Plan written", "Saved", "All scenarios pass", "No changes requested", "Refused" or
  "Ran out of time". The only free text is the author's own comment in a step.
- Writes that follow each other fold into one group row, as [folding group rows](actions-and-controls.md#tables)
  says. The row reads "Wrote 3 resources", or "Changed 3 resources" when one of them is a delete. Its Result
  is the outcome the writes share, or "Results differ". Its Duration runs from the first write's start to
  the last write's end.
- Each write is a row under its group, with its own Result, Started and Duration. It names its resource
  with [`ResourceReference`](shared-widgets.md#which-widget), so the name is the only link and the kind is muted.

### The guide

The bar at the foot of the window guides the author in every state. It is an
[action bar](actions-and-controls.md#buttons) with a few words of where the request stands, at most one main
action, and the other actions before it. Actions that do not fit fold into More, and
[what comes next](actions-and-controls.md#what-comes-next) gives the rule for every screen. Drop… sits in the
bar, never in the heading.

| State                                         | Words                                                    | Main action                                                                                                       | Others                                                              |
| --------------------------------------------- | -------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| AI writes the plan                            | AI is writing the plan                                   | none                                                                                                              | Drop…                                                               |
| The plan waits, for its author                | Plan ready for your approval                             | Approve plan                                                                                                      | Drop…, Change the plan…                                             |
| The plan waits with questions, for its author | "1 question to answer", or "<n> questions to answer"     | Answer…, which opens the reply as "Your answers"                                                                  | Drop…                                                               |
| The plan waits, for anyone else               | Waiting for its author's approval                        | none                                                                                                              | none                                                                |
| AI works, checks or reviews                   | AI is working on it                                      | none                                                                                                              | Drop…                                                               |
| AI waits for an outside app                   | The app's line, as [below](#an-outside-apps-request) gives it | none                                                                                                         | Drop…                                                               |
| Ready, a finding must change                  | "1 blocking finding", or "<n> blocking findings"         | Ask AI to fix it, which asks for changes with "Fix what the review found must change:" and each blocking finding as the comment | Drop…, Ask for other changes…, Accept anyway…, see [the diff](#the-diff) |
| Ready, passed                                 | Checks and review passed                                 | Accept                                                                                                            | Drop…, Ask for other changes…                                       |
| Ready, an accept met a conflict               | Resources changed since AI read them                     | Make the change again                                                                                             | Drop…                                                               |
| Ready, for anyone else                        | Waiting for its author to accept                         | none                                                                                                              | none                                                                |
| Accepting                                     | Accepting the draft                                      | none                                                                                                              | none                                                                |
| Failed, for its author                        | AI could not finish                                      | Send again, which sends its words as a new request and opens it                                                   | Drop…                                                               |
| Accepted                                      | Accepted. Live once published                            | none                                                                                                              | none                                                                |
| Dropped                                       | Dropped. Nothing changed                                 | none                                                                                                              | none                                                                |

### Replies

Each reply opens a dialog drawn by [`ReasonDialog`](shared-widgets.md#which-widget). A reply takes up to
2,000 characters, and its dialog has Cancel. A failure shows inside the dialog.

- **Change the plan…** is a secondary action for a plan that waits with no questions. Its `medium` dialog,
  "Change the plan", asks "What should the plan do differently?", and its button is **Ask for a new plan**.
- **Answer…** is the main action while the plan has questions. Its `medium` dialog, "Answer the plan's
  questions", asks for "Your answers". Its help says "AI writes the plan again with your answers, and you
  approve it." Its button is **Ask for a new plan**, and it sends the answers as a change of plan.
- **Ask for other changes…** is a secondary action for a ready draft with no conflict. Its `medium` dialog,
  "Ask for other changes", asks "What should AI change?". Its help says "AI works on the same draft with your
  comment, then the checks and the review run again." Its button is **Ask AI to change it**.
- **Drop…** is a secondary action that opens a `small` dialog, "Drop this request?". It says that AI stops
  working on the request and that its draft is deleted. Its button is **Drop**, in the destructive role.
- **Send again** is the main action of a failed request. It sends the request's words as a new request and
  opens that request's page.

### An outside app's request

Axis's agent never works on a request that an outside app is working on. The app goes on with its own
request, also after a reply on the web, see [states](authoring-requests.md#states). A reply on the web is kept
for the app to read.

While the request is in `planning`, `working` or `fixing`, the page shows no spinner. Its author reads
"Go on in <app>, and it reads what you asked here." Anyone else reads "Its author goes on with it in <app>."
These lines are the bar's words and the Work item's line.

## The plan

_(planned for M35)_

A request waits for its author's approval of the plan before any change is made. The plan says what the
application will change, so its author can decide. The page shows it as the Plan item of [the feed](#the-feed).

The Plan item shows the plan in each of its states.

- While AI writes the plan, a spinner and "AI is reading the application and writing a plan. Nothing changes
  until the plan is approved."
- While the plan waits for its author, nothing sits beside its title, and the plan is open.
- Once the plan is approved, the item reads "Approved on <time>", and the plan is folded under its summary.
- The summary is under the title in every state once AI has written the plan. **Show the plan** or
  **Hide the plan** follows it on the same line.
- The author's comments that asked for another plan come first, oldest first, as "Asked for another plan:
  “…”", in the text colour.
- A request that failed before its plan was approved stops here, with its failure.

The groups of the plan follow in this order. Each has a bold heading in the text colour, and it shows only
when it has something to show.

1. **Questions** are what AI cannot decide alone. Each question has its options under it, with the
   recommended one marked.
2. **What changes** is what the application will do differently, one bullet each, in words only.
3. **Decisions to check** are the choices AI made that the request did not settle. Each gives the choice,
   then why.
4. **Risks and limits** are what the change does to the data and the people already there, and where it stops.
5. **Acceptance criteria** say how the result will be checked.
6. **Checklist** gives each item of Axis's checklist, then how the plan meets it.
7. **Implementation notes** sit behind **Show the implementation notes**, with the edits grouped by resource.

A question is one that AI leaves to its author. A decision is a choice that AI made, which its author may undo.

A plan with questions waits for its author's answers. While it has questions, the page offers **Answer…**
and no **Approve plan**. Without questions, the bar offers the author these actions.

- **Approve plan** starts the work.
- **Change the plan…** asks for another plan with the author's comment.
- **Drop…** ends the request.

Only the author of the request approves its plan.

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
