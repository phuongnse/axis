# D82. Performance SLOs and load tests

Status: **Agreed**

## Context

A per-request budget shows that one call is fast alone. It does not show what happens when many users
share a tenant, a replica and a database, at the
[typical application size](../domain/knowledge.md#typical-application-size).

## Decision

- **Objectives are 95th percentiles at 50 concurrent users per tenant:** reads within 300 ms, writes
  within 500 ms, due work claimed within 5 seconds, and fewer than 0.1% of requests failing with a
  `5xx`.
- **They hold for one reference environment,** two server replicas behind the TLS proxy of
  [D49](d49-several-server-replicas-behind-a-tls-proxy.md), one worker and one PostgreSQL server.
  An installation measures its own.
- **Each objective maps to a metric the hosts export,** so an operator can watch it in production.
  Axis builds no alerting.
- **A k6 load test measures them** against the Compose stack, signed in through the identity
  provider's page, and fails when one is missed.
- **It never gates a merge.** CI runs it weekly and on demand.
- **A new route in a group joins the load test.**

The routes, the environment and the script are in [performance](../reference/performance.md). When
the `*PerformanceTests` classes run is
[D139](d139-ci-runs-on-main-alone-and-nexkit-checks-every-branch.md).

## Consequences

- Two runs in the same environment can be compared.
- A slowdown shows up in the weekly run, after the merge that caused it.

## Alternatives

- **Gate merges on it.** A run on a shared CI machine varies too much to block a pull request.
