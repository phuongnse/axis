# Process blocks

_(planned for M38)_

A block is a step that holds steps of its own. A `parallel` step runs its branches at once, and a
`repeat` step runs its body again and again. This page holds the rules of both blocks, how their steps
must be shaped, and their limits. The other steps are in [processes](processes.md#steps), and the choice
behind blocks is in
[D155](../decisions/d155-processes-run-paths-at-once-repeat-steps-give-tasks-to-many-people-and-take-error-paths.md).

## Parallel step

_(planned for M38)_

A `parallel` step runs paths at once, and goes on to its `next` when its rule of completion holds. Legal
and finance can read a contract side by side, and the process moves on when both reviews are in.

```json
{
  "name": "reviews",
  "type": "parallel",
  "branches": [
    { "name": "legal", "start": "legalReview", "steps": [ … ] },
    { "name": "finance", "when": "totalAmount >= FinanceThreshold()", "start": "financeReview", "steps": [ … ] }
  ],
  "completion": { "rule": "all" },
  "next": "decide"
}
```

- **Branches.** `branches` is a list of at least one branch. Each branch has a `name`, a `start` step and
  a `steps` list of its own, and it may have a `when`. A branch name is unique in its block, ignoring
  letter case.
- **Choosing branches.** A branch's `when` is evaluated once, when the block starts. It has the scope of a
  decision's `when`, and a `null` counts as false. Only the branches whose `when` is true run, and only
  those count toward the rule. When no branch runs, the block goes on to `next` at once.
- **Completion.** `completion` has a `rule`, and the rule is `all` when `completion` is left out. The
  rules are:
  - `all`: every branch that runs has ended.
  - `any`: one branch that runs has ended.
  - `count`: `count` branches have ended. `count` is a positive whole number. When fewer branches run than
    `count`, the block waits for all of them.
  - `condition`: a boolean `condition` is true. It is checked each time a branch ends, with the scope of a
    decision's `when`.
- **Going on.** When its rule holds, the block goes on to `next`. When every branch has ended, the block
  goes on whatever its rule says, so no path waits for a join that never comes.
- **Cancelling.** When the rule holds, the branches still running are cancelled, and their open tasks are
  closed. The cancel is kept in the instance's history.

## Repeat step

_(planned for M38)_

A `repeat` step runs its body for each item of a list, a number of times, while a condition holds, or
until one does. Each pass of the body runs on its own path. A repeat needs no waiting step inside its
loop, because its own limits end it.

```json
{
  "name": "checkLines",
  "type": "repeat",
  "forEach": "lineItems",
  "as": "line",
  "atOnce": 5,
  "start": "checkLine",
  "steps": [ … ],
  "gather": { "into": "checkedLines", "value": "line.item.description" },
  "next": "summarize"
}
```

- **Kinds.** A repeat has exactly one of these:
  - `forEach` names the items. It is a child collection of the subject record, a `references` field, or
    `{ "dataSource": "<name>" }` for a loaded data source. The items are read once, when the repeat starts.
  - `times` is an integer expression, read once when the repeat starts. A number below 1 runs no pass.
  - `while` is a boolean expression, checked before each pass. When it is false at the start, no pass runs.
  - `until` is a boolean expression, checked after each pass. The body therefore runs at least once.
- **Order.** Passes run in order, one after another, unless `atOnce` is set. With `atOnce`, up to that many
  passes run at the same time. `atOnce` is a positive whole number, and it is allowed with `forEach` and
  `times` only.
- **Passes.** `as` names a pass, and defaults to `pass`. The body reads the item of its pass as
  `<as>.item`, which `forEach` gives, and the number of its pass as `<as>.number`, which counts from 1.
  Naming the pass keeps a field or a nested repeat from clashing with it.
- **Skipping.** An `end` step in the body may have `"skip": true`. It ends its pass, and leaves that pass's
  item out of `gather`.
- **Gathering.** `gather` is optional. Its `into` names a list variable of the process, and its `value` is
  an expression. When a pass ends without a skip, `value` is evaluated and added to the list. Values are
  added in the order of the passes, so the list follows the items. See [variables](processes.md#variables).
- **Stopping early.** `stopWhen` is a boolean expression, checked after each pass. When it is true, no new
  pass starts, the passes still running are cancelled, and the repeat goes on to `next`.
- **Waiting between passes.** `waitBetween` is a fixed-length duration, by the rule of a task's `dueIn`.
  After a pass, the instance waits that long before the next one starts, as it waits at a timer. See
  [timer step](process-flow.md#timer-step).
- **Limits of while and until.** `maxPasses` is required on `while` and `until`, and it is a positive whole
  number. `maxTime` is an optional duration on the same two kinds. Both are refused on `forEach` and
  `times`, because those kinds end when their items or passes run out.
- **Reaching a limit.** When the repeat has run `maxPasses` passes and must run another, it fails with the
  error kind `limit`. It fails the same way when another pass is due and the time since the repeat started
  is past `maxTime`. Going on silently would hide a condition that never changed. Failing lets the path take
  an [error path](processes.md#error-paths).

## Structure

_(planned for M38)_

Blocks keep the paths of a process easy to follow. A path that starts in a block ends in it, so the
compiler can check that every path ends.

- **A path stays in its block.** Every transition inside a branch or a body names a step of the same list,
  and so does each `onError` entry in it. A path leaves a block only by reaching an `end` step of its list.
- **Nothing outside leads in.** No step outside a block leads into it. A path enters a branch or a body at
  its `start`.
- **Blocks nest.** A branch or a body may hold a block of its own, up to a fixed depth. A deeper block does
  not compile, see [compile checks](#compile-checks).
- **An end ends its path.** An `end` inside a branch or a body ends that path, not the instance. An `end`
  in a repeat body may skip its pass, see [repeat step](#repeat-step).
- **Names are unique.** Step names are unique across the whole process, blocks and their steps included.
- **Each list is checked alone.** The checks of [processes](processes.md#compile-checks) apply within each
  list of steps, at paths such as `/steps/{i}/branches/{j}/steps/{k}`. Every step of a list has a path to
  an `end` step of that list.
- **A repeat is not a loop.** The body of a repeat is not a cycle for the loop check, because the repeat
  ends by its own limits. See [loops](process-flow.md#loops).
- **A failed path fails its block.** When one path of a block fails, the block cancels its other paths.
  Then it takes its own `onError`, see [error paths](processes.md#error-paths). Without one, the instance
  fails.
- **Processes without a subject.** Both blocks are allowed in a process with a subject and in one without,
  see [processes without a subject](process-flow.md#processes-without-a-subject).

## Limits

_(planned for M38)_

Limits keep one instance bounded, so that one process cannot hold the worker for ever.

- **Run-time limits.** An instance runs a bounded number of paths at once, and a repeat has bounded items
  and bounded passes. Going over one of these limits fails the step with a fixed error that names the
  limit. The values are in [worker settings](process-execution.md#worker-settings).
- **Depth.** The number of nested blocks is bounded. A process that nests deeper does not compile, see
  `AXC0379` below.
- **Repeat limits.** A `while` or `until` repeat has its own limits, `maxPasses` and `maxTime`, which the
  author sets in the process. See [repeat step](#repeat-step).

## Compile checks

_(planned for M38)_

The compiler reports each problem of a block as a diagnostic, at the path of the element, in the same pass
as the rest of the process. Each code is in the [diagnostic code table](diagnostic-codes.md).

- `AXC0378` at a transition: a transition crosses a block boundary. A step inside a block names a step
  outside its list, or a step outside a block names a step inside it.
- `AXC0379` at a block: blocks nest deeper than the limit.
- `AXC0380` at `/steps/{i}/branches` or a branch: a `parallel` step has no branches, a branch has no `name`,
  `start` or `steps`, or a branch repeats the `name` of an earlier one.
- `AXC0381` at `/steps/{i}/completion`: the rule is unknown, `count` is not a positive whole number, a
  `condition` is missing with the rule `condition` or set with another rule, or a task's `otherwise` is
  missing or names no outcome. See [several assignees](processes.md#steps) for a task.
- `AXC0382` at `/steps/{i}`: a repeat does not have exactly one of `forEach`, `times`, `while` and `until`.
- `AXC0383` at `/steps/{i}/forEach`: `forEach` is not a child collection, a `references` field or a loaded
  data source.
- `AXC0384` at the property, such as `/steps/{i}/maxPasses`: a `while` or `until` has no positive
  `maxPasses`, `maxPasses` or `maxTime` is set on `forEach` or `times`, or `atOnce` is set on `while` or
  `until`, or is not a positive whole number.
- `AXC0385` at `/steps/{i}/as`: `as` names a field, a parameter, a variable, a rule or the `as` of an outer
  repeat.
- `AXC0386` at `/steps/{i}/skip`: an `end` step has `skip` outside a repeat body.
- `AXC0084` at `/steps/{i}/waitBetween`: `waitBetween` is not a positive fixed-length duration.
- `AXC0048` at the expression: `when`, `condition`, `while`, `until`, `times`, `stopWhen` or `value` does not
  give the type its place needs.

Variables, error paths and a task's several assignees have their own checks in
[processes](processes.md#steps): see [variables](processes.md#variables) and
[error paths](processes.md#error-paths).
