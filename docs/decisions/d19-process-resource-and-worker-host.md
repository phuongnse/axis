# D19. Process resource and worker host

Status: **Agreed**

Processes and the worker host arrive in M3. See
[the reference](../reference/processes.md).

- **Process resource:** a `process` names one entity, its subject. Each
  instance runs for one record of that entity, the subject record. `start`
  names the first step.
- **Step kinds in M3:** `decision`, `task` (a human task), `operation` and
  `end`. Each step has a name, and a transition names the next step. Wait for
  event, timer and sub-process come later.
  - [D69](d69-timer-wait-for-event-and-sub-process-steps-loops-and-create-record.md)
    replaces this line: M14 adds timer, wait for event and sub-process steps,
    and loops.
- **Decision:** a decision has ordered branches, each a boolean expression and
  a next step, plus an `otherwise` step. The first branch that is true wins.
- **Process expressions:** they see the subject record's fields, paths
  through reference fields (at most 3 hops, as in data source filters) and
  the named rules. The purchase request threshold is a rule, so it is
  configured, not hard-coded.
- **Operation:** M3 has one built-in operation, `updateRecord`. It sets fields
  of the subject record from expressions, through the record update command,
  inside the step's transaction. M5 adds `raiseEvent`, `callConnector` and
  package operations, see [D25](d25-connectors-deployment-bindings-and-secrets.md),
  [D26](d26-events-and-triggers.md) and [D28](d28-extension-packages.md).
- **Start:** a process can declare a `startCondition`, an expression with a
  message text key. A start that fails it is a `400`. The start pins the
  instance to the active release.
- **One start per submission:** at most one running instance exists per
  process and record, so a second start is a `409`. A repeat of a start
  request with the same `Idempotency-Key` returns the first result.
- **Worker host:** `Axis.Worker` runs the steps, not the server. Each work
  item carries its tenant id, and the worker sets the tenant context from it.
- **One transaction per step:** a step commits these together: business
  writes, the new instance state and revision, the step history, audit
  records and the next work item. A revision mismatch aborts the transaction.
- **Claims:** workers claim work with `FOR UPDATE SKIP LOCKED`, a lease and a
  claim token. The commit checks the token. When a lease expires, another
  worker can claim the item.
- **Failure:** a failing step rolls back. A following transaction records the
  error in the history and marks the instance `failed`. Connector and package
  operation steps have retry policies (D25, D28). Operator [retry](../reference/process-inspection.md#retry) is built.
- **History:** every step occurrence records its input, output, decision and
  error.

_Why:_ see [knowledge](../domain/knowledge.md#execution) and
[D11](d11-durable-process-engine.md). Steps run in a worker, never on the
request thread, and each step is one transaction, so a crash leaves no partial
work.
