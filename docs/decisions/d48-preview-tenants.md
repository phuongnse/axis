# D48. Preview tenants

Status: **Agreed**

## Context

[D46](d46-a-general-purpose-low-code-platform-and-the-milestones-after-m9.md) asks that an author can
try a change before publishing it. A tenant's data is live and its bindings point at real systems, so
the author needs a place with its own data and bindings.

## Decision

- **A preview tenant belongs to one main tenant,** which has at most one, shared by all its
  applications. A preview tenant has no preview of its own.
- **It is a tenant like any other,** with its own database, roles, hosts and status. The operator
  creates it once.
- **It signs people in through its main tenant,** and a session stays bound to the tenant it signed
  in on.
- **Its bindings are its own and empty by default,** so a preview never reaches a real system by
  accident. A missing binding is only a warning at activation.
- **Publishing to the preview needs the author role only,** runs as
  [D47](d47-publishing-from-studio.md) describes and is recorded in the preview's own history.
  Publishing to the main tenant needs no preview first.
- **A preview starts with seed data,** and its records stay until the operator recreates it.

The hosts' rules are in [preview tenants](../architecture.md#preview-tenants) and the route in
[preview](../reference/authoring.md#preview).

## Consequences

- Two authors who preview different applications of one tenant share one preview.
- A preview with a connector shows failed calls until the operator gives it bindings.
- Any author can preview, and only publishing to the main tenant needs the publisher role.

## Alternatives

- **One preview tenant per application.** It would keep authors of different applications apart,
  but one preview tenant per main tenant is simpler.
