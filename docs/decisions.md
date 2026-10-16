# Decisions

Each decision has a status. **Agreed** means the owner has accepted it and
work may rely on it. **Proposed** means it is a working default that can still
change. Add new decisions at the end, and change an existing decision only by
recording a newer one that replaces it.

## D1. Applications are configuration — Agreed

Business-specific models, queries, rules, processes, UI, policies and text are
resources in an application's configuration. Platform code contains nothing
specific to any application.

*Why:* this is the product. It is also the only way many applications can
share upgrades, security fixes and engine guarantees.

## D2. Axis owns its engines — Agreed

Axis implements its own configuration compiler, expression engine, data engine,
process engine, presentation runtime and policy engine. Third-party libraries
are fine for infrastructure: HTTP, database access, serialization, UI
components, standard security protocols and telemetry. No third-party workflow
or rule engine.

*Why:* versioning, durability, authorization and inspection have to work the
same way across every engine. An external engine would set its own semantics
for all four.

## D3. Technology stack — Agreed

- **Backend:** C# on .NET 10 (LTS) with ASP.NET Core.
- **Frontend:** React and TypeScript, Ant Design and ProComponents.
- **Storage:** PostgreSQL. EF Core manages the fixed platform tables. A
  controlled Npgsql layer manages the tables generated for configured
  entities.
- **Tests:** xUnit, Testcontainers (real PostgreSQL) and Playwright.

## D4. Modular monolith — Agreed

Axis is a single codebase with explicit module boundaries.

- **M1–M2:** one ASP.NET Core host serves the API, the BFF and the SPA.
- **M3:** a separate worker host is added. It runs the same modules and
  executes background work.
- **Never needed:** a message broker or microservices.

*Why:* a small team gets the most speed from a monolith. Module boundaries
keep the option to split hosts later.

## D5. Configuration is authored as files first — Agreed

- **Format:** resources are JSON files, one resource per file. Each file has a
  stable ID and a human-readable name, and is validated against a published
  JSON Schema.
- **Where they live:** an application folder in git. Axis compiles that folder
  into a release.
- **Authoring APIs:** the APIs use the same contracts and support partial
  updates.
- **Studio:** the visual Studio comes later and writes the same resources.

*Why:* files can be reviewed, diffed and tested in CI, and AI agents can author
them. Building a visual editor before the contracts settle would mean
rewriting it.

## D6. In-configuration logic uses a typed expression language — Agreed

Validation, computed fields, conditions, routing and policy filters use a small,
typed, side-effect-free expression language that Axis interprets.

- **Type checking:** expressions are checked when the release is compiled.
- **Cost:** evaluation has bounded cost.
- **Reuse:** named rules let an application reuse an expression.
- **No inline code:** no script, SQL or general-purpose code can be embedded
  in configuration.

*Why:* see [knowledge](domain/knowledge.md#logic-in-configuration).

## D7. Custom code lives in extension packages — Agreed

Logic that does not fit expressions is written in C# as an extension package,
for example file parsing, complex calculations or system integrations.

- **Repository and build:** a package is a separate project with its own unit
  tests, a version and a build artifact.
- **Typed operations:** each operation in a package declares its inputs and
  outputs.
- **Declared behaviour:** each operation also declares the capabilities it
  needs (data access, outbound hosts, secrets) and whether it is idempotent.
- **How it runs:** processes call these operations through the same operation
  catalog as built-in operations. The engine keeps control of transactions,
  retries, history and inspection.
- **Isolation:** the first version loads packages in-process through
  `AssemblyLoadContext` and gives them only the platform APIs they declared.
  Out-of-process isolation comes with multi-tenant hosting.

*Why:* real applications always need some custom code. Without an official
place for it, code ends up as unsafe inline scripts or as
application-specific platform code.

## D8. Custom UI widgets are packaged React components — Agreed, deferred

A custom widget is a React and TypeScript component in an extension package.

- **What it is built from:** it uses the shared design system, theme and text
  resources.
- **What it declares:** a schema for its properties and data bindings.
- **What it may call:** the backend only through platform APIs.
- **Not allowed:** raw HTML or JavaScript in configuration.
- **When:** after Studio.

The standard widgets and form components must be good enough that custom
widgets stay rare. Each custom widget is a signal of a missing platform
capability.

## D9. Authentication: OIDC through a BFF, ready for FAPI 2.0 — Agreed

- **Protocol:** OIDC authorization code flow with PKCE.
- **Client:** a .NET backend-for-frontend (BFF) acts as the confidential
  client.
- **Session:** the browser holds only a secure HttpOnly session cookie.
  Tokens never reach the browser.
- **Identity provider:** an external OIDC provider. Development uses Keycloak.
  Enterprise SAML or directory identities are handled by brokering through the
  identity provider, not inside Axis.
- **FAPI 2.0:** the structure follows FAPI 2.0 roles (authorization server,
  confidential client, resource server). PAR, `private_key_jwt` and DPoP can be
  enabled later without restructuring. FAPI conformance is a later milestone.

*Why:* the BFF pattern gives most of the protection. For a configuration
platform the real risk is business authorization, which FAPI does not cover.
Requiring full FAPI early would block every milestone.

## D10. Tenant-aware from day one, full tenancy later — Agreed

- **Database per tenant:** each tenant has its own PostgreSQL database.
- **Request scope:** every request and every background job runs inside a
  resolved `TenantContext`. All database connections, cache keys, file paths,
  jobs and log scopes carry the tenant.
- **First milestones:** tenants come from a configuration file. A test with
  two tenant databases proves that no data leaks between them.
- **Later milestone:** the tenant directory, provisioning, migrations across
  many databases and hosting profiles.

*Why:* adding tenant context afterwards touches every query and job, which is
expensive. Building full tenant management before there is a second customer
delivers nothing.

## D11. Durable process engine — Agreed

- **Engine model:** processes run on a persisted state machine.
- **Version pinning:** an instance is pinned to the release it started on.
- **Atomic commit:** each step commits in one PostgreSQL transaction: business
  writes, process progress, idempotency receipt, audit and next work (outbox).
- **External calls:** these never run inside a transaction. They are staged as
  durable work with stable operation identities.
- **Worker claims:** workers claim work with leases and fencing.

*Why:* see [knowledge](domain/knowledge.md#execution).

## D12. Authorization is server-side and default-deny — Agreed

Policies grant access to resources, records, fields and actions.

- **No matching grant:** access is denied.
- **Policy evaluation fails:** access is denied.
- **Client-side conditions:** these only shape the UI.
- **Enforcement:** the server enforces every read, write, task action, export
  and inspection.

## D13. Delivery workflow — Agreed

- **Tooling:** NexKit runs one issue per agent session.
- **Milestones:** milestones are vertical slices.
- **Issue planning:** issues are created only for the current milestone
  (rolling wave).
- **Issue size:** each issue is one behaviour, aiming for 2–5 acceptance
  criteria that a test or command can check. More than five is a signal to
  check whether the issue holds more than one behaviour. If it does, split it.
  If every criterion checks the same behaviour, more are fine. A plan is not
  revised only to cut the number of criteria.
- **Documentation:** these docs hold the product and architecture knowledge.
  Issues link to them.
- **Bootstrap:** the solution skeleton, CI and the tool setup are done
  directly, not through NexKit, because NexKit needs working checks first.

*Why:* a large up-front breakdown into layered issues delays anything
runnable until the very end, and its issues are too big for one session.

See [delivery.md](delivery.md) for the step-by-step workflow and who does each
step.

## D14. Repository language — Agreed

Everything in the repository and on GitHub is in English.

## D15. Presentation model — Agreed

- **Pages:** a page is a route in a site.
- **Widgets:** widgets are a page's content. A page has no entity or
  template of its own.
- **Layout:** layout comes later as container widgets, such as tabs,
  sections and columns, that hold other widgets.

- **Data:** see [D18](#d18-data-sources--agreed).
- **Forms:** a `form` resource serves both a `form` widget and a process
  task. See [D20](#d20-human-tasks-task-inbox-and-forms--agreed).

Still **Proposed**, because it shows how the M1 shortcut is expected to grow
but is not settled yet:

- **Navigation:** `formPage` gives way to navigate actions with page
  parameters.

*Why:* the page and widget split shapes data sources (M2), process task forms
(M3) and authorization (M4). Keeping the page free of an entity or template
lets more widgets and a layout be added without a format change.

## D16. Expression language — Agreed

One expression language holds the logic of validation, named rules, computed
fields and data source filters. See [the reference](reference/expressions.md).

- **Syntax:** a small infix syntax familiar to authors, such as
  `quantity * unitPrice` and `status == 'submitted' and total >= 10000`.
- **Nothing else:** no loops, no variables or assignment, no side effects and
  no inline code (D6).
- **Bounded cost:** limits are checked at compile time, and a step budget
  applies at run time.
- **Two back ends:** one language, run by an interpreter and translated to SQL
  for data source filters.

*Why:* see [knowledge](domain/knowledge.md#logic-in-configuration).

## D17. Entity logic and child collections — Agreed

Entities get logic and owned rows in M2. See
[configuration](reference/configuration.md#entity-logic),
[storage](reference/storage.md#child-tables-and-computed-columns) and the
[record API](reference/record-api.md#child-rows-computed-fields-and-validations).

- **Named rules:** a `rule` resource holds a named expression with typed
  parameters and a result type. Any expression can call it.
- **Validation:** an entity declares validation rules. Each has a boolean
  expression, a text key for its message and the field it is reported on. The
  server checks them on create and update, and a failure is a `400` at the
  field's path. Validation on the client as the user types comes later
  ([D20](#d20-human-tasks-task-inbox-and-forms--agreed)).
- **Computed fields:** a field can have an expression. Its value is stored in
  its column and recomputed on every write of the record, in the same
  transaction. Clients cannot write it.
- **Child collections:** a field type that owns the rows of a child entity,
  such as line items. The rows are stored in the child entity's table with a
  foreign key to the owner, are deleted with the owner, and are read and
  written only through the owner record, in one request, under the owner's
  version.

*Why:* see [knowledge](domain/knowledge.md#logic-in-configuration). Logic
stays in typed expressions with no side effects (D16). Line items are part of
their owner, so they share its version and its transaction.

## D18. Data sources — Agreed

Data sources are read-only queries over entities, added in M2. See
[the reference](reference/data-sources.md).

- **The resource:** a `dataSource` has one root entity. It projects the
  entity's own fields and fields reached through references. It has a filter
  expression with typed parameters, a default sort and paging, and
  aggregates: group by, with count, sum, min and max.
- **Filters are safe:** a filter becomes parameterized SQL and is never
  concatenated into it. A filter that cannot be translated is a compile
  error.
- **Serving:** a read endpoint under the application serves each data source.
  It follows the record API's paging and sorting rules. The SPA gets each data
  source's schema in page metadata.
- **Read-only:** data sources never write. Policy record filters are added in
  M4.
- **Widget binding:** a widget binds to a `dataSource`, and `entity` stays as
  shorthand for all records of an entity. In M2 only the `table` widget binds.

*Why:* see [knowledge](domain/knowledge.md#authoring-and-packaging). Related
data is queried, not copied into each record, so it never goes stale. Filters
use the one expression language (D16), so one check covers them.

## D19. Process resource and worker host — Agreed

Processes and the worker host arrive in M3. See
[the reference](reference/processes.md).

- **Process resource:** a `process` names one entity, its subject. Each
  instance runs for one record of that entity, the subject record. `start`
  names the first step.
- **Step kinds in M3:** `decision`, `task` (a human task), `operation` and
  `end`. Each step has a name, and a transition names the next step. Wait for
  event, timer and sub-process come later.
- **Decision:** a decision has ordered branches, each a boolean expression and
  a next step, plus an `otherwise` step. The first branch that is true wins.
- **Process expressions:** they see the subject record's fields, paths
  through reference fields (at most 3 hops, as in data source filters) and
  the named rules. The purchase request threshold is a rule, so it is
  configured, not hard-coded.
- **Operation:** M3 has one built-in operation, `updateRecord`. It sets fields
  of the subject record from expressions, through the record update command,
  inside the step's transaction. M5 adds `raiseEvent`, `callConnector` and
  package operations, see [D25](#d25-connectors-deployment-bindings-and-secrets--agreed),
  [D26](#d26-events-and-triggers--agreed) and [D28](#d28-extension-packages--agreed).
- **Start:** a process can declare a `startCondition`, an expression with a
  message text key. A start that fails it is a `400`. The start pins the
  instance to the active release.
- **One start per submission:** at most one running instance exists per
  process and record, so a second start is a `409`. A repeat of a start
  request with the same `Idempotency-Key` returns the first result.
- **Worker host:** `Axis.Worker` runs the steps, not the server. Each work
  item carries its tenant id, and the worker sets the tenant context from it.
- **One transaction per step:** a step commits these together: business
  writes, the new instance state and revision, the step history, audit
  records and the next work item. A revision mismatch aborts the transaction.
- **Claims:** workers claim work with `FOR UPDATE SKIP LOCKED`, a lease and a
  claim token. The commit checks the token. When a lease expires, another
  worker can claim the item.
- **Failure:** a failing step rolls back. A following transaction records the
  error in the history and marks the instance `failed`. Connector and package
  operation steps have retry policies (D25, D28). Operator retry comes in M7.
- **History:** every step occurrence records its input, output, decision and
  error.

*Why:* see [knowledge](domain/knowledge.md#execution) and
[D11](#d11-durable-process-engine--agreed). Steps run in a worker, never on the
request thread, and each step is one transaction, so a crash leaves no partial
work.

## D20. Human tasks, task inbox and forms — Agreed

Human work arrives in M3. See [processes](reference/processes.md#steps) and
[the frontend](reference/frontend.md). The `form` resource, the `form`
widget that names one, the task inbox widget, the task page and the start
process action are built. So are task steps, task completion with its
one-decision and who-may-act checks, and saving the task form with the
decision. Only the items under **Later** are not built.

- **Task step:** a `task` step has a `label`, an assignee, a `form`, an
  optional `dueIn` and its outcomes.
  - `label` is a required text key. The task inbox and the task page show
    it, because every UI string comes from text resources.
  - The assignee is either `user`, an expression that gives a user id, such
    as `department.manager`, or `role`, a role name. Queues come later.
  - `dueIn` is an ISO 8601 duration. It is stored as the task's due date and
    shown. Escalation needs timers, so it comes later.
  - Each outcome has a name, a label text key and a next step.
- **Exactly one decision:** an open task can be completed once. A later or
  concurrent completion is a `409`. An outcome the step does not allow is a
  `400`.
- **Who may act:** the current user may act when they are the assignee, or
  hold the assignee role. Anyone else gets a `403`, and a request without a
  user gets a `401`. This is checked at the moment of action. Policies in M4
  add to this check and never replace it.
- **Task form:** the task page shows the step's form over the subject
  record. The fields the form makes editable are saved with the decision, in
  the same transaction.
- **Task inbox:** a `taskInbox` widget lists the current user's open tasks in
  the application and opens the task page.
- **Start process action:** a `form` widget can declare a `startProcess`
  action, shown as a button. It saves the record first, then starts the
  process. Navigate actions stay **Proposed** in
  [D15](#d15-presentation-model--agreed).
- **Form resource:** a `form` resource has one entity and sections of fields,
  and each field can be read-only. A `form` widget names a `form`, or an
  `entity` as shorthand for all its fields. The same `form` serves a page and
  a task. This settles the forms point of D15.
- **Later:** these come later, with no milestone yet:
  - conditional visibility, required and read-only states;
  - validation as the user types;
  - wizard steps;
  - file upload.

  This replaces the sentence of D17 that put validation as the user types in
  M3.

*Why:* see [knowledge](domain/knowledge.md#human-work) and
[user interface](domain/knowledge.md#user-interface). A task is a first-class
step, so exactly one decision is recorded and the instance resumes from it.
One form resource serves a page and a task, so a layout is written once.
Moving the richer form features out keeps M3 on processes.

## D21. Development test users, audit records and sequences — Agreed

M3 adds three platform pieces before processes run. Test users, their
sign-in, the current user, the SPA user picker, sequences, the audit records
of record API writes, process starts, process steps and task decisions, and
record history are built.

- **Test users:** replaced by
  [D23](#d23-sign-in-through-oidc-and-the-bff-session--agreed), which removed
  them in M4.
- **Current user:** `GET /api/me` returns the session user from
  [D23](#d23-sign-in-through-oidc-and-the-bff-session--agreed), or `401`
  when there is none. Before M4, record writes and process starts stayed
  open with no signed-in user, and the actor was then `anonymous`.
- **Audit records:** rows in `axis.audit_records` are append-only.
  - Each one commits in the same transaction as its action.
  - M3 records these actions: record create, update and delete through the
    record API, process start, each process step and each task decision.
  - Each holds the time, the actor (a user id, or `system` for the worker),
    the action, the application, the entity, the record id, the process
    instance id and a details object.
  - A database trigger rejects every `UPDATE` and `DELETE`.

  See [storage](reference/storage.md#audit-records-and-sequence-counters)
  and [the record API](reference/record-api.md#audit-records-and-history).
- **Record history:** an endpoint returns the audit records of one record,
  newest first. The form page shows them. See
  [the record API](reference/record-api.md#audit-records-and-history).
- **Sequences:** a `sequence` resource has a format, such as
  `PR-{yyyy}-{n:5}`. See
  [configuration](reference/configuration.md#sequences).
  - The counter restarts each UTC year, but only when the format has
    `{yyyy}`. The year is UTC
    [until applications have a time zone](#d22-current-time-and-time-zones--agreed).
  - An entity `text` field names a `sequence`. Its value is assigned on
    create, and clients cannot write it.
  - The counter row is locked and updated in the caller's transaction, so a
    create that rolls back uses no number and there are no gaps. Concurrent
    creates of the same sequence wait on that row lock, which is accepted at
    the expected volumes.

*Why:* see [knowledge](domain/knowledge.md#operations). Processes need an
actor to record and a trail that cannot be rewritten. Real authentication
waits for M4, so a fixed list of test users stands in, and it must never
reach Production. Business numbers come from the same transaction as the
record, so they never repeat and never skip.

## D22. Current time and time zones — Agreed

The expression function `now()` gives the current time as a `date-time`. It is
an instant, so it never depends on a time zone. See
[expressions](reference/expressions.md#functions).

- **One value per transaction:** every `now()` in one database transaction
  gives the same value, the transaction's start time.
  - A process step uses the step's transaction. That covers its decision
    branches, its `updateRecord` values and a task's `assignee.user`. The
    value is the same instant as the step history's `started_at`.
  - A start condition uses the start transaction.
  - A validation uses the transaction that writes the record.
  - A data source filter uses the query. It is translated to SQL as
    PostgreSQL's `now()`.
  - A rule uses the transaction of whatever calls it.
- **Both back ends agree:** the interpreter and the SQL translation give the
  same value for `now()` in one transaction.
- **Not in computed fields:** a computed field's value is stored, so it would
  go stale. `now()` in a computed field, including inside an aggregate's item
  expression, is `AXC0086` at the call.

**Waits for a time zone rule.** Applications have no time zone yet, and the
rule has no milestone. These wait for it:

- `today()`, and taking a date from a `date-time`.
- Calendar durations in a task's `dueIn`, such as `P1M` and `P1Y`. They are
  `AXC0084` for now.
- The local year in a sequence's `{yyyy}`, which is UTC today.

*Why:* processes need the time of a step, for example to set `submittedAt`.
One value per transaction keeps every expression in a step consistent with
the step's own record. An instant needs no time zone, so `now()` can come
first, and everything that does need one waits for a single rule.

## D23. Sign-in through OIDC and the BFF session — Agreed

M4 replaces the test users with real sign-in through each tenant's identity
provider. The flow, the identity provider per tenant, the endpoints, the
session, its tenant binding, the roles, the current user, sign-in needed,
CSRF, Keycloak and the audit actor are in place, and the test users are
removed. Everything in this decision is in place. See
[architecture](architecture.md#sign-in-and-the-session) for the details.

- **Flow:** OIDC authorization code flow with PKCE. The server is the
  confidential client (D9).
- **Identity provider per tenant:** each tenant in the `Tenants` section has
  an `Oidc` block with an authority, a client id and a client secret.
  - It names the claims that give the user id (default `sub`), the display
    name (default `name`) and the roles (default `roles`).
  - Startup fails, naming each problem, when a tenant has no `Oidc` block or
    an incomplete one.
  - The client secret comes from the environment, never from a committed
    file outside development fixtures.
- **Endpoints:** `GET /api/auth/sign-in?returnUrl=` redirects to the
  identity provider. A `returnUrl` that is not a local path is a `400`. The
  callback creates the session and redirects to `returnUrl`.
  `POST /api/auth/sign-out` ends the session and signs out at the identity
  provider.
- **Session:** one HttpOnly, Secure, SameSite=Lax cookie.
  - It holds the user id, the display name, the roles and the tenant id.
  - It holds no tokens, and no token reaches the browser.
  - It ends after 8 hours, or after 1 hour without requests.
- **Bound to its tenant:** a session used on another tenant's host is no
  session.
- **Roles:** read from the roles claim at sign-in. A role change applies at
  the next sign-in.
- **Current user:** `GET /api/me` keeps its shape and reads the session.
- **Sign-in needed:** every endpoint under `/api/apps/`, and the site and
  page metadata, answer `401` without a session. The platform shell's texts
  and `/health` stay open.
- **CSRF:** every `POST`, `PUT`, `PATCH` and `DELETE` under `/api` must carry
  the header `X-Axis-Csrf: 1`, otherwise `403`. This adds to the JSON content
  type rule and the SameSite cookie. There is no antiforgery token.
- **Test users go:** `TestUsers`, their endpoints and the SPA picker are
  removed. Integration tests sign in through a test authentication scheme
  that only test hosts register. This replaces the test-user part of D21.
- **Keycloak:** Docker Compose, `scripts/dev.sh` and the E2E run start
  Keycloak with an imported development realm.
  - The realm holds the purchase request users, two employees, two department
    managers and a finance reviewer, with a development-only password.
  - It maps their roles to the `roles` claim.
  - It fixes each user's id, so seed data such as a department's manager can
    name them.
  - The realm and its client secret are committed as development fixtures.
    They are never valid outside Development and tests.
  - Integration tests that cover sign-in start the same realm through
    Testcontainers.
- **Audit actor:** the actor is the user id from the session. The display
  name is stored with the audit record, because Axis has no user directory
  to look it up later.

*Why:* see [knowledge](domain/knowledge.md#authorization). Policies and
processes need to know who acts, so test users must give way to real
identities. Keeping tokens on the server and the session bound to one tenant
means a stolen or reused cookie cannot act in another tenant. `sub` never
changes, so it is the default user id, and a tenant can name another claim.

## D24. Roles and policies — Agreed

M4 adds authorization through roles and policies. The `role` and `policy`
resources and their compile checks are built, and so is the compile check of
`currentUser()`. The record API enforces entity grants, their record
filters and their field lists. The data source endpoint enforces grants,
the root entity's read filters and the readable fields. A process start
enforces the `start` grant and read access to the subject record. The task
API keeps the assignee check and leaves out the fields the user may not
read, and the worker's steps run outside policies. Navigation and page
metadata follow the grants, and page metadata leaves out the fields the user
may not read. Everything below is in place. See
[architecture](architecture.md#authorization) for the details. It builds on [D12](#d12-authorization-is-server-side-and-default-deny--agreed).

- **Role resource:** a `role` resource has a name and a label text key. A
  policy and a task's `role` assignee must name a declared role, or
  compilation fails. Role claim values that name no declared role are
  ignored.
- **Policy resource:** a `policy` names one or more roles and holds grants.
  Each grant has one target and its actions:
  - an `entity`, with `read`, `create`, `update` and `delete`;
  - a `dataSource`, with `read`;
  - a `process`, with `start`.
- **Record filter:** an entity grant may have a `filter`, a boolean
  expression over the record. It may use reference paths, rules and
  `currentUser()`.
- **`currentUser()`:** a new expression function that gives the signed-in
  user's id as `text`. It is allowed only in policy filters. Anywhere else it
  is a compile error.
- **Field list:** an entity grant may have `fields`. Without it, the grant
  covers every field.
- **Default-deny:**
  - No session is `401`.
  - A signed-in user with no matching grant gets `403`.
  - A record outside every read filter does not exist for the user, so it is
    `404`.
- **Combining:** the grants of all the user's policies combine with OR. A
  filter that is false, `null` or fails gives no records through its grant,
  so it can never widen access. A failure is logged.
- **Writes:** update and delete need the stored record to match an `update`
  or `delete` filter. Create and update also need the new values to match,
  so a user cannot move a record out of their reach.
- **Fields on reads:** the user may read the union of the `fields` of their
  `read` grants on the entity. Other fields are left out of records, data
  source rows, history details and page metadata. The readable fields are
  per entity, not per record.
- **Fields on writes:** writing a field that no `create` or `update` grant
  covers is `403` with the field's path.
- **Data sources:** the root entity's read filters are added to the data
  source's own filter in SQL. A data source that projects or aggregates a
  field the user may not read is `403` as a whole.
- **Reference labels and lookups:** the label of a referenced record shows
  when the user may read the target's display field. The target's record
  filters do not hide it. Lookups list target records through the user's read
  filters.
- **Record history:** needs read access to the record. Details leave out
  fields the user may not read.
- **Process start:** needs a `start` grant on the process and read access to
  the subject record.
- **Tasks:** the assignee check stays and is enough to list, read and
  complete a task. The task form leaves out fields the user may not read.
- **System work:** the worker, seed data and `updateRecord` steps run as
  `system` and are not checked by policies.
- **Record creator:** a `text` field can declare `"creator": true`. On create
  it gets the signed-in user's id, and clients cannot write it, like a
  sequence field. Policies use it, for example `requester == currentUser()`.
- **UI:** page metadata says which actions the user may take on the page's
  widget. Navigation leaves out pages the user cannot read. This only shapes
  the UI. The server checks every request.
- **Module:** `Axis.Policy` holds the compiled policies and their
  evaluation. Every endpoint and engine command calls it with a resource and
  an action.

*Why:* see [knowledge](domain/knowledge.md#authorization). A filter that is
false, `null` or fails adds nothing, so a mistake in one policy can never
widen access. Readable fields per entity keep queries simple: they do not
depend on which grant matched a record.

## D25. Connectors, deployment bindings and secrets — Agreed

M5 lets a process call external systems. The `Bindings` block in host
configuration, its startup checks, resolving a binding at run time, the
secret value that hides itself from text, JSON and logs, the `connector`
resource with its compile checks and the activation check are built. Outside
Development, startup fails when a secret comes from a configuration file.
Calling a connector, using secrets in its calls and the call-time HTTPS check
are built. Retries are built. Reconciliation is built. See
[configuration](reference/configuration.md#connectors-and-deployment-bindings)
for bindings and the `connector` resource, and
[processes](reference/processes.md#connector-calls) for the call, its
retries and reconciliation.

- **Binding:** a named setting that a release refers to by name and never
  holds as a value. M5 has two kinds, `url` and `secret`. No resource kind
  declares bindings. A connector names one as `{ "binding": "<name>" }`, and
  where the name is used decides its kind: `baseUrl` needs a `url` binding,
  and `auth` needs a `secret` binding.
- **Where values live:** per tenant, in `Tenants:{id}:Bindings:{name}` of
  the server and worker configuration, until the tenant directory in M9.
  Both hosts must hold them.
- **Binding kinds in host configuration:** each binding has a `Kind`, `url`
  or `secret`, and a `Value`. A secret is set with an environment variable,
  such as `Tenants__default__Bindings__PurchasingSystemToken__Value`.
- **Activation check:** activating a release for a tenant fails with a
  diagnostic naming the tenant and the binding, when a binding the release
  uses is missing or of the wrong kind.
- **Secret values:** they come from the environment. Only in Development may
  a committed file hold one, and elsewhere startup fails. They never appear in a release, step
  history, audit record, log or problem details. There is no secret store
  such as a vault yet.
- **Resolved at run time:** the worker reads the binding when it makes a
  call, so changing a secret needs no new release.
- **Connector:** a `connector` resource has a `baseUrl` (a `url` binding),
  `auth` and a `timeout`. `auth` is none, a bearer token or a named header,
  each from a `secret` binding. `timeout` defaults to 30 seconds and is at
  most 120.
- **Operations:** a connector has named `operations`. Each has a method, a
  path template, typed `input` fields sent as a JSON body and typed `output`
  fields read from the JSON response.
- **Idempotency:** `idempotency` names the request header that carries the
  idempotency key, such as `Idempotency-Key`. It is absent when the system
  does not support one.
- **Lookup:** `lookup` is optional and set per operation. It names another
  operation of the same connector that finds an earlier call by its
  idempotency key. That operation is a `GET` whose path holds
  `{idempotencyKey}`, with the same outputs. A `200` means found, with the
  output. A `404` means not found. Anything else is a failed lookup.
- **HTTPS:** `http` base URLs are allowed only in Development. Elsewhere
  they must be `https`.
- **No redirects:** the worker never follows a redirect, and a `3xx`
  response fails the call. Following one could send the secret to another
  host.
- **No `Retry-After`:** the worker ignores it, so backoff is decided in one
  place.
- **Call connector step:** an `operation` step with
  `operation: "callConnector"` names a connector and one of its operations.
  Its `input` maps operation inputs to expressions over the subject record.
  Its `output` maps operation outputs to subject fields. It has `next` and
  an optional `retry`.
- **Staged:** the step's transaction stores the call with a stable
  operation identity and a work item, and the instance waits. No external
  call runs inside a database transaction.
- **Operation identity:** the id of the call's row in
  `axis.connector_calls`, a version 7 UUID. It is stored before any request
  is sent, so every attempt and every lookup uses the same key. A unique
  index on instance and step lets a step stage only one call. An instance
  waits on one call at a time.
- **Stored input:** the worker stores the evaluated input on the call row
  until the call settles, then clears it. A retry then sends the same body
  as the first attempt. Input never appears in history, audit records or
  logs. No response body is stored.
- **The call:** the worker sends the request with the operation identity as
  the idempotency key. A following transaction records the outcome. On
  success it writes the output fields with the record update rules, writes
  the step history and the audit record, and queues the next step.
- **Sending mark:** before each attempt, a short transaction marks the call
  `sending`, counts the attempt and extends the work item's lease past the
  connector's timeout. A worker that later finds a call still `sending`
  treats that attempt's outcome as unknown.
- **Waiting while a call is open:** while its call is pending, retrying or
  unknown, the instance is `waiting` on the step. Settling the call resumes
  it, as completing a task does. So a second start for the same record is
  blocked.
- **No new outbox table:** calls are rows of a new `axis.connector_calls`
  table. The work is a new kind of `axis.process_work_items` item, whose
  `due_at` gives the backoff.
- **Retry policy:** `retry` has `attempts` and `backoff`.
  - `attempts` counts the first attempt. It defaults to 3 and is at most 10.
  - `backoff` is an ISO 8601 initial delay that doubles each attempt, up to
    1 hour. It defaults to `PT30S`. `timeout` is an ISO 8601 duration too.
- **Failed:** a `4xx` other than `408` and `429` fails the call at once.
- **Retryable:** a connection that never sent the request, `408`, `429` and
  `503` are retried.
- **Unknown:** a timeout, a reset after the request was sent, or another
  `5xx` means the system may have acted. It is retried with the same key
  only when the connector declares `idempotency`. Without that, it goes to
  reconciliation. A `2xx` whose body does not fit the declared output is
  unknown too, and goes straight to reconciliation, even with
  `idempotency`.
- **Attempts run out:** the instance becomes `failed` with the error in its
  history. After an unknown outcome, the call goes to reconciliation
  instead. Operator retry comes in M7. Alerts and compensation come later.
- **Reconciliation:** the worker calls `lookup` with the operation
  identity.
  - Found settles the call as succeeded, with its output.
  - Not found retries the call with the same identity. When no attempts are
    left, the instance fails.
  - A failed lookup stays `unknown`. It is tried again with doubling
    backoff, capped at 1 hour, with no limit.
- **Never blind:** an unknown outcome is never retried without the
  idempotency key or a lookup. Without a `lookup`, it stays `unknown` and
  visible in the history until an operator resolves it in M7.
- **A late original request:** if a lookup answers not found and the
  original request arrives later, only the system's idempotency key
  prevents a second order. Without `idempotency`, a connector depends on its
  lookup being correct.
- **Audit and history:** each settled call writes one audit record
  `connector.called`, with actor `system`. Its details are the connector,
  the operation, the operation identity, the attempts, the outcome and the
  last HTTP status, with no request or response bodies. When it writes
  output fields, it also writes `record.updated`, as `updateRecord` does. A
  failed call also writes `process.failed`. The step history holds the
  operation identity and the outcome, and no field values.

*Why:* see [knowledge](domain/knowledge.md#execution) and
[authoring and packaging](domain/knowledge.md#authoring-and-packaging), and
[D7](#d7-custom-code-lives-in-extension-packages--agreed) and
[D11](#d11-durable-process-engine--agreed). A call can succeed while its
response is lost. A stable operation identity, stored before the first
attempt, lets every retry and every lookup ask about the same call, so the
approved request creates exactly one purchase order. Bindings keep URLs and
secrets out of releases, so one release runs in every tenant and
environment.

## D26. Events and triggers — Agreed

M5 lets things that happen start processes. The event and trigger resources,
the raise event step, sync and async event triggers, data change triggers and
schedule triggers are built. See
[configuration](reference/configuration.md#events-and-triggers) for the
`event` and `trigger` resources, and
[processes](reference/processes.md#events-and-triggers) for the raise event
step and how each trigger starts a process.

- **One record per start:** a process has a subject record
  ([D19](#d19-process-resource-and-worker-host--agreed)). So every trigger
  starts a process for one record of the process's subject entity.
  - Starting an operation without a process comes later.
  - So do processes without a subject.
- **Event resource:** an `event` has a name, the `entity` it is about and
  typed payload `fields`, of scalar types only.
  - An occurrence of the event names one record of that entity.
  - Events raised by the platform itself come later. Data change triggers
    cover record changes.
- **Raise event step:** an `operation` step with `operation: "raiseEvent"`
  names an event about the subject entity. Its `fields` map payload fields to
  expressions over the subject record.
  - The step's transaction stores the occurrence in `axis.events`, with the
    subject record, the payload and the release.
  - Events are kept even when no trigger listens, so a later wait for event
    step can match early events.
- **Trigger resource:** a `trigger` names its trigger kind in `type`:
  `event`, `dataChange` or `schedule`. Its `kind` stays `trigger`, the
  resource kind, as with steps, fields and widgets. It names the `process` it
  starts.
  - Inbound endpoints are their own resource, in
    [D27](#d27-inbound-endpoints--agreed).
- **Event trigger:** it names an `event` whose entity is the process's
  subject entity. It has an optional `when` over the payload, and a `mode` of
  `async`, the default, or `sync`.
  - **Async:** the worker delivers the occurrence. It starts the process at
    most once per occurrence and trigger, even after a worker restart.
  - **Sync:** the start commits in the raising step's transaction, so both
    commit or neither does.
- **Data change trigger:** it names an `entity` and `on`: `create`, `update`
  or both. It has optional `fields`, so it fires only when one of them
  changed, and an optional `when` over the new record.
  - The start commits in the write's transaction.
  - **Which writes:** it fires on every committed write through the record
    command. That is the record API, a task form save, an `updateRecord` step
    and a connector output.
  - **Delete waits:** `delete` comes later, because a deleted record cannot
    be a subject.
- **Starts that cannot happen:**
  - When the process already runs for the record, the trigger skips the
    start. This is no error, and it is the guard against loops.
  - A start whose `startCondition` is false is skipped too.
  - Both are logged.
- **Schedule trigger:** it has a 5-field cron `schedule` and a required IANA
  `timeZone`.
  - It names a `dataSource` whose root entity is the process's subject
    entity, with fixed parameter values. Each occurrence starts the process
    for each returned record.
  - **Once per occurrence:** each occurrence runs once per tenant, across all
    workers.
  - **Fan-out:** records are started in batches. Each record starts at most
    once per occurrence, also after a worker restart in the middle.
  - **After downtime:** only the latest missed occurrence runs, once.
  - **Daylight saving:** a local time that does not exist runs at the next
    valid time. A repeated local time runs once.
  - **Time zone:** the schedule's own time zone does not settle the
    application time zone rule of
    [D22](#d22-current-time-and-time-zones--agreed).
- **Release, actor and audit:**
  - Triggers come from the active release.
  - A start from a trigger pins the release active at the start, as every
    start does.
  - Triggers run as `system` and are not checked by policies, like other
    system work in [D24](#d24-roles-and-policies--agreed).
  - The `process.started` audit record names the trigger and, for an event,
    the occurrence.
- **Errors count as false:** a trigger's `when`, or the process's
  `startCondition`, that hits a run-time error counts as false. The start is
  skipped and logged, and the write or the raising step still commits.
- **`fields` on updates only:** a data change trigger's `fields` filter
  applies to updates. A create always passes it.
- **Plain data source:** a schedule trigger needs a data source that is not
  grouped. Its fan-out covers every row, in record id order, and ignores the
  data source's `pageSize` and `sort`.
- **Release when it fires:** triggers are read from the release that is
  active when the trigger fires. For an async event trigger, that is when the
  event is delivered, not when it was raised.
- **Skipped counts as handled:** a skipped start still counts as handled for
  that occurrence and trigger. It is never tried again.
- **No own audit record for an event:** raising an event writes
  `process.stepCompleted`, as other operations do. Its history output holds
  the event's id. The `axis.events` row records the occurrence.

*Why:* see [what applications use most](domain/knowledge.md#what-applications-use-most)
and [execution](domain/knowledge.md#execution). Processes run per record
([D19](#d19-process-resource-and-worker-host--agreed)), so a trigger always
names the record it starts for. The one-running-instance rule stops loops,
so data change triggers can fire on system writes and let processes chain.
Receipts make every start happen at most once, after a crash or a restart.
Triggers run as `system`, like other system work in
[D24](#d24-roles-and-policies--agreed), and a schedule's own time zone leaves
the application rule of [D22](#d22-current-time-and-time-zones--agreed) open.
A broken trigger never blocks a user's save or fails a running process. This
matches how D24 treats a policy filter that fails: it counts as false.

## D27. Inbound endpoints — Agreed

M5 lets other systems create a record and start a process in one call. The
bearer token checks are built, with the tenant binding, the CSRF exemption
and the development realm's `purchasing-system` client. The endpoint resource,
the `invoke` grant, the call with its `output` and `wait`, its
`Idempotency-Key`, and the status location are built. See
[configuration](reference/configuration.md#endpoint-resource) for the
`endpoint` resource and its policy grant, and
[record API](reference/record-api.md#inbound-endpoints) for the call, its
responses and its errors. The owner confirmed these points on issue #312 on
2026-10-12.

- **Endpoint resource:** an `endpoint` has a name, typed `input` fields, the
  `entity` of the record it creates, `values` that map input to fields, and
  the `process` it starts on that record.
  - It is its own resource kind, not a `trigger`, because it has an input
    schema, an authorization grant and a response.
  - An endpoint always creates its subject record. Acting on an
    existing record comes later.
- **Address:** it answers `POST /api/apps/{app}/endpoints/{name}`.
- **Input types:** input fields use the entity field types, including
  `reference` and `child-collection`. `values` maps each subject field to an
  expression over the input. A child collection takes a child-collection
  input by name. The sample's start condition needs a total amount, which
  needs line items.
- **One transaction:** the call creates the subject record through the
  record create command, so references, sequences, computed fields and
  validations apply. It starts the process in the same transaction, pinned
  to the active release.
- **Bearer tokens only:** callers are systems, not browsers. They send an
  OAuth 2.0 bearer access token from the tenant's identity provider, got
  through client credentials.
- **Token checks:** the server checks the token's signature, issuer,
  audience and expiry against the tenant's `Oidc` authority. The issuer must
  be the tenant's authority, so another tenant's token is a `401`.
- **Audience:** the expected audience is a new tenant setting,
  `Oidc:EndpointAudience`. In the development realm it is `axis-endpoints`,
  added by an audience mapper on the `purchasing-system` client. A separate
  audience keeps endpoint tokens apart from the browser sign-in client.
  - The setting is optional. A tenant without it accepts no bearer token, so
    every endpoint call there is a `401`.
  - The server's startup checks do not change.
- **No cookie session:** endpoints ignore the session cookie, so the CSRF
  header does not apply to them. Bearer tokens are accepted only on endpoint
  routes, and ignored everywhere else.
- **Roles:** the caller's roles come from the token's roles claim, matched
  as for users.
- **Keycloak:** the development realm gets a confidential client
  `purchasing-system` with a service account. The account holds the `axis`
  client role `purchasing-system`, and a roles mapper writes it to the
  `roles` claim.
- **Grant:** a new policy grant target, `endpoint`, with the action
  `invoke`. It takes no `filter` or `fields`.
- **The grant is enough:** the record create and the process start inside
  the call are part of the endpoint's contract. They need no entity or
  process grant of the caller. The endpoint's `values` limit what it can
  write.
- **Denied:** no token, or a bad one, is `401`. A caller with no `invoke`
  grant is `403`.
- **`400`:** input that does not fit the schema is keyed `/input/<name>`.
  Reference, validation and unique-value errors from the record create are
  keyed `/values/<field>`, since input need not map one-to-one to fields. A
  start condition that fails is keyed `/input`. In every case nothing is
  written.
- **`201`:** the call answers with the record id and the process instance
  id.
- **Output:** an endpoint may declare `output` fields of the subject record
  and a `wait` of at most 30 seconds. The two go together. When the instance
  completes within `wait`, the `201` carries those fields. Otherwise the
  call answers `202` with a status location.
- **Status location:** `GET /api/apps/{app}/endpoints/{name}/instances/{instanceId}`.
  It needs the same bearer token checks and the `invoke` grant. Only the
  caller who made the call can read it. Anyone else, and an unknown
  instance, gets `404`.
- **Idempotency:** a repeat with the same `Idempotency-Key` returns the same
  record and instance, and creates nothing. A key is unique per endpoint and
  caller.
  - A repeat never waits. It follows the instance's state: `201` with output
    when the instance has completed, otherwise `202`.
  - The same key with a different input is `422`, as on the process start
    endpoint.
- **Actor:** the caller's id is the token's `sub` claim. It is the audit
  actor of `record.created` and `process.started`, and both details name the
  endpoint.
- **Creator field:** the new record's creator field gets the caller's `sub`.
- **Display name:** the audit display name comes from the display name
  claim, or from the token's client id (`azp`) when that claim is missing.
  Keycloak service-account tokens have no `name` claim.

*Why:* see [what applications use most](domain/knowledge.md#what-applications-use-most)
and [authorization](domain/knowledge.md#authorization). Other systems need
to hand work to Axis, and an endpoint does that through the same record
command and process start as a user, so no rule is skipped. Bearer tokens
keep machine callers out of the browser session of
[D23](#d23-sign-in-through-oidc-and-the-bff-session--agreed), and checking
them against each tenant's own authority and audience follows
[D9](#d9-authentication-oidc-through-a-bff-ready-for-fapi-20--agreed).
A missing audience, a missing grant or another caller's instance gives no
access, as [D12](#d12-authorization-is-server-side-and-default-deny--agreed)
requires. The `invoke` grant extends the policies of
[D24](#d24-roles-and-policies--agreed), and the endpoint's `values` mapping
limits what a caller can write. A repeat follows the instance's state rather
than a stored response, because storing the response after the wait would
need a second write that a crash can lose.

## D28. Extension packages — Agreed

M5 lets a process run custom C# code from an extension package. The manifest,
the contracts, installation and loading are built. Version pinning, the
package operation step, its retries and the capability limits are built too.
The planned activation check for a missing pinned version is replaced by
[D29](#d29-release-pinning-and-text-resources--agreed)'s start check.
See [extension packages](reference/extension-packages.md) for
the manifest, the contracts, installation, loading and capabilities,
[configuration](reference/configuration.md#extension-packages) for an
application's `packages`, and
[processes](reference/processes.md#package-operations) for the package
operation step. The owner confirmed these points on issue #313 on
2026-10-12.

- **What a package is:** a .NET class library with its own unit tests and a
  SemVer version.
  - It references only `Axis.Extensions`, a small contracts assembly with the
    operation interface and the operation context.
  - Its build artifact is a folder with the assemblies and a manifest,
    `axis-package.json`.
  - Data access from packages comes later.
- **Manifest:** it holds the package id, its version and its operations.
- **Each operation:** it declares typed `input` and `output` fields, whether
  it is idempotent, its `timeout` and its capabilities. The capabilities are
  the `url` bindings it may call and the `secret` bindings it may read.
- **Installation:** packages are installed per installation, in the folder
  that the server and worker setting `Extensions:Path` names. There is one
  subfolder per package id and version. Every tenant sees the same installed
  packages, and an application uses only those it declares.
- **Dependencies:** an application manifest declares its `packages`, each
  with a version range.
  - A range is an exact version `x.y.z`, `^x.y.z` for the same major
    version, or `~x.y.z` for the same minor version.
  - Versions are plain `major.minor.patch`. Pre-release versions come later.
- **Pinning:** compilation picks the highest installed version in each range
  and pins that exact version in the release. A range that no installed
  version meets is a diagnostic.
- **Loading:** each package version loads in its own `AssemblyLoadContext`.
  It shares only `Axis.Extensions` with the host. A manifest that does not
  match the assembly stops activation with a diagnostic.
- **Package operation step:** an `operation` step with
  `operation: "<package>/<operation>"`.
  - Its `input` maps operation inputs to expressions over the subject
    record. Its `output` maps operation outputs to subject fields.
  - It has `next` and an optional `retry`.
  - Its inputs and outputs are type-checked against the manifest of the
    pinned version.
- **Runs like a connector call:** every package operation runs outside the
  step transaction, staged like a connector call in
  [D25](#d25-connectors-deployment-bindings-and-secrets--agreed). This holds
  even for a pure operation, so there is one rule for all package
  operations.
  - The operation identity is its idempotency key, the same on every
    attempt.
  - It follows the same retry policy as a connector call.
  - An output that does not fit the manifest fails the call. So does any
    other exception.
  - A `RetryableException` from `Axis.Extensions` is a retryable attempt. So
    is a pinned version that is missing on the worker.
  - A timeout, or a call found still `sending` after a crash, is an unknown
    outcome. It is retried only when the operation declares
    `idempotent: true`. Otherwise it stays unknown until an operator resolves
    it in M7.
- **Timeout:** each operation declares its own `timeout`. It defaults to
  `PT30S` and is at most `PT2M`, as for connectors.
- **Capabilities:** the operation context gives the operation an HTTP client
  that reaches only its declared `url` bindings, and only its declared
  secrets.
  - Anything else the context is asked for throws, and the attempt fails.
    The attempt fails even if the operation catches the exception.
  - Secret values never appear in history, audit records or logs.
- **Audit:** each settled call writes one audit record `operation.called`,
  with actor `system`. History and audit records hold the exception type of a
  failed attempt, never its message.
- **Not a sandbox:** loading in-process is not isolation. A package that uses
  .NET APIs directly bypasses these limits.
  - A package can read environment variables, including every tenant's
    secrets.
  - It can also open its own HTTP clients and sockets, and ignore
    cancellation.
  - Packages are trusted code that the operator installs and reviews.
  - Out-of-process isolation comes with multi-tenant hosting
    ([D7](#d7-custom-code-lives-in-extension-packages--agreed)).
- **Sample package:** `samples/packages/` holds one package with a pure
  operation and no capabilities. The purchase request sample uses it. The
  sample package is `payment-references`, whose operation computes an ISO 11649
  creditor reference. A test-only package
  under `tests/` covers the capability limits.

*Why:* see [knowledge](domain/knowledge.md#logic-in-configuration) and
[D7](#d7-custom-code-lives-in-extension-packages--agreed). Some logic does not
fit expressions, and a package gives it a tested, versioned home. Pinning the
exact version in the release keeps a running process on the code it was
compiled against. Staging every call like a connector call in
[D25](#d25-connectors-deployment-bindings-and-secrets--agreed) keeps custom
code out of database transactions, and gives retries and unknown outcomes one
rule. The capability limits stop mistakes, not hostile code, so the docs say
plainly that a package is trusted code.

## D29. Release pinning and text resources — Agreed

M6 keeps running work on its release, including its tasks, forms, texts and
package versions *(planned for M6)*. Roles and policies still come from the
active release. See [releases and running work](reference/processes.md#releases-and-running-work)
for the endpoint and command rules and a purchase request walkthrough.
The owner confirmed these points on issue #412 on 2026-10-15.

- **New work and pinned work:**
  - New work starts on the active release. The record API, data sources,
    process starts, inbound endpoints, triggers, sites and pages use it.
  - Running work stays on its release until it ends. Its steps, connector
    and package calls, tasks, forms and texts all use that release.
  - A data change or event trigger set off by old-release work starts new
    work on the active release.
- **Tasks:**
  - Each task list item and task read returns `releaseId`. The inbox loads
    the texts of each release its rows name. The task page loads the texts
    of the task's release.
  - Step labels, forms, outcomes and field definitions come from the task's
    release. The subject label uses that release's display field.
  - Roles and policies always come from the active release. They decide
    which fields the user may read, including the subject's display field.
    The assignee check stays as it is.
  - A tightened policy applies at once. Removing or renaming a policy can
    lock a user out of an old-release task's fields.
- **Text resources:**
  - Each release carries its own texts. Removing a key in a newer release
    does not change a task still running on an older release.
  - Without `release`, `GET /api/sites/{path}/texts/{locale}` uses the active
    release. With `?release={releaseId}`, it answers for any stored release
    of the site's application. An unknown id, a non-UUID or another
    application's release answers `404`.
  - A key used by a label must exist in every locale of that release, as
    `AXC0027` and `AXC0028` already check. A newer release can remove a key
    its own labels no longer use.
  - Old texts stay readable by release id for as long as the release is
    stored, even after its instances end.
- **Missing pinned package version:**
  - At the start of the server and the worker, every active release and
    every release with a running or waiting instance is rebuilt.
  - A pinned package version that is not installed stops the start with a
    diagnostic naming the tenant, application, release and package version.
  - This start check replaces D28's planned activation check. Its
    diagnostic code is `AXC0146`.

*Why:* [D11](#d11-durable-process-engine--agreed) and
[execution](domain/knowledge.md#execution) keep running work on the version
it started with. Pinning texts keeps removed keys readable on old tasks.
[D24](#d24-roles-and-policies--agreed) keeps authorization current, so an old
task cannot preserve a loosened policy. Checking pinned package versions at
start extends [D28](#d28-extension-packages--agreed): a host cannot rebuild
work without the code its release pins.

## D30. Schema migrations — Agreed

Activation applies the `renameField`, `dropField`, `dropEntity`, `fill` and
`convert` migrations once per tenant. The start lock is built too. The
old-release work check and the recompute of computed fields are not built yet
*(planned for M6)*. Until
the recompute is built, a `fill` or `convert` on a child entity and a `fill` or
`convert` of a field that a computed field reads are refused. Migrations
change entity storage in ways that are not additive, before provisioning.
See [configuration](reference/configuration.md#resource-file-shape) for the
resource shape and [storage](reference/storage.md#migrations) for planning,
statement order, locks and a waiting purchase request example.
The owner confirmed these points on issue #413 on 2026-10-15.

- **Migration resource:**
  - A new `migration` resource kind has one file per migration, with an
    `id`, a `name`, `formatVersion: 1`, the `entity` it changes and an
    ordered list of `changes`.
  - Files run in ordinal order of their relative path, so authors name
    them `migrations/0001-….json`. Changes run in file order.
  - Migration files are part of the release and its content hash.
- **Changes:**
  - `renameField`, with `from` and `to`, renames the column and keeps its
    values. Its `uq_` and `fk_` constraints are renamed too, because their
    names hash the column name.
  - `dropField`, with `field`, drops the column and its values.
  - `dropEntity` drops the child tables, then the entity table and its
    provisioning records. It never uses `CASCADE`, so a remaining foreign
    key fails rather than disappearing.
  - `fill`, with `field` and `value`, fills every null with an expression
    over the record. This lets a field become required on a table with
    rows, or adds a new required field to one.
  - A `fill` on a missing column first adds it as nullable, then runs
    `UPDATE ... SET f = <value> WHERE f IS NULL`. Additive provisioning
    afterwards adds `NOT NULL` for a required field.
  - `convert`, with `field` and `value`, uses an expression over stored
    values to change the column to its new type or map removed enum values
    to kept ones.
  - A type-changing `convert` runs four statements in the activation
    transaction. It adds a temporary nullable column `axis_convert`, fills it
    with one parameterized `UPDATE ... SET axis_convert = (<value>)::<type>`,
    runs `ALTER TABLE ... ALTER COLUMN ... TYPE ... USING axis_convert`, then
    drops the temporary column. DDL cannot take parameters, and values are
    always parameters. An enum mapping is one
    `UPDATE ... SET f = <value> WHERE f IN (<removed values>)`.
  - Both expressions must translate to the
    [SQL subset](reference/expressions.md#sql-subset). An expression outside
    it is a compile diagnostic. Each fill or enum mapping uses one `UPDATE`.
    A type conversion uses one `UPDATE` and one `ALTER COLUMN` statement.
  - `fill` and `convert` increment `version` on every row they change.
    For child rows, they increment the owner's `version` instead. They
    write no per-row audit records. The `axis.applied_migrations` row is
    the trace, and the version bump protects concurrent edits.
- **At activation:**
  - Pending migrations run before additive provisioning, in the same
    transaction and under the same schema lock.
  - With a pending migration, the active release and its `axis.active_sites`
    rows are also written in that transaction. Any failure, including a
    refused name or site path, rolls back everything. The active release
    does not change, and a retry runs the migrations again.
  - The lock order is schema lock, then the application's start lock
    exclusively, then the old-release count. Both locks stay held to commit.
    Process starts never take the schema lock.
- **Once per tenant:**
  - `Axis.Data` owns `axis.applied_migrations` in each tenant database.
    It records the migration `id` as primary key, the application, the
    release that applied it, its content hash and the time.
  - The hash is SHA-256 of the file's canonical JSON, hashed as for release
    content. No matching row means the migration is pending.
- **Planning:**
  - The planner applies pending migrations to its catalog snapshot first:
    rename or drop columns or tables, convert types and mark filled columns
    as having no nulls. It then plans as today.
  - A removal or incompatible change without a migration keeps its current
    diagnostic: `AXC0015`, `AXC0016` or `AXC0017`.
- **Changed history:**
  - An applied migration with a changed hash or a missing file is a new
    diagnostic at activation. Nothing runs.
  - **Forward only:** migrations run forward only. Activating a release
    that lacks an applied migration file is refused. An older release
    without that file cannot be activated again. The way back is a new
    release with a new forward migration.
- **Old-release work:**
  - `renameField`, `dropField`, `dropEntity` and `convert` change columns
    that older releases read.
  - After taking both locks, activation counts `running` and `waiting`
    instances pinned to another release whose model has the affected
    column. For a rename, that is the `from` column.
  - For each such release, a new diagnostic names the migration, the
    release and the number of open instances. Activation changes nothing.
    `fill` and additive changes never wait for old-release work.
  - **Start lock:** every transaction that reads the active release and
    inserts a process instance takes `pg_advisory_xact_lock_shared` keyed
    on the application `id`, before reading the active release. This covers
    process start endpoints, inbound endpoints, trigger starts and schedule
    fan-out.
  - An activation with a pending migration takes the same lock exclusively
    before the count and holds it to commit. A start either commits before
    the count and is counted, or reads the new release after activation
    commits. No start can slip between the count and the switch.
- **Computed fields:**
  - A `fill` or `convert` recomputes, in the same transaction, every stored
    computed field that reads the changed field. It may read it directly,
    through a rule or through a child aggregate.
  - Only the changed rows are recomputed, and for changed child rows,
    their owners. Recompute last, after additive statements, using the
    release being activated, so every column its model reads exists.
  - A run-time error rolls back activation. A migration never leaves a
    stored computed value stale. Rule calls in computed fields remain
    unbuilt (`AXC0050`), so that case describes future behaviour.

*Why:* [D11](#d11-durable-process-engine--agreed) and
[D29](#d29-release-pinning-and-text-resources--agreed) keep running work on
its release. Destructive changes must wait until that work stops reading
the old columns. The start lock keeps that check true until activation
commits. [D17](#d17-entity-logic-and-child-collections--agreed) stores computed
values with their records, so migrations must recompute affected values in
the same transaction. [Execution](domain/knowledge.md#execution) rules out
partial commits that leave storage and the active release disagreeing.

## D31. Separate database roles — Agreed

Each tenant gets two database roles, one for runtime access and one for
schema changes *(planned for M6)*. Runtime code can then read and write
data, but it cannot change the schema, a stored release, the active release
or the provisioning records. Today the server reads both connection strings
and startup activation uses `SchemaConnectionString`. Installations pass one
user in both until the roles land.
See [storage](reference/storage.md#database-roles) for the grants and the
connection string of each step.
The owner confirmed these points for issue #414 on 2026-10-16.

- **Two connection strings:**
  - Each tenant has `ConnectionString` for the runtime database role and
    `SchemaConnectionString` for the schema database role.
  - The server needs both. The worker needs only `ConnectionString` and never
    holds the schema database role.
  - A missing or empty setting stops the start, naming the tenant and the
    setting.
  - The start also stops when both connection strings name the same user.
    There is no fallback to one connection string.
- **Schema database role:**
  - It owns the `axis` and `entities` schemas.
  - It runs the EF Core migrations, entity provisioning, migrations, release
    storage and activation.
  - Only startup activation in the server uses it. The server keeps it for its
    whole life until tenant provisioning in M9.
- **Runtime database role:**
  - It reads and writes rows in entity tables and system tables, and uses their
    sequences. It may not create, alter or drop anything.
  - It may only read the tables that activation writes: `axis.releases`,
    `axis.release_resources`, `axis.release_packages`, `axis.active_releases`,
    `axis.active_sites`, `axis.provisioned_entities`,
    `axis.provisioned_enum_values`, `axis.applied_migrations` and each EF Core
    migrations history table.
  - Default privileges grant `SELECT`, `INSERT`, `UPDATE` and `DELETE` on new
    tables in `entities`, and `SELECT` only on new tables in `axis`. A
    migration that adds an `axis` table that runtime writes grants those rights
    explicitly. Sequences in both schemas get `USAGE` and `SELECT`.
  - The schema database role grants these rights in a migration. The runtime
    database role name comes from the user of `ConnectionString`.
  - The start stops when the runtime database role is a superuser, owns the
    `axis` or `entities` schema, or may create objects in either.
- **Stored releases:** a trigger refuses every `UPDATE`, `DELETE` and
  `TRUNCATE` on the three release tables, for every database role. It replaces
  the guard that only EF Core change tracking gives today. A stored release can
  no longer be deleted at all, and cleaning up old releases needs a later
  decision.
- **Creating the database roles:**
  - Axis creates no database roles or databases. Whoever installs Axis does,
    until tenant provisioning in M9.
  - Docker Compose, `scripts/dev.sh`, `scripts/e2e.sh` and the test fixtures
    create two database roles, `axis_schema` and `axis_runtime`, with
    development passwords.

*Why:* [D10](#d10-tenant-aware-from-day-one-full-tenancy-later--agreed) puts
each tenant in its own database, so a tenant's database role is the last
barrier between its data and its schema.
[Execution](domain/knowledge.md#execution) and security rules need runtime code
to be unable to change the schema, a stored release, the active release or the
provisioning records that the next activation plans from. A separate schema
database role makes that a property of the database, not of the code.

## D32. Test scenarios — Agreed

A `scenario` resource is a configuration-level test that runs against its own
temporary tenant database *(planned for M6)*. It seeds data, runs actions as
given users and asserts the outcomes, including denied actions. See
[configuration](reference/configuration.md#test-scenarios) for the resource
shape and a purchase request example.
The owner confirmed these points for issue #415 on 2026-10-16.

- **Scenario resource:**
  - A new `scenario` resource kind has one file per scenario, under
    `scenarios/` in the application folder.
  - Scenario files are compiled with the application but left out of the
    release and its content hash. Adding a test never makes a new release.
  - A scenario has a `name`, `users`, an optional `now`, optional `stubs`
    and ordered `steps`.
- **Users:**
  - `users` maps each user id to the role names it holds, such as
    `{ "anna": ["requester"] }`. A role name must be a declared `role`.
  - No identity provider takes part. The runner gives each step the user's
    id and roles, as a session would.
- **Steps:**
  - `createRecord`, `updateRecord` and `deleteRecord` go through the record
    command, with `entity`, `values` and an `as` user. A created record gets
    an alias through `save`, which later steps use.
  - `startProcess`, with `process`, the record alias and `as`.
  - `completeTask`, with the record alias, the task's `step`, the `outcome`,
    optional `values` and `as`.
  - `expectRecord`, with the record alias and the expected `values`.
  - `expectInstance`, with `process`, the record alias, the `state` and,
    optionally, the `step`.
  - `expectTask`, with the record alias, the `step` and the expected
    `assignee` or `state`.
  - An action may declare `expect`: `denied`, `notFound`, `conflict` or a
    validation error at a field. An action without `expect` must succeed.
  - Each step's references to entities, fields, processes, steps, outcomes
    and users are checked when the application compiles.
- **Running:**
  - Each scenario gets its own temporary tenant database, created under a
    new unique name (`axis_scenario_<uuid>`). The runner migrates it,
    compiles and activates the application, applies its seed data and runs
    the steps. It drops the database when the scenario ends, pass or fail.
    It drops only databases it created.
  - After each action the runner runs the due work items in-process until
    none is due, so a process reaches its next wait before the next step.
  - If more than 1,000 work items run after one action, the scenario fails.
  - `now` fixes the time that expressions and task due dates read, for the
    whole scenario. Timestamps that PostgreSQL writes, such as `created_at`,
    keep the real clock.
  - `stubs` give the output of each connector operation and package
    operation that the scenario calls. A call without a stub fails the
    scenario.
- **Where it runs:**
  - A new console project, `Axis.Scenarios`, runs every scenario of an
    application folder against a PostgreSQL server:
    `dotnet run --project src/Axis.Scenarios -- <folder> --server <connection string>`.
  - The `--server` connection must be allowed to create databases. The
    runner uses it for every step, so scenarios do not test the separate
    database roles of #414.
  - It writes a JUnit file to `artifacts/test-results/` and exits non-zero
    when a scenario fails.
  - `scripts/integration.sh` runs the scenarios of every application under
    `samples/apps/` against its Testcontainers PostgreSQL, so CI runs them.
- **Limits:**
  - **Outside the release.** Scenarios do not travel with a release, so a
    scenario always tests the folder, not a stored release.
  - **Stubs only.** Scenarios never call a real external system, and never
    run real package code. Package operations get unit tests in their own
    project.
  - **Fixed time.** One `now` per scenario. Advancing time to fire schedules
    comes later.
  - **New project.** `Axis.Scenarios` is a new host next to the server and
    the worker.

*Why:* [D5](#d5-configuration-is-authored-as-files-first--agreed) makes
configuration files the source of truth, so tests over them are files too and
CI can run them. A scenario sets users and roles itself, so it checks
[D24](#d24-roles-and-policies--agreed) denials without an identity provider.
A unique database that the runner always drops keeps a failed run from
leaving data behind or touching a real tenant. The work item limit stops a
looping process from hanging CI. See
[testing applications](domain/knowledge.md#testing-applications).
