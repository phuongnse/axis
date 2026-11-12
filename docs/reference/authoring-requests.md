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
- **One client of the MCP server.** It calls the [MCP server](mcp-server.md) over HTTP, like any
  client, and has no other way into a tenant. It never opens a tenant database for an application's
  resources, and never reads a record. It opens its session on the tenant's first host, at
  `https://<host>/mcp`, with the author's access token, and follows no redirect.
- **Its own state.** It reads its tenants as the worker does, see [AI settings](host-settings.md#ai-settings),
  and reads the request tables below in each tenant database with the tenant's runtime role. It refuses a
  tenant whose role can change the schema. It writes claims, steps and tokens there with the same role.
- **Models.** It calls a model through an adapter, by the binding the request uses, see
  [model bindings](model-bindings.md). A request that stores no binding uses `Ai:DefaultBinding`.

## Acting for the author

The agent host acts with exactly the grants of the author who sent the request.

- **Token exchange.** When an author sends a request, the server exchanges the author's session for a
  token of the agent host's client, by OAuth 2.0 token exchange (RFC 8693) at the tenant's realm. The
  token's subject is the author and its audience is the MCP server. The send refuses with `403` a
  token that does not carry the author role, the check that the MCP server makes on the same token.
- **Kept with the request.** The refresh token is stored in the request row, encrypted with the
  tenant's data key. A drop, an accept or a failure erases it: the commit that moves the request to
  `accepted`, `dropped` or `failed` sets it to null, and a step that moves it to any other state
  keeps it. The commit that moves it to `accepted` is the publisher's, see [accepting](#accepting).
- **Ends with the session.** When a step needs the author's access token, the agent host refreshes
  the stored token at the tenant's realm as `axis-agent` and keeps the new refresh token that the
  realm returns. The token ends with the author's realm session, at most 8 hours. A token that the
  realm refuses with `invalid_grant`, a request that keeps none and a token that no longer decrypts
  stop the request as `failed`, with the step outcome `tokenEnded`, and the author sends it again. A
  `401` from the MCP server refreshes the token once and the call is made again, for each call on
  its own. A `403`, or a second `401` on the same call, stops the request as `failed` with the outcome
  `tokenEnded` too. A realm that does not answer, or answers another error, and any other failure of
  the MCP server change nothing: the lease lapses and a later turn runs the step again.
- **No shortcut.** The agent host holds no role of its own on any tenant. A token exchange for a
  person who has lost the author role is refused.

## Durable requests

A request and each step of its loop are rows in the tenant database, so a restart of the agent host
loses no work.

- **Tables.** `axis.authoring_requests` holds one row per request: its id, application, author,
  request text, binding, state, draft, plan, round, token use, the auto-accept flag and its times.
  `axis.authoring_request_steps` holds one row per step of the loop, in order: its kind, input
  summary, outcome, tokens, checks and times. The columns are in [authoring request storage](authoring-request-storage.md#authoring-requests).
- **Model messages.** A step's full model messages, resource contents included, are kept in its row
  in `messages`.
- **Deleting model messages** _(planned for M35)_. A step's model messages are deleted after 30 days,
  and are kept until then for the author and the audit.
- **Claimed with a lease.** The agent host claims a request whose state needs work, as the publisher
  claims [publish requests](authoring.md#lifecycle): it sets `claimed_by` and `claimed_until`, renews
  the lease while it works, and lets it lapse when it stops. A host that dies leaves the request
  claimable after the lease. The host claims only a state whose step it has. Every write after the
  claim is fenced by `claimed_by`, which is new for each claim, so a host that lost its claim, or
  whose request was dropped, changes nothing and stops working on that request.
- **Resuming.** Each finished step is committed with the request's next state before the next
  starts. The commit frees the request, and a later turn claims it again. After a restart the next host resumes at the last finished step. It closes the step
  that never finished with the outcome `interrupted`, which keeps its tokens, and runs that step
  again as a new step. A step's tokens are written to its row and to the request as soon as its
  call ends. A model call that was in flight is made again, and its tokens count again.
- **Fair use.** The agent host takes the tenants that have claimable requests in turn, as the worker shares its time,
  see [fair share](worker-scheduling.md#fair-share). Each turn claims one request of the tenant and runs one step of it. Every
  tenant has the same share, one step per turn, so one tenant's long request does not hold back another's. A tenant joins
  the turns at each poll and leaves when its claim finds nothing.

## Routes

An author sends, lists, reads, approves, changes, drops and accepts requests through the session routes under
`/api/studio/apps/{application}/authoring-requests`, with the CSRF header of the other authoring
routes. The shared answers are in [responses](authoring.md#responses). The accept route only asks for
an accept, which [accepting](#accepting) describes.

| Route                                                                     | What it does                                                                                            |
| ------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `POST /api/studio/apps/{application}/authoring-requests`                  | Stores a request in `planning` and answers `202` with it and a `Location` for the read route            |
| `GET /api/studio/apps/{application}/authoring-requests`                   | Lists the newest 100 requests of the application, newest first                                          |
| `GET /api/studio/apps/{application}/authoring-requests/{id}`              | Reads one request with its steps in order                                                               |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/approve`     | Moves a request from `awaitingApproval` to `working`                                                    |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/change-plan` | Moves a request from `awaitingApproval` back to `planning` and keeps the comment as a `planChange` step |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/drop`        | Moves a request to `dropped` from any state but `accepted` and `dropped`                                |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/accept`      | Stores an `accept` publish request for the draft of a `ready` request and answers `202` with it         |

- **Order of checks.** A send checks the session (`401`), the author role (`403`), in a cell the
  tenant's [`authoringRequests` quota](tenant-quotas.md#refusals) (`403`), the application (`404`),
  its manifest `id` (`409`), the content type (`415`) and the body (`400`). It then gets the agent
  host's [token](#acting-for-the-author): `409` for a `fapi2` tenant or a tenant that has no agent
  client, `401` for a grant the realm refuses or a session that keeps no platform token, `503` for a
  realm that does not answer, and `403` for an exchanged token without the author role. None of these
  writes a row or an audit record, and a bad body never calls the realm. Just before the insert it
  counts the quota again under a lock, so sends at the same time cannot pass it together. The other
  routes check the session, the author role and the application, then answer `404` for an `id` that is
  not a UUID of this application. Approve, change plan, drop and accept then answer `403` to anyone but
  the author of the request, and last answer `409` for a request in a state the action does not accept.
  Accept answers `409` for a request that is not `ready` or has no draft, and for a draft that already
  has an accept queued or running. A refused accept stores nothing. In a cell, an accept over the
  [`storage` quota](tenant-quotas.md#refusals) is refused with the quota's `403` before these checks,
  as a save is, because it writes the draft as a revision. A drop is not, so the author can still end
  the request.
- **Who.** Any author lists and reads the requests of an application. Only the author of a request
  approves its plan, changes it, drops it or accepts its draft.
- **Body.** A send takes `{ text, autoAccept? }` with a `text` of 1 to 4,000 characters. A change of
  plan takes `{ comment }` of 1 to 2,000 characters. The other routes take no body.
- **Answer.** Accept answers `202` with the queued publish request, in the shape of
  `GET /api/studio/apps/{application}/requests/{id}`, and a `Location` for that route. The other
  routes answer a request, which has its `id`, `application`, `author`, `authorName`, `text`, `state`, `plan`,
  `round`, `tokens`, `autoAccept`, `sentAt`, `approvedAt` and `finishedAt`. The read route adds `steps`,
  each with its `kind`, `summary`, `outcome`, `tokens`, `startedAt`, `finishedAt` and `checks`, which is
  the results of a `check` step or the review of a `review` step, and null on any other step.

## States

A request follows the same loop as a change to the platform: a plan the author approves, the change,
the checks, an independent review and fix rounds.

| State                           | What happens                                                                                                                            | Next                                  |
| ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- |
| `planning`                      | The agent reads the application through the tools and writes a plan in plain words: what it will change and which scenarios it will add | `awaitingApproval`                    |
| `awaitingApproval`              | The author reads the plan and approves it, or asks for another plan with a comment                                                      | `working`, or `planning` again        |
| `working`                       | The agent creates the draft and makes the change through the tools                                                                      | `checking`                            |
| `checking`                      | The compiler, the application's scenarios and the contract scenarios of its modules run on the draft                                    | `reviewing`, or `fixing` on a failure |
| `reviewing`                     | A second agent session, with no memory of the first, reviews the draft against the request and the plan                                 | `ready`, or `fixing` on findings      |
| `fixing`                        | The agent works on the failures and findings                                                                                            | `checking`                            |
| `ready`                         | The draft passed. The author accepts or drops it, or the agent accepts it, see [accepting](#accepting)                                  | `accepted` or `dropped`               |
| `accepted`                      | The draft is in the working folder as a revision                                                                                        | none                                  |
| `dropped`                       | The author dropped the request at any state before `accepted`, and its draft is deleted                                                 | none                                  |
| `failed`                        | A budget ran out, the token ended, or a step failed in a way a retry does not help. The draft is kept for the author                    | `dropped`                             |

- **Built-in modules first.** When a request matches a built-in module, the plan declares the module
  and writes a [customization layer](customization-layers.md) over it instead of new resources, so the
  application keeps the module's upgrades and its contract. It adds scenarios for what it adds.
- **Planning reads only.** The model is offered `read_reference`, `list_resources`, `read_resource`,
  `read_module_resource` and `read_names`, and only for the request's application. Any other call gets a
  tool error from the agent host and never reaches the MCP server. The plan is the text of the model's
  last answer, which ends its turn: an answer that stops at the output limit, or has no text, is no plan.
  The plan goes into the request's `plan` and the request moves to `awaitingApproval`.
- **Working writes only its draft.** The step makes the draft with `create_draft` and keeps its id in
  `draft_id` before the first model call, and a step that runs again uses the same draft. The model is offered
  the five read tools of planning plus `write_resource`, `delete_resource`, `write_layer`, `check_expression`,
  `validate_draft` and `diff_draft`, and only for the request's application and its draft. A call without a
  draft gets the request's draft. Any other call gets a tool error from the agent host and never reaches the
  MCP server. When the model ends its turn, the request moves to `checking`.
- **Checking runs every scenario.** The step makes no model call. It calls `validate_draft` on the request's
  draft. When no diagnostic is an `error`, it calls `run_scenarios` with no names, so the run covers the
  application's scenarios and every module scenario, contract scenarios included, and asks for the run's
  status until it ends. A warning is kept and does not fail the compile. The step stores `{ compile, scenarios }`
  in `checks` on its `check` step: the compile outcome with its diagnostics, and the scenario run with its
  request id, its diagnostics and its items, as `run_scenarios` returns them. When the compile fails, the
  scenarios are `notRun` and no run starts. When both passed, the request moves to `reviewing` with the
  outcome `passed`. Otherwise it moves to `fixing` with the outcome `failed`. A step whose run does not end in
  time, whose tool call is a tool error, whose request has no draft, or whose application the author can no longer
  list stops the request as `failed` with the outcome `unfinished`. A step that runs again starts a new run.
- **Reviewing reads only.** The step starts a new model conversation with no memory of the working session, on
  `Ai:ReviewBinding`, or else the request's binding or `Ai:DefaultBinding`. The model sees the request text, the
  approved plan and the latest check results. It is offered `read_reference`, `list_resources`, `read_resource`,
  `read_module_resource`, `read_names` and `diff_draft`, and only for the request's application and its draft. A
  call without a draft gets the request's draft. Any other call gets a tool error from the agent host and never
  reaches the MCP server. The verdict is the text of the model's last answer. When its first line is exactly
  `PASSED`, the request moves to `ready` with the outcome `passed`. Any other text moves it to `fixing` with the
  outcome `findings`, and the text is the findings. The step stores `{ review: { outcome, findings } }` in `checks`
  on its `review` step, with `findings` null when the review passed.
- **Fixing works on the failures.** The step uses the tools and the draft rules of working, on the draft that the
  working step stored, and never makes a draft. The model sees the request text, the approved plan, what failed
  in the latest `check` step and the findings of the latest `review` step after it. What failed is the compile
  diagnostics when the compile failed, and the scenario items whose outcome is not `passed`. When the model ends
  its turn, the request moves to `checking`.
- **A new plan.** After a change of plan, the next planning step sees the request text, the earlier plan
  and every comment of the request, oldest first. Its plan replaces the earlier one.
- **Plans need approval** _(planned for M35)_. No request leaves `awaitingApproval` without the author, also when the
  application allows auto-accept.

## Budgets

Each request has limits on its rounds, tokens and time, so the cost of one request is bounded.

- **Planning calls.** At most 25 model calls in one planning step. A step that has no plan by then stops
  the request as `failed` with the outcome `noPlan`, and so does a request whose application the author
  can no longer list.
- **Working calls.** At most 50 model calls in one working or fix step. A step whose model has not ended its turn
  by then, or whose answer stops at the output limit, stops the request as `failed` with the outcome
  `unfinished`, and so does a request whose application the author can no longer list. So does a working step
  whose draft cannot be made, and a fix step whose request has no draft.
- **Checking time.** At most 450 polls of the scenario run, 2 seconds apart, which is about 15 minutes. A
  run that has not ended by then stops the request as `failed` with the outcome `unfinished`, so a run that
  never starts does not hold the agent host for ever. The publisher stops a test run after 10 minutes.
- **Review calls.** At most 25 model calls in one review step. A step whose model has not ended its turn by
  then, whose answer stops at the output limit or has no text, stops the request as `failed` with the outcome
  `unfinished`, and so does a request without a draft or whose application the author can no longer list.
- **Fix rounds.** At most `Ai:FixRounds` rounds, 3 by default. Each move to `fixing` adds 1 to the request's
  `round`. A move to `fixing` when `round` already equals `Ai:FixRounds` stops the request as `failed` instead,
  and its draft is kept. The check or review step that failed keeps its own outcome.
- **Tokens.** At most `Ai:RequestTokenBudget` tokens, input and output together, across every model
  call of the request, review included, 2,000,000 by default. A call is not made when the tokens used so
  far, the last call's input in the step and the binding's `MaxOutputTokens` together would pass the
  budget. The request then stops as `failed` with the outcome `overBudget` and keeps its draft.
- **Time.** At most `Ai:RequestTimeout` from the plan's approval, 30 minutes by default. A request past
  it stops as `failed` with the outcome `timedOut` before its next step, and a step still running at that
  time is cancelled and stops the request the same way. The draft is kept.
- **Quota.** The request counts toward the tenant's `authoringRequests` quota once, when the author
  sends it. A request past the quota is refused with the quota's `403`, see
  [tenant quotas](tenant-quotas.md#refusals). A request that asks for a new plan or a new fix round
  does not count again. [Tenant quotas](tenant-quotas.md#quotas) says how the month is counted.

## Accepting

Accepting moves a draft's changes into the working folder as one revision. It never puts them live:
only [publishing](authoring.md#publishing) does.

- **Who.** The author of a `ready` request accepts its draft through the accept route, see
  [routes](#routes). The route stores an `accept` with the client `studio`, and the request stays
  `ready` until the accept succeeds.
- **In Studio** _(planned for M35)_. Studio gives the author an Accept button for the route.
- **By the agent.** The agent, or any MCP client of the author, asks through `accept_draft`, see
  [tools](mcp-server.md#tools). The tool stores an `accept` with the client's id only when all of these hold:
  - the application's auto-accept setting is on, see
    [AI authoring in Studio](ai-authoring-in-studio.md#auto-accept);
  - the request asked for it when it was sent;
  - its checks and its review passed in the last round, which means the newest `check` step and the newest
    `review` step have the outcome `passed` and the review comes after the check;
  - the caller holds the author role, which the MCP route checks and answers `403` without it, and is the
    author of the `ready` request that owns the draft.

  A refused call is a tool error that stores nothing. A call while an accept of the draft is queued or running
  answers that accept and stores no second one.
- **Through the publisher.** An accept is a [publish request](authoring.md#publishing) of the kind
  `accept`. The publisher runs the requests of one application one at a time, so two accepts never
  interleave. When an accept succeeds, the publisher moves the `ready` request of that draft to
  `accepted`, sets its `finishedAt`, erases its refresh token and writes an `authoringRequest.accepted`
  audit record, in the transaction that ends the publish request. A failed accept, such as a conflict,
  leaves the request `ready` with its token, and the author can accept again or drop the request.
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
- **Audit records.** Sending a request, approving a plan, dropping it and accepting its draft each
  write an audit record with the author, the request id and the application, see
  [audit actions](storage.md#audit-actions).
- **Usage.** Every model call, review included, adds its tokens to the tenant's
  [usage](tenant-quotas.md#usage) by binding and model, with a tenant's own key counted apart.

## What reaches the model

The model sees the application's configuration and the results of its checks, never the tenant's
data.

- **Sent:** the request text, the plan, the application's resources and its modules' resources,
  diagnostics, scenario results and the reference text of the tools.
- **Never sent:** records, files, record history, process instances, secrets, binding values, keys,
  tokens and the names of people. A scenario's data is made up for the scenario.
- **Checked.** `AgentPlanningModelInputTests` runs a planning step whose scripted model reads every
  resource of an application. The tenant holds a record, a `secret` binding and a `url` binding, and
  the author's names hold markers. The test fails when a message sent to the adapter holds one of them.
