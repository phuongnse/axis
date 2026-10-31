# D41. Leave requests as the second sample application

Status: **Agreed**

Leave requests is the second, smaller sample application. It is built, and
every point below holds. See the
[Second application section](../product-brief.md#second-application-leave-requests)
of the product brief for the data, flow, security, pages and scenarios. The
owner confirmed these points on issue #572 on 2026-10-18.

- **Application:** leave requests, in `samples/apps/leave-requests`. An
  employee asks for days off, and the department manager decides. It needs no
  platform code change.
- **Data:**
  - A leave request has a number from the sequence `LR-{yyyy}-{n:5}`, a
    title, an employee, a department, a leave type (`annual`, `sick` or `unpaid`),
    a first day, a last day, days, a reason, a status and a decision comment.
    The title is required and is the display field, because a sequence number
    cannot be required and the reason is `sensitive` ([D109](d109-sensitive-fields-and-backups-are-encrypted-and-history-keeps-no-names.md)),
    so neither can label a request in task lists.
  - Departments come from the `departments` module of
    [D40](d40-reusable-modules.md), with its seed departments.
  - Days are computed as `daysBetween(firstDay, lastDay) + 1`. They count
    calendar days, including both ends. Weekends and holidays are not left out.
- **Flow:**
  - An employee creates a draft and submits it. Submitting checks that the
    last day is not before the first day. It starts the approval process only
    once.
  - The department manager gets a task and approves, returns or rejects.
  - A returned request can be edited and submitted again.
  - There is no finance step, connector or extension package.
- **Security:**
  - The roles are `employee` and `department-manager`. They are the Keycloak
    realm roles that the purchase request sample already uses, so the realm
    needs no change.
  - The sample reuses the development users that hold these roles.
  - An employee sees only their own requests. A manager sees the requests of
    their departments. Everything else is denied by default.
- **Pages:** My leave requests, the request form and My tasks, in a site at
  path `leave`.
- **Tests:**
  - Test scenarios cover approve, return then resubmit, reject and denied
    access.
  - One Playwright journey: an employee submits, a department manager approves,
    and the employee sees the request approved.
  - The server activates the sample next to purchase requests under
    `scripts/dev.sh`, Compose and E2E.

_Why:_ The sample is small. It uses tasks and policies as purchase requests
do, and it uses the departments module of [D40](d40-reusable-modules.md).
It needs no package or connector. So it shows reuse without platform code. Any
platform change it needs is a gap to raise, not to hide in the sample.
