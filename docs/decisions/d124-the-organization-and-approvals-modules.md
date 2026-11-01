# D124. The organization and approvals modules

Status: **Agreed**

The owner agreed this on 2026-11-01. It sets what the first built-in modules of
[D102](d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) and
[D117](d117-small-businesses-first-and-the-milestones-in-three-phases.md) hold, and the general
expression features they need, so that M26 can be broken into issues. Everything here is
_(planned for M26)_. [Built-in modules](../reference/built-in-modules.md) holds the detail.

- **Request types decide the approval path:**
  - `approvals` holds request types. Each lists its approval steps in order: the requester's line
    manager, the head of their department, or a named employee, each with an optional minimum
    amount.
  - A business's administrator changes them in the running application, without Studio or a
    publish.
  - Studio and layers stay for deeper changes, such as a field of a type's own or another rule.
- **Organization holds departments and employees:**
  - `organization` holds departments, with a head and a parent, and employees, with a line manager
    and the account they sign in with.
  - It keeps no personal data beyond what routing work needs. The phone number is `sensitive`.
- **Delegation in M26:**
  - An employee names a delegate and the dates of an absence.
  - While the absence lasts, an approval goes to the delegate, and the decision records whom it was
    made for.
- **The contract of approvals:**
  - Nobody decides their own request, and each step is decided once, whatever process change a
    business makes.
  - `approved` and `rejected` are protected states.
  - Moving to the next step needs a new deciding task, through the `decisionTask()` of
    [D121](d121-how-a-module-contract-is-declared-and-checked.md).
  - A step whose approver would be the requester goes to the `admin` slot.
- **General expression features, not approvals code:**
  - An aggregate may take a path that ends at another record's collection, such as
    `count(type.steps)`, in process expressions.
  - `outer.<field>` reads the outer record inside an item expression.
  - `first(list, condition, item)` picks the first matching row.
  - A reference field may `match` its record by a unique field, such as the employee whose `user`
    is the requester.
  - Expressions still run no query of their own. The rows come from references the step follows,
    so they stay deterministic and within the cost bounds.
- **Built-in applications are templates marked built in:**
  - `applications/approvals` is a thin template with `"builtIn": true`. Studio lists it first, and
    the operator creates tenants from it.
  - The `departments` sample module and the `request-approval` template are replaced by
    `organization` and the `approvals` application.

_Why:_ a small business has no IT team, so who approves what must be data that an office manager
changes. This is how Lark Approval, Base.vn and Odoo Approvals work. A fixed process would send
every new request type through Studio and a publish. Routing by line manager needs employees and
their managers, not only departments. Delegation is an everyday need for a business whose director
travels, and the existing reassign and task assignees carry it with no new engine feature. The
expression features are general: any module or application that reads a list on a related record
needs them, and keeping them pure keeps expressions free of hidden queries, unlike a `lookup()`
function. The protected states, the deciding task and the accountable user make the module's promise
hold against any layer, which is what sets Axis apart. The cost is a larger M26: five general
platform features before the modules, and a process with a loop instead of a fixed path.
