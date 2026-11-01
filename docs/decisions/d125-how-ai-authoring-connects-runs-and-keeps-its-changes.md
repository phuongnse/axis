# D125. How AI authoring connects, runs and keeps its changes

Status: **Agreed**

The owner agreed this on 2026-11-01. It sets the shapes that
[D119](d119-ai-authoring-in-studio.md) left open, so that M35 can be broken into issues. It adds to
D119 and changes none of its points. Everything here is _(planned for M35)_. The detail is in
[the MCP server](../reference/mcp-server.md), [authoring requests](../reference/authoring-requests.md),
[revisions and drafts](../reference/revisions-and-drafts.md),
[model bindings and the authoring eval](../reference/model-bindings.md) and
[AI authoring in Studio](../reference/ai-authoring-in-studio.md).

- **Revisions and drafts live in git:**
  - Each application folder has an internal git repository that the server keeps with
    LibGit2Sharp, outside the folder, and never serves.
  - Each Studio save and each accept is a commit that names who made it and through which request.
  - A draft is a branch. Undo is a commit that reverts a revision.
  - In a cell the repository sits in the tenant's Studio folder, so the backup, the export and the
    deletion of [D123](d123-how-an-axis-saas-cell-keeps-tenants-apart-measured-and-recoverable.md)
    cover it.
- **The MCP server:**
  - It speaks Streamable HTTP at `/mcp` on each tenant's host, so the host names the tenant as it
    does for every route.
  - Axis pins one version of the MCP specification, written in one place, the MCP server reference.
  - Its tools read the working folder or a draft, write only drafts, validate, run scenarios and
    accept under D119's rules. No tool publishes, reads records or reaches the network.
- **Sign-in for MCP clients:**
  - The tenant's realm is the authorization server: the authorization code flow with PKCE (`S256`),
    a resource indicator for the MCP server's URL, and tokens whose audience is that URL.
  - Access tokens live 5 minutes. Refresh tokens rotate and end with the realm session, at most
    8 hours.
  - Clients register as the specification prefers, by a Client ID Metadata Document, with dynamic
    client registration as the fallback.
  - The person sees a consent screen that names the client and what it may do.
  - A tenant administrator chooses whether outside clients may connect: all, an allow list, or none.
    They can see and revoke each client's consent.
- **The agent host runs requests durably:**
  - An authoring request and each step of its loop are rows in the tenant database. The agent host
    claims requests as the publisher claims publish requests, with a lease, so a restart resumes a
    request at its last finished step.
  - The agent host acts for the person who sent the request: when the request is sent, the server
    exchanges the person's session for a token of the agent host's client with the MCP server as
    its audience, by OAuth token exchange. It keeps that token encrypted with the request until the
    request ends.
  - A request moves through `planning`, `awaitingApproval`, `working`, `checking`, `reviewing`,
    `fixing`, `ready`, then `accepted` or `dropped`. It may stop as `failed`.
- **Accepts go through the publisher:**
  - An accept is a publish request of the kind `accept`, so accepts of one application run one at a
    time in the publisher's queue.
  - The publisher checks the recorded versions, applies the draft, runs the compiler and the
    scenarios on the result, and commits it, all or nothing.
- **Budgets and the AI quota:**
  - A request counts toward the tenant's `authoringRequests` quota when the author sends it.
  - Each request has a token budget and at most 3 fix rounds by default. Past either, it stops as
    `failed` and keeps its draft for the author.
  - Usage records the requests and the tokens of each model.
  - Tool calls from outside clients are rate limited in a partition of their own, and count toward
    no quota, because they bring their own model.
- **Auto-accept is an application setting:** only a person with the publisher role turns it on, in
  Studio. It is off by default, and each change writes an audit record. A tool cannot change it.
- **Model bindings:**
  - The installation names its bindings in `Ai:Bindings`, each of the kind `anthropic` or
    `openai-compatible`, with its endpoint, model and key file.
  - `Ai:DefaultBinding` names the one requests use, and `Ai:ReviewBinding` may name another for the
    review.
  - A tenant may bring its own key, kept as a tenant secret, only where `Ai:AllowTenantKeys` is on.
- **The authoring eval:**
  - Its cases live in `tests/ai-eval/`. Each holds a starting application, a request and the
    expected outcome: scenarios that must pass, or a refusal by the contract.
  - `scripts/ai-eval.sh --binding <name>` runs them through the MCP server.
  - A model is supported when it passes every refusal case and at least 90% of the others.
- **Audit:**
  - Each request keeps who asked, the client, the binding and model, the plan, the diff, the
    checks, the review, the tokens and who accepted it.
  - Consent, revocation, the outside client switch, auto-accept and each accept write audit
    records.
- **What never reaches a model:** records, files, record history, secrets, binding values, keys,
  tokens and the names of people. The application's resources, diagnostics, scenario results and
  the request do.
- **New trust boundaries:** the MCP server, its sign-in, its clients and the agent host. The M35
  issue that models them adds their rows to the [threat model](../reference/threat-model.md), as
  [D106](d106-a-threat-model-kept-current-like-the-asvs-tracking.md) agrees.

_Why:_ git is the standard store for versioned files. It already does diffs, history, branches,
reverts and all-or-nothing updates, and it keeps
[D5](d5-configuration-is-authored-as-files-first.md)'s files first. Streamable HTTP and OAuth with
PKCE, resource indicators and audience-bound tokens are what the MCP specification asks of a remote
server. Metadata documents and dynamic registration let a developer connect from the tools they use,
while consent and the administrator's switch keep the tenant in charge. Token exchange lets the agent
host act with exactly the person's grants, with no shortcut of its own, as D119 asks. Claimed rows
with a lease are how the publisher already survives restarts. The publisher's queue already runs one
request at a time per application and already runs scenarios, so accepts gain both for free. A
budget and a cap on fix rounds bound the cost of one request, and counting tokens lets M34 price
plans from real cost. A setting that only a publisher may change keeps an agent from giving itself
the right to accept.
