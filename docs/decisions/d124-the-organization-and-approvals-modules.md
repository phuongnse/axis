# D124. The organization and approvals modules

Status: **Agreed**

## Context

The first built-in modules of [D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md)
and [D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) serve a small business
with no IT team, so who approves what must be data that an office manager changes. Routing work by a
line manager needs employees and their managers, not only departments, and the module's promise must
hold against any layer a business writes.

## Decision

- **Request types decide the approval path.** `approvals` holds request types, each with its
  approval steps in order: the requester's line manager, the head of their department or a named
  employee, each with an optional minimum amount. A business's administrator changes them in the
  running application, with no Studio and no publish. Studio and layers stay for deeper changes.
- **`organization` holds departments and employees.** A department has a head and a parent, and an
  employee a line manager and the account they sign in with. It keeps only the contact and routing
  data an office needs, and no further personal data, see
  [the organization module](../reference/built-in-modules.md#the-organization-module).
- **Delegation.** An employee names a delegate and the dates of an absence. While it lasts, an
  approval goes to the delegate, and the decision records whom it was made for.
- **The contract of approvals.** Nobody decides their own request, and each step is decided once,
  whatever a business changes. `approved` and `rejected` are protected states, moving to the next
  step needs a new deciding task through the `decisionTask()` of
  [D121](d121-how-a-module-contract-is-declared-and-checked.md), and a step whose approver would be
  the requester goes to the `admin` slot.
- **General expression features, not approvals code.** An aggregate may follow a path to another
  record's collection, `outer.<field>` reads the outer record inside an item expression,
  `first(list, condition, item)` picks the first matching row, and a reference field may `match` its
  record by a unique field. Expressions still run no query of their own: their rows come from the
  references a step follows.
- **A built-in application is a template marked built in.** `applications/approvals` is a thin
  template with `"builtIn": true`, which Studio lists first and from which tenants are created. It
  replaces the `departments` sample module and the `request-approval` template.

The detail is in [built-in modules](../reference/built-in-modules.md), the
[expression functions](../reference/expressions.md#functions) and
[matched references](../reference/resource-files.md#matched-references).

## Consequences

- A new request type needs no Studio and no publish, as in Lark Approval, Base.vn and Odoo Approvals.
- Delegation uses the existing reassign and task assignees, with no new engine feature.
- Any module or application that reads a list on a related record gets the expression features, and
  expressions stay deterministic and within their cost bounds.
- The protected states, the deciding task and the accountable user make the module's promise hold
  against any layer.
- The approval process is a loop over the type's steps rather than a fixed path, and five general
  platform features come before the modules.

## Alternatives

- **A fixed approval process.** Every new request type would go through Studio and a publish.
- **A `lookup()` function in expressions.** It would hide a query inside an expression.
