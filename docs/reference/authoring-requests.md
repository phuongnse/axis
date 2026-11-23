# Authoring requests

An authoring request is a change that an author describes in words. The agent host plans it, makes it
on a draft, checks it, has it reviewed and fixes what fails, and then the draft is accepted or
dropped. This page gives the agent host, how it acts for the author, how a request is stored and
moves through its states, its budgets, how a draft is accepted, and what reaches the model. How an
author works with a request in Studio is in [AI authoring in Studio](ai-authoring-in-studio.md), and
the reasons are in [D150](../decisions/d150-each-authors-own-ai-with-axis-counting-no-requests.md).

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
  claims [publish requests](authoring-publishing.md#lifecycle): it sets `claimed_by` and `claimed_until`, renews
  the lease while it works, and lets it lapse when it stops. A host that dies leaves the request
  claimable after the lease. The host claims only a state whose step it has, and a `ready` request only when it asked
  for auto-accept, has a draft and no `accept` step has finished since its newest `review` step. An `accept` step
  that was interrupted does not count, so it runs again. The claim leaves the application's auto-accept setting to
  `accept_draft`. Every write after the claim is fenced by `claimed_by`, which is new for each claim, so a host that
  lost its claim, or whose request was dropped, changes nothing and stops working on that request.
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
routes. The shared answers are in [responses](authoring-responses.md). The accept route only asks for
an accept, which [accepting](#accepting) describes.

| Route                                                                                             | What it does                                                                                               |
| ------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `POST /api/studio/apps/{application}/authoring-requests`                                          | Stores a request in `planning` and answers `202` with it and a `Location` for the read route               |
| `GET /api/studio/apps/{application}/authoring-requests`                                           | Lists the newest 100 requests of the application, newest first                                             |
| `GET /api/studio/apps/{application}/authoring-requests/{id}`                                      | Reads one request with its steps in order                                                                  |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/approve`                             | Moves a request from `awaitingApproval` to `working`                                                       |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/change-plan`                         | Moves a request from `awaitingApproval` back to `planning` and keeps the comment as a `planChange` step    |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/drop`                                | Moves a request to `dropped` from any state but `accepted` and `dropped`                                   |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/accept`                              | Stores an `accept` publish request for the draft of a `ready` request and answers `202` with it            |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/retry` _(planned for M35)_           | Moves a `ready` request whose accept met a conflict back to `working` with no draft                        |
| `POST /api/studio/apps/{application}/authoring-requests/{id}/request-changes`                     | Moves a request from `ready` back to `working` and keeps the comment as a `changeRequest` step             |
| `GET /api/studio/apps/{application}/authoring-requests/{id}/diff`                                 | Reads the changes of the request, as the [draft diff](authoring-revisions.md#draft-diff) shows them        |
| `GET /api/studio/ai` _(planned for M35)_                                                          | Tells the session's author whether their AI is connected, and the address of the MCP server                |
| `POST /api/studio/apps/{application}/drafts/{draft}/preview` _(planned for M35)_                  | Tries the draft on the preview tenant, see [the draft preview](#routes)                                    |

- **Order of checks.** A send checks the session (`401`), the author role (`403`), in a cell the
  tenant's [`authoringRequests` quota](tenant-quotas.md#refusals) (`403`), the application (`404`),
  its manifest `id` (`409`), the content type (`415`) and the body (`400`). It then gets the agent
  host's [token](#acting-for-the-author): `409` for a `fapi2` tenant or a tenant that has no agent
  client, `401` for a grant the realm refuses or a session that keeps no platform token, `503` for a
  realm that does not answer, and `403` for an exchanged token without the author role. None of these
  writes a row or an audit record, and a bad body never calls the realm. Just before the insert it
  counts the quota again under a lock, so sends at the same time cannot pass it together. The other
  routes check the session, the author role and the application, then answer `404` for an `id` that is
  not a UUID of this application. Approve, change plan, request changes, drop and accept then answer `403` to anyone but
  the author of the request, and last answer `409` for a request in a state the action does not accept.
  Accept answers `409` for a request that is not `ready` or has no draft, and for a draft that already
  has an accept queued or running. A refused accept stores nothing. Request changes answers `409` for a
  request that is not `ready` and for a draft that has an accept queued or running, and it stores nothing then. In a cell, an accept over the
  [`storage` quota](tenant-quotas.md#refusals) is refused with the quota's `403` before these checks,
  as a save is, because it writes the draft as a revision. A drop is not, so the author can still end
  the request.
- **Order of checks with an own key** _(planned for M35)_. A send checks the session (`401`), the author role
  (`403`), the tenant's AI authoring setting (`403` when it is off), the application (`404`), its manifest `id`
  (`409`), the content type (`415`) and the body (`400`). It then checks the author's own key (`409`) when the
  request's binding needs a key and the author has none. Only then does it get the agent host's
  [token](#acting-for-the-author). No quota is checked.
- **Who.** Any author lists and reads the requests of an application. Only the author of a request
  approves its plan, changes it, asks for other changes to its draft, drops it or accepts its draft.
- **Body.** A send takes `{ text, autoAccept? }` with a `text` of 1 to 4,000 characters. A change of
  plan or a request for changes takes `{ comment }` of 1 to 2,000 characters. The other routes take no body.
- **Answer.** Accept answers `202` with the queued publish request, in the shape of
  `GET /api/studio/apps/{application}/requests/{id}`, and a `Location` for that route. The other
  routes answer a request, which has its `id`, `application`, `author`, `authorName`, `text`, `title`, `state`, `plan`,
  `round`, `tokens`, `autoAccept`, `sentAt`, `approvedAt` and `finishedAt`. `title` is at most 80 characters and from the plan, and `plan` is
  [the plan's shape](#states), an object. Both are null until AI has proposed a plan. The read route adds `steps`,
  each with its `kind`, `summary`, `outcome`, `tokens`, `startedAt`, `finishedAt` and `checks`, which is
  the results of a `check` step or the review of a `review` step, and null on any other step.
- **Paging the list** _(planned for M35)_. The list route takes the record API's `page`, `pageSize`, `sort`, `search`
  and `filter`, as [paging and sorting](record-api.md#paging-and-sorting) defines them. The columns are `title` and
  `author` (the author's name), which are text, `sentAt`, which is a date, and `state`, an enum column whose values are the state names. `state` sorts in the order a request goes through its states. The order is newest
  first unless `sort` says otherwise, and `search` keeps the requests whose `title` or `text` contains the text. It
  answers `{ items, page, pageSize, totalCount }` through the [shared list query](shared-widgets.md#which-widget),
  and a parameter that is invalid is `400`, as [the revision list](authoring-revisions.md#list) answers. It replaces
  the newest 100.
- **Requesting changes.** Only the author of the request (`403` to anyone else) takes `{ comment }`
  of 1 to 2,000 characters. The route answers `409` unless the request is `ready`, and while the draft has an
  accept queued or running, so an accept cannot commit a draft that AI is changing. It keeps the draft, sets `round`
  back to 0, clears the agent host's claim, as a drop does, and moves the request to `working`. It answers the request
  as it now is. `Ai:RequestTimeout` counts from the `changeRequest` step, and the
  token budget goes on. Without a restart, a request that used up its rounds could never be changed, see
  [D150](../decisions/d150-each-authors-own-ai-with-axis-counting-no-requests.md).
- **Retrying a conflict as the author** _(planned for M35)_. Only the author of the request (`403` to anyone else)
  retries it. The route answers `409` unless the request is `ready` and its latest accept failed with `AXC0377`. It
  clears the draft, sets `round` back to 0 and moves the request to `working`. `Ai:RequestTimeout` counts from the
  retry, and the token budget goes on, as requesting changes does above. Without that restart, the time limit of a
  request that waited a long time for its author would stop it as `timedOut` before its next step. The agent's own
  retry keeps its round and its time limit, see [retrying a conflict](#accepting). It answers the request as it now is.
- **The request diff.** Any author reads the changes of the request. A request with a draft that is not
  `accepted`, such as a `ready` or `failed` one, answers the changes of its draft, in the shape and the pages of the
  [draft diff](authoring-revisions.md#draft-diff), read as its author's draft, with `module` as below. A cursor the draft did not give
  answers `400` and a write lock that cannot be taken `503`, as the draft diff does. Once `accepted`, it answers the
  changes of the revision its accept made, as the [revision diff](authoring-revisions.md#diff) gives them, with `titles`,
  in one page as `{ start, tip, resources, cursor }`. `start` is the parent of that revision, `tip` is the revision
  and `cursor` is null, and a cursor answers `400`. A request with neither a draft nor an accept answers `404` with
  the title "The request has no draft and no accept."
  A layer file's entry, one with a `target`, also has `module`: `{ id, kind, name, file, before, after, marks, removed }`.
  `before` and `after` are the module resource's JSON text with the layer applied at `start` and at `tip`. Either is
  null where the target names no module resource. `marks` and `removed` are those of `after`, as the
  [module resource read](customization-layers.md#in-studio) answers them. An entry that is not a layer file has no `module`.
- **AI status** _(planned for M35)_. `GET /api/studio/ai` takes the session and the author role and answers
  `{ aiConnected, mcp }`. `aiConnected` is true when the tenant's AI authoring setting is on and the person has
  stored a key, or when `Ai:DefaultBinding` needs none, see
  [a person's own key](model-bindings.md#a-persons-own-key). `mcp` is `https://<host>/mcp`.
- **Draft preview** _(planned for M35)_. The route follows the rules of [preview](authoring-publishing.md#preview) and takes its
  snapshot from the draft's tip. It answers `404` for a draft that is not the person's, and `409` when the tenant
  has no preview tenant.
- **New answer fields** _(planned for M35)_. A request answers `app` as `{ client, name }` (`studio` with a null name
  for a request sent in Studio), `coAuthors` (the apps that wrote to the draft, in the shape of `app`, in order of first
  write), `aiConnected` (the AI status rule, for the request's author, so the page can say that no review will run) and
  `draft` (the draft's id, or null). Each step answers `resource`, `{ kind, id, name }` on a `write` step and null on the
  others.
- **Approving an outside app's request** _(planned for M35)_. The approve route also gets the agent host's
  [token](#acting-for-the-author), as a send does and with the same refusals, so the agent host can run the checks
  and the review for the author. The agent host runs no plan or change step of such a request.

## States

A request follows the same loop as a change to the platform: a plan the author approves, the change,
the checks, an independent review and fix rounds.

| State              | What happens                                                                                                                                      | Next                                                        |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| `planning`         | The agent reads the application through the tools and proposes a plan in [the plan's shape](#states): what it will change and which scenarios it will add | `awaitingApproval`                                          |
| `awaitingApproval` | The author reads the plan and approves it, or asks for another plan with a comment                                                                | `working`, or `planning` again                              |
| `working`          | The agent creates the draft and makes the change through the tools                                                                                | `checking`                                                  |
| `checking`         | The compiler, the tests of each package the draft changed, the application's scenarios and the contract scenarios of its modules run on the draft | `reviewing`, or `fixing` on a failure                       |
| `reviewing`        | A second agent session, with no memory of the first, reviews the draft against the request and the plan                                           | `ready`, or `fixing` on findings                            |
| `fixing`           | The agent works on the failures and findings                                                                                                      | `checking`                                                  |
| `ready`            | The draft passed. The author accepts or drops it, or the agent accepts it, see [accepting](#accepting)                                            | `accepted`, `dropped`, `working` on a conflict, or `failed` |
| `accepted`         | The draft is in the working folder as a revision                                                                                                  | none                                                        |
| `dropped`          | The author dropped the request at any state before `accepted`, and its draft is deleted                                                           | none                                                        |
| `failed`           | A budget ran out, the token ended, or a step failed in a way a retry does not help. The draft is kept for the author                              | `dropped`                                                   |

- **Built-in modules first.** When a request matches a built-in module, the plan declares the module
  and writes a [customization layer](customization-layers.md) over it instead of new resources, so the
  application keeps the module's upgrades and its contract. It adds scenarios for what it adds.
- **Planning reads only.** The model is offered `read_reference`, `list_resources`, `read_resource`,
  `read_module_resource`, `read_names`, `list_package_files` and `read_package_file`, and only for the request's application. Any other call gets a
  tool error from the agent host and never reaches the MCP server. The model is also offered `propose_plan`, which the agent
  host answers itself with the input of [the tool](mcp-server.md#tools), so the call never reaches the MCP server. A valid call stores
  the plan in the request's `plan` and its title in `title`, ends the step and moves the request to `awaitingApproval`. An invalid call
  gets a tool error that says what is wrong, and the model may call again. A turn that ends, or is cut off at the output limit,
  without a valid call has no plan.
- **Working writes only its draft.** The step makes the draft with `create_draft` and keeps its id in
  `draft_id` before the first model call, and a step that runs again uses the same draft. The model is offered
  the seven read tools of planning plus `write_resource`, `delete_resource`, `write_layer`, `check_expression`,
  `validate_draft`, `diff_draft`, `write_package_file`, `delete_package_file` and `test_package`, and only for the request's application and its draft. A call without a
  draft gets the request's draft. Any other call gets a tool error from the agent host and never reaches the
  MCP server. When the model ends its turn, the request moves to `checking`.
- **Packages.** Planning is also offered `list_package_files` and `read_package_file`, and any write tool it
  calls, the package ones included, gets a tool error from the agent host and never reaches the MCP server.
  Working and fixing are also offered `write_package_file`, `delete_package_file` and `test_package`, always
  on the request's own draft. When the draft changes a file under `packages/<id>/`, checking calls
  `test_package` after the compile, stores the result in `checks` as `packages` and moves the request to
  `fixing` on a failure. The author who sends the request needs the developer role to write a package,
  because the tools act for the author, see [roles](user-administration.md#roles). The agent host adds no
  check of its own: the MCP server refuses each package write and the model gets the refusal as a tool
  error. A request that writes no package needs no developer role.
- **Checking runs every scenario.** The step makes no model call. It calls `validate_draft` on the request's
  draft. When no diagnostic is an `error`, it calls `run_scenarios` with no names, so the run covers the
  application's scenarios and every module scenario, contract scenarios included, and asks for the run's
  status until it ends. A warning is kept and does not fail the compile. Before the scenarios, the step
  tests the packages. It reads every page of `diff_draft` for the files under `packages/<id>/`, keeps the
  packages that still have files in the draft, according to `list_package_files`, and calls `test_package`
  for each in turn, asking for its status until it ends. A package whose files were all deleted has nothing
  to test. The step stores `{ compile, packages, scenarios }` in `checks` on its `check` step: the compile
  outcome with its diagnostics, the package tests, and the scenario run with its request id, its diagnostics
  and its items, as `run_scenarios` returns them. `packages` is `{ outcome, items }`, and each item is the
  `package` with its `request`, `status`, `diagnostics` and `tests`, as `test_package` returns them. Its
  outcome is `notRun` when the compile failed or no package changed, and otherwise `passed` or `failed`. When
  the compile fails, the packages and the scenarios are `notRun` and no run starts. When the compile, the
  package tests and the scenarios passed, the request moves to `reviewing` with the outcome `passed`.
  Otherwise it moves to `fixing` with the outcome `failed`, also when a package test failed and every
  scenario passed. A step whose run or package test does not end in
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
- **The review's model** _(planned for M35)_. The review runs on the request's binding, with its author's key. It
  has no review binding of its own.
- **Fixing works on the failures.** The step uses the tools and the draft rules of working, on the draft that the
  working step stored, and never makes a draft. The model sees the request text, the approved plan, what failed
  in the latest `check` step and the findings of the latest `review` step after it. What failed is the compile
  diagnostics when the compile failed, the package tests that did not pass, with their diagnostics and the tests whose
  outcome is not `passed`, and the scenario items whose outcome is not `passed`. When the model ends
  its turn, the request moves to `checking`.
- **Accepting by the agent.** The step makes no model call. It claims a `ready` request that asked for auto-accept,
  once after each review, and calls `accept_draft` on the request's draft. It asks for the accept's status until it
  ends. The step ends with one of four outcomes. `accepted` is a success, and the request is `accepted`. `refused` is a
  refusal of `accept_draft`, such as the application's auto-accept being off: the request stays `ready`, the agent
  does not ask again, and the author can still accept or drop it. `conflict` is a failure with the `AXC0377`
  diagnostic: the request moves to `working` and its `draft_id` is cleared, so the working step makes a new draft
  from the folder as it is now and the checks and the review run again, see [retrying a conflict](#accepting). Any
  other failure, such as a draft that no longer compiles on the folder, a tool error, an application the author can no longer
  list or an accept that does not end in time, stops the request as `failed` with the outcome `notAccepted` and keeps
  its draft. The step stores no `checks`, and the accept's own diagnostics stay on its publish request.
- **A new plan.** After a change of plan, the next planning step sees the request text, the earlier plan
  and every comment of the request, oldest first. Its plan replaces the earlier one.
- **Plans need approval**. No request leaves `awaitingApproval` without the author, also when the
  application allows auto-accept.
- **Key failures** _(planned for M35)_. When the provider refuses the author's key with `401` or `403`, the
  request stops as `failed` with the outcome `keyRefused`. When the key is removed, or no longer opens, the
  request stops as `failed` with the outcome `noKey`. The draft is kept in both cases.
- **Disabled authors** _(planned for M35)_. Disabling a user stops each of their requests that is not `accepted`,
  `dropped` or `failed` as `failed`, with the outcome `authorDisabled`, and erases its refresh token. A stopped
  request sends no more model calls with the author's key.
- **The frame** _(planned for M35)_. Every request, from Studio or from an outside app, follows the same six
  steps: Plan (`planning`), Approval (`awaitingApproval`), Change (`working` and `fixing`), Checks (`checking`),
  Review (`reviewing`) and Ready (`ready`). `accepted`, `dropped` or `failed` ends it.
- **Requesting changes.** A `ready` request goes back to `working` when its author requests
  changes, see [routes](#routes). The next working step works in the same draft and sees every
  `changeRequest` comment, oldest first.
- **Requesting changes and the review** _(planned for M35)_. The next working step also sees the open findings of the review.
- **The plan's shape**. The plan is an object, and `title` is stored beside it:

  ```
  { summary, questions: [{ question, options, recommended }], changes: [text], decisions: [{ decision, reason }],
    risks: [text], checks: [text], checklist: [{ item, how }], details: [text] }
  ```

  `summary` and `changes` are required, and `changes` has at least one text. `title` is not blank and at most 80
  characters. A question's `recommended` is the number of one of its `options` counting from 1, or 0 for none. A property that
  the shape does not name is refused. `checklist` has one entry for each item of Axis's checklist, in its order and with the
  item word for word. A group that the call leaves out is stored as `[]`, so a stored plan has all eight groups.
  The whole plan is at most 64 KiB. The planning step ends by calling `propose_plan` for its own request, see
  [tools](mcp-server.md#tools). A turn that ends without a valid call has no plan (`noPlan`). The author answers
  `questions` through a change of plan.
- **No approval while the plan has questions** _(planned for M35)_. The page offers no approval while the plan has questions.

- **Axis's checklist**. The plan answers it, and the planning step asks for these four items:
  1. The draft does what the request and the approved plan say, and nothing more.
  2. A change that a built-in module covers uses it, with a customization layer.
  3. Each behaviour that the draft adds or changes has a scenario.
  4. Every text the draft adds has each locale the application offers.
- **The review checks the checklist** _(planned for M35)_. The review checks the draft against each item of Axis's checklist.
- **The review's shape** _(planned for M35)_. The review model's last answer is
  `{ outcome, summary, findings: [{ severity, text, file, path }], criteria: [{ check, outcome, note }], checklist: [{ item, outcome, note }], open }`,
  and the review step stores it as `checks.review`. `summary` is a sentence or two. `severity` is `blocking` or
  `suggestion`. `path` is the place in `file` that the finding is about. `criteria` has one entry for each entry of the
  plan's `checks`. A criterion's or a checklist item's `outcome` is `met`, `notMet` or `skipped`, where `skipped` means
  the review could not check it. `note` is the reviewer's short reason for an outcome that is `notMet` or `skipped`, and
  null otherwise. The review's `outcome` is `passed` when no finding is blocking, and a suggestion never causes a fix round. `open`
  holds the findings still open at `ready`: the suggestions, and the blocking findings when the fix rounds ran out,
  which moves the request to `ready` instead of `failed`. An answer that does not fit the shape is `unfinished`.
  The review runs with Axis's instructions and checklist, on the read-only tools of
  [reviewing reads only](#states), for every request.
- **Outside apps** _(planned for M35)_. A request that an outside MCP client proposes follows the frame, and the
  app makes the change, as [D150](../decisions/d150-each-authors-own-ai-with-axis-counting-no-requests.md) gives the
  app's own subscription as the reason.
  - **Plan.** The app calls `propose_plan`. The request is stored in `awaitingApproval`, with `app` set to the client
    and `autoAccept` as the call asked.
  - **Approval.** The author approves on the request's page, which also gets the agent host's token, see
    [routes](#routes).
  - **Change.** The app writes through the write tools on the request. Its Change ends when a `validate_draft` that
    it calls has no `error` diagnostic, or a `run_scenarios` run that it calls ends `passed`, on the request's draft
    with at least one change. That call stores the `work` step, or the `fix` step in `fixing`, with no tokens and
    moves the request to `checking`. `accept_draft` never ends a Change.
  - **Checks.** The agent host runs the `check` step as for any request.
  - **Review.** The agent host runs the `review` step as for any request, as a second AI on the author's own key,
    with Axis's instructions, its checklist and the read-only tools. The app never reviews its own change. When the
    author has not connected their AI, the step makes no model call, stores the outcome `notReviewed` and moves the
    request to `ready`. The page says that no review ran, and only the author accepts the request on its page.
  - **Fixing.** A failed check or a review with findings moves the request to `fixing` and adds 1 to `round`. The
    app reads the results through `read_request` and fixes.
  - **Claims.** The agent host claims no `planning`, `working` or `fixing` state of an outside app's request.
  - **Budgets.** The fix rounds and `Ai:RequestTimeout` apply. The token budget applies, and `tokens` count only the
    review's model calls on the author's key, because Axis cannot count the calls of the app's own model.

### Steps

_(planned for M35)_

Each step of a request has a kind that belongs to one step of [the frame](#states).

| Kind            | Frame step | Outcomes                                          | `checks`                           |
| --------------- | ---------- | ------------------------------------------------- | ---------------------------------- |
| `plan`          | Plan       | `noPlan`                                          | null                               |
| `planChange`    | Approval   | none, the comment is in `input_summary`           | null                               |
| `work`          | Change     | `unfinished`                                      | null                               |
| `write`         | Change     | `saved`, `refused`                                | null                               |
| `check`         | Checks     | `passed`, `failed`, `unfinished`                  | `{ compile, packages, scenarios }` |
| `review`        | Review     | `passed`, `findings`, `notReviewed`, `unfinished` | `{ review }`                       |
| `fix`           | Change     | `unfinished`                                      | null                               |
| `changeRequest` | Ready      | none, the comment is in `input_summary`           | null                               |
| `accept`        | Ready      | `accepted`, `refused`, `conflict`, `notAccepted`  | null                               |

Each write tool call of an outside app stores a `write` step, with its `resource` as `{ kind, id, name }`.

Any step may also end `interrupted`, `tokenEnded`, `overBudget`, `timedOut`, `keyRefused`, `noKey` or
`authorDisabled`.

## Budgets

Each request has limits on its rounds, tokens and time, so the cost of one request is bounded.

- **Planning calls.** At most 25 model calls in one planning step. A step that has no valid `propose_plan` call by then stops
  the request as `failed` with the outcome `noPlan`, and so does a request whose application the author
  can no longer list.
- **Working calls.** At most 50 model calls in one working or fix step. A step whose model has not ended its turn
  by then, or whose answer stops at the output limit, stops the request as `failed` with the outcome
  `unfinished`, and so does a request whose application the author can no longer list. So does a working step
  whose draft cannot be made, and a fix step whose request has no draft.
- **Checking time.** At most 450 polls of the scenario run, and of each package test, 2 seconds apart, which is
  about 15 minutes for each run. A run that has not ended by then stops the request as `failed` with the outcome `unfinished`, so a run that
  never starts does not hold the agent host for ever. The publisher stops a test run after 10 minutes.
- **Accepting time.** At most 450 polls of the accept, 2 seconds apart, the same as checking time, because an
  accept runs a validate and the application's test scenarios under the same publisher limit. An accept that has not
  ended by then stops the request as `failed` with the outcome `notAccepted`.
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
- **Time.** At most `Ai:RequestTimeout` from the plan's approval or from the newest `changeRequest` step, whichever is
  later, 30 minutes by default. A request past
  it stops as `failed` with the outcome `timedOut` before its next step, and a step still running at that
  time is cancelled and stops the request the same way. The draft is kept.
- **Quota** _(planned for M35)_. A request counts toward no quota, because the author's own key pays for it,
  see [a person's own key](model-bindings.md#a-persons-own-key).

## Accepting

Accepting moves a draft's changes into the working folder as one revision. It never puts them live:
only [publishing](authoring-publishing.md#publishing) does.

- **Who.** The author of a `ready` request accepts its draft through the accept route, see
  [routes](#routes). The route stores an `accept` with the client `studio`, and the request stays
  `ready` until the accept succeeds.
- **In Studio**. Studio gives the author an Accept button for the route.
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

- **Open blocking findings** _(planned for M35)_. `accept_draft` refuses when the `open` findings of the newest
  review hold a blocking finding, see [the review's shape](#states), and sends the person to the request's page as
  the gate below says. The author's accept route does not refuse, so the author stays the gate.
- **The gate** _(planned for M35)_. Two refusals send the person to the request's page: the auto-accept rules above
  do not hold, and the newest review has an open blocking finding. `accept_draft` then answers with the page's
  address as a URL elicitation, see [tools](mcp-server.md#tools), and the agent host's accept step records that as
  `refused`. Every other refusal stays a tool error that stores nothing, such as a caller who is not the author of
  the request, or a request that is not `ready`.
- **Outside apps** _(planned for M35)_. `accept_draft` from an outside app works under the rules of a Studio request:
  the application allows auto-accept, the request asked for it when its plan was proposed, and the newest check and
  review passed. A `notReviewed` review never passes. The agent host's accept step claims an outside app's `ready`
  request as it claims any other.
- **Through the publisher.** An accept is a [publish request](authoring-publishing.md#publishing) of the kind
  `accept`. The publisher runs the requests of one application one at a time, so two accepts never
  interleave. When an accept succeeds, the publisher moves the `ready` request of that draft to
  `accepted`, sets its `finishedAt`, erases its refresh token and writes an `authoringRequest.accepted`
  audit record, in the transaction that ends the publish request. It keeps the claim of the agent host, so the
  host's last step still commits, and no step moves an `accepted` request. A failed accept of the author, such as a
  conflict, leaves the request `ready` with its token, and the author can accept again or drop the request. The
  agent's own failed accept ends as [accepting by the agent](#states) says.
- **All or nothing.** The publisher checks that every resource and package file the draft read or changed still has the
  version the draft recorded, applies the draft to the working folder as it is now, runs a validate with its
  contract scenarios and the application's test scenarios on the result, and commits it as a revision, see
  [revisions and drafts](revisions-and-drafts.md#accepting-a-draft). So two changes that pass alone
  cannot break the application together, and on any failure nothing is written.
- **Conflicts.** A resource or a package file that changed since the draft recorded it is a conflict, and the
  accept answers it with one `AXC0377` diagnostic for each, naming the resource, its file and its
  new version, or the package file's path and its new version.
- **Who changed it.** Each `AXC0377` diagnostic of an accept also carries `changedBy`, the author name of the newest
  revision on `main` since the draft's start that changed the resource or the package file, and `changedAt`, that
  revision's time as ISO 8601. The name is the one the revision stored, and a change made outside Studio is
  named `Axis`. Both are null when no such revision is found, for example for a resource moved to a third path.
  `GET .../requests/{id}` answers both on the accept's publish request, and no other diagnostic carries them.
- **Retrying a conflict.** When the agent's accept meets a conflict, the request goes back to `working` with no
  draft. The agent reads the folder again, makes the change on a new draft and runs the checks and the review
  again, within the request's budgets: the request keeps its `round`, tokens and time limit. The conflicted draft
  stays in the revisions repository, because no tool deletes a draft. A folder that keeps changing can conflict
  again in every round, and the budgets bound it. Studio shows a conflict to a person, who decides, see
  [AI authoring in Studio](ai-authoring-in-studio.md).

## Audit

A request keeps the whole story of the change, and its milestones write audit records.

- **The request.** Each request keeps who asked, the client, the binding and model, the plan, the diff,
  each check, the review, the tokens and who accepted or dropped it.
- **Audit records.** Sending a request, approving a plan, dropping it and accepting its draft each
  write an audit record with the author, the request id and the application, see
  [audit actions](audit-records-and-sequence-counters.md#audit-actions).
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
