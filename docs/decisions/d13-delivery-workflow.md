# D13. Delivery workflow

Status: **Agreed**

- **Tooling:** NexKit runs one issue per agent session.
- **Milestones:** milestones are vertical slices.
- **Issue planning:** issues are created only for the current milestone
  (rolling wave).
- **Issue size:** each issue is one behaviour, aiming for 2–5 acceptance
  criteria that a test or command can check. More than five is a signal to
  check whether the issue holds more than one behaviour. If it does, split it.
  If every criterion checks the same behaviour, more are fine. A plan is not
  revised only to cut the number of criteria.
- **Documentation:** these docs hold the product and architecture knowledge.
  Issues link to them.
- **Bootstrap:** the solution skeleton, CI and the tool setup are done
  directly, not through NexKit, because NexKit needs working checks first.

_Why:_ a large up-front breakdown into layered issues delays anything
runnable until the very end, and its issues are too big for one session.

See [delivery.md](../delivery.md) for the step-by-step workflow and who does each
step.
