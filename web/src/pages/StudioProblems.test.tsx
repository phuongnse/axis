import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ShellAsideContext } from '../platform/shellAsideContext'
import { TextProvider } from '../platform/TextProvider'
import type { StudioDiagnostic, StudioResourceItem } from '../platform/studio'
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
          <StudioProblems diagnostics={diagnostics} addressOf={vi.fn()} onClose={vi.fn()} />
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
  it('draws in the side panel frame, in the right column, with its resize handle', () => {
    renderProblems([error])

    const panel = screen.getByRole('complementary', { name: 'Problems' })
    expect(aside).toContainElement(panel)
    expect(within(panel).getByRole('separator', { name: 'Resize panel' })).toBeInTheDocument()
  })

  it('says there are no problems when there is no diagnostic', () => {
    renderProblems([])

    expect(emptyDescription('No problems.')).toBeInTheDocument()
  })

  it('lists a problem and shows no empty state', () => {
    renderProblems([error])

    expect(screen.getByText('AXIS-E101')).toBeInTheDocument()
    expect(screen.queryByText('No problems.')).not.toBeInTheDocument()
  })

  it('shows an error as a danger tag reading "Error" and a warning as an attention tag reading "Warning"', () => {
    renderProblems([error, { ...error, code: 'AXIS-W201', severity: 'warning' }])

    expect(screen.getByText('Error')).toHaveAttribute('data-tone', 'danger')
    expect(screen.getByText('Warning')).toHaveAttribute('data-tone', 'attention')
    expect(screen.queryByText('error')).not.toBeInTheDocument()
  })

  it('stops the panel at its end, so the page does not scroll', () => {
    renderProblems([error])

    const body = screen.getByText('AXIS-E101').closest('ul')!.parentElement!
    expect(body).toHaveStyle({ overflowY: 'auto', overscrollBehavior: 'contain' })
  })
})

describe('StudioProblemList', () => {
  const resourceError: StudioDiagnostic = { ...error, code: 'AXIS-E102', resourceId: 'e1' }
  const resources: StudioResourceItem[] = [
    {
      id: 'e1',
      kind: 'entity',
      name: 'PurchaseRequest',
      file: 'entities/purchase-request.json',
      version: 'a',
      usedBy: 0,
    },
  ]

  it('lists errors before warnings, and a resource with no onSelect is plain text, not a button', () => {
    const warning: StudioDiagnostic = { ...error, code: 'AXIS-W201', severity: 'warning', resourceId: 'w1' }
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioProblemList diagnostics={[warning, resourceError]} resources={resources} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const codes = screen.getAllByRole('listitem').map((item) => item.querySelector('strong')?.textContent)
    expect(codes).toEqual(['AXIS-E102', 'AXIS-W201'])
    expect(screen.getByText('PurchaseRequest')).toBeInTheDocument()
    expect(screen.getByText('Entity')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('makes the resource of a problem a link named by its title when onSelect is given', async () => {
    const onSelect = vi.fn()
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioProblemList diagnostics={[resourceError]} resources={resources} addressOf={onSelect} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'PurchaseRequest' }))
    expect(onSelect).toHaveBeenCalledWith('e1', '/fields/0')
  })

  it('shows the file of a problem with no resource in muted text, and no button', () => {
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioProblemList diagnostics={[error]} resources={resources} addressOf={vi.fn()} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const item = screen.getByRole('listitem')
    expect(within(item).getByText('entities/purchase-request.json')).toBeInTheDocument()
    expect(within(item).queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByText(/Path:/)).not.toBeInTheDocument()
  })

  it('makes the file of a problem with no resource a link named by its file and line when onOpenFile is given', async () => {
    const onOpenFile = vi.fn()
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioProblemList
            diagnostics={[{ ...error, file: 'packages/tools/lib/x.py', line: 3 }, error]}
            resources={resources}
            fileAddressOf={onOpenFile}
          />
        </TextProvider>
      </ThemeModeProvider>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'packages/tools/lib/x.py, line 3' }))
    expect(onOpenFile).toHaveBeenCalledWith('packages/tools/lib/x.py', 3)

    await userEvent.click(screen.getByRole('button', { name: 'entities/purchase-request.json' }))
    expect(onOpenFile).toHaveBeenLastCalledWith('entities/purchase-request.json', undefined)
  })
})
