# D20. Human tasks, task inbox and forms

Status: **Agreed**

Human work arrives in M3. See [processes](../reference/processes.md#steps) and
[the frontend](../reference/frontend.md). The `form` resource, the `form`
widget that names one, the task inbox widget, the task page and the start
process action are built. So are task steps, task completion with its
one-decision and who-may-act checks, and saving the task form with the
decision. Only the items under **Later** are not built.

- **Task step:** a `task` step has a `label`, an assignee, a `form`, an
  optional `dueIn` and its outcomes.
  - `label` is a required text key. The task inbox and the task page show
    it, because every UI string comes from text resources.
  - The assignee is either `user`, an expression that gives a user id, such
    as `department.manager`, or `role`, a role name. Queues come later.
  - `dueIn` is an ISO 8601 duration. It is stored as the task's due date and
    shown. Escalation needs timers, so it comes later.
  - Each outcome has a name, a label text key and a next step.
- **Exactly one decision:** an open task can be completed once. A later or
  concurrent completion is a `409`. An outcome the step does not allow is a
  `400`.
- **Who may act:** the current user may act when they are the assignee, or
  hold the assignee role. Anyone else gets a `403`, and a request without a
  user gets a `401`. This is checked at the moment of action. Policies in M4
  add to this check and never replace it.
- **Task form:** the task page shows the step's form over the subject
  record. The fields the form makes editable are saved with the decision, in
  the same transaction.
- **Task inbox:** a `taskInbox` widget lists the current user's open tasks in
  the application and opens the task page.
- **Start process action:** a `form` widget can declare a `startProcess`
  action, shown as a button. It saves the record first, then starts the
  process. Navigate actions stay **Proposed** in
  [D15](d15-presentation-model.md).
- **Form resource:** a `form` resource has one entity and sections of fields,
  and each field can be read-only. A `form` widget names a `form`, or an
  `entity` as shorthand for all its fields. The same `form` serves a page and
  a task. This settles the forms point of D15.
- **Later:** these come later, with no milestone yet:
  - conditional visibility, required and read-only states;
  - validation as the user types;
  - wizard steps;
  - file upload.

  This replaces the sentence of D17 that put validation as the user types in
  M3.

_Why:_ see [knowledge](../domain/knowledge.md#human-work) and
[user interface](../domain/knowledge.md#user-interface). A task is a first-class
step, so exactly one decision is recorded and the instance resumes from it.
One form resource serves a page and a task, so a layout is written once.
Moving the richer form features out keeps M3 on processes.
