# Authoring requests

An authoring request is a change that an author describes in words. The agent host plans it, makes it
on a draft, checks it, has it reviewed and fixes what fails, and then the draft is accepted or
dropped. This page gives the agent host, how it acts for the author, how a request is stored and
moves through its states, its budgets, how a draft is accepted, and what reaches the model. How an
author works with a request in Studio is in [AI authoring in Studio](ai-authoring-in-studio.md), and
the reasons are in [D119](../decisions/d119-ai-authoring-in-studio.md).

## The agent host

The agent host is the process that runs authoring requests. It is one client of the MCP server, with
no shortcut of its own.

- **A host of its own.** `axis-agent` runs beside the server, the worker and the publisher, from the
  same images, Helm chart and Compose file. It runs the same way in a cell and on premises, and needs
  no GitHub, no CI service and no particular cloud.
- **One client of the MCP server** _(planned for M35)_. It calls the [MCP server](mcp-server.md) over HTTP, like any
  client, and has no other way into a tenant. It never opens a tenant database for an application's
  resources, and never reads a record.
- **Its own state.** It reads its tenants as the worker does, see [AI settings](host-settings.md#ai-settings),
  and reads the request tables below in each tenant database with the tenant's runtime role. It refuses a
  tenant whose role can change the schema. It writes claims, steps and tokens there with the same role.
- **Models** _(planned for M35)_. It calls a model through an adapter, by the binding the request uses, see
  [model bindings](model-bindings.md).

## Acting for the author

_(planned for M35)_

The agent host acts with exactly the grants of the author who sent the request.

- **Token exchange.** When an author sends a request, the server exchanges the author's session for a
  token of the agent host's client, by OAuth 2.0 token exchange (RFC 8693) at the tenant's realm. The
  token's subject is the author and its audience is the MCP server.
- **Kept with the request.** The refresh token is stored in the request row, encrypted with the
  tenant's data key, and deleted when the request ends. It ends with the author's realm session, at
  most 8 hours. A request that outlives it stops as `failed`, and the author sends it again.
- **No shortcut.** The agent host holds no role of its own on any tenant. A token exchange for a
  person who has lost the author role is refused.

## Durable requests

A request and each step of its loop are rows in the tenant database, so a restart of the agent host
loses no work.

- **Tables.** `axis.authoring_requests` holds one row per request: its id, application, author,
  request text, binding, state, draft, plan, round, token use, the auto-accept flag and its times.
  `axis.authoring_request_steps` holds one row per step of the loop, in order: its kind, input
  summary, outcome, tokens and times. A step's full model messages are kept for 30 days for the author
  and the audit, then deleted. The columns are in [authoring request storage](authoring-request-storage.md#authoring-requests).
- **Claimed with a lease.** The agent host claims a request whose state needs work, as the publisher
  claims [publish requests](authoring.md#lifecycle): it sets `claimed_by` and `claimed_until`, renews
  the lease while it works, and lets it lapse when it stops. A host that dies leaves the request
  claimable after the lease. The host claims only a state whose step it has. Every write after the
  claim is fenced by `claimed_by`, which is new for each claim, so a host that lost its claim, or
  whose request was dropped, changes nothing and stops working on that request.
- **Resuming.** Each finished step is committed with the request's next state before the next
  starts, and after a restart the next host resumes at the last finished step. It closes the step
  that never finished with the outcome `interrupted`, which keeps its tokens, and runs that step
  again as a new step. A step's tokens are written to its row and to the request as soon as its
  call ends. A model call that was in flight is made again, and its tokens count again.
- **Fair use** _(planned for M35)_. The agent host takes the requests of the tenants of a cell in turn, as the worker
  shares its time, so one tenant's long request does not hold back another's.

## Routes

An author sends, lists, reads, approves, changes and drops requests through the session routes under
`/api/studio/apps/{application}/authoring-requests`, with the CSRF header of the other authoring
routes. The shared answers are in [responses](authoring.md#responses). The routes never accept a
request, which belongs to [accepting](#accepting).

| Route                                                                     | What it does                                                                                            |
| ------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `POST /api/studio/apps/{application}/authoring-requests`                  | Stores a request in `planning` and answers `202` with it and a `Location` for the read route            |
| `GET /api/studio/apps/{application}/authoring-requests`                   | Lists the newest 100 requests of the application, newest first                                          |
| `GET /api/studio/apps/{application}/authoring-requests/{id}`              | Reads one request with its steps in order                                                               |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/approve`     | Moves a request from `awaitingApproval` to `working`                                                    |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/change-plan` | Moves a request from `awaitingApproval` back to `planning` and keeps the comment as a `planChange` step |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/drop`        | Moves a request to `dropped` from any state but `accepted` and `dropped`                                |

- **Order of checks.** A send checks the session (`401`), the author role (`403`), in a cell the
  tenant's [`authoringRequests` quota](tenant-quotas.md#refusals) (`403`), the application (`404`),
  its manifest `id` (`409`), the content type (`415`) and the body (`400`). Just before the insert it
  counts again under a lock, so sends at the same time cannot pass the quota together. The other
  routes check the session, the author role and the application, then answer `404` for an `id` that is
  not a UUID of this application. Approve, change plan and drop then answer `403` to anyone but the
  author of the request, and last answer `409` for a request in a state the action does not accept.
- **Who.** Any author lists and reads the requests of an application. Only the author of a request
  approves its plan, changes it or drops it.
- **Body.** A send takes `{ text, autoAccept? }` with a `text` of 1 to 4,000 characters. A change of
  plan takes `{ comment }` of 1 to 2,000 characters. The other routes take no body.
- **Answer.** A request has its `id`, `application`, `author`, `authorName`, `text`, `state`, `plan`,
  `round`, `tokens`, `autoAccept`, `sentAt`, `approvedAt` and `finishedAt`. The read route adds `steps`,
  each with its `kind`, `summary`, `outcome`, `tokens`, `startedAt` and `finishedAt`.

## States

A request follows the same loop as a change to the platform: a plan the author approves, the change,
the checks, an independent review and fix rounds.

| State              | What happens                                                                                                                            | Next                                  |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- |
| `planning`         | The agent reads the application through the tools and writes a plan in plain words: what it will change and which scenarios it will add | `awaitingApproval`                    |
| `awaitingApproval` | The author reads the plan and approves it, or asks for another plan with a comment                                                      | `working`, or `planning` again        |
| `working`          | The agent creates the draft and makes the change through the tools                                                                      | `checking`                            |
| `checking`         | The compiler, the application's scenarios and the contract scenarios of its modules run on the draft                                    | `reviewing`, or `fixing` on a failure |
| `reviewing`        | A second agent session, with no memory of the first, reviews the draft against the request and the plan                                 | `ready`, or `fixing` on findings      |
| `fixing`           | The agent works on the failures and findings                                                                                            | `checking`                            |
| `ready`            | The draft passed. The author accepts or drops it, or the agent accepts it, see [accepting](#accepting)                                  | `accepted` or `dropped`               |
| `accepted`         | The draft is in the working folder as a revision                                                                                        | none                                  |
| `dropped`          | The author dropped the request at any state before `accepted`, and its draft is deleted                                                 | none                                  |
| `failed`           | A budget ran out, the token ended, or a step failed in a way a retry does not help. The draft is kept for the author                    | `dropped`                             |

- **Built-in modules first.** When a request matches a built-in module, the plan declares the module
  and writes a [customization layer](customization-layers.md) over it instead of new resources, so the
  application keeps the module's upgrades and its contract. It adds scenarios for what it adds.
- **Plans need approval** _(planned for M35)_. No request leaves `awaitingApproval` without the author, also when the
  application allows auto-accept.

## Budgets

Each request has limits on its rounds, tokens and time, so the cost of one request is bounded.

- **Fix rounds** _(planned for M35)_. At most `Ai:FixRounds` rounds, 3 by default. A request still failing after the last
  round stops as `failed`.
- **Tokens** _(planned for M35)_. At most `Ai:RequestTokenBudget` tokens, input and output together, across every model
  call of the request, review included, 2,000,000 by default. A call that would pass the budget is not
  made, and the request stops as `failed`.
- **Time** _(planned for M35)_. At most `Ai:RequestTimeout` from the plan's approval, 30 minutes by default.
- **Quota.** The request counts toward the tenant's `authoringRequests` quota once, when the author
  sends it. A request past the quota is refused with the quota's `403`, see
  [tenant quotas](tenant-quotas.md#refusals). A request that asks for a new plan or a new fix round
  does not count again. [Tenant quotas](tenant-quotas.md#quotas) says how the month is counted.

## Accepting

Accepting moves a draft's changes into the working folder as one revision. It never puts them live:
only [publishing](authoring.md#publishing) does.

- **Who.** _(planned for M35)_ The author of the request accepts or drops the draft in Studio. The agent may accept it
  through `accept_draft` only when all of these hold:
  - the application's auto-accept setting is on, see
    [AI authoring in Studio](ai-authoring-in-studio.md#auto-accept);
  - the request asked for it when it was sent;
  - its checks and its review passed in the last round;
  - the author still holds the author role.
- **Through the publisher.** An accept is a [publish request](authoring.md#publishing) of the kind
  `accept`. The publisher runs the requests of one application one at a time, so two accepts never
  interleave.
- **All or nothing.** The publisher checks that every resource the draft read or changed still has the version
  the draft recorded, applies the draft to the working folder as it is now, runs a validate with its
  contract scenarios and the application's test scenarios on the result, and commits it as a revision, see
  [revisions and drafts](revisions-and-drafts.md#accepting-a-draft). So two changes that pass alone
  cannot break the application together, and on any failure nothing is written.
- **Conflicts.** A resource that changed since the draft recorded it is a conflict, and the accept
  answers it with one `AXC0377` diagnostic for each resource, naming the resource, its file and its
  new version.
- **Retrying a conflict** _(planned for M35)_. The agent then reads the folder again, makes the change
  on a new draft and runs the checks again, within the request's budgets. Studio shows a conflict to a
  person, who decides.

## Audit

A request keeps the whole story of the change, and its milestones write audit records.

- **The request.** Each request keeps who asked, the client, the binding and model, the plan, the diff,
  each check, the review, the tokens and who accepted or dropped it.
- **Audit records.** Sending a request, approving a plan and dropping it each write an audit record
  with the author, the request id and the application, see
  [audit actions](storage.md#audit-actions).
- **Accepting's record** _(planned for M35)_. Accepting a draft writes an audit record too, with the
  same fields.
- **Usage** _(planned for M35)_. The tokens of each request and model join the tenant's [usage](tenant-quotas.md#usage).

## What reaches the model

_(planned for M35)_

The model sees the application's configuration and the results of its checks, never the tenant's
data.

- **Sent:** the request text, the plan, the application's resources and its modules' resources,
  diagnostics, scenario results and the reference text of the tools.
- **Never sent:** records, files, record history, process instances, secrets, binding values, keys,
  tokens and the names of people. A scenario's data is made up for the scenario.
