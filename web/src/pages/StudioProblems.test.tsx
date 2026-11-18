import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ShellAsideContext } from '../platform/shellAsideContext'
import { TextProvider } from '../platform/TextProvider'
import type { StudioDiagnostic } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { emptyDescription } from '../test/antd'
import { StudioProblems } from './StudioProblems'

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
