import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type {
  StudioPackageTestResult,
  StudioRequest,
  StudioScenarioResult,
  StudioSkippedScenarioResult,
} from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { emptyDescription } from '../test/antd'
import type { FileAddressOf } from '../platform/studioAddress'
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

function renderResults(
  value: StudioRequest,
  onSelect = vi.fn(() => '/studio'),
  extra: { fileAddressOf?: FileAddressOf; titled?: boolean } = {},
) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioTestResults request={value} addressOf={onSelect} {...extra} />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return { section: screen.getByTestId('studio-test-results'), onSelect }
}

const passingTest: StudioPackageTestResult = { name: 'test_adds', outcome: 'passed', message: null, durationMs: 1234 }

const failingTest: StudioPackageTestResult = {
  name: 'test_greets',
  outcome: 'failed',
  message: 'AssertionError: Hello Ada != Hi Ada',
  durationMs: 30,
  file: 'lib/x.py',
  line: 3,
  column: 5,
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

  it('shows every scenario of a succeeded run as passed, with its name as the only link', async () => {
    const { section, onSelect } = renderResults(request({ status: 'succeeded', scenarios: [finance] }))

    expect(section).toHaveAttribute('data-status', 'succeeded')
    const rows = screen.getAllByTestId('studio-test-result')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveAttribute('data-outcome', 'passed')
    expect(rows[0]).toHaveTextContent('1.2 sec')
    expect(within(rows[0]).getByText('passed').closest('[data-tone]')).toHaveAttribute('data-tone', 'success')
    expect(within(rows[0]).getByText('Test scenario')).toBeInTheDocument()

    await userEvent.click(within(rows[0]).getByRole('button', { name: 'FinanceApproval' }))
    expect(onSelect).toHaveBeenCalledWith('7f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f', '')
  })

  it('shows a failed scenario with its step and message, and opens it at the step when selected', async () => {
    const { onSelect } = renderResults(request({ status: 'failed', scenarios: [finance, approval] }))

    const rows = screen.getAllByTestId('studio-test-result')
    expect(rows).toHaveLength(2)
    const failed = rows[1]
    expect(failed).toHaveAttribute('data-outcome', 'failed')
    expect(failed).toHaveTextContent('ApprovalBelowThreshold')
    expect(failed).toHaveTextContent('< 1 sec')
    expect(within(failed).getByText('failed').closest('[data-tone]')).toHaveAttribute('data-tone', 'danger')
    expect(within(failed).getByText('Test scenario')).toBeInTheDocument()
    expect(within(failed).getByText('Step 6')).toBeInTheDocument()
    expect(failed).toHaveTextContent('The record status is approved, not rejected.')

    expect(within(failed).getAllByRole('button')).toHaveLength(1)
    await userEvent.click(within(failed).getByRole('button', { name: 'ApprovalBelowThreshold' }))
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
    expect(within(rows[0]).getByText('skipped').closest('[data-tone]')).toHaveAttribute('data-tone', 'muted')
    expect(rows[0]).toHaveTextContent('department-budgets/ApprovalRecordsDecision')
    expect(within(rows[0]).getByText('Relaxed point: Budget/effects/decisionRecorded')).toBeInTheDocument()
    expect(within(rows[0]).queryByRole('button')).not.toBeInTheDocument()

    await userEvent.click(rows[0])
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('says no scenario ran when a failed run has no results', () => {
    const { section } = renderResults(request({ status: 'failed', scenarios: [] }))

    expect(section).toHaveAttribute('data-status', 'failed')
    expect(emptyDescription('No scenario ran. See the problems.')).toBeInTheDocument()
    expect(screen.queryAllByTestId('studio-test-result')).toHaveLength(0)
  })
})

describe('StudioTestResults of a package test', () => {
  const packageTest = (overrides: Partial<StudioRequest> = {}) =>
    request({ status: 'failed', package: 'tools', packageTests: [passingTest, failingTest], ...overrides })

  it('shows a row a test with its result, name, duration and message, and the failed place opens its line', async () => {
    const onOpenFile = vi.fn(() => '/studio')
    renderResults(
      packageTest(),
      vi.fn(() => '/studio'),
      { fileAddressOf: onOpenFile },
    )

    const [passed, failed] = screen.getAllByTestId('studio-test-result')
    expect(passed).toHaveAttribute('data-outcome', 'passed')
    expect(passed).toHaveTextContent('test_adds')
    expect(passed).toHaveTextContent('1.2 sec')
    expect(within(passed).getByText('passed').closest('[data-tone]')).toHaveAttribute('data-tone', 'success')
    expect(within(passed).queryByRole('button')).not.toBeInTheDocument()
    expect(failed).toHaveAttribute('data-outcome', 'failed')
    expect(failed).toHaveTextContent('test_greets')
    expect(failed).toHaveTextContent('< 1 sec')
    expect(failed).toHaveTextContent('AssertionError: Hello Ada != Hi Ada')
    expect(within(failed).getByText('failed').closest('[data-tone]')).toHaveAttribute('data-tone', 'danger')

    await userEvent.click(within(failed).getByRole('button', { name: 'packages/tools/lib/x.py, line 3' }))
    expect(onOpenFile).toHaveBeenCalledWith('packages/tools/lib/x.py', 3)
  })

  it('shows the place as plain text without onOpenFile, and no duration or place that the host did not send', () => {
    renderResults(
      packageTest({
        packageTests: [{ name: 'test_old', outcome: 'failed', message: 'Boom', file: 'lib/x.py' }],
      }),
    )

    const row = screen.getByTestId('studio-test-result')
    expect(within(row).getByText('packages/tools/lib/x.py')).toBeVisible()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    expect(row).not.toHaveTextContent('sec')
  })

  it('draws the heading unless titled is off', () => {
    const { section } = renderResults(packageTest())
    expect(within(section).getByRole('heading', { name: 'Test results' })).toBeInTheDocument()
  })

  it('has no heading when titled is off', () => {
    const { section } = renderResults(packageTest(), vi.fn(), { titled: false })
    expect(within(section).queryByRole('heading')).not.toBeInTheDocument()
  })

  it('says no test ran when an ended run has none', () => {
    renderResults(packageTest({ packageTests: [] }))
    expect(emptyDescription('No test ran. See the problems.')).toBeInTheDocument()
  })

  it('shows a running package test as pending', () => {
    const { section } = renderResults(packageTest({ status: 'running', packageTests: null }))
    expect(section).toHaveTextContent('The results show when the run ends.')
  })
})

describe('StudioTestResults without onSelect', () => {
  it('shows a failed scenario as plain text, with no button to open it', () => {
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioTestResults request={request({ status: 'failed', scenarios: [approval, finance] })} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const failed = screen.getAllByTestId('studio-test-result').find((row) => row.dataset.outcome === 'failed')!
    expect(within(failed).getByText('ApprovalBelowThreshold')).toBeVisible()
    expect(within(failed).getByText('The record status is approved, not rejected.')).toBeVisible()
    expect(within(failed).queryByRole('button')).not.toBeInTheDocument()
  })
})
