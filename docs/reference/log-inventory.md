# Log inventory

Every log Axis writes: what it records, its format, where it goes, who reads it and how long it is
kept. The design of telemetry is in [telemetry](../architecture.md#telemetry), and the rows of the
[ASVS](asvs.md#requirements) chapter on logging point here. OTLP is the OpenTelemetry export
protocol.

## Logs

| Log                                | Events                                                                                                                           | Format                                                              | Where it goes                                                                                          | Who reads it                                   | Kept for                                            |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ | ---------------------------------------------- | --------------------------------------------------- |
| Server console                     | Startup, failed requests, query timeouts, refused bearer tokens and `403` answers, see [logs](../architecture.md#logs)          | One entry per line with a UTC timestamp, a level, a category and the scopes | Standard output of the server                                                                          | The operator                                   | The operator's: the container runtime or log driver |
| Worker console                     | Startup, failed steps, failed work items, skipped trigger starts, see [logs](../architecture.md#logs)                            | The same as the server console                                      | Standard output of the worker                                                                          | The operator                                   | The operator's                                      |
| OTLP logs of both hosts            | The same entries as the consoles                                                                                                 | OpenTelemetry log records with the scopes as attributes             | The operator's collector, when an endpoint is set. Outside Development the endpoint must be `https`, see [telemetry export](host-settings.md#telemetry-export) | The operator, in the collector's tools         | The operator's: the collector's store               |
| Tenant audit records               | Record writes, process steps, task completions, imports, operator commands, publish and rollback, sign-in, sign-out and refused sign-in | Rows of `axis.audit_records` in the tenant database, see [audit records](storage.md#audit-records-and-sequence-counters) | The tenant database, in the same transaction as the action                                             | Users with a grant, through the record history, and the operator | Forever. The database rejects every update and delete, see [D50](../decisions/d50-sign-in-audit-records-and-security-scanning-in-ci.md) |
| Platform audit records             | Every `Axis.Admin` command that acts on a tenant, see [platform audit records](tenant-setting-commands.md#platform-audit-records) | Rows of `platform.audit_records`                                    | The platform database                                                                                  | `Axis.Admin` only. The server and the worker cannot read them | Forever. The database rejects every update, delete and truncate |
| Step history and call attempt rows | Every step run and every connector, package or lookup call attempt, with the `trace_id` of the run, see [tables](processes.md#tables) | Rows in the tenant database                                         | The tenant database                                                                                    | Users who may inspect the instance, and the operator | As long as the instance's rows                      |

- **Keycloak.** The events of the identity provider that Axis ships belong to it. Its sign-in and
  admin events are the operator's, see [Axis's identity provider](identity-provider.md).
- **Access.** Console and OTLP logs are the operator's to protect. Axis writes no log file, so no
  file permission is needed. The database rejects changes to the audit tables, see the
  [ASVS](asvs.md#requirements) row for protected logs.
- **Storage and retention.** The operator owns the storage and the retention of console and OTLP
  logs. Axis sets neither, and ships no collector.
- **Nothing else.** The hosts store or send logs nowhere else than the rows above.

## Security events

| Event                                      | Logged as                                                                                                             |
| ------------------------------------------ | --------------------------------------------------------------------------------------------------------------------- |
| Sign-in, sign-out and a refused sign-in    | Tenant audit records `session.signedIn`, `session.signedOut` and `session.signInRefused`, with the reason and the client address |
| A refused bearer token                     | A warning with the tenant, the caller kind and the reason. The reason is a fixed text or the type of the exception that the token library threw, never its message or any part of the token |
| A `403` answer                             | A warning with the HTTP method and the route template, such as `/api/apps/{app}/entities/{entity}/records`, never the path or the query string. It covers a missing grant, a record outside the user's filters, a missing CSRF header and the Studio role checks |
| A rate-limit refusal (`429`)               | The counter `axis.rate_limits.refused`, by partition, see [rate limits](headers-and-rate-limits.md#rate-limits)         |
| Publish and rollback                       | Tenant audit records, when they succeed and when they fail                                                            |
| A platform setting command                 | A platform audit record, see [platform audit records](tenant-setting-commands.md#platform-audit-records)               |

- **No entry for an anonymous `401`.** A request with no token, and one with no session, writes no
  entry. It carries no signal beyond noise.
- **A valid token.** It writes no entry of its own. The audit records of the actions it performs
  name the caller.
- **Alerts** _(planned for M28)_. Each event above is also a metric, and the Helm chart ships alert
  rules for a spike of refused sign-ins, refused tokens, `403` answers and `429` refusals per
  tenant, a failing health check and a failed startup check, see
  [D110](../decisions/d110-security-alerts-and-a-hardening-checklist-for-the-operator.md).

## Entry metadata and time

- **When.** The console shows a UTC timestamp, such as `2026-01-31T09:30:12.345Z`. OTLP records carry
  their own timestamp. An audit record's `occurred_at` is `timestamptz`.
- **Where.** The service name, and the `TenantId` in the scope of a request.
- **What.** The message names things and never holds a value, see
  [what telemetry never holds](../architecture.md#what-telemetry-never-holds).
- **Correlation.** The `TraceId` and `SpanId` in the scope, and the `traceId` of a problem details
  answer, see [correlation](../architecture.md#correlation).
- **Who.** A log entry holds no user id, by design. The audit record holds the actor.
- **Clocks.** The operator keeps the clocks of the hosts in sync, for example with NTP, so the
  timestamps of the server, the worker, the database and the collector line up.
