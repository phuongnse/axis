import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioRequest, StudioScenarioResult, StudioSkippedScenarioResult } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioTestResults } from './StudioTestResults'

const approval: StudioScenarioResult = {
  name: 'ApprovalBelowThreshold',
  resourceId: '09d75317-fd9e-411e-8322-317778fc1b2b',
  file: 'scenarios/approval-below-threshold.json',
  outcome: 'failed',
  durationMs: 412,
  step: 5,
  path: '/steps/5',
  message: 'The record status is approved, not rejected.',
}

const finance: StudioScenarioResult = {
  name: 'FinanceApproval',
  resourceId: '7f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f',
  file: 'scenarios/finance-approval.json',
  outcome: 'passed',
  durationMs: 1234,
  step: null,
  path: null,
  message: null,
}

function request(overrides: Partial<StudioRequest>): StudioRequest {
  return {
    id: '5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293',
    application: 'PurchaseRequests',
    kind: 'test',
    target: 'tenant',
    status: 'queued',
    label: null,
    note: null,
    releaseId: null,
    diagnostics: [],
    scenarios: null,
    ...overrides,
  }
}

function renderResults(value: StudioRequest, onSelect = vi.fn()) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioTestResults request={value} onSelect={onSelect} />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return { section: screen.getByTestId('studio-test-results'), onSelect }
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('StudioTestResults in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it.each(['queued', 'running'] as const)('shows a %s run as pending, with no rows', (status) => {
    const { section } = renderResults(request({ status }))

    expect(section).toHaveAttribute('data-status', status)
    expect(section).toHaveTextContent('The results show when the run ends.')
    expect(screen.queryAllByTestId('studio-test-result')).toHaveLength(0)
  })

  it('shows every scenario of a succeeded run as passed, with no button', () => {
    const { section } = renderResults(request({ status: 'succeeded', scenarios: [finance] }))

    expect(section).toHaveAttribute('data-status', 'succeeded')
    const rows = screen.getAllByTestId('studio-test-result')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveAttribute('data-outcome', 'passed')
    expect(rows[0]).toHaveTextContent('FinanceApproval')
    expect(rows[0]).toHaveTextContent('1234 ms')
    expect(within(rows[0]).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows a failed scenario with its step and message, and opens it at the step when selected', async () => {
    const { onSelect } = renderResults(request({ status: 'failed', scenarios: [finance, approval] }))

    const rows = screen.getAllByTestId('studio-test-result')
    expect(rows).toHaveLength(2)
    const failed = rows[1]
    expect(failed).toHaveAttribute('data-outcome', 'failed')
    expect(failed).toHaveTextContent('ApprovalBelowThreshold')
    expect(failed).toHaveTextContent('Step 6')
    expect(failed).toHaveTextContent('The record status is approved, not rejected.')

    await userEvent.click(within(failed).getByRole('button'))
    expect(onSelect).toHaveBeenCalledWith('09d75317-fd9e-411e-8322-317778fc1b2b', '/steps/5')
  })

  it('shows a skipped contract scenario with its relaxed point, and opens nothing when clicked', async () => {
    const skipped: StudioSkippedScenarioResult = {
      name: 'department-budgets/ApprovalRecordsDecision',
      file: 'scenarios/approval-records-decision.json',
      outcome: 'skipped',
      point: 'Budget/effects/decisionRecorded',
    }
    const { section, onSelect } = renderResults(
      request({ kind: 'publish', status: 'succeeded', scenarios: [skipped, finance] }),
    )

    expect(section).toHaveAttribute('data-status', 'succeeded')
    expect(screen.getByRole('heading', { name: 'Contract scenarios' })).toBeInTheDocument()
    const rows = screen.getAllByTestId('studio-test-result')
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveAttribute('data-outcome', 'skipped')
    expect(rows[0]).toHaveTextContent('skipped')
    expect(rows[0]).toHaveTextContent('department-budgets/ApprovalRecordsDecision')
    expect(rows[0]).toHaveTextContent('Relaxed point Budget/effects/decisionRecorded')
    expect(within(rows[0]).queryByRole('button')).not.toBeInTheDocument()

    await userEvent.click(rows[0])
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('says no scenario ran when a failed run has no results', () => {
    const { section } = renderResults(request({ status: 'failed', scenarios: [] }))

    expect(section).toHaveAttribute('data-status', 'failed')
    expect(section).toHaveTextContent('No scenario ran. See the diagnostics.')
    expect(screen.queryAllByTestId('studio-test-result')).toHaveLength(0)
  })
})
