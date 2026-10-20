import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioRequest } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioRequestStatus } from './StudioRequestStatus'

function request(overrides: Partial<StudioRequest>): StudioRequest {
  return {
    id: '5b6c7d8e-9f01-4a2b-8c3d-4e5f60718293',
    application: 'PurchaseRequests',
    kind: 'publish',
    target: 'tenant',
    status: 'queued',
    label: null,
    note: null,
    releaseId: null,
    diagnostics: [],
    ...overrides,
  }
}

function renderStatus(value: StudioRequest, releaseNumber: number | null = null) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioRequestStatus request={value} releaseNumber={releaseNumber} />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return screen.getByTestId('studio-request-status')
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('StudioRequestStatus in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('shows a queued request as pending', () => {
    const status = renderStatus(request({ status: 'queued' }))

    expect(status).toHaveAttribute('data-status', 'queued')
    expect(status).toHaveTextContent('Queued')
    expect(screen.queryByTestId('studio-release-number')).not.toBeInTheDocument()
  })

  it('shows a running request as pending', () => {
    const status = renderStatus(request({ status: 'running' }))

    expect(status).toHaveAttribute('data-status', 'running')
    expect(status).toHaveTextContent('Running')
  })

  it('shows the release number of a succeeded publish', () => {
    const status = renderStatus(request({ status: 'succeeded', releaseId: 'r3' }), 3)

    expect(status).toHaveAttribute('data-status', 'succeeded')
    expect(status).toHaveTextContent('Published')
    expect(screen.getByTestId('studio-release-number')).toHaveTextContent('3')
  })

  it('shows a succeeded validate without a release number', () => {
    const status = renderStatus(request({ kind: 'validate', status: 'succeeded' }))

    expect(status).toHaveAttribute('data-status', 'succeeded')
    expect(status).toHaveTextContent('Validated')
    expect(screen.queryByTestId('studio-release-number')).not.toBeInTheDocument()
  })

  it('shows a failed publish as a failure, even when it names a release', () => {
    const status = renderStatus(request({ status: 'failed', releaseId: 'r3' }), 3)

    expect(status).toHaveAttribute('data-status', 'failed')
    expect(status).toHaveTextContent('Publish failed')
    expect(screen.queryByTestId('studio-release-number')).not.toBeInTheDocument()
  })

  it('shows a failed validate as a failure', () => {
    const status = renderStatus(request({ kind: 'validate', status: 'failed' }))

    expect(status).toHaveAttribute('data-status', 'failed')
    expect(status).toHaveTextContent('Validation failed')
  })

  it('shows the release number of a succeeded rollback', () => {
    const status = renderStatus(request({ kind: 'rollback', status: 'succeeded', releaseId: 'r1' }), 1)

    expect(status).toHaveAttribute('data-status', 'succeeded')
    expect(status).toHaveTextContent('Rolled back')
    expect(screen.getByTestId('studio-release-number')).toHaveTextContent('1')
  })

  it('shows a failed rollback as a failure, without a release number', () => {
    const status = renderStatus(request({ kind: 'rollback', status: 'failed', releaseId: 'r1' }), 1)

    expect(status).toHaveAttribute('data-status', 'failed')
    expect(status).toHaveTextContent('Rollback failed')
    expect(screen.queryByTestId('studio-release-number')).not.toBeInTheDocument()
  })
})
