import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import {
  attemptOutcomeText,
  attemptOutcomeTone,
  callStateText,
  callStateTone,
  historyStateRank,
  inspectTarget,
  processTitle,
  runStateText,
  runStateTone,
  startKindText,
  stateText,
  stateTone,
  stepRuns,
  stepTypeText,
  takenTransition,
  taskStateText,
  taskStateTone,
  transitionId,
  runParts,
  runProblem,
  type InstanceCall,
  type InstanceTask,
  type InstanceDetail,
  type PathEntry,
} from './inspection'

describe('processTitle', () => {
  const texts = { 'processes.purchaseRequestApproval.title': 'Purchase request approval' }
  const t = (key: string) => (Object.hasOwn(texts, key) ? texts[key as keyof typeof texts] : key)

  it('gives the title of the process in its release', () => {
    expect(processTitle(t, 'processes.purchaseRequestApproval.title', 'PurchaseRequestApproval')).toBe(
      'Purchase request approval',
    )
  })

  it('gives the name of the process where its release gives no title', () => {
    expect(processTitle(t, 'processes.childJob.title', 'ChildJob')).toBe('ChildJob')
  })
})

describe('startKindText', () => {
  it('gives the platform text of each start kind that no person made', () => {
    expect(startKindText((key) => platformEnglish[key as keyof typeof platformEnglish], 'parent')).toBe(
      'Parent process',
    )
    expect(startKindText((key) => platformEnglish[key as keyof typeof platformEnglish], 'schedule')).toBe('Schedule')
  })

  it('shows a start kind this build does not know as the API sends it', () => {
    expect(startKindText((key) => key, 'webhook')).toBe('webhook')
  })
})

describe('inspectTarget', () => {
  it('reads the process and the instance of an instance detail path', () => {
    expect(inspectTarget('/api/apps/StepApp/processes/Submit/instances/0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01')).toEqual({
      process: 'Submit',
      instanceId: '0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01',
    })
  })

  it('decodes the segments', () => {
    expect(inspectTarget('/api/apps/StepApp/processes/Big%20Order/instances/abc')).toEqual({
      process: 'Big Order',
      instanceId: 'abc',
    })
  })

  it('returns null for a path that is not an instance detail path', () => {
    expect(inspectTarget('/api/apps/StepApp/processes/Submit/instances')).toBeNull()
    expect(inspectTarget('/api/apps/StepApp/entities/Request/records/abc')).toBeNull()
  })
})

/** A stub text lookup that gives the English platform text of a key. */
const t = (key: string) => (platformEnglish as Record<string, string>)[key] ?? key

describe('the tones and words of a process state', () => {
  it.each([
    ['running', 'neutral', 'Running'],
    ['waiting', 'neutral', 'Waiting'],
    ['completed', 'success', 'Completed'],
    ['failed', 'danger', 'Failed'],
    ['cancelled', 'muted', 'Cancelled'],
  ])('gives the instance state %s the tone %s and the words %s', (state, tone, words) => {
    expect(stateTone(state)).toBe(tone)
    expect(stateText(t, state)).toBe(words)
  })

  it.each([
    ['done', 'success', 'Done'],
    ['running', 'neutral', 'Running'],
    ['waitingForEvent', 'neutral', 'Waiting for an event'],
    ['waitingForPerson', 'attention', 'Waiting for a person'],
    ['failed', 'danger', 'Failed'],
    ['cancelled', 'muted', 'Cancelled'],
  ])('gives the run state %s the tone %s and the words %s', (state, tone, words) => {
    expect(runStateTone(state)).toBe(tone)
    expect(runStateText(t, state)).toBe(words)
  })

  it.each([
    ['succeeded', 'success', 'Succeeded'],
    ['found', 'success', 'Found'],
    ['failed', 'danger', 'Failed'],
    ['retryable', 'muted', 'Retryable'],
    ['notFound', 'muted', 'Not found'],
    ['unknown', 'attention', 'Outcome unknown'],
  ])('gives the attempt outcome %s the tone %s and the words %s', (outcome, tone, words) => {
    expect(attemptOutcomeTone(outcome)).toBe(tone)
    expect(attemptOutcomeText(t, outcome)).toBe(words)
  })

  it.each([
    ['pending', 'neutral', 'Pending'],
    ['sending', 'neutral', 'Sending'],
    ['unknown', 'attention', 'Outcome unknown'],
    ['succeeded', 'success', 'Succeeded'],
    ['failed', 'danger', 'Failed'],
    ['cancelled', 'muted', 'Cancelled'],
  ])('gives the call state %s the tone %s and the words %s', (state, tone, words) => {
    expect(callStateTone(state)).toBe(tone)
    expect(callStateText(t, state)).toBe(words)
  })

  it.each([
    ['open', 'attention', 'Open'],
    ['completed', 'success', 'Completed'],
    ['cancelled', 'muted', 'Cancelled'],
    ['expired', 'muted', 'Expired'],
  ])('gives the task state %s the tone %s and the words %s', (state, tone, words) => {
    expect(taskStateTone(state)).toBe(tone)
    expect(taskStateText(t, state)).toBe(words)
  })

  it('ranks the run states for History: Failed, Waiting, Running, Done, then Cancelled', () => {
    const states = ['cancelled', 'done', 'running', 'waitingForEvent', 'waitingForPerson', 'failed'] as const
    expect([...states].sort((a, b) => historyStateRank(a) - historyStateRank(b))).toEqual([
      'failed',
      'waitingForEvent',
      'waitingForPerson',
      'running',
      'done',
      'cancelled',
    ])
  })

  it.each([
    ['decision', 'Decision'],
    ['task', 'Task'],
    ['operation', 'Operation'],
    ['subProcess', 'Sub-process'],
    ['timer', 'Timer'],
    ['waitEvent', 'Wait for an event'],
    ['end', 'End'],
  ])('gives the step type %s the words %s', (type, words) => {
    expect(stepTypeText(t, type)).toBe(words)
  })

  it('shows a state or a type this build does not know as the API sends it, in neutral', () => {
    expect(stateTone('paused')).toBe('neutral')
    expect(stateText(t, 'paused')).toBe('paused')
    expect(runStateTone('skipped')).toBe('neutral')
    expect(runStateText(t, 'skipped')).toBe('skipped')
    expect(attemptOutcomeTone('timedOut')).toBe('neutral')
    expect(attemptOutcomeText(t, 'timedOut')).toBe('timedOut')
    expect(callStateTone('queued')).toBe('neutral')
    expect(callStateText(t, 'queued')).toBe('queued')
    expect(taskStateTone('escalated')).toBe('neutral')
    expect(taskStateText(t, 'escalated')).toBe('escalated')
    expect(stepTypeText(t, 'script')).toBe('script')
  })

  it('does not take an inherited property for a known state', () => {
    expect(stateTone('toString')).toBe('neutral')
    expect(callStateTone('constructor')).toBe('neutral')
  })
})

describe('stepRuns', () => {
  const move = (next: string, on: string | null = null) => ({ next, on, branch: null, labelKey: null, after: null })
  const step = (name: string, type: string, transitions: ReturnType<typeof move>[]) => ({
    name,
    type,
    labelKey: null,
    event: null,
    eventLabelKey: null,
    transitions,
  })
  const row = (stepName: string, second: number, extra: Partial<PathEntry> = {}): PathEntry => ({
    step: stepName,
    startedAt: `2026-10-10T02:05:${String(second).padStart(2, '0')}Z`,
    finishedAt: `2026-10-10T02:05:${String(second + 1).padStart(2, '0')}Z`,
    decision: null,
    output: null,
    error: null,
    problem: null,
    traceId: null,
    ...extra,
  })
  function instance(patch: Partial<InstanceDetail>): InstanceDetail {
    return {
      id: 'i',
      process: 'P',
      processTitleKey: 'p.title',
      subjectId: null,
      subjectEntity: null,
      subjectLabel: null,
      releaseId: 'r',
      releaseNumber: 1,
      state: 'completed',
      step: 'done',
      timerDueAt: null,
      createdAt: '2026-10-10T02:05:00Z',
      endedAt: null,
      startedBy: null,
      input: null,
      result: null,
      studio: null,
      graph: {
        start: 'approve',
        steps: [
          step('approve', 'task', [move('register', 'approve'), move('done', 'reject')]),
          step('register', 'operation', [move('done')]),
          step('done', 'end', []),
        ],
      },
      path: [],
      tasks: [],
      calls: [],
      parent: null,
      subProcesses: [],
      commands: [],
      ...patch,
    }
  }

  it("makes a task's created row and its completion row one run", () => {
    const { runs } = stepRuns(
      instance({
        path: [
          row('approve', 0, { output: { state: 'waiting' } }),
          row('approve', 5, { decision: 'approve' }),
          row('register', 6),
          row('done', 7),
        ],
      }),
    )

    expect(runs.map((run) => [run.step, run.number])).toEqual([
      ['approve', 1],
      ['register', 1],
      ['done', 1],
    ])
    expect(runs[0]).toMatchObject({ startedAt: '2026-10-10T02:05:00Z', finishedAt: '2026-10-10T02:05:06Z' })
    expect(runs[0].entries).toHaveLength(2)
  })

  it('numbers the runs of a step that a decision loops back to', () => {
    const loop = instance({
      graph: {
        start: 'a',
        steps: [
          step('a', 'operation', [move('b')]),
          step('b', 'decision', [move('a', '0'), move('done', 'otherwise')]),
          step('done', 'end', []),
        ],
      },
      path: [
        row('a', 0),
        row('b', 2, { decision: '0' }),
        row('a', 4),
        row('b', 6, { decision: 'otherwise' }),
        row('done', 8),
      ],
    })

    const { runs } = stepRuns(loop)

    expect(runs.map((run) => `${run.step}${run.number}`)).toEqual(['a1', 'b1', 'a2', 'b2', 'done1'])
  })

  it('counts a failed row and the retried row after it as two runs, the first failed and the second the instance state', () => {
    const { runs, runStates } = stepRuns(
      instance({
        state: 'running',
        step: 'register',
        path: [
          row('approve', 0, { decision: 'approve' }),
          row('register', 1, { error: 'It failed' }),
          row('register', 5),
        ],
      }),
    )

    expect(runs.filter((run) => run.step === 'register').map((run) => [run.number, run.state])).toEqual([
      [1, 'failed'],
      [2, 'running'],
    ])
    expect(runStates.get('register')).toBe('running')
    expect(runStates.get('approve')).toBe('done')
  })

  it('gives the run at the current step the state of the instance, and leaves it open', () => {
    const waitingAtTask = stepRuns(
      instance({ state: 'waiting', step: 'approve', path: [row('approve', 0, { output: { state: 'waiting' } })] }),
    )
    expect(waitingAtTask.runs[0]).toMatchObject({ state: 'waitingForPerson', finishedAt: null })

    const waitingAtTimer = stepRuns(instance({ state: 'waiting', step: 'register', path: [row('register', 0)] }))
    expect(waitingAtTimer.runStates.get('register')).toBe('waitingForEvent')

    const failed = stepRuns(instance({ state: 'failed', step: 'register', path: [row('register', 0, { error: 'x' })] }))
    expect(failed.runStates.get('register')).toBe('failed')

    const cancelled = stepRuns(instance({ state: 'cancelled', step: 'approve', path: [row('approve', 0)] }))
    expect(cancelled.runStates.get('approve')).toBe('cancelled')
  })

  it('gives a current step with no row an open run of its own, and leaves a step not reached out', () => {
    const { runs, runStates } = stepRuns(
      instance({ state: 'waiting', step: 'register', path: [row('approve', 0, { decision: 'approve' })] }),
    )

    expect(runs.map((run) => [run.step, run.number, run.state, run.startedAt, run.finishedAt])).toEqual([
      ['approve', 1, 'done', '2026-10-10T02:05:00Z', '2026-10-10T02:05:01Z'],
      ['register', 1, 'waitingForEvent', null, null],
    ])
    expect(runStates.has('done')).toBe(false)
  })

  it("marks the transition that a run's decision took when two lead to the same step", () => {
    const { taken } = stepRuns(
      instance({
        graph: {
          start: 'check',
          steps: [step('check', 'decision', [move('done', '0'), move('done', 'otherwise')]), step('done', 'end', [])],
        },
        path: [row('check', 0, { decision: 'otherwise' }), row('done', 2)],
      }),
    )

    expect([...taken]).toEqual([transitionId('check', 1)])
  })

  it('marks the only transition to the next run, and none between two runs of the same step', () => {
    const { taken } = stepRuns(
      instance({
        state: 'completed',
        path: [
          row('approve', 0, { decision: 'reject', error: 'x' }),
          row('approve', 2, { decision: 'approve' }),
          row('register', 4),
          row('done', 6),
        ],
      }),
    )

    expect([...taken].sort()).toEqual([transitionId('approve', 0), transitionId('register', 0)])
  })

  const task = (stepName: string, second: number): InstanceTask => ({
    id: `task-${stepName}-${second}`,
    step: stepName,
    assigneeKind: 'role',
    assignee: 'manager',
    assigneeId: null,
    state: 'completed',
    outcome: 'approve',
    completedBy: null,
    completedById: null,
    createdAt: `2026-10-10T02:05:${String(second).padStart(2, '0')}Z`,
    dueAt: null,
    completedAt: null,
  })
  const call = (stepName: string, second: number | null, state = 'succeeded'): InstanceCall => ({
    id: `call-${stepName}-${second}`,
    kind: 'connector',
    step: stepName,
    connector: 'ERP',
    operation: 'send',
    state,
    attempts:
      second === null
        ? []
        : [
            {
              kind: 'attempt',
              number: 1,
              startedAt: `2026-10-10T02:05:${String(second).padStart(2, '0')}Z`,
              finishedAt: `2026-10-10T02:05:${String(second).padStart(2, '0')}.5Z`,
              outcome: state === 'failed' ? 'failed' : 'succeeded',
              httpStatus: null,
              error: state === 'failed' ? 'refused by ERP' : null,
              problem: null,
              traceId: null,
            },
          ],
    input: null,
    inputHidden: false,
    resolvable: false,
  })
  const looping = (patch: Partial<InstanceDetail> = {}) =>
    instance({
      state: 'completed',
      step: 'done',
      path: [row('register', 0), row('approve', 2), row('register', 4), row('done', 6)],
      ...patch,
    })

  it('gives each run of a step that looped its own tasks, calls and sub-processes, by the time they began', () => {
    const detail = looping({
      tasks: [task('approve', 2)],
      calls: [call('register', 0), call('register', 4)],
      subProcesses: [
        {
          id: 'c1',
          process: 'Child',
          processTitleKey: 'child.title',
          step: 'register',
          state: 'completed',
          createdAt: '2026-10-10T02:05:04Z',
          endedAt: null,
        },
      ],
    })
    const { runs } = stepRuns(detail)

    expect(runParts(detail, runs, runs[0]!).calls.map((item) => item.id)).toEqual(['call-register-0'])
    expect(runParts(detail, runs, runs[2]!).calls.map((item) => item.id)).toEqual(['call-register-4'])
    expect(runParts(detail, runs, runs[0]!).subProcesses).toEqual([])
    expect(runParts(detail, runs, runs[2]!).subProcesses.map((item) => item.id)).toEqual(['c1'])
    expect(runParts(detail, runs, runs[1]!).tasks.map((item) => item.id)).toEqual(['task-approve-2'])
    expect(runParts(detail, runs, runs[1]!).calls).toEqual([])
  })

  it("gives a call with no attempts to its step's last run", () => {
    const detail = looping({ calls: [call('register', null)] })
    const { runs } = stepRuns(detail)

    expect(runParts(detail, runs, runs[0]!).calls).toEqual([])
    expect(runParts(detail, runs, runs[2]!).calls).toHaveLength(1)
  })

  it('words the problem of the last row of a run, and of the last failed attempt when no row has one', () => {
    const failed = looping({
      state: 'failed',
      step: 'register',
      path: [row('register', 0, { problem: { code: 'refused', values: {} }, error: 'x' })],
    })
    const { runs } = stepRuns(failed)
    expect(runProblem(t, 'en', failed, runs, runs[0]!)).toBe('The outside system refused the call.')

    const viaCall = looping({
      state: 'failed',
      step: 'register',
      path: [row('register', 0)],
      calls: [call('register', 0, 'failed')],
    })
    const viaRuns = stepRuns(viaCall).runs
    expect(runProblem(t, 'en', viaCall, viaRuns, viaRuns[0]!)).toBe('refused by ERP')

    const none = looping({ path: [row('register', 0), row('done', 2)] })
    const noneRuns = stepRuns(none).runs
    expect(runProblem(t, 'en', none, noneRuns, noneRuns[0]!)).toBeNull()
  })

  it("gives the transition a run took to its next step, by the run's decision", () => {
    const detail = instance({
      graph: {
        start: 'check',
        steps: [step('check', 'decision', [move('done', '0'), move('done', 'otherwise')]), step('done', 'end', [])],
      },
      path: [row('check', 0, { decision: 'otherwise' }), row('done', 2)],
    })
    const { runs } = stepRuns(detail)

    expect(takenTransition(detail, runs[0]!, 'done')?.on).toBe('otherwise')
    expect(takenTransition(detail, runs[0]!, 'nowhere')).toBeUndefined()
  })
})
