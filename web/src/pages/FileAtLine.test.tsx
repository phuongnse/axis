import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { FileAtLine } from './FileAtLine'

function renderFileAtLine(props: { file: string; line?: number; href?: string }) {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <FileAtLine {...props} />
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

describe('FileAtLine', () => {
  it('draws the file and its line as a link to the address when it has an href', () => {
    renderFileAtLine({
      file: 'packages/tools/lib/x.py',
      line: 3,
      href: '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py&line=3',
    })

    expect(screen.getByRole('link', { name: 'packages/tools/lib/x.py, line 3' })).toHaveAttribute(
      'href',
      '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py&line=3',
    )
  })

  it('draws the file as muted text, with no link, when it has no href', () => {
    renderFileAtLine({ file: 'packages/tools/lib/x.py', line: 3 })

    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.getByText('packages/tools/lib/x.py, line 3')).toHaveStyle({
      color: designTokensFor('light').colour.textMuted,
    })
  })
})
