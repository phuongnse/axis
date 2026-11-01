# Built-in modules

The first built-in modules are `organization` and `approvals`, and the first built-in application,
`approvals`, uses them. A small business starts from the application and changes it to fit: its
administrator sets request types and approval steps in the running application, and an author
changes anything else in Studio through [customization layers](customization-layers.md). The
[module contracts](module-contracts.md) hold whatever either of them changes.
[D102](../decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) agrees the
idea, and [D124](../decisions/d124-the-organization-and-approvals-modules.md) agrees what the modules
hold. Everything here is _(planned for M26)_. The `service-desk` and `assets` modules are
_(planned for M30)_.

The conventions of [architecture.md](../architecture.md) apply. Diagnostic codes come with the
issues that build each check.

## Where they live

- **Modules** are under `modules/<id>/` at the repository root, each a module folder with its
  `module.json`, as [modules](modules-and-templates.md#modules) describes. Each has its own SemVer
  version, starting at `1.0.0`.
- **Built-in applications** are under `applications/<id>/`. A built-in application is an
  [application template](modules-and-templates.md#application-templates) whose `template.json` has
  `"builtIn": true`. It is thin: `application.json` declares the modules and maps their role slots,
  and the folder holds the roles, a site, an identity provider and texts. It holds no entity,
  process, page or policy of its own.
- **Installed with Axis.** `scripts/build.sh`, `scripts/dev.sh` and the E2E run install the modules
  under `artifacts/modules/` and the built-in applications under `artifacts/templates/`, next to the
  samples. The images hold them in `/modules` and `/templates`, so every installation has them.
- **Creating an application from one.** Studio's new application dialog lists the built-in
  applications first, marked as built in, then the templates. Creating one copies the thin folder,
  as any template does, so the application declares the modules instead of copying them, and every
  change after that is a layer. The platform operator creates a tenant from one too, see
  [tenant lifecycle](tenant-lifecycle.md) _(planned for M31)_.
- **No platform code.** Neither module is named by any engine. They are configuration, compiled by
  the same compiler as any application.

## The organization module

`organization` holds the departments and the employees of the business, which other modules route
work by. It keeps no personal data beyond what that needs.

- **`Department`:** `code`, unique, in capital letters, `name`, `parent`, a reference to another
  department, `head`, a reference to an employee, and `active`.
- **`Employee`:**
  - `fullName`, `workEmail`, unique, `phone`, which is `sensitive`, `jobTitle`, `department` and
    `manager`, a reference to another employee;
  - `user`, unique, the id of the person's account, empty for an employee who never signs in;
  - `active`;
  - `delegate`, a reference to another employee, with `delegateFrom` and `delegateUntil`, the
    dates of an absence.
- **No more personal data.** No date of birth, no identity number, no address. A business that
  needs one adds it through a layer, and marks it `sensitive`, as
  [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md)
  asks.
- **Linking an account.** An administrator invites the person on the
  [users page](user-administration.md), then picks the account in the employee's `user` field,
  which lists the tenant's users.
- **Rules.** `IsDelegating(from, until)` is true when today is between the two dates. Other modules
  call it as `organization:IsDelegating(…)`.
- **Contracts:**
  - `Department`: a department is not its own parent.
  - `Employee`: an employee is not their own manager or their own delegate, and a delegate needs
    both dates, the second not before the first.
  - Ceilings: the `admin` slot gets every action on both. The `member` slot reads both, and updates
    only `phone`, `delegate`, `delegateFrom` and `delegateUntil` of the employee whose `user` is
    their own.
- **Role slots.** `admin` and `member`.
- **Pages.** Departments and Employees, each a table with a form, and "Out of office", where a
  member sets their own delegate and dates.
- **Texts** in `en` and `vi`.

## The approvals module

`approvals` holds requests that people submit and that are decided step by step, by the request's
type. It depends on `organization` `^1.0.0` and maps its slots `admin` and `member` to its own
slots of the same names.

### Request types

A request type, such as a payment request, an advance or a purchase, says who decides a request of
that type and in which order. An administrator of the business changes request types in the
running application, without Studio or a publish.

- **`RequestType`:** `name`, `code`, unique, `description`, `active`, and `steps`, a child
  collection of approval steps.
- **An approval step:** `sequence`, a positive integer unique in the type, `approver`, one of
  `lineManager`, `departmentHead` and `employee`, `employee` when the approver is `employee`, an
  optional `minAmount`, and an optional `label`.
  - `lineManager` is the `manager` of the requester's employee record.
  - `departmentHead` is the `head` of the requester's department.
  - A step with a `minAmount` applies only to a request whose `amount` is at least that much.
- **Contract.** A type has at least one step without a `minAmount`, so every request has a step to
  decide it.

### Requests

- **`Request`:** `number`, from a sequence, `type`, `title`, `description`, `amount`, `requester`, the
  creator, `requesterEmployee`, and `status`.
- **The requester's employee.** `requesterEmployee` is a reference with
  [`match`](resource-files.md#matched-references): it is the employee whose `user` is the
  requester, found on each write.
- **The step fields:** `stepsRequired`, the number of steps that apply, `step`, the number decided,
  `stepSequence`, the sequence of the step being decided, `approver`, the employee who decides it,
  `approverUser`, the account the task goes to, `onBehalfOf`, the approver's account when a delegate
  decides, `stepDecision`, the task that moved `step` last, and `decisionComment`.
- **`Decision`:** one record for each decision: `request`, `stepSequence`, `outcome`, one of
  `approved`, `returned` and `rejected`, `decidedBy`, `onBehalfOf`, `comment` and `decidedAt`.

### The request contract

The promise of the module is that **nobody decides their own request, and each step is decided
once**, whatever process change a business makes.

- **Lifecycle on `status`:**

  | Transition | From | To | Guard |
  | --- | --- | --- | --- |
  | `submit` | `draft`, `returned` | `submitted` | The requester submits, and at least one step applies |
  | `approve` | `submitted` | `approved` | Someone other than the requester, once `step` reaches `stepsRequired` |
  | `return` | `submitted` | `returned` | Someone other than the requester |
  | `reject` | `submitted` | `rejected` | Someone other than the requester |
  | `cancel` | `draft`, `submitted`, `returned` | `cancelled` | The requester |

  `draft` is initial and deletable. `approved` and `rejected` are protected, so a layer can add
  states before them but no other way into them.
- **Core validations:**
  - `oneDecisionPerStep`: `step` changes only by one, only with a new deciding task, stored in
    `stepDecision`, and only by someone other than the requester. A submit sets it back to 0.
  - `frozenAfterSubmit`: `type`, `amount` and `stepsRequired` change only while the request is
    `draft` or `returned`, or by the submit.
- **Ceilings:**
  - `member` reads the requests they submitted, the ones they are to decide, and the ones they
    decide for someone else. They create, update and delete only their own, and only the fields of
    the request form.
  - `admin` reads and exports every request.
  - Nobody writes a `Decision` through a grant: only the process writes them. A `member` reads the
    decisions on their own requests and the ones they made.
- **Contract scenarios** cover each point: a requester who approves their own request through any
  step is refused, one task decision that moves `step` twice is refused, and a request above a
  step's `minAmount` reaches `approved` only after that step.

### The approval process

`RequestApproval` runs on a request from its submit to its outcome. Its steps, in order:

1. **`submit`** sets `status` to `submitted`, `step` and `stepSequence` to 0, and
   `stepsRequired` to
   `count(type.steps, outer.amount == null or minAmount == null or outer.amount >= minAmount)`.
2. **`nextStep`**, a decision: while `step < stepsRequired` it goes to `advance`, otherwise to
   `markApproved`.
3. **`advance`** sets `stepSequence` to the smallest `sequence` above it that applies, with
   `min(type.steps, if(sequence > outer.stepSequence and (…), sequence, null))`.
4. **`findApprover`** sets `approver` from the step's `approver`, with
   `first(type.steps, sequence == outer.stepSequence, approver)`: the requester's manager, the
   department's head, or the step's `employee`.
5. **`findDelegate`** sets `onBehalfOf` to the approver's account and `approverUser` to the
   delegate's account while `organization:IsDelegating(…)` holds for the approver, else to the
   approver's account.
6. **`route`**, a decision: when `approverUser` is empty or is the requester, the step goes to
   `adminDecide`, a task for the `admin` slot. Otherwise it goes to `decide`, a task for
   `approverUser`. Both tasks show the `RequestReview` form, with outcomes `approve`, `return`
   and `reject`.
7. **On `approve`**, `recordApproval` sets `step` to `step + 1` and `stepDecision` to
   `decisionTask()`, `logApproval` creates the `Decision` with `decidedBy` set to
   `accountableUser()`, and the process goes back to `nextStep`.
8. **On `return` or `reject`**, the process logs the `Decision` and sets `status`. The contract
   checks that someone other than the requester decided.
9. **`markApproved`** sets `status` to `approved`. The contract checks the last decider and that
   every step that applies was decided.

A requester who is a department head, and whose step would go to themselves, gets an administrator
instead. When the only administrator is the requester too, the guard refuses, and a business of
that size relaxes `Request/transitions/approve` with its reason.

### Pages

- **My requests:** the member's own requests, with a "New request" form and the submit action.
- **To decide:** the [task inbox](task-inbox-widget.md) for the member's approval tasks.
- **Request:** one request with its decisions and its approval path.
- **All requests** and **Request types**, for the `admin` slot. A request type's form edits its steps
  as child rows.

## The approvals application

The built-in application `approvals` is what a business starts from.

- **Modules.** It declares `organization` `^1.0.0` and `approvals` `^1.0.0`.
- **Roles.** `admin` and `employee`, mapped to the `admin` and `member` slots of both modules.
- **Site.** One site with My requests, To decide, Out of office, and, for `admin`, All requests,
  Request types, Departments and Employees.
- **Sign-in.** Axis accounts with a second step, as
  [the identity provider](identity-provider.md) describes for an application's own accounts.
- **Texts** in `en` and `vi`.

## Samples

The samples become the built-in modules, as D102 agrees:

- **`departments`** becomes `organization`. The purchase request and leave request samples declare
  `organization` and map its slots, and `department-budgets` depends on it. The `departments`
  sample module is removed.
- **The `request-approval` template** becomes the `approvals` application, and is removed. The
  Studio journeys that create an application from it create one from the built-in application.

## The journey

Two businesses start from the `approvals` application, and each changes it differently:

- **A business that changes data.** Its administrator adds a director's step with a minimum amount to
  the "Purchase" request type in the running application. A purchase above the amount goes to the
  requester's manager, then to the director. The director is away, so the director's delegate
  decides it, and the decision records whom the delegate decided for.
- **A business that changes the module.** Its author adds a `costCenter` field to `Request` in
  Studio, which writes a layer, and its administrator adds a "Travel advance" request type. The
  author then adds a step that sets `approved` right after the submit. The contract scenario fails,
  so the publish is refused.
- **For both:** a requester who is also a department head never decides their own request, and a
  task decision never moves a request two steps.
