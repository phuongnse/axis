# Built-in modules

The first built-in modules are `organization` and `approvals`, and the first built-in application,
`approvals`, uses them. A small business starts from the application and changes it to fit: its
administrator sets request types and approval steps in the running application, and an author
changes anything else in Studio through [customization layers](customization-layers.md). The
[module contracts](module-contracts.md) hold whatever either of them changes.
[D102](../decisions/d102-built-in-modules-open-to-change-and-bound-by-a-contract.md) agrees the
idea, and [D124](../decisions/d124-the-organization-and-approvals-modules.md) agrees what the modules
hold. `organization` 1.0.0 exists under `modules/organization`, with its entities, data sources, rule,
role slots, contracts, policies, contract scenarios, pages, one form and texts. `approvals` 1.0.0 exists under
`modules/approvals`, with its entities, data sources, `RequestNumber` sequence, role slots, contracts,
process, policies, contract scenarios, pages and texts. Its built-in
application is _(planned for M26)_. The `service-desk` and `assets` modules are _(planned for M30)_.

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
  under `artifacts/modules/`, next to the sample modules, and the built-in applications _(planned
  for M26)_ under `artifacts/templates/`. The development images hold the modules in `/modules`
  today. The production images `axis-server`, `axis-worker` and `axis-admin` do not yet, and the
  built-in applications are not installed yet. Shipping them in every installation _(planned for
  M26)_ needs its own design, because the Helm chart mounts a volume over `/modules`.
- **Creating an application from one.** Studio's new application dialog lists the built-in
  applications first, marked as built in, then the templates. Creating one copies the thin folder,
  as any template does, so the application declares the modules instead of copying them, and every
  change after that is a layer. The platform operator creates a tenant from one too, see
  [tenant lifecycle](tenant-lifecycle.md) _(planned for M31)_.
- **No platform code.** Neither module is named by any engine. They are configuration, compiled by
  the same compiler as any application.

## The organization module

`organization` holds the departments and the employees of the business, which other modules route
work by. It keeps no personal data beyond what that needs. Version 1.0.0 holds the entities, the
data sources, the rule, the contracts, the role slots, the policies, the contract scenarios, the pages,
the form and the texts below.

- **`Department`:** `code`, unique, in capital letters, `name`, `parent`, a reference to another
  department, `head`, a reference to an employee, and `active`.
- **`Employee`:**
  - `fullName`, `workEmail`, unique, `phone`, which is `sensitive`, `jobTitle`, `department` and
    `manager`, a reference to another employee;
  - `user`, unique, marked `userId`, the id of the person's account, empty for an employee who never signs in;
  - `active`;
  - `delegate`, a reference to another employee, with `delegateFrom` and `delegateUntil`, the
    dates of an absence.
- **No more personal data.** No date of birth, no identity number, no address. A business that
  needs one adds it through a layer, and marks it `sensitive`, as
  [D109](../decisions/d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md)
  asks.
- **Linking an account.** An administrator invites the person on the
  [users page](user-administration.md), then picks the account in the employee's `user` field. The
  field is marked `userId`, so the form shows a [user picker](form-widget.md) in place of a text
  box. The picker lists only people who have signed in at least once. An invited person appears
  after their first sign-in.
- **Data sources.** `Departments` lists the department fields. `Employees` lists the employee
  fields except `phone`, so a list carries no personal data. A form can still show the phone.
  `OutOfOffice` lists `fullName`, `delegate`, `delegateFrom` and `delegateUntil` of the employees
  whose `user` is the person who reads, so it holds at most their own record.
- **Rules.** `IsDelegating(from, until)` is true when today, in the application's time zone, is
  between the two dates, both days included, and false when either date is empty. Other modules
  call it as `organization:IsDelegating(…)`.
- **Contracts:**
  - `Department`: a department is not its own parent. The named point is
    `Department/validations/notOwnParent`.
  - `Employee`: an employee is not their own manager (`notOwnManager`) or their own delegate
    (`notOwnDelegate`). A delegate needs both dates (`delegateHasDates`), the second not before the
    first (`delegateDatesInOrder`). Dates without a delegate are allowed, and their order is checked
    whenever both are set. The points are `Employee/validations/<name>`.
  - Each check is a [core validation](module-contracts.md#core-validations) that reads `id`, the
    record's own id. It refuses only a direct reference to the record itself. A loop of several
    departments or managers is not refused, because an expression runs no query.
  - Ceilings: the `admin` slot gets every action on both. The `member` slot reads both, and updates
    only `phone`, `delegate`, `delegateFrom` and `delegateUntil` of the employee whose `user` is
    their own. The ceilings have no `others` entry, so a role mapped to neither slot gets no grant on
    these entities, and that includes the roles of other modules until an application maps them to
    a slot. The points are `Department/ceiling/admin`, `Department/ceiling/member`,
    `Employee/ceiling/admin` and `Employee/ceiling/member`.
- **Policies.**
  - `AdminAccess` grants the `admin` slot every action on both entities, and read and export on the
    `Departments` and `Employees` data sources.
  - `MemberAccess` grants the `member` slot read on departments, read on every employee field except
    `phone`, the update of its own absence, and read on the `OutOfOffice` data source. It grants no
    read on the `Departments` and `Employees` data sources, so a member gets `403` on their rows and
    on the pages that list them. It reads no phone, its own included, because the fields a role reads
    belong to the entity and not to a record. A member still updates their own `phone`, `delegate`,
    `delegateFrom` and `delegateUntil`.
  - Only `MemberAccess` reads `OutOfOffice`, so an administrator who does not hold the `member` slot
    has no Out of office page.
  - An application that only maps the slots needs no policy of its own, and a grant of its own that
    goes past a ceiling fails the compile with `AXC0334`.
- **Contract scenarios.** Five scenarios run when an application that declares the module is
  published or validated: `DepartmentIsNotItsOwnParent`, `EmployeeIsNotTheirOwnManagerOrDelegate`,
  `DelegateNeedsDatesInOrder`, `AdminManagesOrganization` and `MemberUpdatesOnlyTheirOwnAbsence`. Each
  covers the points its name says.
- **Role slots.** `admin` and `member`.
- **Pages.** Each is a [module page](modules-and-templates.md#modules), and a site lists it as
  `organization/<name>`.
  - `Departments` and `Employees` are for the `admin` slot. Each is a table with a New button and an
    Open action that leads to the form page `DepartmentForm` or `EmployeeForm`, a form widget on the
    entity. A page is readable only to a person who reads its data source, so a member is refused both.
  - `OutOfOffice` is for the `member` slot. Its table lists the `OutOfOffice` data source, so a member
    sees only their own record, and has no New button. Open leads to `AbsenceForm`, a form widget on
    the `Absence` form, which holds only `delegate`, `delegateFrom` and `delegateUntil`.
- **Texts** in `en` and `vi`.

## The approvals module

`approvals` holds requests that people submit and that are decided step by step, by the request's
type. It depends on `organization` `^1.0.0` and maps its slots `admin` and `member` to its own
slots of the same names.

Version 1.0.0 holds the entities below, the data sources `RequestTypes`, `Requests`, `Decisions`, `MyRequests` and
`RequestDecisions`,
the sequence `RequestNumber` with the format `RQ-{yyyy}-{n:5}`, `amount` and `minAmount` as `decimal`
with precision 18 and scale 2, the contracts of `Request`, `RequestType` and `Decision`, the process
`RequestApproval`, the form `RequestReview`, the policies `MemberAccess` and `AdminAccess`, four module
scenarios, `RequestGoesToManagerThenNamedEmployee`, `DepartmentHeadReturnsAndRejects`,
`DelegateDecidesWhileApproverIsAway` and `DepartmentHeadRequestGoesToAdmin`, the contract scenarios
listed under [the request contract](#the-request-contract), the pages, the forms `RequestForm` and
`RequestDetail`, and the texts.

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
  decide it. The core validation `hasStepForEveryAmount` refuses a type without one, with a `400` at
  `steps`.

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
  - `oneDecisionPerStep`, the named point `Request/validations/oneDecisionPerStep`: `step` changes only by one, only with a new deciding task, stored in
    `stepDecision`, and only by someone other than the requester. A submit sets it back to 0.
  - `frozenAfterSubmit`, the named point `Request/validations/frozenAfterSubmit`: `type`, `amount`
    and `stepsRequired` change only while the request is `draft` or `returned`, or by the submit. It
    reports at `amount`, whichever of the three changed.
  - `RequestType/validations/hasStepForEveryAmount`, described under [request types](#request-types).
- **Ceilings**, the named points `Request/ceiling/admin`, `Request/ceiling/member`,
  `Decision/ceiling/admin` and `Decision/ceiling/member`:
  - `member` reads the requests they submitted, the ones they are to decide, and the ones they
    decide for someone else. They create, update and delete only their own, and only the fields of
    the request form and `status`. A member may write `status` on their own request so that they
    can cancel it. The lifecycle still refuses a self-decision, with a `409`.
  - `admin` reads and exports every request.
  - Nobody writes a `Decision` through a grant: only the process writes them. A `member` reads the
    decisions on their own requests and the ones they made, and `admin` reads and exports every
    decision.
- **Contract scenarios** cover each point: a requester who approves their own request through any
  step is refused, one task decision that moves `step` twice is refused, and a request above a
  step's `minAmount` reaches `approved` only after that step. Seven scenarios run when an application
  that declares the module is published or validated: `RequesterCannotDecideOwnRequest`,
  `OneDecisionMovesOneStep`, `RequestGoesToManagerThenNamedEmployee`,
  `RequestMovesOnlyThroughItsLifecycle`, `AmountIsFrozenAfterSubmit`,
  `AccessStaysWithinTheCeilings` and `RequestTypeNeedsAStepForEveryAmount`. Each covers the points
  its name says. `OneDecisionMovesOneStep` shows that one decision moves a request one step. No
  scenario user may write `step`, so no scenario can provoke the refusal itself.

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
   delegate's account while `organization:IsDelegating(…)` holds for the approver and the approver has
   a delegate. Otherwise it sets `approverUser` to the approver's account and clears `onBehalfOf`.
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

### Policies

`MemberAccess` and `AdminAccess` give the slots what the contracts allow, so an application that only
maps the slots works.

- **`MemberAccess`** gives the `member` slot the ceiling of `Request`: read the requests they submitted, the
  ones they are to decide and the ones they decide for someone else, and create, update and delete their
  own, with the fields `type`, `title`, `description`, `amount` and `status`. It also reads request types
  and the decisions on their own requests or made by them, and starts `RequestApproval`. A request that
  is not theirs is outside their read filter, so starting the process on it is refused with a `400`
  at `subjectId`.
  It also reads the data sources `MyRequests` and `RequestDecisions`. `MyRequests` keeps only the
  requests whose requester is the reader, so a member's My requests page never lists another
  member's request.
- **`AdminAccess`** gives the `admin` slot every action on request types, read and export on requests and
  decisions, and inspect on `RequestApproval`. It also reads and exports the data sources `Requests`,
  `RequestTypes` and `Decisions`, and reads `RequestDecisions`. A member has no grant on `Requests`,
  `RequestTypes` or `Decisions`, so those pages and data sources answer a member with `403`.

### Pages

Each is a [module page](modules-and-templates.md#modules), and a site lists it as `approvals/<name>`. A page
is readable only to a person who reads its data source.

- **`MyRequests`** is for the `member` slot. Its table lists the `MyRequests` data source, so a member
  sees only the requests they submitted. The "New request" button and the Open action lead to
  `RequestForm`, a form widget on the form `RequestForm` with the Submit action, which starts
  `RequestApproval`. View leads to `RequestDetail`.
- **`ToDecide`** is for whoever decides requests. It is the [task inbox](task-inbox-widget.md) of the signed-in
  person's open tasks. An approver opens a task there and approves, returns or rejects the request.
- **`RequestDetail`** shows one request read-only, from the page parameter `request`, with the form
  `RequestDetail`. Its "Approval path" section shows `stepsRequired`, `step`, `stepSequence`, `approver`,
  `approverUser` and `onBehalfOf`, which say where the request stands, and not the type's steps. A table
  of the `RequestDecisions` data source lists the request's decisions. A person who may read the request sees it.
- **`AllRequests`** is for the `admin` slot. Its table lists the `Requests` data source, with a View
  action that leads to `RequestDetail`.
- **`RequestTypes`** is for the `admin` slot. Its table lists the `RequestTypes` data source, with a New
  button and an Open action that lead to `RequestTypeForm`, a form widget on the entity. The form edits
  the steps as child rows.

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

- **`departments`** becomes `organization`. The leave request sample declares `organization`, maps
  both of its slots (`admin` to `department-manager`, `member` to `employee`) and seeds its employees
  and departments, and a leave request goes to the head of its department. The purchase request
  sample and `department-budgets` still use `departments` until they move, and then the `departments`
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
