import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { colourTokens } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import type { StudioDiagnostic, StudioReleaseItem, StudioReleases } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioBand } from './StudioBand'

function diagnostic(severity: 'error' | 'warning', code: string): StudioDiagnostic {
  return { code, severity, message: 'A message.', file: 'application.json', path: '', resourceId: null }
}

const live: StudioReleases = {
  active: { releaseId: 'r7', number: 7, contentHash: 'hash-7' },
  folderContentHash: 'hash-7',
  changes: null,
  items: [],
}

function activation(releaseId: string, number: number, offStandard: boolean): StudioReleaseItem {
  return {
    releaseId,
    number,
    contentHash: `hash-${number}`,
    activatedAt: '2026-05-02T10:00:00Z',
    actor: 'author',
    actorName: null,
    label: null,
    note: null,
    requestId: null,
    offStandard,
    relaxed: [],
  }
}

function release3(offStandard: boolean): StudioReleases {
  return {
    active: { releaseId: 'r3', number: 3, contentHash: 'hash-3' },
    folderContentHash: 'hash-3',
    changes: null,
    items: [activation('r3', 3, offStandard)],
  }
}

function renderBand(
  history: StudioReleases | 'failed' | undefined,
  diagnostics: StudioDiagnostic[] = [],
  onToggleProblems = vi.fn(),
) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioBand
          diagnostics={diagnostics}
          history={history}
          problemsOpen={false}
          onToggleProblems={onToggleProblems}
        >
          <button type="button">Action</button>
        </StudioBand>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onToggleProblems
}

describe('StudioBand', () => {
  it('says the errors and warnings as one button, the errors in dangerText and the warnings in warningText', () => {
    renderBand(live, [diagnostic('error', 'AXC1'), diagnostic('error', 'AXC2'), diagnostic('warning', 'AXC3')])

    const button = screen.getByTestId('studio-problems')
    expect(button).toHaveTextContent('2 errors, 1 warning')
    expect(button).toHaveAttribute('aria-expanded', 'false')
    expect(button).toHaveAttribute('aria-controls', 'studio-problems-panel')
    expect(screen.getByText('2 errors')).toHaveStyle({ color: colourTokens.dangerText.light })
    expect(screen.getByText('1 warning')).toHaveStyle({ color: colourTokens.warningText.light })
  })

  it('says one error in the singular', () => {
    renderBand(live, [diagnostic('error', 'AXC1')])
    expect(screen.getByTestId('studio-problems')).toHaveTextContent(/^1 error$/)
  })

  it('says only the warnings when there is no error', () => {
    renderBand(live, [diagnostic('warning', 'AXC1'), diagnostic('warning', 'AXC2')])

    expect(screen.getByTestId('studio-problems')).toHaveTextContent(/^2 warnings$/)
  })

  it('shows no problems button without a problem', () => {
    renderBand(live)

    expect(screen.queryByTestId('studio-problems')).not.toBeInTheDocument()
  })

  it('calls the toggle when the problems button is clicked', async () => {
    const toggle = renderBand(live, [diagnostic('error', 'AXC1')])

    await userEvent.click(screen.getByTestId('studio-problems'))

    expect(toggle).toHaveBeenCalledOnce()
  })

  it('says Changes not live when the folder differs from the live release', () => {
    renderBand({ ...live, folderContentHash: 'other' })

    expect(screen.getByTestId('studio-folder-differs')).toHaveTextContent('Changes not live')
  })

  it('says Not published yet when there is no live release', () => {
    renderBand({ active: null, folderContentHash: null, changes: null, items: [] })

    expect(screen.getByText('Not published yet')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-folder-differs')).not.toBeInTheDocument()
  })

  it('says which release is live when nothing needs attention', () => {
    renderBand(live)

    expect(screen.getByText('Release 7 is live')).toHaveStyle({ color: colourTokens.textMuted.light })
    expect(screen.queryByTestId('studio-folder-differs')).not.toBeInTheDocument()
  })

  it('says the live release is off-standard when its history item is', () => {
    renderBand(release3(true))

    expect(screen.getByText('Release 3 is live, off-standard')).toHaveStyle({ color: colourTokens.textMuted.light })
  })

  it('does not say off-standard when the live release is standard', () => {
    renderBand(release3(false))

    expect(screen.getByText('Release 3 is live')).toBeInTheDocument()
    expect(screen.getByTestId('studio-band')).not.toHaveTextContent(/off-standard/i)
  })

  it('says Releases did not load in warningText', () => {
    renderBand('failed')

    expect(screen.getByTestId('studio-releases-error')).toHaveTextContent('Releases did not load')
    expect(screen.getByTestId('studio-releases-error')).toHaveStyle({ color: colourTokens.warningText.light })
  })

  it('shows only the problems while the history loads', () => {
    renderBand(undefined, [diagnostic('error', 'AXC1')])

    expect(screen.getByTestId('studio-problems')).toBeInTheDocument()
    expect(screen.getByTestId('studio-band')).not.toHaveTextContent(/release|published|live/i)
  })

  it.each([
    ['Release is live', live, []],
    ['Changes not live', { ...live, folderContentHash: 'other' }, []],
    ['Not published yet', { active: null, folderContentHash: null, changes: null, items: [] }, []],
    ['Releases did not load', 'failed' as const, []],
    ['off-standard live', release3(true), []],
    ['problems', live, [diagnostic('error', 'AXC1'), diagnostic('warning', 'AXC2')]],
  ])('has no icon and no link in the %s state', (_, history, diagnostics) => {
    renderBand(history, diagnostics)

    const band = screen.getByTestId('studio-band')
    expect(band.querySelector('svg')).toBeNull()
    expect(band.querySelector('[role="img"]')).toBeNull()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
