import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import {
  attemptOutcomeText,
  attemptOutcomeTone,
  callStateText,
  callStateTone,
  inspectTarget,
  processTitle,
  runStateText,
  runStateTone,
  startKindText,
  stateText,
  stateTone,
  stepTypeText,
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
    expect(stepTypeText(t, 'script')).toBe('script')
  })

  it('does not take an inherited property for a known state', () => {
    expect(stateTone('toString')).toBe('neutral')
    expect(callStateTone('constructor')).toBe('neutral')
  })
})
