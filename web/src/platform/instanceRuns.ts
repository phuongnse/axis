import type { InstanceCall, InstanceDetail, InstanceTask, PathEntry, RunOutput, SubProcess } from './inspection'
import type { StepProblem } from './stepProblems'

/**
 * Proposed, see the review on #3650: one run of a step, as the instance's timeline shows it. A step run again, such as
 * after a request came back for changes, is a run of its own with its `number`. A run holds the tasks, calls and
 * sub-processes it started. The run the instance is at has no `finishedAt`.
 */
export interface StepRun {
  key: string
  step: string
  /** Proposed, see D155 and the review on #3650: the path that ran it, when the instance had several. */
  path?: string
  /** Proposed, see the review on #3650: what the run made or changed. */
  made?: RunOutput
  /** Proposed, see the review on #3650: the values a decision's conditions read. */
  read?: PathEntry['read']
  number: number
  startedAt: string
  finishedAt: string | null
  decision: string | null
  error: string | null
  problem: StepProblem | null
  traceId: string | null
  state: 'done' | 'failed' | 'waiting' | 'running'
  tasks: InstanceTask[]
  calls: InstanceCall[]
  subProcesses: SubProcess[]
}

const within = (at: string, start: string, end: string | null) => at >= start && (end === null || at <= end)

/**
 * The runs of an instance's steps, oldest first: each entry of its path, then, while it has not ended, the step it is
 * at. A task, a call or a sub-process belongs to the run of its step that it started in.
 */
export function stepRuns(instance: InstanceDetail): StepRun[] {
  const counts = new Map<string, number>()
  const runs: StepRun[] = instance.path.map((entry, index) => {
    const number = (counts.get(entry.step) ?? 0) + 1
    counts.set(entry.step, number)
    return {
      key: `${index}`,
      step: entry.step,
      path: entry.path,
      made: entry.made,
      read: entry.read,
      number,
      startedAt: entry.startedAt,
      finishedAt: entry.finishedAt,
      decision: entry.decision,
      error: entry.error,
      problem: entry.problem ?? null,
      traceId: entry.traceId,
      state: entry.error === null ? 'done' : 'failed',
      tasks: [],
      calls: [],
      subProcesses: [],
    }
  })
  if (instance.endedAt === null) {
    // Proposed, see D155 and the review on #3650: an instance at several steps has a run at each, which started when
    // its path reached it: after the last step of that path, or else when the instance left its last shared step.
    const shared = instance.path.filter((entry) => entry.path === undefined).at(-1)?.finishedAt
    const current = instance.paths ?? [
      { id: '', step: instance.step, state: instance.state === 'failed' ? 'failed' : instance.state },
    ]
    for (const path of current) {
      const number = (counts.get(path.step) ?? 0) + 1
      counts.set(path.step, number)
      const before =
        path.id === ''
          ? instance.path.at(-1)?.finishedAt
          : (instance.path.filter((entry) => entry.path === path.id).at(-1)?.finishedAt ?? shared)
      runs.push({
        key: path.id === '' ? 'current' : `current-${path.id}`,
        step: path.step,
        path: path.id === '' ? undefined : path.id,
        number,
        startedAt: before ?? instance.createdAt,
        finishedAt: null,
        decision: null,
        error: null,
        problem: null,
        traceId: null,
        state: path.state === 'failed' ? 'failed' : path.state === 'running' ? 'running' : 'waiting',
        tasks: [],
        calls: [],
        subProcesses: [],
      })
    }
  }
  const runOf = (step: string, at: string) =>
    runs.find((run) => run.step === step && within(at, run.startedAt, run.finishedAt)) ??
    runs.findLast((run) => run.step === step)
  for (const task of instance.tasks) {
    runOf(task.step, task.createdAt)?.tasks.push(task)
  }
  for (const call of instance.calls) {
    runOf(call.step, call.attempts[0]?.startedAt ?? instance.createdAt)?.calls.push(call)
  }
  for (const child of instance.subProcesses) {
    runOf(child.step, child.createdAt)?.subProcesses.push(child)
  }
  return runs
}

/** A span of time in its two largest units, such as "1 day 5 hr", in the locale's words for them. */
export function durationText(from: string, to: string, locale: string): string {
  const seconds = Math.max(0, Math.floor((Date.parse(to) - Date.parse(from)) / 1000))
  const unit = (value: number, name: 'day' | 'hour' | 'minute' | 'second') =>
    new Intl.NumberFormat(locale, { style: 'unit', unit: name, unitDisplay: 'short' }).format(value)
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  if (days > 0) return hours > 0 ? `${unit(days, 'day')} ${unit(hours, 'hour')}` : unit(days, 'day')
  if (hours > 0) return minutes > 0 ? `${unit(hours, 'hour')} ${unit(minutes, 'minute')}` : unit(hours, 'hour')
  if (minutes > 0) return unit(minutes, 'minute')
  // A run that took no whole second reads as under one, never as 0.
  return seconds < 1 ? `< ${unit(1, 'second')}` : unit(seconds % 60, 'second')
}

/**
 * Proposed, see the review on #3650: the tone of an instance's state, as `StatusTag` reads it: waiting for a person, a
 * task open at the step it is at, asks for attention; running or waiting for an event or a timer needs nobody;
 * completed ended well, failed failed, and cancelled ended with nothing more to do.
 */
export function instanceTone(instance: InstanceDetail): 'neutral' | 'attention' | 'success' | 'danger' | 'muted' {
  switch (instance.state) {
    case 'completed':
      return 'success'
    case 'failed':
      return 'danger'
    case 'cancelled':
      return 'muted'
    default:
      return instance.tasks.some((task) => task.step === instance.step && task.completedAt === null)
        ? 'attention'
        : 'neutral'
  }
}
