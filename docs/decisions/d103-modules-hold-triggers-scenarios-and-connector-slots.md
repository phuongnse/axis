# D103. Modules hold triggers, scenarios and connector slots

Status: **Agreed**

The owner agreed this on 2026-10-29. Built-in modules
([D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)) must
ship features that run on their own, and later modules such as purchasing and
accounting must talk to systems that the module cannot know. This decision
widens what a module holds. It replaces the point of
[D89](d89-modules-that-hold-processes-pages-and-policies-and-depend-on-other-modules.md)
that a module holds no trigger, and adds connector slots beside its role
slots. Triggers, connector slots and endpoints on module processes are
_(planned for M20)_. Module scenarios are _(planned for M24)_. See the
[roadmap](../roadmap.md#milestones).

- **Triggers:**
  - A module may hold `trigger` resources of every type: `event`,
    `dataChange`, delete triggers included, and `schedule`.
  - A module trigger starts a process of its own module. It reacts to an
    event or an entity of its own module, or of a module it declares, such as
    `leave` reacting to `employees/EmployeeHired`.
  - A schedule trigger runs in the time zone of each application that uses the
    module ([D75](d75-the-application-time-zone.md)). Each application's copy
    of the module has its own occurrences, as it has its own tables.
  - _Why it was left out and why it comes in:_ D89 left triggers out with
    connectors and endpoints, which need bindings or an inbound surface. Event,
    data change and schedule triggers need neither. Without them a module
    cannot, for example, add each year's annual leave or open a leave balance
    for a new employee, and every business would write that again.
- **Scenarios:**
  - A module may hold `scenario` resources. The contract scenarios of D102
    are among them.
  - A module scenario gives its users role slots, not roles. When it runs for
    an application, each slot becomes the application role it maps to, and the
    scenario runs against the application as customized.
- **Connector slots:**
  - `module.json` gets `connectors`, a list of connector slots. Each slot has
    a `name`, a `label` and the `operations` the module needs, each with a
    `name` and typed `input` and `output` fields, as a connector operation
    has ([D25](d25-connectors-deployment-bindings-and-secrets.md)).
  - A module process calls a slot with a `callConnector` step that names the
    slot and one of its operations, as an application step names a connector.
  - The application's `modules` entry gets `connectors`, which maps each slot
    to a connector of the application, as `"connectors": { "orders":
    "PurchasingSystem" }`. A slot left unmapped, or mapped to an unknown
    connector, is a diagnostic.
  - The mapped connector must have every operation of the slot, by name. Each
    input of the slot is an input of the operation with the same type, every
    required input of the operation is an input of the slot, and each output of
    the slot is an output of the operation with the same type. A mismatch is a
    diagnostic that names the slot, the operation and the field.
  - The application's connector keeps its own base URL, auth, bindings,
    timeout, idempotency header and lookup. A module never names a binding.
  - A depending module maps the connector slots of the module it depends on
    to its own slots, as it maps role slots.
- **Endpoints and webhooks stay in the application:** an inbound surface and
  its bindings belong to the application, so a module still holds no
  `endpoint` and no `webhook`. An application's endpoint may name a module
  entity and process as `<module id>/<name>`, and a webhook already names a
  module event.
- **Still out of a module:** `role` (role slots stand for it), `site`,
  `identityProvider`, `connector`, `endpoint` and `webhook`. `migration` comes
  in with D102.
- **M20's name:** M20 becomes "Modules that ship working features", because
  it now builds processes, pages, policies, triggers, connector slots and
  dependencies.

_Why:_ a module that cannot start its own work on a schedule or on a change,
or reach an outside system, is not a working feature, and a business would
rebuild those parts every time. Role slots already let a module name who acts
without knowing the application's roles. Connector slots do the same for the
systems it calls: the module states the operations it needs, and the
application, which owns the bindings, plugs in its real connector. The compile
check of operations and field types makes a wrong mapping fail before it runs.
Endpoints and webhooks stay with the application, because a module cannot know
which inbound surface or outbound address an installation allows.
