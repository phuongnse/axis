import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ShellAsideContext } from '../platform/shellAsideContext'
import { TextProvider } from '../platform/TextProvider'
import type { StudioDiagnostic } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { emptyDescription } from '../test/antd'
import { StudioProblemList, StudioProblems } from './StudioProblems'

const error: StudioDiagnostic = {
  code: 'AXIS-E101',
  severity: 'error',
  message: 'The field has no type.',
  file: 'entities/purchase-request.json',
  path: '/fields/0',
  resourceId: null,
}

let aside: HTMLDivElement | undefined

/** Renders the panel with the shell's right column, which is `aside`. */
function renderProblems(diagnostics: StudioDiagnostic[]) {
  aside = document.createElement('div')
  document.body.append(aside)
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ShellAsideContext.Provider value={{ target: aside, claim: () => () => {} }}>
          <StudioProblems diagnostics={diagnostics} onSelect={vi.fn()} onClose={vi.fn()} />
        </ShellAsideContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

afterEach(() => {
  aside?.remove()
  aside = undefined
})

describe('StudioProblems', () => {
  it('says there are no problems when there is no diagnostic', () => {
    renderProblems([])

    expect(emptyDescription('No problems.')).toBeInTheDocument()
  })

  it('lists a problem and shows no empty state', () => {
    renderProblems([error])

    expect(screen.getByText('AXIS-E101')).toBeInTheDocument()
    expect(screen.queryByText('No problems.')).not.toBeInTheDocument()
  })

  it('stops the panel at its end, so the page does not scroll', () => {
    renderProblems([error])

    const body = screen.getByText('AXIS-E101').closest('ul')!.parentElement!
    expect(body).toHaveStyle({ overflowY: 'auto', overscrollBehavior: 'contain' })
  })
})

describe('StudioProblemList', () => {
  const resourceError: StudioDiagnostic = { ...error, code: 'AXIS-E102', resourceId: 'e1' }

  it('lists errors before warnings, and a problem with no onSelect is plain text, not a button', () => {
    const warning: StudioDiagnostic = { ...error, code: 'AXIS-W201', severity: 'warning', resourceId: 'w1' }
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioProblemList diagnostics={[warning, resourceError]} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const codes = screen.getAllByRole('listitem').map((item) => item.querySelector('strong')?.textContent)
    expect(codes).toEqual(['AXIS-E102', 'AXIS-W201'])
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('makes a problem that names a resource a button when onSelect is given', async () => {
    const onSelect = vi.fn()
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioProblemList diagnostics={[resourceError]} onSelect={onSelect} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    await userEvent.click(screen.getByRole('button', { name: /AXIS-E102/ }))
    expect(onSelect).toHaveBeenCalledWith('e1', '/fields/0')
  })
})
