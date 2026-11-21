// What a step's run panel shows for a run, see docs/reference/process-inspector-widget.md#instance-page.
import { runParts, type InstanceCall, type InstanceDetail, type StepRun } from './inspection'

/** The call a run's Input, Output and Attempts read: the last call of the run, since an operation step makes one. */
export function callOf(instance: InstanceDetail, runs: readonly StepRun[], run: StepRun): InstanceCall | undefined {
  return runParts(instance, runs, run).calls.at(-1)
}

/** Whether a call has an answer: it succeeded or failed, so its last attempt settled it. */
export function settled(call: InstanceCall): boolean {
  return call.state === 'succeeded' || call.state === 'failed'
}

/** Whether a value is an object with at least one member. */
export function hasMembers(value: unknown): boolean {
  return value !== null && typeof value === 'object' && Object.keys(value).length > 0
}

/** Whether the run has an Input tab: it made a call, or it is a decision that read record values. */
export function runHasInput(instance: InstanceDetail, runs: readonly StepRun[], run: StepRun): boolean {
  return callOf(instance, runs, run) !== undefined || (run.entries.at(-1)?.read?.length ?? 0) > 0
}

/**
 * Whether the run has an Output tab: its call has settled or returned values, or it made a changed field, an event, a
 * notification or a record. A run that makes nothing has none.
 */
export function runHasOutput(instance: InstanceDetail, runs: readonly StepRun[], run: StepRun): boolean {
  const call = callOf(instance, runs, run)
  const made = run.entries.at(-1)?.made
  return (
    (call !== undefined && (settled(call) || hasMembers(call.returned))) ||
    (made?.changes?.length ?? 0) > 0 ||
    Boolean(made?.event) ||
    Boolean(made?.notified) ||
    Boolean(made?.created)
  )
}
