import { describe, expect, it } from 'vitest'
import type {
  AuthoringRequest,
  AuthoringRequestChecks,
  AuthoringRequestStep,
  StudioDiagnostic,
  StudioScenarioResult,
} from '../platform/studio'
import { requestResult, requestTitle } from './studioAuthoringRequest'

const error: StudioDiagnostic = {
  code: 'AXIS-E101',
  severity: 'error',
  message: 'The field has no type.',
  file: 'entities/cost-centre.json',
  path: '/fields/0',
  resourceId: null,
}

const warning: StudioDiagnostic = { ...error, code: 'AXIS-W201', severity: 'warning' }

const passedScenario: StudioScenarioResult = {
  name: 'ApprovalBelowThreshold',
  resourceId: 'r1',
  file: 'scenarios/approval.json',
  outcome: 'passed',
  durationMs: 10,
  step: null,
  path: null,
  message: null,
}

const failedScenario: StudioScenarioResult = { ...passedScenario, name: 'FinanceApproval', outcome: 'failed' }

const compiled = { outcome: 'passed' as const, diagnostics: [] }

/** Checks of a compile that passed, with its scenarios as given. */
function checksWith(scenarios: Partial<AuthoringRequestChecks['scenarios']> = {}): AuthoringRequestChecks {
  return {
    compile: compiled,
    packages: { outcome: 'notRun', items: [] },
    scenarios: { outcome: 'passed', request: null, diagnostics: [], items: [passedScenario], ...scenarios },
  }
}

/** A step, passed by default, with no checks. */
function step(overrides: Partial<AuthoringRequestStep>): AuthoringRequestStep {
  return {
    kind: 'work',
    summary: null,
    outcome: 'passed',
    tokens: 0,
    startedAt: '2026-11-18T09:00:00Z',
    finishedAt: '2026-11-18T09:01:00Z',
    checks: null,
    ...overrides,
  }
}

/** A check step with the checks given, passed when its compile and scenarios passed. */
function check(checks: AuthoringRequestChecks, outcome = 'passed'): AuthoringRequestStep {
  return step({ kind: 'check', outcome, checks })
}

function review(outcome: 'passed' | 'findings', findings: string | null = null): AuthoringRequestStep {
  return step({ kind: 'review', outcome, checks: { review: { outcome, findings } } })
}

function requestOf(overrides: Partial<AuthoringRequest>): AuthoringRequest {
  return {
    id: 'r1',
    application: 'PurchaseRequests',
    author: 'u1',
    authorName: 'Linh Nguyen',
    text: 'Add a cost centre.',
    title: null,
    state: 'ready',
    plan: {
      summary: 'Add a cost centre.',
      questions: [],
      changes: [],
      decisions: [],
      risks: [],
      checks: [],
      checklist: [],
      details: [],
    },
    round: 0,
    tokens: 0,
    autoAccept: false,
    sentAt: '2026-11-18T09:00:00Z',
    approvedAt: null,
    finishedAt: null,
    steps: [],
    ...overrides,
  }
}

describe('requestTitle', () => {
  it("is the title AI gave the request, or else the first sentence of the request's words", () => {
    expect(requestTitle(requestOf({ title: 'Add a cost centre', text: 'Please add a cost centre. Thanks.' }))).toBe(
      'Add a cost centre',
    )
    expect(requestTitle(requestOf({ title: null, text: 'Please add a cost centre. Thanks.' }))).toBe(
      'Please add a cost centre.',
    )
  })
})

describe('requestResult', () => {
  it('shows a passed check with all scenarios passing, and a review that was not run for the author', () => {
    const result = requestResult(requestOf({ steps: [step({ kind: 'plan' }), check(checksWith())] }), true)

    expect(result.checks).toMatchObject({
      passed: true,
      summaryKey: 'shell.studio.aiRequest.checks.allPass',
      values: {},
    })
    expect(result.review).toEqual({
      verdict: 'notReviewed',
      tone: 'muted',
      summaryKey: 'shell.studio.aiRequest.review.notReviewedAuthor',
      findings: null,
    })
    expect(result.failureKey).toBeNull()
  })

  it('tells anyone else that no second AI reviewed a change that was not reviewed', () => {
    const result = requestResult(requestOf({ steps: [check(checksWith())] }), false)

    expect(result.review?.summaryKey).toBe('shell.studio.aiRequest.review.notReviewedOther')
  })

  it('shows a passed review with no summary text, since the agent stores none', () => {
    const result = requestResult(requestOf({ steps: [check(checksWith()), review('passed')] }), true)

    expect(result.review).toEqual({ verdict: 'passed', tone: 'success', summaryKey: null, findings: null })
  })

  it('counts the scenarios that pass, leaving the skipped ones out', () => {
    const skipped = { name: 'Relaxed', file: 'scenarios/relaxed.json', outcome: 'skipped' as const, point: 'x' }
    const checks = checksWith({ outcome: 'failed', items: [passedScenario, failedScenario, skipped] })
    const result = requestResult(requestOf({ state: 'failed', steps: [check(checks, 'failed')] }), true)

    expect(result.checks).toMatchObject({
      passed: false,
      summaryKey: 'shell.studio.aiRequest.checks.somePass',
      values: { passed: 1, total: 2 },
    })
  })

  it('says that the one scenario fails, rather than counting none of one', () => {
    const checks = checksWith({ outcome: 'failed', items: [failedScenario] })
    const result = requestResult(requestOf({ state: 'failed', steps: [check(checks, 'failed')] }), true)

    expect(result.checks).toMatchObject({ passed: false, summaryKey: 'shell.studio.aiRequest.checks.scenarioFails' })
  })

  it('says that a run failed with no failing scenario, rather than that all pass', () => {
    const checks = checksWith({ outcome: 'failed', items: [] })
    const result = requestResult(requestOf({ state: 'failed', steps: [check(checks, 'failed')] }), true)

    expect(result.checks?.summaryKey).toBe('shell.studio.aiRequest.checks.runFailed')
  })

  it('counts only the errors of a compile that does not compile', () => {
    const checks: AuthoringRequestChecks = {
      compile: { outcome: 'failed', diagnostics: [error, error, warning] },
      packages: { outcome: 'notRun', items: [] },
      scenarios: { outcome: 'notRun', request: null, diagnostics: [], items: [] },
    }
    const result = requestResult(requestOf({ state: 'failed', steps: [check(checks, 'failed')] }), true)

    expect(result.checks).toMatchObject({
      passed: false,
      summaryKey: 'shell.studio.aiRequest.checks.compile',
      values: { count: 2 },
    })
    expect(result.review).toBeNull()
  })

  it('uses the one-problem text for a compile with one error', () => {
    const checks: AuthoringRequestChecks = {
      compile: { outcome: 'failed', diagnostics: [error] },
      packages: { outcome: 'notRun', items: [] },
      scenarios: { outcome: 'notRun', request: null, diagnostics: [], items: [] },
    }
    const result = requestResult(requestOf({ state: 'failed', steps: [check(checks, 'failed')] }), true)

    expect(result.checks?.summaryKey).toBe('shell.studio.aiRequest.checks.compileOne')
  })

  it('says that the package tests fail when they do', () => {
    const checks = checksWith()
    checks.packages = { outcome: 'failed', items: [] }
    const result = requestResult(requestOf({ state: 'failed', steps: [check(checks, 'failed')] }), true)

    expect(result.checks?.summaryKey).toBe('shell.studio.aiRequest.checks.packages')
  })

  it('reads the checks of the newest check and the review after it, not an older round', () => {
    const steps = [
      check(checksWith({ outcome: 'failed', items: [failedScenario] }), 'failed'),
      review('findings', 'Old finding.'),
      check(checksWith()),
    ]
    const result = requestResult(requestOf({ steps }), true)

    expect(result.checks?.passed).toBe(true)
    expect(result.review).toEqual({
      verdict: 'notReviewed',
      tone: 'muted',
      summaryKey: 'shell.studio.aiRequest.review.notReviewedAuthor',
      findings: null,
    })
  })

  it('shows a review with findings as changes requested, with its own text as the summary', () => {
    const steps = [check(checksWith()), review('findings', 'The draft leaves out the cost centre.')]
    const result = requestResult(requestOf({ state: 'failed', steps: [...steps] }), true)

    expect(result.review).toEqual({
      verdict: 'findings',
      tone: 'attention',
      summaryKey: null,
      findings: 'The draft leaves out the cost centre.',
    })
  })

  it('gives a failed request whose last check did not run no review', () => {
    const result = requestResult(
      requestOf({ state: 'failed', steps: [check(checksWith()), step({ kind: 'fix', outcome: 'unfinished' })] }),
      true,
    )

    expect(result.review).toBeNull()
  })

  it('names why a failed request failed by its newest step with an outcome', () => {
    const checks = checksWith({ outcome: 'failed', items: [failedScenario] })
    const cases: [AuthoringRequestStep[], string][] = [
      [[check(checks, 'failed')], 'shell.studio.aiRequest.failure.checks'],
      [[check(checksWith()), review('findings', 'Fix it.')], 'shell.studio.aiRequest.failure.review'],
      [[step({ kind: 'work', outcome: 'overBudget' })], 'shell.studio.aiRequest.failure.overBudget'],
      [[step({ kind: 'work', outcome: 'timedOut' })], 'shell.studio.aiRequest.failure.timedOut'],
      [[step({ kind: 'work', outcome: 'tokenEnded' })], 'shell.studio.aiRequest.failure.tokenEnded'],
      [[step({ kind: 'plan', outcome: 'noPlan' })], 'shell.studio.aiRequest.failure.noPlan'],
      [[step({ kind: 'accept', outcome: 'notAccepted' })], 'shell.studio.aiRequest.failure.notAccepted'],
      [[step({ kind: 'fix', outcome: 'interrupted' })], 'shell.studio.aiRequest.failure.unfinished'],
      [[], 'shell.studio.aiRequest.failure.unfinished'],
    ]
    for (const [steps, failureKey] of cases) {
      expect(requestResult(requestOf({ state: 'failed', steps }), true).failureKey).toBe(failureKey)
    }
  })

  it('shows no reason and no review for a request that has not ended', () => {
    const result = requestResult(requestOf({ state: 'working', steps: [check(checksWith())] }), true)

    expect(result.failureKey).toBeNull()
    expect(result.review).toBeNull()
  })

  it('shows no checks for an accepted request without a check, and the review as not reviewed', () => {
    const result = requestResult(requestOf({ state: 'accepted', steps: [] }), true)

    expect(result.checks).toBeNull()
    expect(result.review?.verdict).toBe('notReviewed')
  })

  it('reads a request with no steps as having no checks', () => {
    const result = requestResult(requestOf({ steps: undefined }), true)

    expect(result.checks).toBeNull()
  })
})
