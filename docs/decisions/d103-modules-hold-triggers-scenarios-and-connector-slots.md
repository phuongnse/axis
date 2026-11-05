# D103. Modules hold triggers, scenarios and connector slots

Status: **Agreed**

## Context

Built-in modules ([D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)) must ship
features that run on their own. A leave module must add each year's leave and open a balance for a
new employee, and later modules such as purchasing and accounting must call systems that the module
cannot know. [D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)
left triggers out of a module together with connectors and endpoints, although event, data change and
schedule triggers need no binding and no inbound surface.

## Decision

- **A module holds triggers** of every type: `event`, `dataChange`, delete triggers included, and
  `schedule`. A module trigger starts a process of its own module, and reacts to an event or an
  entity of its own module or of a module it declares. A schedule runs in the
  [time zone](d75-the-application-time-zone.md) of each application that uses the module, with
  occurrences of its own in each application's copy.
- **A module holds scenarios,** the contract scenarios of D102 among them. A module scenario gives
  its users role slots, which become the mapped application roles when it runs against the
  application as customized.
- **Connector slots stand for connectors.** `module.json` declares connector slots, each with the
  operations the module needs and their typed inputs and outputs, as a
  [connector operation](d25-connectors-deployment-bindings-and-secrets.md) has them. A module process
  calls a slot with a `callConnector` step. The application's `modules` entry maps each slot to one
  of its connectors, which must have every operation of the slot with matching fields, or the
  compile fails. A depending module maps the slots of a module it depends on to its own, as it maps
  role slots.
- **The connector keeps its own settings.** The application's connector keeps its base URL, auth,
  bindings, timeout, idempotency header and lookup, and a module never names a binding.
- **The inbound surface stays in the application.** A module holds no `role`, `site`,
  `identityProvider`, `connector`, `endpoint` or `webhook`. An application's endpoint may name a
  module entity and process, and a webhook may name a module event.

The detail is in
[processes, pages, policies and dependencies](../reference/modules-and-templates.md#processes-pages-policies-and-dependencies).

## Consequences

- A module ships a working feature that starts its own work and reaches outside systems, and a
  business does not rebuild those parts every time.
- A wrong mapping of a connector slot fails at compile time, not when the call runs.
- Each application decides which outside system a module calls and which inbound surface it opens.

## Alternatives

- **Connectors inside a module.** A module would have to know the bindings and addresses of every
  installation that uses it.
