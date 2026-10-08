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
- **Issue size:** each issue is one behaviour, with 2–5 verifiable acceptance
  criteria.
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

Still **Proposed**, because they show how the M1 shortcuts are expected to
grow but are not settled yet:

- **Data:** a widget binds to a `dataSource` in M2, and `entity` stays as
  shorthand for all records of an entity.
- **Navigation:** `formPage` gives way to navigate actions with page
  parameters.
- **Forms:** form layout becomes its own `form` resource that both a `form`
  widget and a process task (M3) can use.

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
  field's path. Validation on the client as the user types comes with full
  forms in M3.
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
