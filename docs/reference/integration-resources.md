# Connectors, events and endpoints

Detailed reference for the resources that connect an application to other systems: connectors
and deployment bindings, events and triggers, and inbound endpoints. How a process calls a
connector is in [process-execution.md](process-execution.md#connector-calls), how it raises an
event is in [process-events.md](process-events.md), and how a caller calls an endpoint is in
[inbound-endpoints.md](inbound-endpoints.md).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

## Connectors and deployment bindings

This section follows
[D25](../decisions/d25-connectors-deployment-bindings-and-secrets.md).
The `Bindings` block, its startup checks, resolving a binding at run time,
the secret value that hides itself from text, JSON and logs, the `connector`
resource with its [compile checks](configuration.md#configuration-pipeline) and the activation
check are built. So are the bindings that a package operation declares, which
activation checks and the operation context hands out, see
[Capabilities](extension-packages.md#capabilities). Calling a connector and using secrets in its calls are built
too, and so are retries and reconciliation. A `connector` resource
describes an external system that a process can call. Its URL and its secret
are deployment bindings, so a release holds only their names. How a process calls a connector, retries and
reconciles is in [Connector calls](process-execution.md#connector-calls).

### Deployment bindings

- **Binding.** A named setting that a release refers to by name and never
  holds as a value. M5 has two kinds: `url` and `secret`.
- **No binding resource.** No resource kind declares bindings. A connector
  names one as `{ "binding": "<name>" }`. Where the name is used decides its
  kind: `baseUrl` needs a `url` binding, and `auth` needs a `secret` binding.
  A package operation names them in the `capabilities` of its manifest:
  `urls` need `url` bindings and `secrets` need `secret` bindings.
- **Where values live.** Under `Tenancy:Source=configuration`, each tenant holds its values in
  `Tenants:{id}:Bindings:{name}` of the host configuration. Each binding has a `Kind`, `url` or `secret`, and a
  `Value`. The server and the worker must both hold them. Under
  `Tenancy:Source=directory`, the server reads the values from the
  [tenant directory](../architecture.md#tenant-directory) instead, and `secret`
  values are stored encrypted and decrypted when resolved. Under `directory`
  the worker reads them from the directory too.
- **Secrets from the environment.** A secret's value is set with an
  environment variable, such as
  `Tenants__default__Bindings__PurchasingSystemToken__Value`. A command-line
  argument or a mounted secret file works too. Only in Development may a
  committed `appsettings` file hold one.
- **Secret source check.** Outside Development, startup fails when the
  source that wins for a secret's `Value` is a configuration file, such as
  `appsettings.json`. The message names the tenant, the binding and the file,
  never the value. A file may still hold a placeholder that an environment
  variable overrides, as in the example below.
- **Never shown.** A secret's value never appears in a release, step
  history, audit record, log or problem details.
- **Resolving a secret.** A `secret` binding resolves to a secret value. Its
  text, its JSON and anything logged with it show `[secret]`, never the value
  or a part of it. Code reads the plain value only by asking for it at the
  moment of a call. A secret is never read back from JSON. Asking for a
  `secret` binding as a plain binding fails with a message that names the
  binding.
- **Activation check.** Activating a release for a tenant fails when a
  binding the release uses is missing for that tenant, or is of the wrong
  kind (`AXC0100`). The release uses the bindings of its connectors, and the
  `capabilities` of each package operation that a process step uses. The
  binding diagnostic names the tenant and the binding.
  Activation checks bindings before it provisions anything, and reports every
  bad binding at once. Startup activation passes in each tenant's bindings,
  so a bad binding stops the start. A tenant is checked only when a release
  is activated for it. Under `directory` the bindings come from the tenant's
  directory entry. On a
  [preview tenant](host-settings.md#preview-tenants), a binding that is missing is `AXC0100`
  with severity warning, not an error. Startup activation logs the warning. Activation goes on, and a call that needs
  the binding fails at run time, where the inspector shows it. A binding of the
  wrong kind (`AXC0100`) and an `http` URL outside Development (`AXC0101`) stay
  errors.
- **Startup check.** A binding whose `Kind` is missing or is not `url` or
  `secret`, whose `Value` is blank, or a `url` binding whose value is not an
  absolute `http` or `https` URL, fails startup like the other
  [tenant checks](../architecture.md#tenant-configuration). The message names
  the tenant and the binding, never the value. Under `directory` the same
  check runs when the directory is read, and a `secret` binding must hold a
  stored value, which is not decrypted. If it fails, the server stops its
  start and the worker stays not ready.
- **Resolved at run time.** The worker reads a binding each time it makes a
  call, so changing a secret needs no new release.
- **HTTPS.** A `url` binding may use `http` only in Development. Elsewhere it
  must be `https`. Activation checks each connector's base URL binding and
  each `url` binding that a package operation step declares (`AXC0101`). The
  call checks it again: a worker outside Development never sends a call to an
  `http` URL, and fails the call instead. A package call is retried instead,
  and the operation does not run.

The purchasing system's bindings for tenant `default` look like this:

```json
"Tenants": {
  "default": {
    "Hosts": [ "localhost" ],
    "ConnectionString": "Host=...",
    "Bindings": {
      "PurchasingSystemUrl": { "Kind": "url", "Value": "https://purchasing.example.test/api" },
      "PurchasingSystemToken": { "Kind": "secret", "Value": "<from environment>" }
    }
  }
}
```

### Connector resource

The purchase request sample holds the purchasing system connector in
`connectors/purchasing-system.json`:

```json
{
  "id": "409bc638-0aec-4084-ada2-e3baed50057d",
  "kind": "connector",
  "name": "PurchasingSystem",
  "formatVersion": 1,
  "baseUrl": { "binding": "PurchasingSystemUrl" },
  "auth": { "type": "bearer", "token": { "binding": "PurchasingSystemToken" } },
  "timeout": "PT30S",
  "idempotency": "Idempotency-Key",
  "operations": [
    {
      "name": "createPurchaseOrder",
      "method": "POST",
      "path": "/purchase-orders",
      "input": [
        { "name": "requestNumber", "type": "text", "required": true },
        { "name": "supplierCode", "type": "text" },
        { "name": "amount", "type": "decimal", "required": true }
      ],
      "output": [{ "name": "orderNumber", "type": "text", "required": true }],
      "lookup": "findPurchaseOrder"
    },
    {
      "name": "findPurchaseOrder",
      "method": "GET",
      "path": "/purchase-orders/by-key/{idempotencyKey}",
      "input": [],
      "output": [{ "name": "orderNumber", "type": "text", "required": true }]
    }
  ]
}
```

- `baseUrl` names a `url` binding. An operation's `path` is added to it.
- `auth` is optional. Its `type` is one of these:
  - `none`, the same as no `auth`;
  - `bearer`, with `token`, a `secret` binding sent as
    `Authorization: Bearer <value>`;
  - `header`, with `name`, the header name, and `value`, a `secret` binding.
- `timeout` is an ISO 8601 duration. It defaults to `PT30S` and is at most
  `PT2M`.
- `idempotency` names the request header that carries the idempotency key.
  It is absent when the system does not support one.
- `operations` lists the named operations.
  - `method` is `GET`, `POST`, `PUT`, `PATCH` or `DELETE`.
  - `path` is a path template. `{idempotencyKey}` is the only placeholder.
    Other placeholders come later.
  - `input` fields are sent as a JSON body. A `GET` sends no body, so a `GET`
    with `input` fields is a schema error.
  - `output` fields are read from the JSON response.
  - Input and output field types are the
    [rule parameter types](resource-files.md#resource-file-shape): `text`, `integer`,
    `decimal`, `boolean`, `date` and `date-time`.
- `lookup` is optional and set per operation. It names another operation of
  the same connector that finds an earlier call by its idempotency key.
  - The lookup is a `GET` whose `path` holds `{idempotencyKey}`.
  - It has no inputs, and the same outputs as the operation it serves.
  - A `200` means found, and its body gives the output. A `404` means not
    found. Anything else is a failed lookup.
- The worker never follows a redirect. A `3xx` response fails the call, so
  the secret never goes to another host.

The sample's `PurchaseRequest` entity has a `purchaseOrderNumber` `text`
field, which the call writes. No policy lets a user update it, so the request
form shows it read-only. The sample activates only for a tenant that holds the
`PurchasingSystemUrl` and `PurchasingSystemToken` bindings. The entity also
has a `paymentReference` `text` field, which the sample extension package
writes after the purchase order is created, so the form shows it read-only too.
Development,
Docker Compose, `scripts/dev.sh` and the E2E run point them at the
[purchasing system stub](../architecture.md#purchasing-system-stub).

## Events and triggers

This section follows
[D26](../decisions/d26-events-and-triggers.md). The `event` resource
and its [compile checks](configuration.md#configuration-pipeline) are built: an event file
loads, is checked and is part of the release. A
[raise event step](process-events.md#raise-event-step) raises it. The event
`trigger` resource and its compile checks are built too: an event trigger file
loads, is checked and is part of the release. The worker delivers each raised event to the
`async` event triggers of the active release, see
[Event delivery](process-events.md#event-delivery). A `raiseEvent` step starts the processes of the
`sync` event triggers in its own transaction, see
[Raise event step](process-events.md#raise-event-step). Data change triggers start their
processes in the transaction of a record API create or update, of an inbound endpoint's
create, of a task form save, of an `updateRecord` step, of a connector output and of a package
operation output, see [Starts from triggers](process-events.md#starts-from-triggers). The schedule
`trigger` resource and its compile checks are built: a schedule trigger file loads, is checked
and is part of the release. The worker runs schedule triggers, see
[Schedules](process-events.md#schedules).
An `event` resource names something that happened to one record. A
`trigger` resource starts a process for one record when something happens:
an event is raised, a record is created or updated, or a schedule is due. How
a trigger starts a process, and when it skips the start, is in
[Events and triggers](process-events.md).

The examples use the E2E fixture application in `tests/e2e/fixtures/e2e-app`,
with its `Note` entity, its `NoteRows` data source and its `NoteReview` and
`NoteClose` processes. The fixture files do not change until the build issues
add events and triggers.

### Event resource

An event about a note that was closed, in `events/note-closed.json`:

```json
{
  "id": "f86ca48b-4afc-4060-9e40-c423522ea71d",
  "kind": "event",
  "name": "NoteClosed",
  "formatVersion": 1,
  "entity": "Note",
  "fields": [
    { "name": "priority", "type": "integer" },
    { "name": "amount", "type": "decimal" }
  ]
}
```

- `entity` names the entity the event is about. It is not a child entity.
  Each occurrence names one record of it. In a [module](modules-and-templates.md#modules) event, it
  names an entity of the same module without `/`, and the compiler qualifies it
  as `<module id>/<name>`.
- `fields` lists the typed payload fields. Their types are the
  [rule parameter types](resource-files.md#resource-file-shape): `text`, `integer`, `decimal`,
  `boolean`, `date` and `date-time`.
- `fields` is required, but the list may be empty.
- Field names are unique within the event, ignoring letter case.
- An unknown entity is `AXC0110`, a child entity `AXC0111`, a repeated field
  name `AXC0112` and an `enum`, `reference` or `child-collection` field
  `AXC0113`. A type name that is not a field type is `AXC0004`.
- A [raise event step](process-events.md#raise-event-step) raises the event for its
  subject record. Events raised by the platform itself come later.

### Trigger resource

Every trigger file has `"kind": "trigger"`. Its trigger kind goes in `type`:
`event`, `dataChange` or `schedule`. `kind` stays the resource kind, as with
steps, fields and widgets, see [processes](processes.md#resource-shape).
Every trigger names the `process` it starts, and each start is for one record
of the process's subject entity.

**Event trigger.** It starts `NoteReview` for each closed note of priority 3
or more, in `triggers/review-closed-notes.json`:

```json
{
  "id": "e348a793-af68-42ce-83b5-336a9b10c7f6",
  "kind": "trigger",
  "name": "ReviewClosedNotes",
  "formatVersion": 1,
  "type": "event",
  "event": "NoteClosed",
  "when": "priority >= 3",
  "mode": "async",
  "process": "NoteReview"
}
```

- `event` names the event. Its entity is the process's subject entity, and
  the start is for the record the occurrence names.
- `when` is optional. It is a boolean expression over the payload fields.
- `mode` is `async` or `sync`, and defaults to `async`.
  - `async`: the worker delivers the occurrence later, and starts the process
    at most once per occurrence and trigger.
  - `sync`: the start commits in the transaction of the step that raised the
    event, so both commit or neither does.

**Data change trigger.** It starts `NoteReview` when a note is created, or
when its `amount` changes, and the amount is 1000 or more, in
`triggers/review-large-notes.json`:

```json
{
  "id": "3cefd292-6c4c-4061-83e6-673993c51421",
  "kind": "trigger",
  "name": "ReviewLargeNotes",
  "formatVersion": 1,
  "type": "dataChange",
  "entity": "Note",
  "on": ["create", "update"],
  "fields": ["amount"],
  "when": "amount >= 1000",
  "process": "NoteReview"
}
```

- `entity` names the entity whose records are watched. It is the process's
  subject entity.
- `on` is a non-empty, unique list of `create` and `update`. `delete` comes later,
  because a deleted record cannot be a subject.
- `fields` is optional. If present, it is a non-empty, unique list. On an
  update, the trigger fires only when one of the named fields changed. A
  create always passes it.
- `when` is optional. It is a boolean expression over the new record, with
  the same scope as a process's `when`.
- It fires on every committed write through the record command: the record
  API, an inbound endpoint's create, a task form save, an `updateRecord` step,
  a connector output and a package operation output.

**Schedule trigger.** It starts `NoteClose` every night at 02:00 in Ho Chi
Minh City, for each note the `NoteRows` data source returns, in
`triggers/close-notes-nightly.json`:

```json
{
  "id": "26dfa49f-47d1-495d-b021-dabb0d9cbda8",
  "kind": "trigger",
  "name": "CloseNotesNightly",
  "formatVersion": 1,
  "type": "schedule",
  "schedule": "0 2 * * *",
  "timeZone": "Asia/Ho_Chi_Minh",
  "dataSource": "NoteRows",
  "parameters": {},
  "process": "NoteClose"
}
```

- `schedule` is a standard 5-field cron expression: minute, hour, day of the
  month, month and day of the week. Each field is a comma list of `*`,
  numbers, ranges such as `1-5` and steps such as `*/15` or `10-30/5`. A step
  follows `*` or a range, not a single number. The ranges are 0–59, 0–23,
  1–31, 1–12 and 0–7. Day of week 0 and 7 are both Sunday. Month and day names
  such as `JAN` or `MON` are not accepted. When both the day of month and the
  day of week are restricted, a day matches if either one matches, as in
  crontab. A field that starts with `*` counts as unrestricted. A cron that
  never matches a date is invalid: that is the case only when no allowed
  month has an allowed day of month. February counts as 29 days, so
  `0 0 29 2 *` is valid and runs in leap years.
- `timeZone` is required. It is an IANA time zone id, such as
  `Asia/Ho_Chi_Minh`. It does not settle the application time zone rule of
  [D22](../decisions/d22-current-time-and-time-zones.md).
- `dataSource` names a [data source](data-sources.md) that is not grouped.
  Its root entity is the process's subject entity. Each occurrence starts the
  process for every row it returns, in record id order. The data source's
  `pageSize` and `sort` do not apply.
- `parameters` maps the data source's declared parameters to literal values.
  It may be left out when there are none. A value uses the record API's JSON
  forms: a number for `integer` and `decimal`, `true` or `false` for
  `boolean`, and a string for the other types, which follow the
  [data source parameter](data-sources.md) forms. `null` means the parameter
  is not given, and is allowed for an optional parameter only. Every declared
  parameter needs a key. Keys match the declared names ignoring letter case,
  and the release holds the declared names.

The compiler reports these as compile errors. The event trigger checks are
built, with the codes in the [pipeline](configuration.md#configuration-pipeline): an unknown
`process` is `AXC0129`, an unknown `event` is `AXC0130`, an event about
another entity than the process's subject is `AXC0131`, and a `when` problem
has its expression code at `/when`. The data change trigger checks are built
too: an unknown `entity` is `AXC0143`, an `entity` that is not the process's
subject is `AXC0144`, an unknown or computed name in `fields` is `AXC0145`,
and a `when` problem has its expression code at `/when`. Data change
triggers compile and are part of the release. They fire on every
committed write through the record command. The schedule trigger checks are built too: an invalid `schedule` is
`AXC0160`, an unknown `timeZone` is `AXC0161`, an unknown `dataSource` is
`AXC0162`, a grouped one is `AXC0163`, one over another entity is `AXC0164`,
an unknown or repeated `parameters` key is `AXC0165`, a wrongly typed value
is `AXC0166` and a missing parameter is `AXC0167`. Schedule triggers compile
and are part of the release, and the platform computes the next occurrence of
a schedule in its time zone. On a daylight saving change a local time that
does not exist runs at the next valid time, and a repeated local time runs
once, at the first. The worker runs schedule triggers, see
[Schedules](process-events.md#schedules). The schema accepts only `"type": "event"`, `"type": "dataChange"`
and `"type": "schedule"`, so any other `type`, any other `mode`, a `when` or
`mode` on a schedule trigger and the properties of other types are `AXC0004`.

- The `process` exists.
- The subject entity matches. The event's entity, the `entity`, or the data
  source's root entity is the process's subject entity.
- The `event` exists.
- An event trigger's `when` sees only the payload fields and gives a boolean.
- A data change trigger's `when` has the scope of a process expression over
  the new record, and gives a boolean.
- `on` is non-empty and holds only `create` and `update`.
- Each name in `fields` names a field of the entity that is not computed.
- `mode` is `async` or `sync`.
- `schedule` is a valid cron expression.
- `timeZone` is a known IANA time zone id.
- The `dataSource` exists and is not grouped.
- Each `parameters` key names a declared parameter of the data source, with
  a literal value of its type.
- Each `type` has only its own properties.

## Inbound endpoints

This section follows
[D27](../decisions/d27-inbound-endpoints.md). The `endpoint` resource,
its compile checks, the call with its `output` and `wait`, its idempotency, and the
status location are built. An `endpoint` resource lets another system create
a record and start a process on it in one call. The call, its responses and its errors are in
[Inbound endpoints](inbound-endpoints.md). How a machine caller
signs in is in [Machine callers](security.md#machine-callers).

### Endpoint resource

The resource and its compile checks are built. The purchase request sample
holds an endpoint that
lets the purchasing system submit a purchase request, in
`endpoints/submit-purchase-request.json`:

```json
{
  "id": "bc241668-fe85-45a4-b7cf-d36da8d56b71",
  "kind": "endpoint",
  "name": "SubmitPurchaseRequest",
  "formatVersion": 1,
  "entity": "PurchaseRequest",
  "process": "PurchaseRequestApproval",
  "input": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    { "name": "department", "type": "reference", "target": "Department", "required": true },
    { "name": "supplier", "type": "reference", "target": "Supplier" },
    { "name": "currency", "type": "enum", "values": ["VND", "USD", "EUR"] },
    { "name": "lineItems", "type": "child-collection", "target": "LineItem" }
  ],
  "values": {
    "title": "title",
    "department": "department",
    "supplier": "supplier",
    "currency": "currency",
    "lineItems": "lineItems",
    "status": "'draft'"
  }
}
```

The new request is a `draft` with line items, so it passes the process's
`startCondition`. The process's `submit` step then moves it to `submitted`.
The request's `requester` is its [creator](roles-and-policies.md#record-creator) field, so it gets
the caller's id.

- **Entity and process.** `entity` names the entity of the record the call
  creates. It is not a child entity. `process` names the process the call
  starts on that record, and its subject entity is `entity`.
- **Input.** `input` lists the typed input fields of the call's body.
  - Their types are the
    [entity field types](resource-files.md#entity-field-types-and-constraints), including
    `reference` and `child-collection`, with the same constraints, such as
    `required`, `maxLength`, `values` and `target`.
  - `expression`, `sequence` and `creator` are not allowed on an input field.
  - Names are unique within the endpoint, ignoring letter case.
- **Values.** `values` maps subject fields to expressions over the input.
  - Each key names a writable field of `entity`: not computed, and with no
    `sequence` or `creator`.
  - Each value is an [expression](expressions.md) that sees only the input
    fields, and gives the field's type. A literal is a valid expression, such
    as `"'draft'"`.
  - A `child-collection` field takes a `child-collection` input with the same
    target, by its name. The input's rows become the record's rows.
  - Fields that `values` leaves out are `null`, as in a record API create.
- **Output.** `output` is optional. It lists field names of `entity` that a
  `201` returns when the instance completes within `wait`.
- **Wait.** `wait` is an ISO 8601 duration, more than zero and at most
  `PT30S`. It is how long the call waits for the instance to complete.
- **Together.** `output` and `wait` are declared together, or neither is.

An endpoint without `output` answers `201` as soon as the record and the
instance are stored. The approval process always waits for a manager, so
`SubmitPurchaseRequest` declares no output.

An endpoint whose process finishes without a human task can return output.
The E2E fixture application in `tests/e2e/fixtures/e2e-app` has a `Note`
entity and a `NoteClose` process that closes a note. An endpoint that creates
a note, closes it and returns its status, in
`endpoints/import-closed-note.json`:

```json
{
  "id": "83244ba2-b26f-4348-aa7a-0e39eb992fd3",
  "kind": "endpoint",
  "name": "ImportClosedNote",
  "formatVersion": 1,
  "entity": "Note",
  "process": "NoteClose",
  "input": [
    { "name": "title", "type": "text", "required": true, "maxLength": 200 },
    { "name": "code", "type": "text", "maxLength": 50 },
    { "name": "amount", "type": "decimal", "precision": 18, "scale": 2 }
  ],
  "values": {
    "title": "title",
    "code": "code",
    "amount": "amount",
    "status": "'open'"
  },
  "output": ["code", "status"],
  "wait": "PT10S"
}
```

When the worker runs the `close` step within 10 seconds, the call answers
`201` with `code` and `status`. Otherwise it answers `202` with a status
location. The purchase request sample holds `SubmitPurchaseRequest`. The E2E
fixture application does not hold `ImportClosedNote`.

A [data change trigger](#trigger-resource) on `create` of the same entity
that starts the same process would start it inside the endpoint's record
create. The endpoint's own start would then find the instance running, and
every call would answer `409`. This is a configuration mistake. The compiler
does not prevent it.

The compiler reports these as compile errors, all in one pass. The
[Resolve step](configuration.md#configuration-pipeline) gives the details.

- The `entity` exists and is not a child entity: `AXC0114` and `AXC0115`.
- The `process` exists, and its subject entity is `entity`: `AXC0116` and
  `AXC0117`.
- An input field's type is an entity field type, with only that type's
  properties. A property on the wrong type is `AXC0013`, and a missing
  `target` or `values` is `AXC0014`. The schema refuses `label`, `unique`,
  `expression`, `sequence` and `creator` on an input field, as `AXC0004`.
- Input names are unique, ignoring letter case: `AXC0118`.
- A `reference` or `child-collection` input names an existing `target`
  (`AXC0012`). A `reference` target is not a child entity (`AXC0040`). A
  `child-collection` target is a child entity (`AXC0119`).
- Each `values` key names a field of `entity`, once (`AXC0120`). The field is
  not computed, and has no `sequence` or `creator` (`AXC0121`).
- Each `values` expression sees only the input fields, and gives the field's
  type. It cannot call rules or aggregates, or follow a path. An `enum`
  input fits an `enum` field when all its values are the field's values. A
  `reference` input fits a `reference` field with the same target. A
  problem gets its [expression diagnostic](expressions.md#diagnostics) code,
  such as `AXC0048`.
- A `child-collection` field is mapped to a `child-collection` input with the
  same target, by its name: `AXC0122`.
- Each `output` entry names a field of `entity`: `AXC0123`.
- `output` and `wait` are declared together. The schema checks this, so
  breaking it is `AXC0004`.
- `wait` is a valid ISO 8601 duration, more than zero and at most `PT30S`:
  `AXC0124`. So `PT0S` is `AXC0124`.
