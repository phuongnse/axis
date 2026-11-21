import { useMemo } from 'react'
import { fillText } from './fillText'
import { formatValue } from './formatValue'
import {
  problemText,
  processTitle,
  runParts,
  runStateText,
  stepProblems,
  takenTransition,
  taskStateText,
  type CallAttempt,
  type InstanceCall,
  type InstanceDetail,
  type InstanceTask,
  type StepRun,
  type SubProcess,
} from './inspection'
import { useText } from './texts'

/** What {@link useRunWords} gives: one sentence of fixed texts for each kind of row of History. */
export interface RunWords {
  /** What a run did. */
  run: (run: StepRun) => string
  /** What a call came to. */
  call: (call: InstanceCall) => string
  /** What became of a task. */
  task: (task: InstanceTask) => string
  /** The sub-process a run started. */
  subProcess: (child: SubProcess) => string
  /** What an attempt or a lookup of a call came to. */
  attempt: (attempt: CallAttempt) => string
  /** The name of an attempt or a lookup, such as "Attempt 3". */
  attemptName: (attempt: CallAttempt) => string
}

/** The attempts a call made, not counting its lookups, which is the number of its last attempt. */
function attemptsMade(call: InstanceCall): number {
  return call.attempts.reduce(
    (most, attempt) => (attempt.kind === 'attempt' ? Math.max(most, attempt.number) : most),
    0,
  )
}

/**
 * What a run did, in one sentence of fixed texts, see docs/reference/shared-widgets.md#which-widget. The sentence never
 * shows a stored error message: a failed step is worded by its `problem` code alone. A run reads its last call, else a
 * failure, its last task, its last sub-process, what it made, then what its step is, and last where it went on to. The
 * sentences are the texts under `shell.inspector.happened`, filled through `fillText`.
 */
export function useRunWords(
  instance: InstanceDetail,
  runs: readonly StepRun[],
  stepLabel: (name: string) => string,
  locale: string,
): RunWords {
  const t = useText()
  return useMemo(() => {
    const say = (key: string, values: Record<string, string | number> = {}) =>
      fillText(t(`shell.inspector.happened.${key}`), values)
    const time = (value: string) => formatValue({ name: 'time', type: 'date-time' }, value, {}, { locale, text: t })
    const subProcess = (child: SubProcess) =>
      say('subProcess', { process: processTitle(t, child.processTitleKey, child.process) })

    const call = (item: InstanceCall): string => {
      const values = {
        operation: item.operation,
        target: item.connector ?? item.package ?? '',
        number: attemptsMade(item),
      }
      const made = values.number > 0
      switch (item.state) {
        case 'succeeded':
          return say(made ? 'callSucceededOn' : 'callSucceeded', values)
        case 'failed':
          return say(made ? 'callFailedOn' : 'callFailed', values)
        case 'unknown':
          return say('callUnknown', values)
        case 'cancelled':
          return say('callCancelled', values)
        default:
          return say('callInProgress', values)
      }
    }

    const task = (item: InstanceTask): string => {
      switch (item.state) {
        case 'open':
          return say('taskOpen', { assignee: item.assignee })
        case 'completed':
          return item.completedBy !== null && item.outcome !== null
            ? say('taskCompleted', { person: item.completedBy, outcome: item.outcome })
            : taskStateText(t, item.state)
        case 'cancelled':
          return say('taskCancelled')
        case 'expired':
          return say('taskExpired')
        default:
          return taskStateText(t, item.state)
      }
    }

    const run = (item: StepRun): string => {
      const { calls, tasks, subProcesses } = runParts(instance, runs, item)
      const lastCall = calls.at(-1)
      if (lastCall) {
        return call(lastCall)
      }
      if (item.state === 'failed') {
        // Only a code this build knows is worded, so a stored error message never shows.
        const entry = item.entries.findLast(
          (row) => row.problem !== null && (stepProblems as readonly string[]).includes(row.problem.code),
        )
        const problem = entry && problemText(t, locale, entry.problem, null)
        return problem ? say('failedBecause', { problem }) : say('failed')
      }
      const lastTask = tasks.at(-1)
      if (lastTask) {
        return task(lastTask)
      }
      const lastChild = subProcesses.at(-1)
      if (lastChild) {
        return subProcess(lastChild)
      }
      const made = item.entries.at(-1)?.made
      if (made?.event) {
        return say('event', { event: made.event.name })
      }
      if (made?.notified) {
        return say('notified')
      }
      if (made?.created) {
        return say('created')
      }
      if (made?.changes && made.changes.length > 0) {
        return say('changed')
      }

      const step = instance.graph.steps.find((candidate) => candidate.name === item.step)
      const after = runs[runs.indexOf(item) + 1]
      const transition = after === undefined ? undefined : takenTransition(instance, item, after.step)
      const waiting = item.finishedAt === null
      switch (step?.type) {
        case 'decision':
          if (transition) {
            // The catalog answers a key it does not hold with the key itself, so a branch without a label has no text.
            const labelled = transition.labelKey === null ? undefined : t(transition.labelKey)
            const branch =
              labelled !== undefined && labelled !== transition.labelKey
                ? labelled
                : (transition.branch ?? t('shell.inspector.graph.otherwise'))
            return say('branch', { branch })
          }
          break
        case 'timer':
          if (waiting && instance.timerDueAt !== null) {
            return say('timerWaits', { time: time(instance.timerDueAt) })
          }
          if (!waiting) {
            return say('timerDone')
          }
          break
        case 'waitEvent': {
          const event = step.event ?? ''
          if (item.state === 'waitingForEvent') {
            return say('eventWaits', { event })
          }
          if (!waiting && transition) {
            return say(transition.on === 'timeout' ? 'eventTimedOut' : 'eventArrived', { event })
          }
          break
        }
        case 'end':
          return say('ended')
      }
      return after === undefined ? runStateText(t, item.state) : say('wentTo', { step: stepLabel(after.step) })
    }

    const attempt = (item: CallAttempt): string => {
      const problem = problemText(t, locale, item.problem, null)
      if (problem) {
        return problem
      }
      return item.httpStatus === null ? say('noAnswer') : say('answered', { status: item.httpStatus })
    }
    const attemptName = (item: CallAttempt) =>
      say(item.kind === 'lookup' ? 'lookupStep' : 'attemptStep', { number: item.number })

    return { run, call, task, subProcess, attempt, attemptName }
  }, [instance, runs, stepLabel, locale, t])
}
