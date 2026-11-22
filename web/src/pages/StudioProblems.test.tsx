import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor } from '../platform/designTokens'
import { studioFileAddress, studioResourceAddress } from '../platform/studioAddress'
import { Shell } from '../platform/Shell'
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
function renderProblems(
  diagnostics: StudioDiagnostic[],
  fileAddressOf?: (file: string, line?: number) => string | undefined,
) {
  aside = document.createElement('div')
  document.body.append(aside)
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ShellAsideContext.Provider value={{ target: aside, claim: () => () => {} }}>
            <StudioProblems
              diagnostics={diagnostics}
              addressOf={(id, path) => studioResourceAddress('Shop', id, path)}
              fileAddressOf={fileAddressOf}
              onClose={vi.fn()}
            />
          </ShellAsideContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
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

  it("links a package file's problem to its address, and draws another file's problem as muted text", () => {
    renderProblems([{ ...error, file: 'packages/tools/lib/x.py', line: 3 }, error], (file, line) =>
      studioFileAddress('Shop', file, line),
    )

    expect(screen.getByRole('link', { name: 'packages/tools/lib/x.py, line 3' })).toHaveAttribute(
      'href',
      '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py&line=3',
    )
    expect(screen.queryByRole('link', { name: 'entities/purchase-request.json' })).not.toBeInTheDocument()
    expect(screen.getByText('entities/purchase-request.json')).toHaveStyle({
      color: designTokensFor('light').colour.textMuted,
    })
  })

  it('stops the panel at its end, so the page does not scroll', () => {
    renderProblems([error])

    const body = screen.getByText('AXIS-E101').closest('ul')!.parentElement!
    expect(body).toHaveStyle({ overflowY: 'auto', overscrollBehavior: 'contain' })
  })
})

describe('StudioProblems in the shell', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    localStorage.removeItem('axis.themeMode')
  })

  it("draws a problem's code and message in the dark text token of the shell", async () => {
    localStorage.setItem('axis.themeMode', 'dark')
    // The shell asks for the version and the account, and nobody is signed in.
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input === '/api/about'
          ? new Response(null, { status: 401 })
          : new Response(JSON.stringify({ title: 'Not found', status: 404 }), { status: 404 }),
      ),
    )
    render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider
            catalogs={[{ texts: { ...platformEnglish, 'site.title': 'Workspace' }, fallbackTexts: platformEnglish }]}
            development={false}
          >
            <Shell titleKey="site.title" navigation={[]} locales={['en']} locale="en" onLocaleChange={() => {}}>
              <StudioProblems diagnostics={[error]} onClose={vi.fn()} />
            </Shell>
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )

    // The panel draws in the shell's right column once the shell has mounted its target.
    await screen.findByRole('complementary', { name: 'Problems' })
    const line = screen.getByText('AXIS-E101').parentElement!
    expect(screen.getByText('AXIS-E101')).toHaveStyle({ color: designTokensFor('dark').colour.text })
    expect(line).toHaveStyle({ color: designTokensFor('dark').colour.text })
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

  it('lists errors before warnings, and a resource with no addressOf is plain text, not a link', () => {
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
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it("links the resource of a problem to its address at the problem's path, named by its title", async () => {
    render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioProblemList
              diagnostics={[resourceError]}
              resources={resources}
              addressOf={(id, path) => studioResourceAddress('Shop', id, path)}
            />
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'PurchaseRequest' })).toHaveAttribute(
      'href',
      '/studio?app=Shop&resource=e1&path=%2Ffields%2F0',
    )
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it("draws a problem's severity tag outside its text column, which holds the code, the message and the link", () => {
    render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioProblemList
              diagnostics={[resourceError]}
              resources={resources}
              addressOf={(id, path) => studioResourceAddress('Shop', id, path)}
            />
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )

    const item = screen.getByRole('listitem')
    const tag = within(item).getByText('Error').closest<HTMLElement>('[data-tone]')!
    const text = within(item).getByTestId('studio-problem-text')
    expect(text).not.toContainElement(tag)
    expect(tag.nextElementSibling).toBe(text)
    expect(within(text).getByText('AXIS-E102')).toBeInTheDocument()
    expect(within(text).getByText('The field has no type.')).toBeInTheDocument()
    expect(within(text).getByRole('link', { name: 'PurchaseRequest' })).toBeInTheDocument()
  })

  it('shows the file of a problem with no resource in muted text, and no link', () => {
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioProblemList diagnostics={[error]} resources={resources} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const item = screen.getByRole('listitem')
    expect(within(item).getByText('entities/purchase-request.json')).toBeInTheDocument()
    expect(within(item).queryByRole('link')).not.toBeInTheDocument()
    expect(within(item).queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByText(/Path:/)).not.toBeInTheDocument()
  })

  it('makes the file of a problem with no resource a link to its address when fileAddressOf gives one', () => {
    render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioProblemList
              diagnostics={[{ ...error, file: 'packages/tools/lib/x.py', line: 3 }, error]}
              resources={resources}
              fileAddressOf={(file, line) => studioFileAddress('Shop', file, line)}
            />
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'packages/tools/lib/x.py, line 3' })).toHaveAttribute(
      'href',
      '/studio?app=Shop&view=packages&package=tools&file=lib%2Fx.py&line=3',
    )
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
