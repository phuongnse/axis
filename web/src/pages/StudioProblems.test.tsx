import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
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

function renderProblems(diagnostics: StudioDiagnostic[]) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioProblems diagnostics={diagnostics} onSelect={vi.fn()} onClose={vi.fn()} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

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

    expect(screen.getByTestId('studio-diagnostics')).toHaveStyle({ overscrollBehavior: 'contain' })
  })
})
