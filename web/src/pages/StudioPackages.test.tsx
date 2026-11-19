import { ConfigProvider } from 'antd'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { packageStarter } from '../platform/packageStarters'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { chooseOption, hasFieldError } from '../test/antd'
import { StudioNewPackage } from './StudioNewPackage'
import { StudioPackagePage, StudioPackages } from './StudioPackages'

const files = '/api/studio/apps/Shop/package-files'
const fileRoute = (pkg: string, path: string) => `/api/studio/apps/Shop/packages/${pkg}/files/${path}`

const manifest = (id: string, language: string, label: string, version: string) =>
  JSON.stringify({ id, language, label, version })

afterEach(() => vi.unstubAllGlobals())

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs the list of two packages, `tools` with a manifest and `broken` with a manifest that is not JSON, and their reads. */
function stubPackages() {
  const fetchMock = vi.fn(async (input: string): Promise<Response> => {
    if (input === files) {
      return json({
        items: [
          { package: 'broken', path: 'axis-package.json', version: 'v1' },
          { package: 'broken', path: 'index.js', version: 'v2' },
          { package: 'tools', path: 'adapter.py', version: 'v3' },
          { package: 'tools', path: 'axis-package.json', version: 'v4' },
        ],
      })
    }
    if (input === fileRoute('broken', 'axis-package.json')) {
      return json({ package: 'broken', path: 'axis-package.json', version: 'v1', content: '{ not json' })
    }
    if (input === fileRoute('tools', 'axis-package.json')) {
      return json({
        package: 'tools',
        path: 'axis-package.json',
        version: 'v4',
        content: manifest('tools', 'python', 'Purchasing tools', '1.2.0'),
      })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderInRouter(content: React.ReactNode) {
  return render(
    // Without motion a closed dialog leaves the document at once, as jsdom runs no animations.
    <ConfigProvider theme={{ token: { motion: false } }}>
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            {content}
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>
    </ConfigProvider>,
  )
}

describe('StudioPackages', () => {
  it('lists one row for each package folder with its label, id, language and version from its manifest', async () => {
    stubPackages()
    renderInRouter(<StudioPackages application="Shop" folderRevision={0} />)

    const table = await screen.findByTestId('studio-packages')
    const tools = (await within(table).findByTestId('studio-package-tools')).closest('tr')!
    expect(
      within(tools)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['Purchasing tools', 'tools', 'Python', '1.2.0'])
    expect(within(tools).getByRole('link', { name: 'Purchasing tools' })).toHaveAttribute(
      'href',
      '/studio?app=Shop&view=packages&package=tools',
    )
    // A manifest that is not JSON still gives a row, with the id as the label.
    const broken = within(table).getByTestId('studio-package-broken').closest('tr')!
    expect(
      within(broken)
        .getAllByRole('cell')
        .map((cell) => cell.textContent),
    ).toEqual(['broken', 'broken', '—', '—'])
    expect(within(table).getAllByTestId(/^studio-package-/)).toHaveLength(2)
  })

  it('shows the empty text for an application with no package', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json({ items: [] })),
    )
    renderInRouter(<StudioPackages application="Shop" folderRevision={0} />)

    expect(await screen.findByText('The application has no packages yet.')).toBeInTheDocument()
  })

  it('shows the load failure', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json({ title: 'No.' }, 500)),
    )
    renderInRouter(<StudioPackages application="Shop" folderRevision={0} />)

    expect(await screen.findByTestId('studio-packages-load-error')).toHaveTextContent(
      'The packages could not be loaded.',
    )
    warn.mockRestore()
  })
})

describe('StudioPackagePage', () => {
  it("is headed with the package's label and lists its files by path", async () => {
    stubPackages()
    renderInRouter(<StudioPackagePage application="Shop" packageId="tools" folderRevision={0} />)

    expect(await screen.findByRole('heading', { level: 4, name: 'Purchasing tools' })).toBeInTheDocument()
    const table = screen.getByTestId('studio-package-files')
    expect(
      within(table)
        .getAllByRole('row')
        .slice(1)
        .map((row) => row.textContent),
    ).toEqual(['adapter.py', 'axis-package.json'])
  })

  it('shows the id as the heading of a package whose manifest is broken', async () => {
    stubPackages()
    renderInRouter(<StudioPackagePage application="Shop" packageId="broken" folderRevision={0} />)

    expect(await screen.findByRole('heading', { level: 4, name: 'broken' })).toBeInTheDocument()
  })

  it('warns when the application has no such package', async () => {
    stubPackages()
    renderInRouter(<StudioPackagePage application="Shop" packageId="nothing" folderRevision={0} />)

    expect(await screen.findByTestId('studio-package-missing')).toHaveTextContent(
      'The application has no package with this ID.',
    )
  })
})

describe('StudioNewPackage', () => {
  type Write = { path: string; ifNoneMatch: string | null; body: { content: string } }

  /** Stubs the package file routes. `answer` gives each write's status, by the order of the writes. */
  function stubWrites(answer: (index: number) => number = () => 200) {
    const writes: Write[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string, init?: RequestInit) => {
        const index = writes.length
        writes.push({
          path: input.slice(input.indexOf('/files/') + '/files/'.length),
          ifNoneMatch: new Headers(init!.headers).get('If-None-Match'),
          body: JSON.parse(init!.body as string),
        })
        const status = answer(index)
        return status === 200 ? json({ version: `v${index}`, diagnostics: [] }) : json({ title: 'No.' }, status)
      }),
    )
    return writes
  }

  async function fill(id: string, language: string | null) {
    await userEvent.click(await screen.findByRole('button', { name: 'New package…' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'ID' }), id)
    if (language !== null) {
      await chooseOption(
        language,
        screen.getByRole('combobox', { name: 'Language' }).closest('.ant-form-item') as HTMLElement,
      )
    }
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
  }

  it("writes each file of the language's starter with If-None-Match, the manifest first, and calls onCreated", async () => {
    const writes = stubWrites()
    const onCreated = vi.fn()
    renderInRouter(<StudioNewPackage application="Shop" onCreated={onCreated} />)

    await fill('purchasing-tools', 'TypeScript')

    const starter = packageStarter('node', 'purchasing-tools')
    await waitFor(() => expect(onCreated).toHaveBeenCalledTimes(1))
    expect(writes.map((write) => write.path)).toEqual([
      'axis-package.json',
      'index.js',
      'package.json',
      'package-lock.json',
    ])
    expect([...writes.map((write) => write.path)].sort()).toEqual(Object.keys(starter).sort())
    for (const write of writes) {
      expect(write.ifNoneMatch).toBe('*')
      expect(write.body.content).toBe(starter[write.path])
    }
    expect(onCreated).toHaveBeenCalledWith([])
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('writes a C# starter under the package route, with its project file named after the id', async () => {
    const writes = stubWrites()
    renderInRouter(<StudioNewPackage application="Shop" onCreated={vi.fn()} />)

    await fill('purchasing-tools', 'C#')

    await waitFor(() => expect(writes.length).toBeGreaterThan(1))
    expect(writes[0].path).toBe('axis-package.json')
    expect(writes.map((write) => write.path)).toContain('PurchasingTools.csproj')
  })

  it('shows an existing id under the id field, keeps the dialog open and sends no other file', async () => {
    const writes = stubWrites(() => 409)
    const onCreated = vi.fn()
    renderInRouter(<StudioNewPackage application="Shop" onCreated={onCreated} />)

    await fill('tools', 'Python')

    expect(await screen.findByText('A package with this ID already exists.')).toBeInTheDocument()
    expect(hasFieldError(screen.getByRole('textbox', { name: 'ID' }))).toBe(true)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(writes.map((write) => write.path)).toEqual(['axis-package.json'])
    expect(onCreated).not.toHaveBeenCalled()
  })

  it('refuses an id the manifest schema does not allow before it sends anything', async () => {
    const writes = stubWrites()
    renderInRouter(<StudioNewPackage application="Shop" onCreated={vi.fn()} />)

    await fill('Tools', 'Python')

    expect(
      await screen.findByText('Use lower-case letters, digits and hyphens, and start with a letter.'),
    ).toBeInTheDocument()
    expect(writes).toHaveLength(0)
  })

  it('asks for the id and the language on Create and sends nothing', async () => {
    const writes = stubWrites()
    renderInRouter(<StudioNewPackage application="Shop" onCreated={vi.fn()} />)

    await userEvent.click(await screen.findByRole('button', { name: 'New package…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText('Enter an ID.')).toBeInTheDocument()
    expect(screen.getByText('Choose a language.')).toBeInTheDocument()
    expect(writes).toHaveLength(0)
  })

  it('keeps the dialog open on a failure after the manifest, and still tells the page so the list loads again', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubWrites((index) => (index === 0 ? 200 : 500))
    const onCreated = vi.fn()
    renderInRouter(<StudioNewPackage application="Shop" onCreated={onCreated} />)

    await fill('tools', 'Python')

    expect(
      await screen.findByText('The package could not be created. Some of its files may have been written.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(onCreated).toHaveBeenCalledTimes(1)
    warn.mockRestore()
  })
})
