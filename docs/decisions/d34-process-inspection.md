# D34. Process inspection

Status: **Agreed**

Authorized users inspect process instances: where an instance is, what it did,
with which inputs, and why it failed or waits. The design is built. See [processes](../reference/process-inspection.md#process-inspection) for the
endpoints, the order of checks and the rules that hide values.
The owner confirmed these points on issue #477 on 2026-10-17, with two
changes. The changes are the masking bullet, which covers every entity, and
the step error bullet.

- **Who may inspect:**
  - A `process` grant gets a new action, `inspect`, next to `start`.
  - Without an `inspect` grant on the process, every inspection request is
    `403`.
  - An instance whose subject record the user may not read does not exist for
    the user, so it is `404`. This is the same rule as a start.
- **Instance list:**
  - `GET /api/apps/{app}/processes/{process}/instances` lists the process's
    instances, newest first and paged.
  - The filters are `state` and `subject`, a record id.
  - Each row has the id, the subject id, the state, the current step, the
    release, and the created and ended times.
- **Instance detail:**
  - `GET /api/apps/{app}/processes/{process}/instances/{id}` gives the graph
    of the release the instance is pinned to, with its steps and their
    transitions.
  - It gives the path: the history rows in order, each with the step, the
    times, the decision, the outcome and the error.
  - It gives the tasks, with the step, the assignee, the state, the outcome
    and who completed it.
  - It gives the connector and package calls, with their state and attempts.
- **Attempts:**
  - A new table, `axis.process_call_attempts`, holds one row per attempt and
    per lookup of a connector or package call.
  - A row has the call, its number, the start and end times, the outcome
    class, the HTTP status and a fixed error text.
  - It never holds a request or response body.
- **Call input kept:** a connector call keeps its input after it settles, so
  inspection shows what was sent.
- **Values and masking:**
  - Inputs and outputs show only values of fields the user may read
    ([D24](d24-roles-and-policies.md) field lists).
  - A call's input or output is shown only when the user may read every field
    that the call's mapping reads or writes. This covers any entity, not only
    the subject entity.
  - The fields counted are every field the mapping reads or writes, on any
    entity. That includes fields reached through reference paths, where every
    field along the path counts, and fields reached through named rules,
    transitively. A child field counts through the owner's child-collection
    grant.
  - Otherwise the whole value is left out and marked hidden. This fails
    closed: a partly readable input is never shown.
- **Sensitive flag:**
  - A field can declare `"sensitive": true`.
  - Its value never appears in inspection, for any user.
  - A call input that reads it is hidden.
  - The record API still shows it to users whose grants cover it.
- **Step errors:**
  - History stores a step error as the exception type, such as
    `System.DivideByZeroException`.
  - Or it stores a fixed text that names the step, the property, the field
    pointer or the message key.
  - It never stores the exception message or a value.
- **Widget:**
  - A new `processInspector` widget names one process.
  - It lists the instances and opens one, showing the graph with the path
    drawn on it.
  - A page that holds it is authorized like any page. The server checks every
    request.
- **Record history:** the record history links a process audit record to its
  instance when the user may inspect that process.

_Why:_ [Product goal 4](../product-brief.md#product-goals) asks that people see
what a process did and why it failed. The engine stores that, but no API shows
it. [D24](d24-roles-and-policies.md) grants decide what a user may read,
so inspection follows them and fails closed.
[D29](d29-release-pinning-and-text-resources.md) pins an instance to its
release, so the graph comes from that release while grants come from the
active one. [Operations](../domain/knowledge.md#operations) asks that sensitive
fields are masked in inspection by a field flag. The `sensitive` flag keeps
that rule.
