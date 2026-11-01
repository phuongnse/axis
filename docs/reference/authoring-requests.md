# Authoring requests

An authoring request is a change that an author describes in words. The agent host plans it,
makes it on a draft, checks it, has it reviewed and fixes what fails, and the draft is accepted
or dropped. [D119](../decisions/d119-ai-authoring-in-studio.md) agrees the loop, and
[D125](../decisions/d125-how-ai-authoring-connects-runs-and-keeps-its-changes.md) the shapes below.
Everything here is _(planned for M35)_. The conventions of [architecture.md](../architecture.md)
apply.

## The agent host

- **A host of its own.** `axis-agent` runs beside the server, the worker and the publisher, from the
  same images, Helm chart and Compose file. It runs the same way in a cell and on premises, and
  needs no GitHub, no CI service and no particular cloud.
- **One client of the MCP server.** It calls the [MCP server](mcp-server.md) over HTTP, like any
  client, and has no other way into a tenant. It never opens a tenant database for an application's
  resources, and never reads a record.
- **Its own state.** It reads and writes the request tables below in each tenant database, with the
  tenant's runtime role, as the worker does.
- **Models.** It calls a model through an adapter, by the binding the request uses, see
  [model bindings](model-bindings.md).

## Acting for the author

- **Token exchange.** When an author sends a request, the server exchanges the author's session
  for a token of the agent host's client, by OAuth 2.0 token exchange (RFC 8693) at the tenant's
  realm. The token's subject is the author and its audience is the MCP server, so the agent host
  holds exactly the author's grants.
- **Kept with the request.** The refresh token is stored encrypted with the tenant's data key in the
  request row, and deleted when the request ends. It ends with the author's realm session, at most
  8 hours. A request that outlives it stops as `failed`, and the author sends it again.
- **No shortcut.** The agent host holds no role of its own on any tenant. A token exchange for a
  person who has lost the author role is refused.

## Durable requests

- **Tables.** `axis.authoring_requests` holds one row per request: its id, application, author,
  request text, binding, state, draft, plan, round, token use, the auto-accept flag and its times.
  `axis.authoring_request_steps` holds one row per step of the loop, in order: its kind, input
  summary, outcome, tokens and times. A step's full model messages are kept for 30 days for the
  author and the audit, then deleted.
- **Claimed with a lease.** The agent host claims a request whose state needs work, as the
  publisher claims publish requests: it sets `claimed_by` and `claimed_until`, renews the lease
  while it works, and lets it lapse when it stops. A host that dies leaves the request claimable
  after the lease.
- **Resuming.** Each finished step is committed before the next starts. After a restart, the next
  host resumes at the last finished step. A model call that was in flight is made again, and its
  tokens count again.
- **Fair use.** The agent host takes requests of the tenants of a cell in turn, as the worker shares
  its time, so one tenant's long request does not hold back another's.

## States

| State | What happens | Next |
| --- | --- | --- |
| `planning` | The agent reads the application through the tools and writes a plan in plain words: what it will change and which scenarios it will add | `awaitingApproval` |
| `awaitingApproval` | The author reads the plan, approves it, or asks for another plan with a comment | `working`, or `planning` again |
| `working` | The agent creates the draft and makes the change through the tools | `checking` |
| `checking` | The compiler, the application's scenarios and the contract scenarios of its modules run on the draft | `reviewing`, or `fixing` on a failure |
| `reviewing` | A second agent session, with no memory of the first, reviews the draft against the request and the plan | `ready`, or `fixing` on findings |
| `fixing` | The agent works on the failures and findings | `checking` |
| `ready` | The draft passed. The author accepts or drops it, or the agent accepts it, see [accepting](#accepting) | `accepted` or `dropped` |
| `accepted` | The draft is in the working folder as a revision | none |
| `dropped` | The author dropped the request at any state before `accepted`. Its draft is deleted | none |
| `failed` | A budget ran out, the token ended, or a step failed in a way a retry does not help. The draft is kept for the author | `dropped` |

- **Built-in modules first.** When a request matches a built-in module, the plan declares the module
  and writes a layer over it instead of new resources, and adds scenarios for what it adds, as D119
  agrees.
- **Plans need approval.** No request leaves `awaitingApproval` without the author, also when the
  application allows auto-accept.

## Budgets

- **Fix rounds.** At most `Ai:FixRounds` rounds, 3 by default. A request still failing after the
  last round stops as `failed`.
- **Tokens.** At most `Ai:RequestTokenBudget` tokens, input and output together, across every model
  call of the request, review included. 2,000,000 by default. A call that would pass the budget is
  not made, and the request stops as `failed`.
- **Time.** At most `Ai:RequestTimeout` from the plan's approval, 30 minutes by default.
- **Quota.** The request counts toward the tenant's `authoringRequests` quota once, when the author
  sends it. A request past the quota is refused with the quota's `403`, see
  [tenant quotas](tenant-quotas.md#refusals). A request that asks for a new plan or a new fix round
  does not count again.

## Accepting

Accepting moves a draft's changes into the working folder as one revision. It never puts them live:
only publishing does.

- **Who.** The author of the request accepts or drops the draft in Studio. The agent may accept it
  through `accept_draft` only when all of these hold:
  - the application's auto-accept setting is on, see
    [AI authoring in Studio](ai-authoring-in-studio.md#auto-accept);
  - the request asked for it when it was sent;
  - its checks and its review passed in the last round;
  - the author still holds the author role.
- **Through the publisher.** An accept is a [publish request](authoring.md#publishing) of the kind
  `accept`. The publisher runs the requests of one application one at a time, so two accepts never
  interleave.
- **All or nothing.** The publisher checks that every resource the draft changes still has the
  version the draft recorded, applies the draft to the working folder as it is now, runs the
  compiler and the scenarios on the result, and commits it as a revision, see
  [revisions and drafts](revisions-and-drafts.md#accepting-a-draft). On any failure nothing is
  written.
- **Conflicts.** A resource that changed since the draft recorded it is a conflict, and the accept
  answers it with the resources and their new versions.
  - The agent then reads the folder again, makes the change on a new draft and runs the checks
    again, within the request's budgets.
  - Studio shows the conflict to a person, who decides.

## Audit

- Each request keeps who asked, the client, the binding and model, the plan, the diff, each check,
  the review, the tokens and who accepted or dropped it.
- Sending a request, approving a plan, accepting and dropping each write an audit record with the
  author, the request id and the application.
- The tokens of each request and model join the tenant's [usage](tenant-quotas.md#usage).

## What reaches the model

- **Sent:** the request text, the plan, the application's resources and its modules' resources,
  diagnostics, scenario results and the reference text of the tools.
- **Never sent:** records, files, record history, process instances, secrets, binding values, keys,
  tokens and the names of people. A scenario's data is made up for the scenario.
