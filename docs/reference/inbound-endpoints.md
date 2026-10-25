# Inbound endpoint calls

Detailed reference for how another system calls an inbound endpoint: the call, the order of
checks, the responses, idempotency and the status location. The `endpoint` resource is in
[integration-resources.md](integration-resources.md#inbound-endpoints).
The conventions of [architecture.md](../architecture.md) apply: sections marked
_(planned for Mx)_ are not built yet, and Dn refers to [decisions.md](../decisions.md).

The design follows [D27](../decisions/d27-inbound-endpoints.md).
The bearer token check, its `401`, the CSRF exemption and the call are built.
The call creates the record and starts the process, with the order of checks,
the responses, `output` with `wait`, the audit records,
[idempotency](#idempotency) and the [status location](#status-location)
below. An
[`endpoint` resource](integration-resources.md#endpoint-resource) lets
another system create a record and start a process on it in one call. The
caller signs in with a bearer token, as
[Machine callers](security.md#machine-callers) describes. Endpoints
are an exception to the session and CSRF rules above, as are the
[service accounts](security.md#service-accounts) of the record API. Data source
rows and the process API add to them _(planned for M16)_. Under a tenant's `fapi2` profile an endpoint accepts
only DPoP-bound tokens _(planned for M16)_.

## The call

The purchasing system submits a purchase request through the sample's
`SubmitPurchaseRequest` endpoint:

```http
POST /api/apps/PurchaseRequests/endpoints/SubmitPurchaseRequest
Authorization: Bearer eyJhbGciOiJSUzI1NiIs…
Content-Type: application/json
Idempotency-Key: po-import-20261012-0042

{
  "input": {
    "title": "Standing desks",
    "department": "0192f4a7-3c1d-7e2f-9a4b-5c6d7e8f9a0b",
    "currency": "VND",
    "lineItems": [
      { "description": "Desk", "quantity": 2, "unitPrice": 1250.45 }
    ]
  }
}
```

- **Address.** `{app}` and `{name}` match ignoring letter case, as `{app}`
  and `{entity}` do on the record routes.
- **Bearer token.** `Authorization: Bearer <token>` is required. A session
  cookie is ignored, so it neither signs the caller in nor causes an error.
- **No CSRF header.** The call needs no `X-Axis-Csrf` header. A browser
  cannot send a bearer token on its own, so a cross-site page cannot make the
  call for a signed-in user.
- **Content type.** `Content-Type: application/json`, by the
  [record API's rule](record-api.md#create-update-and-delete).
- **Body.** `{ "input": { … } }`. `input` is parsed against the endpoint's
  input fields by the [record API's body rules](record-api.md#request-bodies-and-values),
  with `input` in place of `values`. Any other body property is an error at
  `/<property>`.
- **`Idempotency-Key`.** The header is optional. See
  [Idempotency](#idempotency).
- **No other grants.** The caller needs the `invoke` grant on the endpoint.
  Entity and process grants are not checked, because the record create and
  the process start are part of the endpoint's contract.

## Order of checks

A call is checked in this order, and the first failure is the response:

1. The bearer token, `401` with `WWW-Authenticate: Bearer`. It comes before
   the path, so a caller without a valid token cannot learn which
   applications or endpoints exist.
2. The application and the endpoint, `404`.
3. The `invoke` grant on the endpoint, `403`.
4. The content type, `415`.
5. The body, `400` keyed `/input/…`.
6. The `Idempotency-Key` header, `400`. A value must be non-empty and at
   most 255 characters.

One transaction then does the rest:

1. With a key, it stores the receipt first. When the key is taken, it rolls
   back and answers as [Idempotency](#idempotency) describes.
2. It maps the input to values and creates the record through the record
   create command. So references, sequences, computed fields and validations
   apply, as in a [record API create](record-api.md#create-update-and-delete). The
   creator field gets the caller's id. Errors are a `400` keyed
   `/values/<field>`, and a unique-value conflict is a `409`.
3. It starts the processes of the [data change triggers](process-events.md#starts-from-triggers)
   on `create` for the entity, as `system`, in the order of the release. A
   skipped trigger start does not fail the call.
4. It evaluates the process's `startCondition` on the new record. A failure
   is a `400` keyed `/input`, with the condition's message text key.
5. It inserts the instance, pinned to the active release, with its first
   work item, as the [process start](processes.md#start-endpoint) does. An
   instance already running for the record is a `409`.
6. It writes the audit records `record.created` and `process.started`.

When any part fails, nothing is written. After the commit, an endpoint with
`wait` checks the instance's state until it completes or `wait` runs out.

- **No connection held.** The call holds no transaction and no database
  connection while it waits. It reads the state every 200 ms, each time over
  a pooled connection that it returns at once.
- **Failure ends the wait.** A failed instance never completes, so the call
  stops waiting as soon as it reads `failed`.

- **Actor.** The actor of both audit records is the caller's id, from the
  token's user id claim (`sub`). The display name is stored with them, see
  [Machine callers](security.md#machine-callers).
- **Details.** Both details add `"endpoint": "<name>"`. So
  `record.created` holds `{ "version": 1, "endpoint": "SubmitPurchaseRequest" }`,
  and `process.started` holds
  `{ "processId", "releaseId", "endpoint": "SubmitPurchaseRequest" }`.

## Responses

Errors are problem details (`application/problem+json`), and their titles are
fixed, as in [Errors](record-api.md#errors).

**`201` without output.** The record and the instance are stored. An
endpoint without `output` answers at once, and the instance is still
`running`:

```json
{
  "recordId": "0192f5b1-7a2c-7d3e-8f4a-5b6c7d8e9f01",
  "instanceId": "0192f5b1-7a40-7b1c-9d2e-3f4a5b6c7d8e",
  "state": "running"
}
```

**`201` with output.** The endpoint declares `output` and `wait`, and the
instance completed within `wait`. The fixture's `ImportClosedNote` returns
the note's `code` and `status`:

```json
{
  "recordId": "0192f5c0-1b2c-7d3e-8f4a-5b6c7d8e9f02",
  "instanceId": "0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f",
  "state": "completed",
  "output": { "code": "N-1001", "status": "closed" }
}
```

**`202`.** The endpoint declares `output` and `wait`, and the instance did
not complete within `wait`. The body has no `output`, and `Location` names
the status location. `Location` uses the declared names of the application
and the endpoint, not the letter case of the request. `state` is `running`
or `waiting`, or `failed` when the instance failed:

```http
HTTP/1.1 202 Accepted
Location: /api/apps/E2eApp/endpoints/ImportClosedNote/instances/0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f
Content-Type: application/json

{
  "recordId": "0192f5c0-1b2c-7d3e-8f4a-5b6c7d8e9f02",
  "instanceId": "0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f",
  "state": "running"
}
```

**`400`.** The input does not fit the schema, the record create refuses the
values, or the start condition fails. Nothing is written.

- Input that does not fit the endpoint's input fields is keyed
  `/input/<name>`, or `/input/<collection>/<index>/<field>` for a row.
- A `reference` value that names no record, and a failed validation, are
  keyed `/values/<field>`, or by a row path such as
  `/values/lineItems/0/quantity`. The mapping from input to fields need not
  be one-to-one, so these errors point at the record's own fields.
- A start condition that fails is keyed `/input`.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "/input/title": ["Must be at most 200 characters."],
    "/input/lineItems/0/quantity": ["Must be an integral number within the signed 64-bit range."]
  }
}
```

**`401`.** No `Authorization` header, a token that is not a bearer token,
or a token whose signature, issuer, audience or expiry fails. A tenant
without `Oidc:EndpointAudience` answers `401` to every call. The response
carries `WWW-Authenticate: Bearer`, and it never says which check failed:

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2",
  "title": "A valid bearer token is needed to call this endpoint.",
  "status": 401
}
```

**`403`.** The caller has no `invoke` grant on the endpoint. Only roles
claim values that name a declared role of the application count. The title
is the record API's:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.4",
  "title": "You have no grant for this action.",
  "status": 403
}
```

- **`404`.** An unknown application, an application with no active release,
  or an unknown endpoint.
- **`409`.** A value repeats a `unique` field of another record, keyed
  `/values/<field>` as in the record API. Or an instance of the process
  already runs for the new record.
- **`415`.** A body without `Content-Type: application/json`.
- **`422`.** The `Idempotency-Key` was used before by the same caller on the
  same endpoint with a different input.
- **`503`.** The application's active release changed on each of three
  attempts, found under the [start lock](processes.md#releases-and-running-work).
  The server already ran the call again on the new release each time. Nothing
  was written, and the caller may send the request again. The title is fixed:
  "The application's active release changed while the request ran. Try again."

## Idempotency

A key is unique per endpoint and caller. Two callers can use the same key
without meeting.

- **Repeat.** A repeat with the same key and the same input returns the same
  record and instance. It creates no record and starts no instance.
- **Never waits.** A repeat follows the instance's state at that moment. For
  an endpoint with `output`, it is a `201` with output when the instance has
  completed, otherwise a `202` with the status location. For an endpoint
  without `output`, it is the `201` the first call gave, with the current
  state.
- **Different input.** The same key with a different input is a `422`, and
  nothing is written. The receipt stores a SHA-256 hash of the input, not a
  copy of it. The same JSON value counts as the same input, whatever its
  whitespace and property order. A number written another way, such as `1`
  and `1.0`, is a different input.
- **Only stored on success.** A call that fails stores no receipt. It can be
  retried with the same key after the input is fixed.
- **Concurrent repeats.** They are serialized by the receipt's unique key, so
  only one of them creates a record.

A repeat of the `202` call above, after the worker closed the note:

```json
{
  "recordId": "0192f5c0-1b2c-7d3e-8f4a-5b6c7d8e9f02",
  "instanceId": "0192f5c0-1b40-7c2d-9e3f-4a5b6c7d8e9f",
  "state": "completed",
  "output": { "code": "N-1001", "status": "closed" }
}
```

A repeat of the same key with a different input:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "This idempotency key was used with a different input.",
  "status": 422
}
```

## Status location

`GET /api/apps/{app}/endpoints/{name}/instances/{instanceId}` reads the
state of an instance that an endpoint call started.

- **Checks.** It needs a bearer token and the `invoke` grant on the
  endpoint, checked in the same order as the call: `401`, then `404` for the
  path, then `403`.
- **Own calls only.** Only the caller who made the call can read it. Another
  caller's instance, an instance of another endpoint, an unknown one and an
  `{instanceId}` that is not a UUID are all `404`, titled "No instance of
  this endpoint exists with this id.". So one system cannot read another
  system's output. The caller and the endpoint come from the instance's
  `process.started` audit record.
- **`200`.** The body is the call's body: `recordId`, `instanceId` and
  `state`. `output` is there only when the instance is `completed`.
- **Output as contract.** The output values are read as the endpoint's
  contract, outside field grants, as the call's own `201` does.
