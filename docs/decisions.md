# Decisions

Each decision has a status. **Agreed** means the owner has accepted it and
work may rely on it. **Proposed** means it is a working default that can still
change. Add new decisions at the end, and change an existing decision only by
recording a newer one that replaces it.

## D1. Applications are configuration — Agreed

Business-specific models, queries, rules, processes, UI, policies and text are
resources in an application's configuration. Platform code contains nothing
specific to any application.

_Why:_ this is the product. It is also the only way many applications can
share upgrades, security fixes and engine guarantees.

## D2. Axis owns its engines — Agreed

Axis implements its own configuration compiler, expression engine, data engine,
process engine, presentation runtime and policy engine. Third-party libraries
are fine for infrastructure: HTTP, database access, serialization, UI
components, standard security protocols and telemetry. No third-party workflow
or rule engine.

_Why:_ versioning, durability, authorization and inspection have to work the
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

_Why:_ a small team gets the most speed from a monolith. Module boundaries
keep the option to split hosts later.

## D5. Configuration is authored as files first — Agreed

- **Format:** resources are JSON files, one resource per file. Each file has a
  stable ID and a human-readable name, and is validated against a published
  JSON Schema.
- **Where they live:** an application folder in git. Axis compiles that folder
  into a release.
- **Authoring APIs:** the APIs use the same contracts and support partial
  updates.
- **Studio:** a visual Studio writes the same resources.

_Why:_ files can be reviewed, diffed and tested in CI, and AI agents can author
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

_Why:_ see [knowledge](domain/knowledge.md#logic-in-configuration).

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

_Why:_ real applications always need some custom code. Without an official
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
- **Plan:** [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed) places custom widgets in M17,
  after the page, form and process designers.

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

_Why:_ the BFF pattern gives most of the protection. For a configuration
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
  many databases and hosting profiles. The tenant directory is in
  [D42](#d42-tenant-directory-in-the-platform-database--agreed). Provisioning
  and migrations across tenant databases are in
  [D43](#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed).

_Why:_ adding tenant context afterwards touches every query and job, which is
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

_Why:_ see [knowledge](domain/knowledge.md#execution).

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

_Why:_ a large up-front breakdown into layered issues delays anything
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

_Why:_ the page and widget split shapes data sources (M2), process task forms
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

_Why:_ see [knowledge](domain/knowledge.md#logic-in-configuration).

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

_Why:_ see [knowledge](domain/knowledge.md#logic-in-configuration). Logic
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

_Why:_ see [knowledge](domain/knowledge.md#authoring-and-packaging). Related
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
  operation steps have retry policies (D25, D28). Operator [retry](reference/processes.md#retry) is built.
- **History:** every step occurrence records its input, output, decision and
  error.

_Why:_ see [knowledge](domain/knowledge.md#execution) and
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

_Why:_ see [knowledge](domain/knowledge.md#human-work) and
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

_Why:_ see [knowledge](domain/knowledge.md#operations). Processes need an
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

_Why:_ processes need the time of a step, for example to set `submittedAt`.
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
  - Under `Tenancy:Source=directory`, the `Oidc` settings and the encrypted
    client secret come from the tenant directory, see
    [D42](#d42-tenant-directory-in-the-platform-database--agreed).
- **Endpoints:** `GET /api/auth/sign-in?returnUrl=` redirects to the
  identity provider. A `returnUrl` that is not a local path is a `400`. The
  callback creates the session and redirects to `returnUrl`.
  `POST /api/auth/sign-out` ends the session and signs out at the identity
  provider.
- **Session:** one HttpOnly, Secure, SameSite=Lax cookie.
  - It holds the user id, the display name, the roles and the tenant id.
  - It holds no tokens, and no token reaches the browser.
  - It ends after 8 hours, or after 1 hour without requests.
  - The cookie is protected with a key ring shared by every replica, and
    behind a named proxy the server uses the forwarded scheme and host. See
    [D49](#d49-several-server-replicas-behind-a-tls-proxy--agreed).
- **Bound to its tenant:** a session used on another tenant's host is no
  session.
- **Roles:** read from the roles claim at sign-in. A role change applies at
  the next sign-in.
- **Current user:** `GET /api/me` keeps its shape and reads the session.
  [D38](#d38-studio-edits-application-folders-through-an-authoring-api--agreed)
  adds a `studio` field.
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

_Why:_ see [knowledge](domain/knowledge.md#authorization). Policies and
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

_Why:_ see [knowledge](domain/knowledge.md#authorization). A filter that is
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
  the server and worker configuration under `Tenancy:Source=configuration`.
  Both hosts must hold them. Under `Tenancy:Source=directory`,
  [D42](#d42-tenant-directory-in-the-platform-database--agreed) moves binding
  values and secrets into the tenant directory, and it stores secrets
  encrypted.
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
  instead. Operator [retry](reference/processes.md#retry) is built. Alerts and compensation come later.
- **Reconciliation:** the worker calls `lookup` with the operation
  identity.
  - Found settles the call as succeeded, with its output.
  - Not found retries the call with the same identity. When no attempts are
    left, the instance fails.
  - A failed lookup stays `unknown`. It is tried again with doubling
    backoff, capped at 1 hour, with no limit.
- **Never blind:** an unknown outcome is never retried without the
  idempotency key or a lookup. Without a `lookup`, it stays `unknown` and
  visible in the history until an operator [resolves](reference/processes.md#resolve) it.
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

_Why:_ see [knowledge](domain/knowledge.md#execution) and
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

_Why:_ see [what applications use most](domain/knowledge.md#what-applications-use-most)
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

_Why:_ see [what applications use most](domain/knowledge.md#what-applications-use-most)
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
    `idempotent: true`. Otherwise it stays unknown until an operator [resolves](reference/processes.md#resolve) it.
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

_Why:_ see [knowledge](domain/knowledge.md#logic-in-configuration) and
[D7](#d7-custom-code-lives-in-extension-packages--agreed). Some logic does not
fit expressions, and a package gives it a tested, versioned home. Pinning the
exact version in the release keeps a running process on the code it was
compiled against. Staging every call like a connector call in
[D25](#d25-connectors-deployment-bindings-and-secrets--agreed) keeps custom
code out of database transactions, and gives retries and unknown outcomes one
rule. The capability limits stop mistakes, not hostile code, so the docs say
plainly that a package is trusted code.

## D29. Release pinning and text resources — Agreed

Running work stays on its release, including its tasks, forms, texts and
package versions. Roles and policies still come from the active release. See [releases and running work](reference/processes.md#releases-and-running-work)
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

_Why:_ [D11](#d11-durable-process-engine--agreed) and
[execution](domain/knowledge.md#execution) keep running work on the version
it started with. Pinning texts keeps removed keys readable on old tasks.
[D24](#d24-roles-and-policies--agreed) keeps authorization current, so an old
task cannot preserve a loosened policy. Checking pinned package versions at
start extends [D28](#d28-extension-packages--agreed): a host cannot rebuild
work without the code its release pins.

## D30. Schema migrations — Agreed

Activation applies the `renameField`, `dropField`, `dropEntity`, `fill` and
`convert` migrations once per tenant. The start lock and the old-release work
check are built too. The recompute of computed fields at activation is built,
and so is its use by a `fill` or `convert`, including one on a child entity.
Migrations
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
  - For each such release, `AXC0178` names the migration, the
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
- **Computed fields:** a `fill` or `convert` of a field that a computed field
  reads recomputes the changed rows, and their owners for changed child rows,
  as [D33](#d33-recomputing-stored-computed-fields--agreed) describes. Rule
  calls in computed fields remain unbuilt (`AXC0050`), so that case describes
  future behaviour.

_Why:_ [D11](#d11-durable-process-engine--agreed) and
[D29](#d29-release-pinning-and-text-resources--agreed) keep running work on
its release. Destructive changes must wait until that work stops reading
the old columns. The start lock keeps that check true until activation
commits. [D17](#d17-entity-logic-and-child-collections--agreed) stores computed
values with their records, so migrations must recompute affected values, as
[D33](#d33-recomputing-stored-computed-fields--agreed) describes.
[Execution](domain/knowledge.md#execution) rules out
partial commits that leave storage and the active release disagreeing.

## D31. Separate database roles — Agreed

Each tenant gets two database roles, one for runtime access and one for
schema changes. Runtime code can then read and write data, but it cannot
change the schema, a stored release, the active release or the provisioning
records. The server reads both connection strings, startup activation uses
`SchemaConnectionString`, and the server grants the runtime database role its
rights at every start. The server and the worker refuse to start when the
runtime database role is, or is a member of a role that is, a superuser, owns
a schema or may create objects in one.
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
    whole life while tenants come from the `Tenants` section.
  - Under `Tenancy:Source=directory` only the admin host
    uses it, see [D43](#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed).
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
  - The schema database role grants these rights in a migration, or in a grant
    step that runs with the migrations. The runtime database role name comes
    from the user of `ConnectionString`.
  - The start stops when the runtime database role is a superuser, owns the
    `axis` or `entities` schema, or may create objects in either, or is a member
    of a role that is or may. Membership counts whatever its `INHERIT` option.
- **Stored releases:** a trigger refuses every `UPDATE`, `DELETE` and
  `TRUNCATE` on the three release tables, for every database role. It replaces
  the guard that only EF Core change tracking gives today. A stored release can
  no longer be deleted at all, and cleaning up old releases needs a later
  decision.
- **Creating the database roles:**
  - Axis creates no database roles or databases. Whoever installs Axis does,
    while tenants come from the `Tenants` section.
  - Under `Tenancy:Source=directory`, `tenant create`
    creates them, see
    [D43](#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed).
  - Docker Compose, `scripts/dev.sh`, `scripts/e2e.sh` and the test fixtures
    create two database roles, `axis_schema` and `axis_runtime`, with
    development passwords.

_Why:_ [D10](#d10-tenant-aware-from-day-one-full-tenancy-later--agreed) puts
each tenant in its own database, so a tenant's database role is the last
barrier between its data and its schema.
[Execution](domain/knowledge.md#execution) and security rules need runtime code
to be unable to change the schema, a stored release, the active release or the
provisioning records that the next activation plans from. A separate schema
database role makes that a property of the database, not of the code.

## D32. Test scenarios — Agreed

A `scenario` resource is a configuration-level test that runs against its own
temporary tenant database. It seeds data, runs actions as
given users and asserts the outcomes, including denied actions. See
[configuration](reference/configuration.md#test-scenarios) for the resource
shape and a purchase request example.
The owner confirmed these points for issue #415 on 2026-10-16.
The owner confirmed the points on advancing time for issue #481 on 2026-10-17.

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
- **Advancing time**:
  - A new step, `advanceTime`, has `by`, a positive ISO 8601 duration such as
    `P1D` or `PT2H`.
  - It needs the scenario's `now`. A scenario without `now` that uses it
    fails to compile.
  - It moves `now` forward by `by`. Every later step reads the new time.
  - The runner then runs every work item that is due at the new time, until
    none is due. Schedule triggers and their fan-outs are included. The limit
    of 1,000 work items applies per step.
  - Schedules catch up as on a worker after an outage. Only the latest
    occurrence at or before the new time runs, once. The rules of
    [schedules](reference/processes.md#schedules) apply unchanged. To fire a
    daily schedule three times, a scenario advances one day three times.
  - Time never goes back. No step sets an earlier time.
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
  - Studio runs them through the publisher, see
    [D55](#d55-running-test-scenarios-from-studio--agreed).
- **Limits:**
  - **Outside the release.** Scenarios do not travel with a release, so a
    scenario always tests the folder, not a stored release.
  - **Stubs only.** Scenarios never call a real external system, and never
    run real package code. Package operations get unit tests in their own
    project.
  - **Time moves forward only.** A scenario starts at its `now`, and only
    `advanceTime` moves it, forward.
  - **New project.** `Axis.Scenarios` is a new host next to the server and
    the worker.

_Why:_ [D5](#d5-configuration-is-authored-as-files-first--agreed) makes
configuration files the source of truth, so tests over them are files too and
CI can run them. A scenario sets users and roles itself, so it checks
[D24](#d24-roles-and-policies--agreed) denials without an identity provider.
A unique database that the runner always drops keeps a failed run from
leaving data behind or touching a real tenant. The work item limit stops a
looping process from hanging CI. Advancing time lets CI test schedule
triggers. See
[testing applications](domain/knowledge.md#testing-applications).

## D33. Recomputing stored computed fields — Agreed

A stored computed field stays correct when its expression or its inputs
change. One recompute covers a changed expression, a computed field added to a
table with rows and a migration `fill` or `convert`. Small tables recompute at
activation. Large tables recompute in the worker, in batches. The recompute at
activation of a changed or added computed field, and of the rows a migration
`fill` or `convert` changed, is built, and so are the inline limit, the
pending state, the worker backfill, a newer activation while a field is
pending, and the writes of older releases. A single read, the response of an
update, the engine's reads of a subject record and process start conditions
compute a pending field on the spot and store nothing. The record API and the
process endpoints refuse a sort, a search or a policy filter that reads a
pending field with `409`. Data sources refuse the same way, and a schedule
fan-out over a pending field waits for the backfill. See
[storage](reference/storage.md#recomputing-computed-fields) for the steps and
a walkthrough.
The owner confirmed these points on issue #473 on 2026-10-17.

- **One mechanism for three cases:**
  - A changed computed expression.
  - A computed field added to a table with rows.
  - A migration `fill` or `convert` of a field that a computed field reads.
- **Small tables at activation:**
  - When the affected rows are at most the inline limit, activation
    recomputes them in its own transaction, using the release being
    activated, as D30 says.
  - The limit is the server setting `Activation:InlineRecomputeLimit`. It
    defaults to 10,000 affected rows.
- **Large tables in the worker:**
  - Above the limit, activation marks the computed field **pending** and
    commits.
  - The worker then recomputes the affected rows in batches. Each batch runs in
    its own transaction, with the release being activated.
- **Release stamp:**
  - Each row records the release that computed it. A create, and an update
    that sends every child collection whose child entity has computed fields,
    recompute with their release, so they stamp that release.
  - An update that leaves out such a collection did not recompute its rows. It
    keeps the stamp only when it already names the update's release, and
    otherwise sets it to `NULL`, which means the row is not known to be up to
    date.
  - The backfill works until no row of the table carries an older stamp.
  - A write made under an older release, by work pinned to it, stamps the older
    release. The backfill then takes that row again.
- **Concurrent writes:** a batch updates a row only if its version is
  unchanged since the batch read it. A batch never overwrites a concurrent
  write.
- **Reading one record:** while a field is pending, the record API computes it
  on read for a row with an older stamp.
- **Row errors:** a row whose expression fails at run time gets `NULL`. The
  field and the error are recorded, for operators to see and fix. The work
  goes on.
- **Newer release:** activating another release while a field is pending makes
  the backfill continue with the newest release.
- **Filters and sorts:** a filter or sort on a pending field is refused with
  an error that names the field and says it is being recomputed. It is not
  answered with a flag.
- **Pending is accepted:** for a large table, a short, clearly marked period
  in which lists may not reflect the new expression is accepted.

_Why:_ [D17](#d17-entity-logic-and-child-collections--agreed) stores computed
values with their records, so a release that changes them must correct the
stored values. [D29](#d29-release-pinning-and-text-resources--agreed) keeps
running work on its release, so a row can be written under an older release
after activation. The stamp finds those rows.
[D30](#d30-schema-migrations--agreed) recomputed in the activation
transaction, which does not scale to large tables and stopped activation on one
row error. [Execution](domain/knowledge.md#execution) asks that stored state
and the active release not disagree silently. A list that filters on old values
without warning is that failure, so Axis refuses it.

## D34. Process inspection — Agreed

Authorized users inspect process instances: where an instance is, what it did,
with which inputs, and why it failed or waits. The design is built. See [processes](reference/processes.md#process-inspection) for the
endpoints, the order of checks and the rules that hide values.
The owner confirmed these points on issue #477 on 2026-10-17, with two
changes. The changes are the masking bullet, which covers every entity, and
the step error bullet.

- **Who may inspect:**
  - A `process` grant gets a new action, `inspect`, next to `start`.
  - Without an `inspect` grant on the process, every inspection request is
    `403`.
  - An instance whose subject record the user may not read does not exist for
    the user, so it is `404`. This is the same rule as a start.
- **Instance list:**
  - `GET /api/apps/{app}/processes/{process}/instances` lists the process's
    instances, newest first and paged.
  - The filters are `state` and `subject`, a record id.
  - Each row has the id, the subject id, the state, the current step, the
    release, and the created and ended times.
- **Instance detail:**
  - `GET /api/apps/{app}/processes/{process}/instances/{id}` gives the graph
    of the release the instance is pinned to, with its steps and their
    transitions.
  - It gives the path: the history rows in order, each with the step, the
    times, the decision, the outcome and the error.
  - It gives the tasks, with the step, the assignee, the state, the outcome
    and who completed it.
  - It gives the connector and package calls, with their state and attempts.
- **Attempts:**
  - A new table, `axis.process_call_attempts`, holds one row per attempt and
    per lookup of a connector or package call.
  - A row has the call, its number, the start and end times, the outcome
    class, the HTTP status and a fixed error text.
  - It never holds a request or response body.
- **Call input kept:** a connector call keeps its input after it settles, so
  inspection shows what was sent.
- **Values and masking:**
  - Inputs and outputs show only values of fields the user may read
    ([D24](#d24-roles-and-policies--agreed) field lists).
  - A call's input or output is shown only when the user may read every field
    that the call's mapping reads or writes. This covers any entity, not only
    the subject entity.
  - The fields counted are every field the mapping reads or writes, on any
    entity. That includes fields reached through reference paths, where every
    field along the path counts, and fields reached through named rules,
    transitively. A child field counts through the owner's child-collection
    grant.
  - Otherwise the whole value is left out and marked hidden. This fails
    closed: a partly readable input is never shown.
- **Sensitive flag:**
  - A field can declare `"sensitive": true`.
  - Its value never appears in inspection, for any user.
  - A call input that reads it is hidden.
  - The record API still shows it to users whose grants cover it.
- **Step errors:**
  - History stores a step error as the exception type, such as
    `System.DivideByZeroException`.
  - Or it stores a fixed text that names the step, the property, the field
    pointer or the message key.
  - It never stores the exception message or a value.
- **Widget:**
  - A new `processInspector` widget names one process.
  - It lists the instances and opens one, showing the graph with the path
    drawn on it.
  - A page that holds it is authorized like any page. The server checks every
    request.
- **Record history:** the record history links a process audit record to its
  instance when the user may inspect that process.

_Why:_ [Product goal 4](product-brief.md#product-goals) asks that people see
what a process did and why it failed. The engine stores that, but no API shows
it. [D24](#d24-roles-and-policies--agreed) grants decide what a user may read,
so inspection follows them and fails closed.
[D29](#d29-release-pinning-and-text-resources--agreed) pins an instance to its
release, so the graph comes from that release while grants come from the
active one. [Operations](domain/knowledge.md#operations) asks that sensitive
fields are masked in inspection by a field flag. The `sensitive` flag keeps
that rule.

## D35. Operator retry, cancel and resolve — Agreed

Authorized users can retry a failed process instance, cancel running or
waiting work, and resolve a call whose outcome is unknown. Each is an audited
command that needs its own grant. Cancel and retry are built. Resolve is
built for connector and package calls. See
[processes](reference/processes.md#operator-commands) for the routes, the
states each command accepts, the errors and a walkthrough.
The owner confirmed these points on issue #478 on 2026-10-17. The owner made
two changes to the proposal, which are marked below.

- **Who may act:**
  - A `process` grant takes three new actions, `retry`, `cancel` and
    `resolve`, next to `start`. Each is checked on its own. None implies
    another, and none implies a right to inspect the instance (#477).
  - The subject record must be readable by the user, as for a start.
    Otherwise the instance is `404`.
  - Each command takes a required `reason` of at most 500 characters.
  - Each command writes a history row and an audit record with the user, the
    reason and the instance: `process.retried`, `process.cancelled`,
    `connector.callResolved` or `operation.callResolved`. A call resolved as
    failed also writes `process.failed`. The actor is the user, not `system`.
  - The reason is stored only in the audit record's details, not in step
    history.
- **Retry:**
  - It takes only a `failed` instance. Any other state is `409`.
  - The instance stays on its pinned release.
  - The step that failed runs again, reading the subject record as it is now.
  - A call that failed is staged again under the **same operation identity**,
    with its attempts counted from zero. The system's idempotency key then
    stops a second effect.
  - The instance goes back to `running`, and a call step makes it `waiting`
    again. When another instance of the same process now runs for the record,
    the retry is `409`.
  - **Start lock (changed by the owner):** retry takes the application's start
    lock as the first statement of its transaction, as a start does. So it
    cannot race the old-release check of a destructive migration. A change of
    active release under the lock runs the command again, and three changes
    are a `503`.
  - A retry is also `409` when an applied migration has renamed, dropped or
    converted a column that the instance's pinned release uses. The
    old-release check counts only `running` and `waiting` instances, so a
    failed instance never blocks a destructive migration, and the retry would
    run old steps against a changed table.
- **Cancel:**
  - It takes a `running` or `waiting` instance. It becomes `cancelled`, a new
    end state that does not block a new start.
  - Its open tasks become `cancelled`, a new task state. They leave every
    inbox.
  - A `pending` call becomes `cancelled`, and its work item is removed.
  - A call that is `sending` or `unknown` makes the cancel `409`. The system
    may have acted, so the call must settle or be resolved first. Cancel never
    forgets an external effect.
  - A step that a worker is running when the cancel commits cannot commit
    after it. The instance revision fences it.
  - Cancel does not undo steps that already ran. There is no compensation, as
    D25 says. It comes later.
  - Cancel is the way to drain old-release work before a destructive
    migration.
- **Resolve:**
  - It takes only an `unknown` call, on an instance that is `waiting` on the
    call's step.
  - `"outcome": "succeeded"` with `output` values settles the call as
    succeeded. The output is checked against the operation's declared output,
    as a response would be. The instance moves on.
  - `"outcome": "failed"` settles the call as failed, and the instance
    becomes `failed`. A later retry stages the call again under the same
    identity.
  - **Start lock:** resolve takes the start lock too, because its output
    fields can set off update triggers, as a task completion does.
  - An `output` that does not fit, or an output write that the record refuses,
    is a `400`. Nothing changes and the call stays `unknown`. An operator's
    typing mistake does not fail the instance. The operator can still resolve
    the call as `failed` on purpose.
- **Where users see it:**
  - The process inspector widget (#477) shows Retry, Cancel and Resolve when
    the user's grants allow them. The server checks every request.
  - **Retry warning (changed by the owner):** the widget warns before a retry
    only when the failed call is a package call whose operation is not
    `idempotent: true`. A failed connector call is safe to resend, because
    every unknown outcome was resent under the idempotency key or reconciled.

_Why:_ [D11](#d11-durable-process-engine--agreed) makes the engine durable,
and a failed instance, a stuck call and old work that cannot be drained are
the places where durability needs a human decision.
[D24](#d24-roles-and-policies--agreed) makes every action a grant, so each
command gets its own. [D25](#d25-connectors-deployment-bindings-and-secrets--agreed)
and [D28](#d28-extension-packages--agreed) leave an unknown call for an
operator, and promise no second effect, so retry reuses the operation identity
and cancel refuses a call that may have acted.
[D30](#d30-schema-migrations--agreed) waits for old-release work before a
destructive migration, which needs a way to drain it, and the start lock keeps
a retry from racing that check.
[Operations](domain/knowledge.md#operations) says retry, cancellation and
resolution are authorized, audited commands.

## D36. Correlated logs, metrics and traces — Agreed

The server and the worker emit correlated logs, metrics and traces. Traces,
the stored `trace_id`, OTLP logs, the log scope and the problem details
`traceId` are built. So are the standard metrics and the work item duration,
process instance and call attempt metrics, and the work item gauges and the
computed fields gauge. So is the tenant database gauge of D37. See
[telemetry](architecture.md#telemetry) for the spans and what telemetry never
holds.
The owner confirmed these points on issue #479 on 2026-10-17.

- **Standard:**
  - The server and the worker use OpenTelemetry for traces, metrics and logs,
    with W3C trace context.
  - Export is OTLP, set by the standard `OTEL_EXPORTER_OTLP_ENDPOINT`
    variables.
  - With no endpoint, nothing is exported and logs still go to the console.
  - Docker Compose and `scripts/dev.sh` start no collector.
- **Traces:**
  - The standard ASP.NET Core, HTTP client and Npgsql instrumentation trace
    incoming requests, outgoing HTTP calls and PostgreSQL commands.
  - Axis adds spans for activation, a work item run, a process step, a
    connector call attempt, a lookup and a package call.
  - A work item stores the trace context of the transaction that queued it.
    Its run starts a new trace with a **link** to that one, not a child span.
    Instances wait for days, so one trace per run stays readable.
  - Connector calls send `traceparent` to the external system.
  - Outgoing HTTP spans drop `server.address`, `server.port` and `url.query`.
    A connector call's `url.full` is the operation's path template. Other
    outgoing spans have no `url.full`.
- **Npgsql:** spans keep the statement text without parameter values.
  Npgsql parameter logging stays off.
- **Stored correlation:**
  - Each history row and each call attempt row stores the `trace_id` of its
    run.
  - Every problem details answer carries `traceId`, so a user can report it
    and an operator can find the run.
  - The process inspector shows the trace id of each history row and call
    attempt.
- **Metrics:**
  - The standard ASP.NET Core, HTTP client and .NET runtime metrics.
  - `axis.work_items.due`, a gauge of due items per kind.
  - `axis.work_items.oldest_due_age`, the age in seconds of the oldest due
    item per kind. Stuck work shows here.
  - `axis.work_items.duration`, a histogram per kind and outcome.
  - `axis.process.instances.ended`, a counter per process and end state.
  - `axis.calls.attempts`, a counter per connector or package, operation and
    outcome class.
  - `axis.computed.pending_fields`, a gauge of pending computed fields, see
    [D33](#d33-recomputing-stored-computed-fields--agreed).
  - `axis.tenant.database.available`, a gauge per tenant, 1 when its database
    answered the server's last readiness probe and 0 when it did not.
- **What telemetry may hold:**
  - Attributes name the tenant, application, process, step, connector,
    operation and work item kind.
  - Telemetry never holds a field value, a secret, a binding value, a request
    or response body, or a user id.
  - Log messages from expressions and steps use fixed texts with names, not
    values. A field marked sensitive is therefore never logged.
  - Finding one user's failed request goes through the stored history, not
    the traces.

_Why:_ [Operations](domain/knowledge.md#operations) asks that every request,
instance, step attempt and connector call carry correlation identifiers, so an
operator can go from a user-visible error to the exact attempt and its logs.
It also asks for log protection and for durable history instead of sampled
telemetry, so telemetry names things and never holds values, and the stored
history stays the source of truth. Standard OpenTelemetry lets any collector
receive the data. [D11](#d11-durable-process-engine--agreed) keeps step and call
history in the tenant database, and
[D25](#d25-connectors-deployment-bindings-and-secrets--agreed) keeps secrets in
bindings, so the `trace_id` links them without copying their
content.

## D37. Health checks for the server and the worker — Agreed

The server's readiness covers the platform database and startup activation, and
one tenant database that is down makes the server degraded, not unhealthy. The
worker gets its own health endpoints. The server's readiness and the worker's
health endpoints are built. So is the tenant metric. See
[health checks](architecture.md#health-checks) for the answers
of each endpoint.
The owner confirmed these points on issue #480 on 2026-10-17.

- **Server:**
  - `/health/live` stays as it is: the process answers.
  - `/health/ready` is healthy when the platform database answers and startup
    activation has finished for every tenant.
  - A tenant database that does not answer makes the server **degraded**, not
    unhealthy. Readiness still passes, so one tenant cannot take the others
    offline. The tenant is named in the logs and in a metric, not in the
    answer.
  - The answer holds only the overall status. It names no tenant, database or
    error, because `/health` needs no session.
- **Worker:**
  - The worker serves `/health/live` and `/health/ready` on its own address,
    the setting `Health:Urls`. The default is `http://127.0.0.1:5208`. It
    serves nothing else.
  - Ready means every tenant database is migrated and the worker's loop has
    polled within twice `Worker:LeaseDuration`, one minute by default. A poll
    interval would be too short, because one work item can run for up to a
    lease.
  - A worker loop that stops polling makes `/health/live` fail, so an
    orchestrator restarts it.
  - Docker Compose gives the worker a health check on `/health/ready`.
- **Tenant directory:**
  [D42](#d42-tenant-directory-in-the-platform-database--agreed) adds a
  Degraded state on both hosts for a failed directory refresh. The
  worker is then no longer never Degraded. Both are built.

_Why:_ [Operations](domain/knowledge.md#operations) asks that failed and stuck
work is visible. Without a health endpoint, a stuck worker shows only when
work piles up, and Compose cannot wait for it. The server's
readiness ignores tenant databases and activation. Degraded, not unhealthy,
follows [D10](#d10-tenant-aware-from-day-one-full-tenancy-later--agreed): one
tenant must not take the others offline. `/health` needs no session, so the
answer names nothing that an open endpoint should not show.

## D38. Studio edits application folders through an authoring API — Agreed

Studio edits an application's resource files through an authoring API
on the server. The routes that list applications, list, read, patch, create and
delete resources and give diagnostics are built, and so are the Studio page's
list, search, editor, diagnostics panel, Uses and Used by lists, the delete
button with its warning, and the reconnect message. A diagnostic also opens its
resource at its path. See the
[authoring API](reference/authoring.md) for the routes and answers, the
[Studio settings](reference/configuration.md#studio) and the
[Studio section of the frontend](reference/frontend.md#studio).
The owner confirmed these points on issue #569 on 2026-10-18.

- **Where edits live:**
  - Studio edits the application folders on the server's disk. They are the
    same JSON files that are kept in git. There is no draft store in the
    database.
  - Studio never activates a release in the server process.
    [D47](#d47-publishing-from-studio--agreed) replaces the earlier "Studio
    never activates a release" point: a separate publisher runs a publish
    request from Studio. Under `scripts/dev.sh`, a saved
    file restarts the server, which activates the folder, as it does today.
    Elsewhere a folder is committed and deployed as today.
- **Who may author:**
  - The server setting `Studio:Folders` lists the application folders Studio
    may edit, like `ActivateOnStartup`. It is empty by default, so Production
    has no Studio. The Development settings list the purchase request sample.
    New applications that Studio creates live in `Studio:ApplicationsPath`
    _(planned for M11)_, see
    [D51](#d51-creating-applications-and-resources-from-studio--agreed).
  - The server setting `Studio:AuthorRole` names a roles claim value. Only a
    signed-in user whose roles claim holds it may use Studio. With no value,
    nobody may. Application policies play no part. It is the first
    permission outside application policies.
  - The development realm gets an `author` role and a user `hoa` who holds it.
  - `GET /api/me` gains a `studio` boolean. It is true when `Studio:Folders`
    is not empty and the user holds the author role. It extends the current
    user shape of [D23](#d23-sign-in-through-oidc-and-the-bff-session--agreed),
    and the header uses it to show the Studio link.
- **Authoring API** under `/api/studio/apps/{application}`:
  - It lists the folder's resources, each with its `id`, `kind`, `name`, file
    and `version`. It reads one resource, with its content and `version`.
  - It creates a resource, patches one with JSON Patch (RFC 6902), and deletes
    one.
  - `version` is the SHA-256 of the file's bytes, sent as `ETag`. A write
    sends it in `If-Match`. A stale version is `409`.
  - No session is `401`, no author role is `403`, and an unknown application
    or resource is `404`. Unsafe methods need the CSRF header, as elsewhere.
  - A patch may not change `id` or `kind` (`400`). A schema violation or a
    compile error does not block a save. It is a diagnostic.
  - Files are written atomically, with 2-space JSON, LF and a final newline,
    keeping key order. A new file goes in the kind's folder, named after the
    resource. No path leaves the folder, and only `*.json` files are touched.
  - Every write answers with the folder's diagnostics. `GET .../diagnostics`
    compiles the folder in memory against the installed packages, with the
    same codes as activation. It touches no database.
  - A write takes a lock in the platform database, so the
    version check holds across server replicas. See
    [D49](#d49-several-server-replicas-behind-a-tls-proxy--agreed).
- **Studio in the SPA:**
  - A platform route, `/studio`, outside every site. The header shows it to
    authors only.
  - A resource list grouped by kind, with search by name and id.
  - An editor: a form built from the kind's JSON Schema, and a JSON view of
    the same resource.
  - A diagnostics panel. Each diagnostic opens its file at its path.
  - [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed) brings visual page, form and
    process designers into scope, and plans publishing from Studio in M10.
    [D47](#d47-publishing-from-studio--agreed) replaces the "Studio never
    activates a release" point of this decision.
  - Its texts are platform texts under `shell.studio.*`, in `en` and `vi`.

_Why:_ [D5](#d5-configuration-is-authored-as-files-first--agreed) says
authoring APIs use the same contracts and that a Studio writes the same
resources. [Authoring and packaging](domain/knowledge.md#authoring-and-packaging)
forbids replace-all edits and asks for resource versions, so writes are
patches that carry a version. Editing the folders on disk keeps git the source
of truth. It also keeps the schema role out of requests, as
[D31](#d31-separate-database-roles--agreed) asks, because only startup holds
it and Studio never activates in the server process. The cost was that Studio was a development tool.
[D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed) and
[D47](#d47-publishing-from-studio--agreed) later added publishing from Studio, so Studio also serves a shared server. Authoring on a shared server, with drafts and publishing from Studio,
would need a database store and run-time activation. That could come later. An author can change every policy of an application, so no
application policy can guard authoring. That is why the author role is an
installation-level setting.

## D39. Reference index and where a resource is used — Agreed

Every compile collects each reference it resolves into a reference
index. Studio reads it through two authoring API routes and shows a **Uses**
list and a **Used by** list for each resource. The compile already builds the
index with every relation of the design. Studio's lists and delete warning are
built. See the [reference index](reference/configuration.md#reference-index),
the [authoring API](reference/authoring.md#references) and the
[Studio section of the frontend](reference/frontend.md#studio).
The owner confirmed these points on issue #570 on 2026-10-18.

- **Reference index:**
  - Every compile builds the index, in the same pass and in memory. It is not
    stored in the release.
  - Nodes are resources (id, kind, name, file), text keys, extension packages
    and modules.
  - Edges go from a resource and a JSON Pointer path in it to a node, with a
    relation such as `field target`, `data source entity`, `task form`,
    `rule call`, `label text`, `grant` or `widget process`.
  - It holds every reference the compiler resolves: the entity, data source,
    form, page, process, event, endpoint, connector, role, rule, sequence,
    package and module references, rule calls inside expressions, and the
    text key of every label.
  - An unresolved reference is a diagnostic, not an edge. A resource with
    errors keeps the edges that did resolve.
- **Authoring API:**
  - `GET /api/studio/apps/{application}/resources/{id}/references` answers
    `uses` and `usedBy`. Each entry has the other resource, the file, the path
    and the relation.
  - `GET /api/studio/apps/{application}/texts/{key}/references` answers where a
    text key is used.
  - Both follow the `401`, `403` and `404` rules of the rest of the authoring
    API.
  - The resource list shows each resource's `usedBy` count, so Studio can warn
    before a delete. The warning does not block the delete. The next compile
    reports what broke.
- **Studio:**
  - Each resource shows a **Uses** list and a **Used by** list. A Used by entry
    opens the other resource at the path of the reference. A Uses entry opens
    it at its top. An entry expands one more step on request.
  - There is no graph drawing library. A drawn graph can come later
    without changing the index. M11 adds one for the process graph, see
    [D53](#d53-typed-editors-and-the-process-graph-in-studio--agreed).
- **Cost:**
  - The index adds at most 30% of compile time, or 300 ms when that is more.
    A where-used answer for an application of typical size returns within 10
    seconds. The [reference index](reference/configuration.md#reference-index)
    states both bounds.

_Why:_ [Typical application size](domain/knowledge.md#typical-application-size)
says authoring at 30–90 entities and 150–250 data sources needs a dependency
graph and "where is this used", because lists do not work at that size. The
compiler already resolves every reference, but spreads them over the per-kind
models, so one index collects them and gives the reverse direction. Building it
on every compile keeps it out of the release and costs nothing at run time. A
later feature that needs it at run time would build it again from the release.
Two lists that expand stay accessible and need no new frontend library. Warning
before a delete without blocking it follows
[D38](#d38-studio-edits-application-folders-through-an-authoring-api--agreed),
where a broken resource is a diagnostic and never blocks a save, and
[D5](#d5-configuration-is-authored-as-files-first--agreed), where the files stay
the source of truth.

## D40. Reusable modules — Agreed

Modules are versioned folders of resources that applications reuse.
The manifest, installation, declaring, pinning, the module files stored in
the release, and the compile of a module's entities, sequences, seeds, events,
data sources and forms with ids derived from the application are built. So are the
`target`, `sequence` and `dataSource` references, the module texts that join the
application's texts by locale, a module's rules and the calls to them from
application expressions, module validations that call rules of their own
module, and the codes `AXC0207` to `AXC0214`. So are activating
module entities, seeding and the record API for module entities. So is the
sample module `departments`. See the
[Modules section](reference/configuration.md#modules) of the configuration
reference for the manifest, the diagnostic codes and the rules, and
[storage](reference/storage.md) for the derived ids. The owner
confirmed these points on issue #571 on 2026-10-18.

- **What a module is:**
  - A folder of resources with a manifest, `module.json`, of kind `module`.
    The manifest has an `id` (lower-case letters, digits and `-`), a SemVer
    `version`, a `label` and optional `packages`.
  - A module may hold entities, data sources, rules, sequences, forms, events,
    texts and seeds. Processes, pages, roles and policies stay out, because
    they would need application roles and sites to be known inside the module.
    Any other kind in a module is a diagnostic.
  - A module does not depend on other modules.
- **Installation and pinning:**
  - Modules are installed per installation under
    `Modules:Path/<id>/<version>/`, as packages are. The server, the worker and
    the scenario runner read them.
  - `application.json` declares `modules`, each with an `id` and a version
    range of the same forms as packages: `x.y.z`, `^x.y.z` or `~x.y.z`.
  - Compilation pins the highest installed version in each range. It copies the
    module's files into the release under `modules/<id>/`, so the content hash
    covers them.
  - A release keeps working when its module version is later removed from the
    installation, because the release holds the files.
  - A newer installed version in range gives a new release at the next
    activation, as for packages.
- **References:**
  - An application resource names a module resource as `<module id>/<name>`,
    for example `departments/Department`.
  - A module resource names only resources of its own module.
  - A reference to an undeclared module, or to a name the pinned version lacks,
    is a diagnostic.
  - The record API writes a module entity's name as two path segments,
    `{module id}/{entity name}`, for example
    `/api/apps/{app}/entities/departments/Department/records`.
  - An application expression calls a module rule as `<module id>:<name>(…)`,
    for example `departments:IsManager(x)`, with no spaces around the `:`. `/`
    is already division and `.` is already a path step, so the `:` keeps every
    expression that parses today unchanged. The owner confirmed this syntax on
    issue #617 on 2026-10-18. A call to a rule of an undeclared module is
    `AXC0207`.
  - The data source endpoint writes a module data source's name as two path
    segments, `{module id}/{data source name}`, for example
    `/api/apps/{app}/data-sources/departments/Departments/rows`. This matches
    how the record API names module entities. Only that form reaches it.
- **A copy per application:**
  - Module entities, sequences and seeds belong to the application that uses
    the module. Applications never share stored data. Shared master data would
    need cross-application reads and grants, which this design leaves out.
  - The id of a module resource inside an application is derived from the
    application id and the module resource id, as a UUID v5. So two
    applications in one tenant get separate tables and separate counters.
  - A module upgrade that is not additive for its entities is a diagnostic.
    Modules ship no migrations, so an application also cannot stop using
    a module once its tables exist.
- **Texts:**
  - A module's text keys start with `<module id>.`. A module ships one text
    resource per locale.
  - Every locale of the application needs the module's texts for that locale.
    A missing one is a diagnostic.
- **Packages:** `module.json` keeps an optional `packages`, with the same
  shape as in `application.json`. No kind a module may hold can call a
  package operation yet, because only process steps can.
- **Sample:** `samples/modules/departments`, version `1.0.0`. It has the
  `Department` entity with code, name and manager, a data source over it, a rule
  that tells whether a user manages a department, seed departments, and `en`
  and `vi` texts. The build, dev, Compose and E2E scripts install it under
  `artifacts/modules`, as they do the sample package. The second sample
  application uses it. The purchase request sample keeps its own `Department`
  entity.

_Why:_ [Authoring and packaging](domain/knowledge.md#authoring-and-packaging)
warns against hidden dependencies, so a module is declared, pinned and copied
into the release, and a missing name fails the compile. Reusing the ranges,
pinning and installation folder of
[D28](#d28-extension-packages--agreed) keeps one way to version reusable parts.
Entity tables are named after the entity id, so a module used by two
applications in one tenant would share one table. Deriving the id per
application removes that risk and keeps a module's id stable across its
versions. The cost is that each application holds its own data, and a module
can only grow additively until modules can ship migrations.

## D41. Leave requests as the second sample application — Agreed

Leave requests is the second, smaller sample application. It is built, and
every point below holds. See the
[Second application section](product-brief.md#second-application-leave-requests)
of the product brief for the data, flow, security, pages and scenarios. The
owner confirmed these points on issue #572 on 2026-10-18.

- **Application:** leave requests, in `samples/apps/leave-requests`. An
  employee asks for days off, and the department manager decides. It needs no
  platform code change.
- **Data:**
  - A leave request has a number from the sequence `LR-{yyyy}-{n:5}`, an
    employee, a department, a leave type (`annual`, `sick` or `unpaid`), a
    first day, a last day, days, a reason, a status and a decision comment.
    The reason is required and is the display field, because a sequence number
    cannot be required, so it cannot label a request in task lists.
  - Departments come from the `departments` module of
    [D40](#d40-reusable-modules--agreed), with its seed departments.
  - Days are computed as `daysBetween(firstDay, lastDay) + 1`. They count
    calendar days, including both ends. Weekends and holidays are not left out.
- **Flow:**
  - An employee creates a draft and submits it. Submitting checks that the
    last day is not before the first day. It starts the approval process only
    once.
  - The department manager gets a task and approves, returns or rejects.
  - A returned request can be edited and submitted again.
  - There is no finance step, connector or extension package.
- **Security:**
  - The roles are `employee` and `department-manager`. They are the Keycloak
    realm roles that the purchase request sample already uses, so the realm
    needs no change.
  - The sample reuses the development users: `anna` and `lan` are employees,
    `binh` and `chau` are managers.
  - An employee sees only their own requests. A manager sees the requests of
    their departments. Everything else is denied by default.
- **Pages:** My leave requests, the request form and My tasks, in a site at
  path `leave`.
- **Tests:**
  - Test scenarios cover approve, return then resubmit, reject and denied
    access.
  - One Playwright journey: `anna` submits, `chau` approves, and `anna` sees
    the request approved.
  - The server activates the sample next to purchase requests under
    `scripts/dev.sh`, Compose and E2E.

_Why:_ The sample is small. It uses tasks and policies as purchase requests
do, and it uses the departments module of [D40](#d40-reusable-modules--agreed).
It needs no package or connector. So it shows reuse without platform code. Any
platform change it needs is a gap to raise, not to hide in the sample.

## D42. Tenant directory in the platform database — Agreed

The server and the worker learn their tenants from a tenant directory in the
platform database, and pick up changes without a restart. The tenant catalog contract, the `platform` schema and the directory source
of the server and the worker are built. Under `directory` the server creates the
schema at start and reads the directory every `Tenancy:RefreshInterval`. It
serves new tenants, answers `503` for tenants that are not `active` and `404`
for removed hosts, and runs the role check of D31 on new tenants and on changed
database settings. The worker reads its `active` tenants, and retries the
first read until it works. It then reads the directory every
`Tenancy:RefreshInterval`, runs the role check of D31 on new tenants and on
changed database settings, claims no new work of tenants that are not `active`,
and is Degraded for a failed refresh. The server's Degraded readiness for a
failed refresh is built.
Both hosts,
under `configuration`, take tenants from the `Tenants` section, as in
[D10](#d10-tenant-aware-from-day-one-full-tenancy-later--agreed).
See [tenant directory](architecture.md#tenant-directory) for what each host
answers, [tenancy settings](reference/configuration.md#tenancy-settings) for
the settings and [platform database](reference/storage.md#platform-database)
for the tables and the encryption.
The owner confirmed these points on issue #639 on 2026-10-18.

- **Two tenant sources:**
  - The setting `Tenancy:Source` is `configuration` or `directory`. It
    defaults to `configuration`, which is today's behaviour. Development,
    `scripts/dev.sh`, Docker Compose and the tests keep it.
  - With `directory`, both hosts read tenants only from the platform
    database. A `Tenants` section is then ignored, and the start logs a
    warning when one is present.
  - Both sources answer through one tenant catalog contract, so request
    resolution, tenant connections, deployment bindings and sign-in do not
    know the source.
- **What the directory holds:**
  - It is a `platform` schema in the platform database, with EF Core
    migrations shipped with Axis. The server applies them at start, as it does
    for tenant databases.
  - A tenant has an `id` under the same rule as today, a `status`
    (`provisioning`, `active` or `suspended`), its hosts, its database, its
    OIDC settings and its deployment bindings.
  - Hosts are unique across all tenants, ignoring letter case.
  - An entry may name its main tenant, when it is a
    preview tenant. See [D48](#d48-preview-tenants--agreed).
  - The database is the server host, the port, the database name, and the
    runtime and schema user names.
  - Every entry has a `version` that grows on each change.
- **Secrets:**
  - Database passwords, the OIDC client secret and `secret` binding values are
    stored encrypted, never in plain text.
  - Encryption is AES-256-GCM with an installation key from the setting
    `Platform:SecretKey`, 32 bytes in base64. It comes from the environment or
    a mounted secret file, never from a committed file outside Development. A
    missing or invalid key stops the start.
  - Each encrypted value is bound to its tenant and field through the
    AES-GCM associated data: the tenant id and the field name. Someone with
    write access to the table cannot copy one tenant's secret onto another
    tenant, because it no longer decrypts.
  - No API, log, problem details or backup manifest holds a decrypted secret.
    A value is decrypted only at the moment of use, as `SecretValue` does
    today.
  - Rotating the installation key is out of scope. After a leaked key,
    every stored secret must be set again by hand.
- **Changes without restart:**
  - Both hosts read the directory again every `Tenancy:RefreshInterval`, 30
    seconds by default.
  - A new `active` tenant is served after the next refresh, and its OIDC
    client is registered then.
  - A `suspended` or `provisioning` tenant answers `503` problem details
    titled "This tenant is not available.". The worker claims none of its new
    work, and work already claimed finishes.
  - A removed host answers `404`, as an unknown host does today.
  - A refresh that cannot reach the platform database keeps the last good
    directory and logs an error. A tenant suspended during that outage keeps
    running until the next successful refresh.
  - A tenant that first appears at a refresh is served only after the runtime
    database role check of
    [D31](#d31-separate-database-roles--agreed) passes. Until then it answers
    `503`, like a suspended tenant.
  - A served tenant whose database host, port, name, runtime user or runtime
    password changes at a refresh answers `503` until the D31 check passes on
    the new settings. Then it gets a new connection pool.
- **Who writes the directory:** the server and the worker only read it. The
  admin host of
  [D43](#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed)
  writes it over `ConnectionStrings:PlatformSchema`, and migrates, grants and
  activates the databases of tenants.
- **Platform connection strings:**
  - `ConnectionStrings:Platform` becomes a role that may only read the
    `platform` schema. Under `directory`, both hosts need it. The worker has
    no platform connection today.
  - The server applies the platform migrations over a separate setting,
    `ConnectionStrings:PlatformSchema`. It uses it only at start, as D31 does
    for the tenant databases.
  - The read-only role gains `INSERT` on the data protection key table, and
    under `configuration` the server now creates the platform tables too. See
    [D49](#d49-several-server-replicas-behind-a-tls-proxy--agreed).
- **First load:** the server stops its start when it cannot load the directory
  the first time, because there is no earlier directory that is safe to serve.
  The worker waits and retries, and it is not ready until the first load
  works.
- **Readiness:**
  - When a refresh fails, `/health/ready` answers Degraded (`200`) on both
    hosts. Unhealthy would pull the hosts out of service, which defeats keeping
    the last good directory.
  - This lifts the rule of
    [D37](#d37-health-checks-for-the-server-and-the-worker--agreed) that the
    worker is never Degraded.

_Why:_ a new tenant, a suspended tenant or a changed secret must take effect
without editing the configuration of both hosts and restarting them.
[D25](#d25-connectors-deployment-bindings-and-secrets--agreed) has no secret
store, and provisioning creates database roles with generated passwords that
the hosts must read without a restart. Keeping `configuration` as the default
avoids rewriting every test fixture and development script, at the cost of two
code paths behind one contract. Encrypting each value with its tenant and field
bound in makes a copied value useless. Degraded readiness keeps the hosts
serving from the last good directory while it still shows the problem.

## D43. Tenant provisioning and migrations across tenant databases — Agreed

A platform operator creates tenants, migrates every tenant database and
activates releases with a command-line admin host, `Axis.Admin`. Tenants can still come from the `Tenants` section, where the
server migrates and activates at start, as in
[D31](#d31-separate-database-roles--agreed). See
[administration](reference/administration.md) for every command,
[database roles](reference/storage.md#database-roles) for who holds which
connection and [admin settings](reference/configuration.md#admin-settings)
for `Admin:ConnectionString`.
The owner confirmed these points on issue #640 on 2026-10-19.

- **Admin host:**
  - `Axis.Admin` is a console host that the platform operator runs. There is
    no HTTP API for provisioning.
  - It is the only host that writes the tenant directory of
    [D42](#d42-tenant-directory-in-the-platform-database--agreed). It writes
    it over `ConnectionStrings:PlatformSchema`, the role that owns the
    `platform` schema. It does not use the admin role for that.
  - It is the only host that holds an admin connection to the database server,
    and the only one that holds the schema database role of a tenant when
    tenants come from the directory.
  - `Admin:ConnectionString` names a database role with `CREATEDB` and
    `CREATEROLE`. It is not a superuser. The tool refuses a superuser and
    exits with code 2.
  - Every command writes one line per tenant and step. No command prints a
    password or a connection string.
  - Exit code 0 means every tenant succeeded. Code 1 means at least one tenant
    failed. Code 2 means a usage or settings error before any tenant work: an
    unknown option, a missing setting, a superuser admin role or an unknown
    tenant.
- **Creating a tenant:**
  - `tenant create <id> --host <host>... --oidc-authority <url>
--oidc-client-id <id>` takes the OIDC client secret from an environment
    variable.
  - It writes the directory entry as `provisioning` first. It creates the
    roles `axis_<id>_schema` and `axis_<id>_runtime` with generated
    passwords, and the database `axis_<id>`, owned by the schema role. It runs
    the grants of D31 and the migrations, checks the runtime role, records the
    schema version and sets the entry `active`.
  - A tenant id may be at most 50 characters. PostgreSQL names hold at most 63
    bytes, and `axis_<id>_runtime` adds 13 characters. A tenant id with a
    hyphen gives a quoted role and database name.
  - The new database goes on the same database server as
    `Admin:ConnectionString`. The directory records that server's host and
    port. Axis has one database server.
  - `--preview-of <main>` creates a preview tenant of the
    main tenant, and `--binding <name>=<value>` sets its `url` bindings. See
    [D48](#d48-preview-tenants--agreed).
  - Running it again for a `provisioning` tenant finishes what is missing. For
    an `active` or `suspended` tenant it fails and changes nothing.
- **Suspend, resume and list:**
  - `tenant suspend <id>` and `tenant resume <id>` set the status. Hosts pick
    the change up at their next refresh.
  - `tenant list` prints each tenant's id, status, hosts and schema version.
  - There is no tenant delete.
- **Migrations across tenant databases:**
  - `migrate` applies the grants and the configuration, data and processes
    migrations to every tenant database, or to one with `--tenant <id>`. It
    skips a tenant that is still `provisioning`.
  - Each tenant migrates on its own, in id order. A failed tenant keeps its old
    schema version and does not stop the others. `--parallel <n>` migrates up to
    n tenants at a time, and defaults to 1.
  - The operator runs `migrate` first and then deploys the new server and
    worker.
  - The server no longer migrates tenant databases under
    `Tenancy:Source=directory`.
- **Schema version:**
  - A tenant's schema version is the id of the newest EF Core migration across
    the configuration, data and processes contexts. Migration ids sort by time,
    so the newest one names the schema.
  - The directory stores it as text in `platform.tenants`. It is empty until
    the first migration.
  - Under `directory`, the server and the worker serve a tenant only when its
    schema version is the one they were built for. Otherwise the tenant's hosts
    answer `503`, as for a suspended tenant, and the worker claims none of its
    work. Between `migrate` and the deploy of the new hosts, tenants answer
    `503`.
- **Activation:**
  - `activate <folder> [--tenant <id>]` compiles a folder and activates its
    release for every `active` tenant, or for the one named tenant. It uses the
    same diagnostics, packages, modules, recompute limit and seeding as
    [startup activation](reference/configuration.md#startup-activation).
  - Each tenant succeeds or fails on its own.
  - It runs no EF Core migrations. A tenant whose schema version is not the
    admin host's fails with a message to run `migrate` first.
  - `ActivateOnStartup` is only for `Tenancy:Source=configuration`, where
    startup activation stays as it is, for Development, Compose and the tests.
    Under `directory` a non-empty setting stops the start.
- **Connections per host:**
  - Under `directory`, the server and the worker hold only runtime database
    roles for tenant databases. The server holds no tenant schema connection
    and runs no tenant migration or grant step.
  - The server keeps `ConnectionStrings:PlatformSchema` at start, for the
    platform migrations, as D42 says. "No schema connection" means no tenant
    schema connection.
  - The worker holds no schema connection of any kind.
- **Not in M9:** deleting a tenant, an HTTP provisioning API, rotating the
  generated database passwords and moving a tenant to another database server.

_Why:_ a command-line tool keeps database admin rights out of every
long-running host, and a web console for operators can come later. Tenants
migrate on their own, so one broken tenant database no longer stops the
others. The cost is that tenants can sit on different schema versions, so a
host refuses a tenant whose version it does not expect. A version taken from
the code needs no number that someone must remember to raise. Deleting a tenant
destroys data, so suspend is used instead. Role names that derive from the tenant id
make the roles easy to find, at the cost of quoted names for ids with a hyphen.
Writing the directory over the schema role of the `platform` schema keeps the
admin role limited to creating databases and roles.

## D44. Packaging for on-premises and cloud installation — Agreed

Axis ships as three production images, a Compose example for one machine and a
Helm chart for Kubernetes. The production images, the Compose example and the
chart are built. The development `Dockerfile` default
target and `compose.yaml` hold the samples and development credentials, and
they are not for production. See the
[installation guide](reference/installation.md) for the steps and every
setting, and [administration](reference/administration.md) for the admin
commands.
The owner confirmed these points on issue #641 on 2026-10-19.

- **Production images:**
  - There are three: `axis-server`, `axis-worker` and `axis-admin`. They come
    from new targets of the `Dockerfile`. The development images stay as they
    are.
  - They hold no sample, no sample extension package, no sample module, no
    development realm and no credential. They run the `Production`
    environment.
  - They run as a non-root user and work with a read-only root file system.
  - They read settings only from environment variables and from files in
    `/run/secrets`. Each file is named after a setting in its environment
    variable form, such as `Platform__SecretKey`, and its content is the value.
  - Extension packages are mounted at `/extensions` and modules at `/modules`.
    Application folders for `activate` are mounted into the admin container.
  - `axis-admin` holds the PostgreSQL 18 client tools, for backup and restore.
    PostgreSQL 18 is the supported server version.
  - Every image carries the Axis version in the OCI label
    `org.opencontainers.image.version`. `axis-admin version` prints it.
- **On premises:**
  - `deploy/compose/` holds a production Compose example: the server, the
    worker and a one-shot admin `migrate`. PostgreSQL and the OIDC provider
    are external. Secrets come from files under `secrets:`. It sets
    `Tenancy:Source` to `directory`.
  - A smoke test starts it with a test PostgreSQL and Keycloak. It creates a
    tenant with the admin tool and activates the leave request sample,
    `samples/apps/leave-requests`. It checks that the tenant's host serves its
    site at `/leave` and that both hosts are ready.
  - The leave request sample needs the `departments` module. The smoke test
    mounts it at `/modules` for the test only. The production images still hold
    no sample module.
- **Cloud:**
  - `deploy/helm/axis/` holds a Helm chart. It has a Deployment for the server
    and one for the worker, with their health probes. A pre-upgrade Job runs
    `migrate`.
  - PostgreSQL, the OIDC provider and the source of secrets are external.
  - `helm lint` and `helm template`, validated against the Kubernetes schemas,
    check the chart. Axis has no test against a live cluster.
- **Upgrades:**
  - An upgrade backs up the databases and runs `migrate` with the new admin
    image. Then it rolls out the new server and worker. This is the order of
    [D43](#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed).
  - A host serves only tenants on its own schema version, as D43 says. So an
    old host refuses a migrated tenant and does not corrupt it.
  - Migrations are never reversed. A rollback is restoring the backup and
    running the old images.
- **Publishing:** CI builds the production images on every run. Pushing them to
  a registry on a version tag is a workflow change, so it is a hand-made pull
  request.
- **Not in M9:**
  - A test against a live Kubernetes cluster.
  - A downgrade.
  - Publishing images by NexKit.
  - Deciding where the ASP.NET data protection key ring lives, and forwarded
    headers. Sessions across replicas and restarts need a shared key ring.
    [D49](#d49-several-server-replicas-behind-a-tls-proxy--agreed) now decides
    both.

_Why:_ an image with development settings or sample credentials would weaken
every installation, so the production images hold none. Compose suits one
machine and Helm is the common package format for Kubernetes. The cost of Helm
is a new tool in `scripts/setup.sh`, so the NexKit `cache` setting and the CI
cache steps must follow in a hand-made pull request. The chart is checked only
statically, and the Compose smoke test is the end-to-end proof of the images.
Migrating first and refusing a host on the wrong schema version keeps an
upgrade safe without reversing migrations. A restored backup is the only
rollback. Pushing images is a workflow change, which NexKit may not make.

## D45. Tenant backup and restore — Agreed

A platform operator backs up one tenant into an archive that holds no secret,
and restores it into a new, suspended tenant. The backup and the restore are built. See [tenant backup](reference/administration.md#tenant-backup) and
[tenant restore](reference/administration.md#tenant-restore) for the commands,
the manifest and every refusal.
The owner confirmed these points on issue #642 on 2026-10-19.

- **Backup:**
  - `tenant backup <id> --out <file>` in `Axis.Admin` writes one archive. It
    holds a `pg_dump` of the tenant database in custom format and a manifest.
  - The manifest holds the tenant id, the Axis version, the tenant's schema
    version, the directory entry without any secret, and every extension
    package and module version that a stored release pins.
  - The dump is one consistent snapshot, so the tenant keeps running during a
    backup.
  - The archive holds no secret. Database passwords, the OIDC client secret and
    `secret` binding values are not in it.
- **Archive and manifest:**
  - The archive is one tar file with `manifest.json` and `tenant.dump`.
  - The manifest lists the module versions that stored releases pin. It lists
    the package versions too, and marks which of them a restore requires.
- **Restore:**
  - `tenant restore <file> --tenant <id> --host <host>...` restores into a new
    tenant. It creates the database and both database roles with new
    passwords, as `tenant create` does, restores the dump and runs the grants
    again.
  - The dump is taken and restored without owners and privileges. The grant
    step of [D31](#d31-separate-database-roles--agreed) builds every privilege
    again for the new roles.
  - An archive with an older schema version is restored and then migrated.
  - The tenant stays `suspended` after a restore. The operator sets the
    secrets again and resumes it.
  - The OIDC client secret comes from `AXIS_OIDC_CLIENT_SECRET`, as for
    `tenant create`. Each `secret` binding is restored by name and kind,
    without a value.
  - Restoring over an existing tenant is a suspend, a restore under a new id
    and a host move. There is no in-place overwrite.
  - A failed restore leaves the entry `provisioning`. Running the same restore
    again finishes it.
- **Refusals:**
  - A restore refuses, before it changes anything, with exit code 2 and a
    message that names what is missing.
  - It refuses an archive it cannot read or does not know, an archive whose
    schema version is newer than the admin tool's, an id that exists in the
    directory as `active` or `suspended`, a host that another tenant holds and
    a missing `AXIS_OIDC_CLIENT_SECRET`.
  - It refuses an archive whose required package versions are not installed.
    Required versions are the ones that an active release, or a release with a
    running or waiting process instance, pins. That is the start check
    `AXC0146`.
  - It never refuses a missing module version. A stored release holds its
    module files, so it keeps working without the module installed.
- **Work after a restore:**
  - Process instances, tasks, work items and connector calls are as they were at
    backup time. The worker runs them again once the tenant resumes.
  - A call that was staged before the backup and sent after it is sent again,
    with the same idempotency key, so a system that honours the key does not
    act twice.
  - A call that was staged after the backup is not in the archive. Its step
    runs again and stages a new call with a new operation identity, so a new
    idempotency key. The system may already have acted on the first call, so
    even a system that honours the key can act twice.
  - A connector without `idempotency` sends no key, so the system may act twice
    in either case.
  - Sequence counters, inbound endpoint receipts and schedule state also go
    back to backup time. Numbers issued after the backup can be issued again,
    and inbound requests accepted after it are lost.
  - This is a known effect. See
    [execution](domain/knowledge.md#execution).
- **Outside Axis:**
  - The platform database and the installation key are backed up by the
    database operator with standard PostgreSQL tools.
  - Point-in-time recovery and restoring into another installation are out of
    scope. The product brief keeps migrating live business data between
    installations
    [out of scope](product-brief.md#out-of-scope-for-now).
- **Not in M9:** deleting a tenant, an in-place overwrite, point-in-time
  recovery, restoring into another installation, and a command that moves a host
  between tenants or sets a binding.

_Why:_ a tenant is the unit of isolation, so it is the unit of backup. An
archive without secrets can be stored like any data file, at the cost that a
restored tenant stays suspended until its secrets are set again. Restoring only
into a new tenant never overwrites live data, and moving the hosts is a
separate step. Work goes back to an earlier state, so a call sent after the
backup is sent again. Only the idempotency key protects the external system
from a second effect, and only for a call staged before the backup. A call
staged after it gets a new key. A stored release holds its module files, so a missing
module would not stop a restored tenant. Refusing on it would block restores for
no reason.

## D46. A general-purpose low-code platform and the milestones after M9 — Agreed

Axis aims to be a general-purpose low-code platform. People build applications
of any business domain on it, not one domain such as ERP. This decision records
the direction, the scope changes and the milestones after M9. See the
[roadmap](roadmap.md#direction-after-m9) for the milestone table.
The owner confirmed these points on issue #702 on 2026-10-19.

- **Direction:**
  - An author creates, configures, tests, publishes and runs an application
    through Studio, without editing JSON or code.
  - Files stay the source of truth
    ([D5](#d5-configuration-is-authored-as-files-first--agreed)). Studio and
    every designer write the same resources through the authoring API.
  - No domain gets platform code. Samples are only the means to prove the
    platform.
- **Scope:**
  - Visual page and form designers, and a process designer, move into scope.
  - Custom widgets stay after the designers
    ([D8](#d8-custom-ui-widgets-are-packaged-react-components--agreed-deferred)).
  - FAPI 2.0 and ASVS verification move to the Hardening milestone, M16.
  - Still out of scope: self-service SaaS provisioning, Axis-hosted SaaS
    operations, and migrating live business data between installations.
- **One format, changed in place:**
  - Until the first production release, each resource kind has one format.
  - A change edits that format in place. There is no second version of a kind,
    no compatibility code and no `formatVersion` bump.
  - The same pull request updates the schema, the compiler, the samples, the
    scenarios and the tests.
  - A development or test database whose stored releases use an older format
    is reset, not upgraded.
  - Docs and milestone names never number a second version.
- **Milestones after M9:**
  - They replace the earlier single Hardening milestone. They follow the vertical
    slice: create an application, then its data and UI, logic and workflow,
    permissions, try it out, validate and test, publish and operate.
  - Each milestone ends with an end-to-end journey.
  - M10 Publishing from Studio, M11 Authoring without JSON, M12 Pages, layouts
    and more widgets, M13 Visual page and form designer, M14 Workflow,
    automation and notifications, M15 Data modeling and data management, M16
    Hardening, and M17 Extensibility and operator console.
  - M11 and M12 can run in parallel, and so can M14 and M15.
  - The "Done when" text of each milestone is in the
    [roadmap](roadmap.md#milestones).
- **Proving it is general:**
  - From M11 on, each milestone extends a sample of a different shape: a
    service desk, with SLAs, rework loops and notifications, then asset
    management, with dashboards, many-to-many, files and import.
  - These samples need no platform code.
- **How it is planned:**
  - Rolling wave stays as it is. M10 was broken into issues first. A later
    milestone is broken down when the one before it is nearly done.
  - Each milestone starts with docs issues that record its design as
    decisions, as M9 did.
- **Effect on earlier decisions:** D8 and D38 link here where this decision
  changes their scope. M10 plans publishing and activation from Studio, so
  [D47](#d47-publishing-from-studio--agreed) replaces the "Studio never
  activates a release" point of D38.

_Why:_ the runtime is strong, but authors still need JSON, git and the admin
CLI. The runtime is also narrow for many kinds of applications. A page holds
one widget, `startProcess` is the only action, processes have no timer,
sub-process or loop, and there are no notifications, files, indexes or
import and export. Both samples have one shape, "request, then approval". A
platform that only suits that shape is not general. Hardening moves to M16
because load tests and ASVS numbers mean something only once the model is
stable. Only what a shared Studio server needs moves into M10: the key ring,
forwarded headers, audit of security events and CI scanning. Keeping one
format until the first production release means no compatibility code to
carry. The cost is that development and test databases are reset after a
format change. M11 and M12 run in parallel because Studio editors can build on
today's runtime while the page model grows. The cost is that some editors
change again in M12.

## D47. Publishing from Studio — Agreed

An author validates, publishes and rolls back an application from Studio. The
server never gets schema rights. It stores a **publish request**, and a
separate **publisher** runs it. Every activation also gets a release number in
a per-tenant history. It was built in M10.
See [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
for M10, the [authoring API](reference/authoring.md#publishing) for the routes,
the [publisher](reference/administration.md#publisher) and the
[publish request and release history tables](reference/storage.md#publish-requests-and-release-history).
The owner confirmed these points on issue #704 on 2026-10-19.

- **Publish requests:**
  - Studio never activates in the server process. It writes a publish request
    to the tenant database, and a separate publisher runs it.
  - A request has an `id`, an `application` and a `kind`: `validate`, `publish`
    or `rollback`. Its input is a snapshot of the folder for `validate` and
    `publish`, or a release id for `rollback`.
  - It has an optional `label` and `note`, and the requesting user. Its
    `status` is `queued`, `running`, `succeeded` or `failed`. It keeps its
    diagnostics and its times.
  - The snapshot holds every resource file of the folder at the moment of the
    request, with its content hash. Later edits in Studio do not change a
    queued request.
  - The runtime role may insert and read requests. Only the publisher changes
    their status.
- **Target tenant:** a request runs for the tenant of the author's session.
  Studio on a tenant's host publishes to that tenant only. A preview request
  runs for the session tenant's preview tenant instead, see
  [D48](#d48-preview-tenants--agreed).
- **Who may publish:**
  - A new setting, `Studio:PublisherRole`, names a roles claim value.
  - `validate` needs the author role. `publish` and `rollback` need both the
    author role and the publisher role.
  - With no value, nobody may publish or roll back.
  - The development realm gives `hoa` a `publisher` role.
- **The publisher:**
  - It is a long-running command of the admin tool, `axis-admin publisher`, in
    the `axis-admin` image.
  - It reads tenants from the same source as the server, `configuration` or
    `directory`. It connects to each tenant database with that tenant's schema
    role.
  - It claims queued requests with `FOR UPDATE SKIP LOCKED` and a lease. A
    request whose publisher died is claimed again after its lease.
  - It runs the same compile, check, migrate, activate and seed steps as
    [`axis-admin activate`](reference/administration.md#activate). Activating
    the same content hash twice changes nothing, so a request that runs again
    does no harm.
  - `validate` runs every activation check against the tenant in a transaction
    that is rolled back. It returns the same diagnostic codes as activation and
    changes nothing.
  - `scripts/dev.sh`, Docker Compose, the E2E script and the production installs
    start one publisher.
  - The server still holds no tenant schema connection under
    `Tenancy:Source=directory`. See
    [database roles](reference/storage.md#database-roles).
- **Release history:**
  - Every activation adds one row to a new tenant table of activations: the
    release, the application, the content hash and the time. It also holds the
    actor, which is a user, `startup` or `operator`, and the label, the note and
    the request.
  - This covers activations at startup, from `axis-admin activate`, from a
    publish and from a rollback.
  - The first time a release is activated in a tenant it gets a release
    **number**: 1, 2, 3… per application. Activating it again keeps its number.
  - The same content can have different numbers in two tenants.
- **Rollback:**
  - A `rollback` request names a release from the history. The publisher
    activates the stored release as it is, without compiling it again.
  - It is refused with a diagnostic when a migration applied after that release
    is missing from it, and when a package version it pins is no longer
    installed. See [rollback](reference/authoring.md#rollback).
  - A rollback does not change the folder. Studio shows when the active release
    does not match the folder.
- **Routes** under `/api/studio/apps/{application}`:
  - `POST .../validate`, `POST .../publish` and
    `POST .../releases/{id}/rollback` answer `202` with the request and a
    `Location`. `publish` takes an optional `label` and `note`.
  - `GET .../requests/{id}` answers `200` with the request, its status and its
    diagnostics.
  - `GET .../releases` answers `200` with the activation history, newest first,
    and which release is active.
  - The answers for `401`, `403`, `404` and CSRF are those of the rest of the
    authoring API.
- **Audit:** each `publish` and `rollback` writes an
  [audit record](reference/storage.md#audit-records-and-sequence-counters) with
  the actor, the application, the release and the request. It does so when the
  request succeeds and when it fails.
- **Effect on D38:** D47 replaces the "Studio never activates a release" point
  of [D38](#d38-studio-edits-application-folders-through-an-authoring-api--agreed).
  Studio still never activates in the server process.

*Why:* [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
plans publishing from Studio in M10 without the admin CLI. Activation ran only
at server start or through `axis-admin activate`, so an author on a shared
server had no way to go live. [D31](#d31-separate-database-roles--agreed) and
[D43](#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed)
keep the schema role out of long-running request hosts, so the server cannot
run the activation itself. A request in the tenant database and a separate
publisher keep the server free of schema rights. Studio's diagnostics skip the
checks that need a tenant, so a clean folder can still fail activation.
`validate` runs those checks without changing anything. The tenant database
kept one active release row per application, with no history, no numbers and no
rollback. The history gives each release a number and gives rollback a target.
The costs:

- **A snapshot, not git.** The request holds the files as they were. The release
  is exactly what the author validated, but nothing commits Studio's edits to
  git. Syncing with git can come later.
- **The publisher holds every tenant's schema role.** That keeps the schema role
  out of the server, but the publisher becomes a high-value host. It runs no
  HTTP endpoint.
- **A separate publisher role.** An author can edit and validate. Going live
  needs a second, explicit role. There is no approval flow between two people.
- **Rollback leaves the folder alone.** The live release and the folder can
  differ until the author fixes the folder.
- **Release numbers per tenant.** The same content can have different numbers in
  two tenants.

## D48. Preview tenants — Agreed

An author tries an application on a **preview tenant** before publishing it to
the real tenant. A preview tenant belongs to one main tenant and keeps its own
data, database roles, hosts and bindings. It was built in M10, with one exception:
refusing to restore a preview tenant's archive is planned for M16. See [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
for M10, [preview tenants](architecture.md#preview-tenants) for how the hosts
treat one, [preview](reference/authoring.md#preview) for the route,
[tenant create](reference/administration.md#tenant-create) for the command and
[preview tenant settings](reference/configuration.md#preview-tenants) for the
`PreviewOf` setting.
The owner confirmed these points on issue #705 on 2026-10-19.

- **What a preview tenant is:**
  - It is a tenant that belongs to one main tenant. A main tenant has at most
    one preview tenant. A preview tenant has no preview of its own.
  - It has its own database, database roles and hosts, like any tenant. Its
    data never mixes with the main tenant's.
  - Under `directory`, the operator creates it once with
    `tenant create <id> --preview-of <main> --host <host>`.
  - Under `configuration`, a tenant setting `PreviewOf` names the main tenant.
    Development, Compose and E2E configure one preview tenant.
- **Sign-in:**
  - A preview tenant uses its main tenant's sign-in settings: the same
    authority, client and claim names.
  - The operator registers the preview host's redirect address at the identity
    provider.
  - A session is still bound to the tenant it signed in on.
- **Bindings:**
  - A preview tenant has its own bindings. They are empty by default, so a
    preview never calls a real system by accident.
  - Under `configuration` they are set like any tenant's. Under `directory`,
    `tenant create --preview-of` takes `--binding <name>=<value>` options for
    `url` bindings.
  - On a preview tenant, a missing binding is a warning at activation, not an
    error. A call that needs it fails at run time, where the inspector shows
    it.
- **Publishing to the preview:**
  - `POST /api/studio/apps/{application}/preview` writes a publish request of
    kind `publish` for the session tenant's preview tenant. It answers `202`,
    as for publish.
  - It needs the author role only, not the publisher role.
  - Preview activations go in the preview tenant's own release history, not
    the main tenant's.
  - When the main tenant has no preview tenant, the route answers `409` with a
    problem that says so.
  - Publishing to the main tenant does not require a preview first.
- **Data in a preview:**
  - A preview starts with the application's seed data. Records created in it
    stay until the operator recreates the preview tenant.
  - Resetting a preview from Studio comes later.
- **Effect on earlier decisions:**
  - [D42](#d42-tenant-directory-in-the-platform-database--agreed): a directory
    entry may name its main tenant.
  - [D43](#d43-tenant-provisioning-and-migrations-across-tenant-databases--agreed):
    `tenant create` gains `--preview-of` and `--binding`.
  - [D47](#d47-publishing-from-studio--agreed): a request normally runs for
    the session tenant. A preview request runs for its preview tenant.

*Why:* [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
asked for a preview on a preview tenant in M10. Before it, the only way to try a
change was `scripts/dev.sh`, where a saved file restarted the server and
activated the folder for every tenant. A tenant had no notion of a preview, and
its bindings pointed at real systems. A preview tenant gives the author a place
to try a change with its own data and bindings. The costs:

- **One preview per tenant, shared by every application of that tenant.** That
  is simpler than one per application. Two authors who preview different
  applications share one preview tenant.
- **Empty bindings by default.** A preview with a connector shows failed calls
  until the operator gives it bindings, for example to a test system.
- **No reset from Studio in M10.** A preview tenant collects test records until
  the operator recreates it.
- **Preview needs no publisher role.** Any author can preview. Only publishing
  to the main tenant needs the second role.

## D49. Several server replicas behind a TLS proxy — Agreed

M10 runs the server as several replicas behind a reverse proxy that ends TLS.
That is what a shared Studio server needs. The forwarded headers, the shared
key ring, the platform tables under both tenant sources, the Studio write
lock, the Helm chart's `studio.persistence` and the Compose example with two
replicas behind a TLS proxy and a shared Studio folder, with its smoke test,
are built. See the [installation guide](reference/installation.md#several-server-replicas),
the [forwarded headers settings](reference/configuration.md#forwarded-headers),
the [platform database](reference/storage.md#platform-database) and the
[authoring API](reference/authoring.md#versions).
The owner confirmed these points on issue #706 on 2026-10-19.

- **Shared key ring:**
  - The server keeps its ASP.NET data protection keys in a new platform table,
    `platform.data_protection_keys`. Every replica reads the same keys.
  - Sessions survive a restart and work on every replica.
  - Each key is encrypted at rest with `Platform:SecretKey`, the way tenant
    secrets are
    ([D42](#d42-tenant-directory-in-the-platform-database--agreed)).
  - The associated data of a key is an empty tenant id, one zero byte and the
    field name `data_protection_key:` followed by the key's id. This is the
    byte layout of tenant secrets. A tenant id is never empty, so a key and a
    tenant field cannot be swapped.
  - The server's read-only platform role gets `INSERT` on that table and on
    nothing else. Key rotation happens while the server runs, so the server
    must be able to add keys. Rows are only inserted, never updated or deleted.
  - The platform tables exist under both tenant sources. Under
    `Tenancy:Source=configuration` the server also applies the platform
    migrations. So the server needs `ConnectionStrings:PlatformSchema` and
    `Platform:SecretKey` there too. Development uses a committed development
    key. Development, Compose and the tests keep one code path.
- **Forwarded headers:**
  - Two new server settings name the proxies that count:
    `ForwardedHeaders:KnownProxies` lists IP addresses and
    `ForwardedHeaders:KnownNetworks` lists CIDR ranges.
  - The server reads `X-Forwarded-For`, `X-Forwarded-Proto` and
    `X-Forwarded-Host` only from those addresses. With both settings empty,
    which is the default, it reads none.
  - No proxy is trusted until it is named. Loopback is not trusted by default.
  - The server reads one hop, the nearest proxy. A client then cannot spoof the
    scheme or the host through a chain of proxies.
  - The server never reads `X-Forwarded-Prefix`.
  - Tenant resolution, cookie security and OIDC redirect addresses use the
    scheme and host that result.
  - An entry that cannot be read stops the start and names the setting. The
    worker reads neither setting.
- **Studio across replicas:**
  - Studio folders live on a volume that every replica mounts at the same
    path.
  - A write takes a PostgreSQL advisory lock in the platform database, over
    `ConnectionStrings:Platform`. The version check and the write happen under
    that lock, so it holds across replicas. An advisory lock needs no grant.
  - The lock is keyed by the folder alone. A create checks that its `id` is
    unused anywhere in the folder. A lock per file would let two creates with
    the same `id` both succeed.
  - A write that cannot take the lock answers `503` and writes nothing. It
    waits up to 10 seconds first. The snapshot of `validate` and `publish`
    takes the same lock.
- **Installs:**
  - The Helm chart drops the one-replica limit of the server.
    `studio.persistence` mounts a shared folder volume when Studio is on. In
    Kubernetes that needs a `ReadWriteMany` storage class.
  - The on-premises Compose install can run two server replicas behind a TLS
    proxy.
  - The on-premises smoke test does so. It checks that a session survives
    restarting one replica.
  - The Compose install mounts one shared Studio folder, `AXIS_STUDIO_DIR`, on
    both replicas. The smoke test saves through one replica and reads through
    the other.
- **Effect on earlier decisions:**
  - D23 keeps its session rules. The key ring and the forwarded headers change
    where the cookie key lives and which scheme and host the server sees.
  - D38 keeps its write rules. The write lock moves from the server process to
    the platform database.
  - D42 keeps its tenant directory. The read-only platform role gains `INSERT`
    on one table, and the platform tables now exist under `configuration` too.
  - D44 decides the key ring and the forwarded headers, which it left open.

*Why:* a shared Studio server needs more than one replica, and the server
cannot do that today. Its sessions end at every restart. A second replica
cannot read the first one's cookies. Behind a proxy that ends TLS, the server
sees plain HTTP, which breaks secure cookies and OIDC redirect addresses. Two
replicas that share a folder could both accept a write with the same version.
A key table in the platform database gives every replica the same keys without
a writable root file system, at the cost of one `INSERT` grant to a read-only
role. Platform tables under both tenant sources give one code path for keys and
locks, at the cost of platform tables in development databases. Trusting no
proxy until it is named keeps a client from setting the scheme or the host. A
shared volume for Studio folders keeps one copy of each file. In Kubernetes it
needs a `ReadWriteMany` storage class.

## D50. Sign-in audit records and security scanning in CI — Agreed

M10 adds two security baselines. Sign-in, sign-out and refused sign-ins leave
audit records, and that part is built. CI runs static analysis, which is
built. Running the dependency scan in CI is _(planned for M16)_. See
[audit records](reference/storage.md#audit-records-and-sequence-counters) and
[sign-in and the session](architecture.md#sign-in-and-the-session) for the
detail. The ASVS level and its verification stay in M16
([D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)).
The owner confirmed these points on issue #707 on 2026-10-19.

- **Sign-in audit:**
  - Three new audit actions are `session.signedIn`, `session.signedOut` and
    `session.signInRefused`. They are written to `axis.audit_records` in the
    tenant database, the same append-only table as the other actions.
  - The names are in the past tense, like `record.created`, except
    `session.signInRefused`.
  - `session.signedIn` and `session.signedOut` record the user id and the
    display name.
  - `session.signInRefused` records the actor `anonymous` and a reason code. It
    records no user. The reason is `otherTenant`, `invalidToken`,
    `invalidState` or `providerError`. `providerError` covers a person who
    cancels at the identity provider, and a provider error.
  - Every record holds the client address, after the forwarded headers rules of
    [D49](#d49-several-server-replicas-behind-a-tls-proxy--agreed).
    None holds a token, a cookie or the user agent.
  - A session that expires writes nothing. A sign-out with no session writes
    nothing.
- **Dependency scanning:**
  - A new script, `scripts/scan.sh`, needs network. It runs
    `dotnet list package --vulnerable --include-transitive` on `Axis.slnx`,
    and `npm audit --audit-level=high` in `web` and `tests/e2e`.
  - It fails on a vulnerability of high or critical severity, and prints each
    one with its package and advisory.
  - CI runs it on its existing triggers and once a week. A finding on the
    weekly run opens no issue. The red run is the signal.
  - It is not a NexKit check until a hand-made change to `.nexkit/` adds it.
- **Static analysis:**
  - One CodeQL workflow scans `csharp` and `javascript-typescript` on a push
    to `main` and on pull requests. Its alerts go to GitHub code scanning.
  - A new alert of high or critical severity fails the check. The owner sets
    this threshold in a repository code scanning rule, because CodeQL does not
    fail a run on alerts by itself.
- **Not in M10:** an audit of `403` refusals, an audit of session expiry, and
  the ASVS level. The first two stay in logs and metrics. The ASVS level stays
  in M16.

_Why:_ signing in is the first step of every attack on a shared server, and
today it leaves no trace. A refused sign-in is rare and worth a record, and an
expired session is routine. A closed list of reason codes keeps free text, and
possibly personal data, out of the table. The client address helps trace an
attack, but it is personal data, and audit records are kept forever, so the
cost is that it stays as long as they do. A `403` refusal is common, so
auditing it would flood the table. A scan that stops at high and critical
blocks the real risks without blocking a merge on every low finding. The cost
is that lower severities pass.

## D51. Creating applications and resources from Studio — Agreed

Studio creates a new application, blank or from an installed application
template, and starts a resource of every kind. D46 plans this for M11. It is
_(planned for M11)_. See the
[authoring API](reference/authoring.md#creating-an-application), the
[application templates](reference/configuration.md#application-templates) and
the [Studio section of the frontend](reference/frontend.md#studio). The owner
confirmed these points on issue #778 on 2026-10-20.

- **Where new applications go:**
  - A new server setting, `Studio:ApplicationsPath`, names one folder. Studio
    creates new application folders in it.
  - Every direct subfolder of it that holds an `application.json` is listed by
    `GET /api/studio/apps`, after the folders of `Studio:Folders`. The same
    first-match rule applies to names.
  - Studio is on when `Studio:Folders` is not empty or `Studio:ApplicationsPath`
    is set. `GET /api/me` answers `studioCreate: true` when the user holds the
    author role and the setting is set.
  - A missing folder is created at start. The server stops if it cannot.
  - Under several server replicas, the folder is on the shared Studio folder
    ([D49](#d49-several-server-replicas-behind-a-tls-proxy--agreed)).
  - Development, Compose and E2E set it to `artifacts/studio-apps`, which git
    ignores.
- **Creating an application:**
  - `POST /api/studio/apps` takes `{ name, template? }`. It needs the author
    role and the CSRF header.
  - The new folder is the kebab-case `name`. A name or folder that already
    exists is `409`. A `name` that does not match the manifest name pattern is
    `400`. So is a name whose kebab-case form is longer than 60 characters or
    is a reserved site path (`api`, `health`, `assets`, `studio`).
  - The whole folder is written under the write lock, to a hidden temporary
    folder that is then renamed. A reader never sees half an application.
  - The answer is `201` with the application's `name`, a `Location` and the
    folder's diagnostics. A new application is not activated. The author
    publishes it ([D47](#d47-publishing-from-studio--agreed)).
- **Blank application:**
  - It holds a manifest with a new UUID `id`, one site at `/<kebab-name>` with
    the default locale `en`, one `Home` page with a `taskInbox` widget, and an
    `en` text resource.
  - The text resource holds three keys: the site title, the page title and the
    navigation label.
  - The site schema needs at least one navigation entry that names a page. So a
    blank folder without a page would not compile.
- **Application templates:**
  - An application template is an application folder with a `template.json`:
    `id`, a SemVer `version`, a title text and a description text, in `en` and
    `vi`.
  - Operators install templates under `Templates:Path/<id>/<version>/`, like
    modules. A missing setting means no templates. A folder named in it that
    does not exist stops the server.
  - A template must hold exactly one site. A template with a bad
    `template.json`, folder names that differ from it, or any other number of
    sites is left out and logged as a warning at start.
  - `GET /api/studio/templates` lists the newest version of each template. The
    create body names a template by `id` only, and the server uses the newest
    installed version.
  - A copy takes every `*.json` file except `template.json`. Each resource's
    top-level `id` becomes a new UUID. Seed record ids stay, because they are
    row ids in the new application's own tables.
  - The manifest gets the new `name`, and the site path becomes the kebab-case
    name. References are by name, so they still resolve.
  - The copy has the same diagnostics as the template. A template that does not
    compile is listed, and its diagnostics come with the create answer.
  - Sample templates live under `samples/templates/`. `scripts/build.sh`
    installs them into `artifacts/templates`. The production images hold none.
    The sample `request-approval` has one entity with a sequence number, a form
    page, a table page, a two-step approval process, two roles and their
    policies, texts in `en` and `vi`, and one test scenario.
  - Templates are installed, not uploaded. An author cannot save an application
    as a template in M11.
- **Creating a resource:**
  - Studio's **New resource** asks for the kind and the name, or the locale for
    a text resource.
  - It sends the kind's starter: the smallest content that passes the kind's
    schema. References the author must still choose are left as empty strings.
    The schema's name patterns reject an empty string, so they show up as
    diagnostics.
  - The starters live in the SPA, next to the schemas it bundles. The create
    route stays as it is. A Vitest test checks each starter against its schema.
  - A new text resource copies every key and text of the default locale of the
    first site, so every label still resolves. The author then translates.
- **Effect on [D38](#d38-studio-edits-application-folders-through-an-authoring-api--agreed):**
  - `Studio:Folders` stays for folders kept in git. New applications live in
    `Studio:ApplicationsPath`, which nothing commits to git yet.

_Why:_ an author who cannot start an application or a resource without
hand-written JSON cannot use Studio alone. A second folder setting keeps the
git-kept folders apart from the ones Studio creates, and a fixed list could not
grow. Writing the folder to a hidden name and renaming it means a reader never
sees half an application. Installing templates like modules reuses a rule the
operator knows, and the cost is that authors cannot share their own in M11.
New ids on a copy mean two applications made from one template share no
resource id, so they never clash in one tenant. Starters in the SPA keep the
server's create route simple. The cost is that a starter broken by a schema
change shows up in the Vitest tests, not in a server check.

## D52. Reference pickers, label texts and the expression editor — Agreed

Studio lets an author pick references, edit label texts in every locale and
write expressions with live type checks, in place of typing names, keys and
expression strings by hand. This is _(planned for M11)_. See
[D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
for M11, the [authoring API](reference/authoring.md#names) for the routes, the
[schema keywords](reference/configuration.md#schema-keywords) and
[Studio](reference/frontend.md#studio). The owner confirmed these points on
issue #779 on 2026-10-20.

- **Names:**
  - `GET /api/studio/apps/{application}/names` answers every name the
    application defines, from the in-memory compile of the folder: entities
    with their fields, data sources with their parameters and columns,
    processes with their steps and outcomes, rules with their parameters and
    result type, connectors with their operations, events with their fields,
    roles, sequences, forms, pages and text keys.
  - It also answers the application's locales, and for each text key its text
    in every locale and how many labels use it.
  - Module entities and rules are included and marked with their module. A
    folder with errors still answers with what resolved.
  - It touches no database. The answers for `401`, `403` and `404` are those
    of the rest of the authoring API.
- **Reference pickers:**
  - The form shows a property marked as a reference as a picker over the
    names.
  - A name that does not resolve stays visible, marked as unknown, so nothing
    is lost.
  - The names load with the application, and again after a save, a delete, a
    text write and a reconnect.
- **Schema keywords:**
  - `"x-axis-reference"` marks a reference property. It names a kind, such as
    `entity`, or a part, such as the fields of the entity at another path.
  - `"x-axis-expression": true` marks a string property that holds an
    expression, so the form shows the expression editor there.
  - Both are annotations. The schemas stay the single source of what the form
    shows, and no resource file changes.
- **Label texts:**
  - A label property shows its text in each locale of the application, not
    its key.
  - `PUT /api/studio/apps/{application}/texts/{key}` with
    `{ texts: { <locale>: <text> } }` writes the key in every text resource.
    It needs every locale and answers `400` when one is missing.
  - It writes all text files under the folder write lock. It takes no
    `If-Match`, so the last write to a key wins. Other keys are untouched.
  - A new label gets a key made from the resource and the path, such as
    `pages.myTasks.title`. The author can change it. The text is written
    right away, and the key goes into the resource draft and is saved with
    the next Save.
  - A key that other labels use shows how many, so the author knows the edit
    changes them too.
- **Expression check:**
  - `POST /api/studio/apps/{application}/expressions/check` with
    `{ resourceId, path, expression }` checks one expression in the place it
    would stand: the same names in scope, the same expected type and the same
    limits as the compile.
  - It uses the saved resource to find that place and takes no draft content.
    A path with no expression place is `400`.
  - It answers `{ type, diagnostics }`. A diagnostic has its code, message and
    a range `{ start, end }` in UTF-16 code units of the expression text, with
    an exclusive end. There is at most one, because the compile stops at the
    first problem.
  - It compiles in memory and writes nothing. It is not a save.
- **Expression editor:**
  - An expression property shows a code editor with the expression language's
    highlighting.
  - It checks the text 300 ms after the author stops typing, underlines the
    diagnostic at its range and shows the result type.
  - It completes the names in scope from the names route.
  - A check that gets no answer shows nothing new. It never blocks a save.
- **Editor library:** the editor is CodeMirror 6, at about 120 KB gzipped, in a
  chunk that loads only when Studio first shows an expression.

_Why:_ [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
plans reference pickers and an expression editor with type errors as you type
in M11. Names, keys and expressions are typed by hand today, and a mistake
shows only after a save, as a folder diagnostic. One names route lets Studio
load everything once, in place of a call per picker. One shared keyword per
purpose keeps the schemas the single source of what is a reference or an
expression. CodeMirror 6 is several times smaller than Monaco and needs no web
workers, and a lazy chunk keeps site pages free of a Studio-only editor. The
costs:

- **A larger answer.** The names route holds every name and every text. At the
  typical application size that is a few hundred kilobytes, once per save.
- **Last write wins on one key.** A text write takes no `If-Match`, so two
  authors who edit the same key lose one edit. Other keys are safe.
- **Not atomic across files.** A crash between two renames can leave a key in
  some locales only. The next compile reports `AXC0027`.
- **A compile per check.** Each pause in typing compiles the folder in memory.
  This may be slow for a large application. The build issue sets the budget.
- **A range the compiler lacks.** The compile reports only a character
  position today. The build issue adds the range.
- **Simple completion.** Completion offers names by a simple scope rule, so it
  can offer a name that is not in scope at that exact place. The check then
  flags it.
- **Unused keys.** A new key is written before the Save, so an abandoned draft
  leaves an unused key. That does no harm, because an unused key is allowed.

## D53. Typed editors and the process graph in Studio — Agreed

M11 gives Studio typed editors for pages, process steps and connectors, and
for the kinds that need JSON today. It also adds a read-only process graph. The
seed grid is built. The rest is _(planned for M11)_. See the
[Studio section](reference/frontend.md#studio) for the detail. The owner
confirmed these points on issue #780 on 2026-10-20.

- **Typed editors:**
  - A typed editor replaces the generic form for its kind. The JSON view
    stays, as a second view of the same draft.
  - It edits the same draft and saves the same JSON Patch as the form, with
    the same `If-Match` version rules.
  - It never drops a value it does not show. Unknown keys are kept as they
    are.
  - It uses the reference pickers, label texts and expression editor planned
    for M11.
- **Page editor:**
  - A list of the page's widgets, each with its type and a summary.
  - A panel per widget type: `table`, `form`, `taskInbox` and
    `processInspector`. Each shows the properties the widget has today, and
    the form's `startProcess` actions.
  - M12 runs in parallel
    ([D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)).
    A widget or layout that M12 adds gets its panel in M12. Until then it
    shows in the JSON view.
- **Process step editor:**
  - The steps as a list in graph order, with add, remove and reorder.
  - A panel per step type: `operation`, `decision`, `task` and `end`. An
    operation shows its `set`, `input`, `output` and `retry`. A decision shows
    its `branches` and `otherwise`. A task shows its assignee, form, outcomes
    and `dueIn`.
  - Mappings show as rows of target and expression.
  - Each transition, `next`, a branch, `otherwise` or an outcome, picks its
    target from the process's steps.
- **Connector editor:**
  - The base URL binding and the auth secret binding are picked by name. No
    value of a binding is shown or stored, as today.
  - The timeout and the idempotency header.
  - The operations as a list, each with its method, path, input and output
    fields, and lookup.
- **Other kinds:**
  - Test scenarios: the users with their roles, then the steps as a list,
    with a panel per step type.
  - Seed data: the records as a grid with a column per field of the seed's
    entity.
  - Text resources: a table of keys with a column per locale, with search.
    Adding a key adds it in every locale's `text` resource.
  - Endpoint mapping: rows of field and expression, as for process mappings.
    The rest of an endpoint keeps the generic form.
- **Process graph:**
  - A process shows a drawn graph of its steps and transitions above its
    editor. It is read-only. A layout library places every node, and the
    author never places one.
  - The graph is drawn from the editor's unsaved draft, so an edit in the
    step list redraws it at once. Diagnostic marks come from the diagnostics
    panel's last loaded list.
  - Selecting a node selects that step in the list. Selecting an edge opens
    its transition. Selecting a step in the list highlights its node.
  - A transition that names a step that does not exist draws no edge. The
    diagnostic marks the step that holds the transition. The graph never
    invents a node.
  - A step that a diagnostic names is marked on the graph.
  - The graph works with the keyboard, and each node has a text label with
    the step's name and type.
  - It uses React Flow (`@xyflow/react`, MIT, about 50 KB min+gzip with its
    dependencies) to draw, and dagre (`@dagrejs/dagre`, MIT, about 30 KB
    min+gzip) to lay out. M14's process designer builds on both.
  - Neither package is added until the M11 issue that builds the graph.
- **Effect on earlier decisions:** D39 says there is no graph drawing library.
  [D39](#d39-reference-index-and-where-a-resource-is-used--agreed) links here
  where M11 changes that.

_Why:_ an editor that drops a value it cannot show, or writes a shape the
compiler refuses, costs author trust. So a typed editor keeps unknown keys and
saves the same patch as the form. A newer format is never broken by an older
editor. The generic form cannot show maps or `oneOf` parts, so scenarios,
mappings, seeds and texts need typed editors to work without JSON. The graph is
read-only until M14, because editing on a graph needs the process designer and
its design. Authors edit in the list until then. React Flow gives keyboard
support and custom nodes, which M14 needs. Dagre lays out directed step graphs
of the size Axis processes have. ELK lays out better but weighs over 400 KB.
The cost is two new libraries in the SPA bundle. The page editor changes again
in M12, which D46 accepts.

## D54. The entity designer and the policy matrix — Agreed

M11 gives Studio an entity designer and a policy matrix _(planned for M11)_.
The designer edits the fields of an entity and adds the migrations that keep
stored data. The matrix shows who may do what in one place. A new `changes`
route writes an entity and its migration in one call. See
[Studio](reference/frontend.md#studio) for the screens and
[the changes route](reference/authoring.md#changes) for the route. The
designer follows
[D24](#d24-roles-and-policies--agreed),
[D30](#d30-schema-migrations--agreed),
[D38](#d38-studio-edits-application-folders-through-an-authoring-api--agreed)
and
[D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed).
The owner confirmed these points on issue #781 on 2026-10-20.

- **Entity designer:**
  - An `entity` resource opens in the designer in place of the generic form.
    The JSON view stays.
  - The fields are a table. Its columns are name, label, type, required,
    unique and the type's own settings: `maxLength`, precision and scale,
    enum `values`, the reference `target` and the child entity.
  - A computed field's expression and each validation use the expression
    editor.
  - Fields can be added, removed and reordered. The display field is picked
    from the fields.
  - A module entity is read-only, as today.
- **Migrations from the designer:**
  - The designer compares with the entity as Studio loaded it. It does not
    read the active release.
  - Renaming a loaded field asks whether to keep its data. Yes adds a
    `renameField` change. Removing a loaded field asks whether to drop its
    data. Yes adds a `dropField` change, and No keeps the field.
  - A change the column cannot follow asks for a `convert` expression. That
    covers a new type, a narrower `maxLength` and a removed enum value.
  - A field that becomes required, or a new required field, offers a `fill`.
  - Switching to or from `enum`, and adding `unique`, offer no migration. The
    designer warns, and Validate reports `AXC0016`. No migration type covers
    these two changes.
  - Renaming or removing a `child-collection` field offers no migration,
    because it has no column.
  - Each save that needs migrations creates one new `migration` resource,
    named `<Entity>Change<UTC yyyyMMddHHmmss>`. It holds every change of that
    save in edit order. It never edits an existing migration, so applied
    files stay unchanged. Migrations run in file path order, so the files of
    one entity sort by time.
  - A hand-written migration of the same entity whose name sorts later runs
    after the designer's files. Nothing enforces this.
  - Validate still runs every activation check, so a missing migration shows
    before a publish.
- **The changes route:**
  - `POST /api/studio/apps/{application}/changes` takes a list of creates and
    patches, each patch with its version.
  - It takes the write lock once and checks every change before it writes
    any. A stale version or a bad change refuses the whole call.
  - It takes at most 20 changes and no deletes. Two changes for the same `id`
    are `400`. A patch change without `version` is `428`.
  - A server that dies during the writes can leave the first files written.
    The next diagnostics show the mismatch.
- **Policy matrix:**
  - One view per application shows roles as rows. The columns are entities,
    data sources, processes and endpoints. Child entities are left out,
    because a grant on one is `AXC0093`.
  - A cell shows the actions the role holds on that target from all its
    policies, combined with OR, as the runtime does.
  - A cell opens its grants. Each grant shows its actions, its record filter
    and its field list. The filter uses the expression editor, and the fields
    use the field picker.
  - An edit writes to the policy `<Role>Access`, where `<Role>` is the
    PascalCase form of the role name. So `department-manager` writes
    `DepartmentManagerAccess`. The policy is created when it is missing, with
    only that role in `roles`.
  - A grant from a policy that names several roles is shown as shared and
    opens that policy. So does a grant from another policy that names only
    this role. The matrix never rewrites a hand-written policy.
  - A `<Role>Access` that names other roles, or more than one, counts as
    shared. The matrix does not write to it, turns editing off for that role
    and says why.
  - An action that a target does not take cannot be chosen.
- **Not in M11:** deleting an entity with `dropEntity`, splitting shared
  policies and comparing against the active release.

_Why:_ the designer asks about each change while the author still remembers
it. Today a missing migration shows only when activation refuses it. The cost
is that it compares with what it loaded, not with the tenant. A field added
and renamed before any publish gets a migration it does not need, and
Validate reports it. One policy per role keeps a matrix edit from widening the
access of other roles. The cost is that a hand-written policy with several
roles still works, but the matrix does not split it. The `changes` route lets
an entity and its migration be saved together. The cost is a multi-file write,
which is new in the authoring API. It checks everything before it writes, but
a server that dies in the middle can leave one file written.

## D55. Running test scenarios from Studio — Agreed

An author runs an application's test scenarios from Studio. Studio stores a
new kind of publish request, `test`, and the publisher runs it with a separate
database role that may create databases. The server still gets no such right.
The route, the stored request and the publisher's run are built. The rest is _(planned for M11)_: the 10-minute limit, the immediate stop on `SIGTERM`, and the wiring of the scenario role in Compose, `scripts/dev.sh`, E2E, `init-roles.sql` and the chart. See
[D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
for M11, the [authoring API](reference/authoring.md#test) for the route, the
[publisher](reference/administration.md#publisher) for how it runs, and
[database roles](reference/storage.md#database-roles) for the new role.
The owner confirmed these points on issue #782 on 2026-10-20.

- **A test request:**
  - A new publish request kind, `test`. It holds a snapshot of the folder, as
    `validate` does, and optionally the names of the scenarios to run. With no
    names, it runs them all.
  - `POST /api/studio/apps/{application}/test` answers `202` with the request
    and a `Location`, as `validate` does. It needs the author role only,
    because it changes no tenant.
  - The request status route answers its outcome. For each scenario it gives
    passed or failed, the failing step and its message, and the time it took.
  - The outcome goes in a new `scenarios` field of the request, stored in a new
    `scenario_results` column. Each item is
    `{ name, resourceId, file, outcome, durationMs, step, path, message }`.
    With `resourceId` and `path`, Studio opens a failing scenario at its step,
    as it opens a resource from a diagnostic.
  - A request ends `failed` when any scenario fails. Scenario failures go only
    in `scenarios`. Compile errors, the missing setting, an unknown scenario
    name and the time limit go in `diagnostics`.
  - An unknown scenario name is accepted with `202`. The request then fails
    with a diagnostic. The route does not answer `400`, so diagnostics keep one
    path, as for a folder with errors.
  - A `test` request always has the target `tenant`. It writes no audit record
    and has no release id. It uses the existing lease and three-claim rules. A
    claim after a lost lease runs every scenario again.
- **Who runs it:**
  - The publisher runs `test` requests. The server never does.
  - A new publisher setting, `Scenarios:ServerConnectionString`, names a
    PostgreSQL role that may create databases and owns no tenant database.
  - The publisher runs each scenario as `Axis.Scenarios` does. It creates a new
    `axis_scenario_<uuid>` database, migrates it, activates the snapshot,
    applies the seed data, runs the steps with stubs and drops the database at
    the end, pass or fail. It drops only databases it created.
  - Without the setting, a `test` request fails with a diagnostic that names it.
  - A `test` request never touches the tenant database except to store its own
    status and results. It does not wait behind a `publish` of the same
    application.
  - On `SIGTERM`, the publisher stops a running `test` request at once, drops
    its database and sets the request `failed`. A test may run for 10 minutes,
    which is longer than the stop grace period of both installs.
  - A database left behind by a publisher that crashed is not cleaned up
    automatically. The operator drops the `axis_scenario_*` databases that the
    scenario role owns. With several publishers, none can tell which leftover
    database is still in use. Automatic cleanup needs its own decision.
- **Limits:**
  - The scenarios of one request run one after another.
  - A request has a time limit of 10 minutes. Past it, the scenario that runs
    fails, its database is dropped and the request fails.
  - The work item limit of [D32](#d32-test-scenarios--agreed) applies per
    scenario, as today.
  - At most one `test` request per application runs at a time, oldest first.
- **Studio:**
  - A **Run tests** button next to **Validate**, and a **Run** action on each
    scenario.
  - A results list: each scenario with passed or failed, its time, and the
    failing step. Selecting a failure opens the scenario at that step.
  - They show only when `GET /api/me` answers `studioTest: true`.
- **Settings in each place:**
  - Development, Compose and E2E set `Scenarios:ServerConnectionString` to a
    development role with `CREATEDB`, created by
    `samples/postgres/init-roles.sql`.
  - The production installs leave it empty by default. The installation docs
    say how to set it, and that it should point at a PostgreSQL server that
    holds no tenant data.
  - `studioTest` comes from a new server setting, `Studio:Tests`, which is off
    by default. The installs turn it on together with the publisher's
    `Scenarios:ServerConnectionString`. The server cannot see the publisher's
    settings, and under `Tenancy:Source=configuration` the publisher writes
    nothing the server could read. The owner confirmed this on issue #782.
  - The docs name no new diagnostic codes. The issues that build the feature
    choose the next free `AXC02xx` codes.
- **Effect on earlier decisions:**
  - [D31](#d31-separate-database-roles--agreed): a third kind of role, which
    may create databases, on a host other than the server.
  - [D32](#d32-test-scenarios--agreed): Studio is a second place where
    scenarios run, next to `Axis.Scenarios`.
  - [D47](#d47-publishing-from-studio--agreed): a fourth request kind, with its
    own queue per application.

*Why:* [D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)
plans that Studio runs test scenarios in M11. Scenarios ran only through
`Axis.Scenarios`, a console tool, so an author without a shell could not run
them. The server may not create databases
([D31](#d31-separate-database-roles--agreed)), and the publisher holds no such
right, so a request and a publisher with a new role keep that rule. The costs:

- **The publisher gets a third right.** It already holds every tenant's schema
  role. Now it can also create databases. It still has no HTTP endpoint.
- **A separate PostgreSQL server is advised, not required.** On one server, a
  scenario database sits next to tenant databases, but the scenario role owns
  none of them.
- **Authors may run tests without the publisher role.** A test changes no
  tenant. It costs database time, so the limits above apply.
- **Scenarios test the folder, not a release,** as D32 says.

## D56. A service desk as the third sample application — Agreed

A service desk is the third sample application, in the thin shape that M11 can
run. The sample folder and the realm users exist. The `request-approval`
template and the Playwright journeys that build it through Studio do not, and
are _(planned for M11)_. See the
[Third application section](product-brief.md#third-application-service-desk)
of the product brief for the data, flow, security, pages and scenarios. The
owner confirmed these points on issue #783 on 2026-10-20.

- **Application:** a service desk, in `samples/apps/service-desk`. A requester
  reports a problem, the service desk lead triages it, and an agent resolves
  it. Its shape differs from the two request-then-approval samples of
  [D41](#d41-leave-requests-as-the-second-sample-application--agreed).
- **Data:**
  - A ticket has a number from the sequence `SD-{yyyy}-{n:5}`, a title, a
    description, a category, a priority (`low`, `normal`, `high` or
    `urgent`), a requester, an assigned agent, a status and a resolution. The
    title is the display field.
  - A category has a name and a default agent. A few categories are seeded.
- **Process:**
  - A requester submits a ticket.
  - The service desk lead triages it. They set the category and priority and
    assign an agent, or they reject the ticket.
  - The agent resolves it with a resolution.
  - The requester confirms the resolution, which closes the ticket, or
    reopens it.
- **Reopen without a loop:** a reopened ticket ends the process with the
  status `reopened`. The requester submits it again, as a returned purchase
  request is submitted again today. A loop that holds a task comes in M14.
- **Security:**
  - The roles are `requester`, `agent` and `service-desk-lead`.
  - A requester reads and edits only their own tickets. An agent reads the
    tickets assigned to them. The lead reads every ticket.
  - The requester cannot edit the resolution.
  - Everything else is denied by default.
- **Pages:** My tickets, the ticket form, the task inbox and All tickets for
  the lead, in a site at path `service-desk`.
- **Texts:** `en` and `vi`.
- **Tests:** test scenarios cover the happy path, a rejection, a reopen then
  resubmit, and denied access for each role.
- **Users:** two new realm users, `khoa` with the role `agent` and `linh` with
  the role `service-desk-lead`. `anna` also gets the role `requester`. The
  development password stays `axis`. The roles of the existing users stay as
  they are.
- **Built only through Studio:**
  - The committed folder `samples/apps/service-desk` records what the journey
    builds. CI activates it and runs its scenarios, as for the other samples.
  - The main Playwright journey starts from a blank application in Studio. It
    builds the same application with Studio only. It uses no file edit, no
    JSON view and no `axis-admin` command.
  - A second, smaller journey starts from the `request-approval` template.
  - At the end, the main journey compares the folder it built with the
    committed sample. The comparison ignores resource ids and key order. Any
    other difference fails the journey.
  - Then the main journey runs the scenarios from Studio and publishes. Then
    `anna`, `linh` and `khoa` take one ticket through the process on the site.

_Why:_ the thin shape waits for the timers, loops and notifications of M14
([D46](#d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9--agreed)).
M11 proves authoring, not the service desk shape. Comparing with a committed
folder keeps the sample in CI and proves that Studio writes the same files.
The cost is that a change to the sample needs the journey to follow. Starting
from a blank application proves the most of Studio, and the template journey
stays small. New users leave the roles of the existing users unchanged, so
the other samples and their tests do not change.
