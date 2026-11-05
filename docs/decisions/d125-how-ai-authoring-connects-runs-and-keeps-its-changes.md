# D125. How AI authoring connects, runs and keeps its changes

Status: **Agreed**

## Context

[D119](d119-ai-authoring-in-studio.md) lets an agent change an application through one MCP server,
on a draft that a person or an allowed agent accepts, with revisions that can be undone. That leaves
open where revisions and drafts are kept, how a client signs in, how a long request survives a
restart, how two accepts stay apart, and what bounds the cost of a request.

## Decision

- **Revisions and drafts live in git.** Each application folder has an internal git repository,
  outside the folder and never served. Each save and each accept is a commit, and a draft is a branch.
  In a cell the repository sits in the tenant's Studio folder, so the tenant's backup, export and
  deletion cover it. See [revisions and drafts](../reference/revisions-and-drafts.md).
- **The MCP server follows the MCP specification for a remote server:** Streamable HTTP on each
  tenant's host, and one pinned version of the specification. See
  [the MCP server](../reference/mcp-server.md).
- **The tenant's realm signs MCP clients in** with OAuth: the authorization code flow with PKCE, a
  resource indicator and tokens bound to the MCP server. Clients register by a metadata document, or
  dynamically as a fallback, and a person consents to each. A tenant administrator decides which
  outside clients may connect.
- **The agent host runs requests durably and acts for the author.** A request and each step of its
  loop are rows in the tenant database that the agent host claims with a lease, so a restart resumes
  at the last finished step. The server exchanges the author's session for a token of the agent
  host's client by OAuth token exchange. See [authoring requests](../reference/authoring-requests.md).
- **An accept is a publish request,** so the publisher runs the accepts of one application one at a
  time, checks the recorded versions, runs the compiler and the scenarios, and commits, all or
  nothing.
- **A budget bounds each request,** in tokens and in fix rounds, and a request counts once toward the
  tenant's AI quota. Outside clients bring their own model, so they are rate limited and count toward
  no quota.
- **Only a person with the publisher role turns on auto-accept** for an application, outside any
  resource, so no tool or draft can change it. See
  [AI authoring in Studio](../reference/ai-authoring-in-studio.md#auto-accept).
- **The installation names its model bindings,** and an authoring eval with a fixed pass bar decides
  which models are supported. See [model bindings](../reference/model-bindings.md).

## Consequences

- Git already does diffs, history, branches, reverts and all-or-nothing updates, and keeps
  [D5](d5-configuration-is-authored-as-files-first.md)'s files first.
- A developer connects from the tools they use, while consent and the administrator's choice keep the
  tenant in charge.
- The agent host holds exactly the author's grants, with no shortcut of its own.
- Accepts reuse the publisher's lease, queue and scenario runs instead of building their own.
- A request's cost is bounded, and counting tokens for each model lets plans be priced from real cost.
- The MCP server, its sign-in, its clients and the agent host are new trust boundaries for the
  [threat model](../reference/threat-model.md).
