import { EditorView } from '@codemirror/view'
import { ConfigProvider } from 'antd'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioPackageWorkspace } from './StudioPackageWorkspace'

const files = '/api/studio/apps/Shop/package-files'
const fileRoute = (path: string) => `/api/studio/apps/Shop/packages/tools/files/${path}`
const contents: Record<string, string> = {
  'src/b.ts': 'const b = 1',
  'src/a.cs': 'class A {}',
  'lib/x.py': 'x = 1',
  'adapter.py': 'print(1)',
  'axis-package.json': JSON.stringify({ id: 'tools', label: 'Purchasing tools', version: '1.2.0' }),
}

afterEach(() => vi.unstubAllGlobals())

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs a package `tools` with five files, and answers a file's read from `contents`. */
function stubPackage() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string): Promise<Response> => {
      if (input === files) {
        return json({ items: Object.keys(contents).map((path) => ({ package: 'tools', path, version: `v-${path}` })) })
      }
      const path = Object.keys(contents).find((candidate) => input === fileRoute(candidate))
      return path === undefined
        ? new Response(null, { status: 404 })
        : json({ package: 'tools', path, version: `v-${path}`, content: contents[path] })
    }),
  )
}

function renderWorkspace(canDevelop: boolean, packageId = 'tools') {
  return render(
    // Without motion a closed dialog leaves the document at once, as jsdom runs no animations.
    <ConfigProvider theme={{ token: { motion: false } }}>
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <footer />
            <StudioPackageWorkspace
              application="Shop"
              packageId={packageId}
              folderRevision={0}
              canDevelop={canDevelop}
            />
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>
    </ConfigProvider>,
  )
}

const treeNames = () =>
  [...within(screen.getByTestId('studio-package-tree')).getByRole('tree').querySelectorAll('.ant-tree-title')].map(
    (title) => title.textContent,
  )

/** Opens a file from the tree and waits for its editor. */
async function openFile(name: string, path: string) {
  await userEvent.click(within(screen.getByTestId('studio-package-tree')).getByText(name, { exact: true }))
  return EditorView.findFromDOM(await screen.findByRole('textbox', { name: path }))!
}

const typeAtStart = (view: EditorView) =>
  act(() => view.dispatch({ changes: { from: 0, insert: 'x' }, selection: { anchor: 1 }, userEvent: 'input.type' }))

describe('StudioPackageWorkspace', () => {
  it('shows the explorer with folders first, each group by name, under the label and the kind and version', async () => {
    stubPackage()
    renderWorkspace(true)

    expect(await screen.findByRole('heading', { level: 1, name: 'Purchasing tools' })).toBeInTheDocument()
    expect(screen.getByText('Extension package, version 1.2.0')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Files' })).toBeInTheDocument()
    expect(treeNames()).toEqual(['lib', 'x.py', 'src', 'a.cs', 'b.ts', 'adapter.py', 'axis-package.json'])
    expect(screen.getByText('No file is open.')).toBeInTheDocument()
  })

  it('warns when the application has no such package', async () => {
    stubPackage()
    renderWorkspace(true, 'nothing')

    expect(await screen.findByTestId('studio-package-missing')).toHaveTextContent(
      'The application has no package with this ID.',
    )
  })

  it('marks an edited file on its tab and its row, and the status line tells the cursor, language and unsaved files', async () => {
    stubPackage()
    renderWorkspace(true)
    await screen.findByTestId('studio-package-tree')
    const status = screen.getByTestId('studio-package-status')
    expect(status).toHaveTextContent('No unsaved files')

    const view = await openFile('b.ts', 'src/b.ts')
    expect(within(status).getByText('Line 1, column 1')).toBeInTheDocument()
    expect(within(status).getByText('TypeScript')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-package-unsaved')).not.toBeInTheDocument()

    typeAtStart(view)

    const dots = await screen.findAllByTestId('studio-package-unsaved')
    expect(dots).toHaveLength(2)
    expect(within(screen.getByRole('tab', { name: /b\.ts/ })).getByTestId('studio-package-unsaved')).toBeInTheDocument()
    expect(within(screen.getByTestId('studio-package-tree')).getByTestId('studio-package-unsaved')).toBeInTheDocument()
    expect(within(status).getByText('Line 1, column 2')).toBeInTheDocument()
    expect(within(status).getByText('1 unsaved file')).toBeInTheDocument()
  })

  it('asks before it closes an edited tab, keeps the text on Cancel and drops it on Discard', async () => {
    stubPackage()
    renderWorkspace(true)
    await screen.findByTestId('studio-package-tree')
    const view = await openFile('b.ts', 'src/b.ts')
    typeAtStart(view)
    await screen.findAllByTestId('studio-package-unsaved')

    await userEvent.click(screen.getByRole('button', { name: 'Close src/b.ts' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Discard the unsaved changes in src/b.ts?')).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(view.state.doc.toString()).toBe('xconst b = 1')
    expect(screen.getByRole('tab', { name: /b\.ts/ })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Close src/b.ts' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Discard' }))

    await waitFor(() => expect(screen.queryByRole('tab', { name: /b\.ts/ })).not.toBeInTheDocument())
    expect(screen.getByText('No file is open.')).toBeInTheDocument()
    expect(screen.getByTestId('studio-package-status')).toHaveTextContent('No unsaved files')

    // Opened again, the file is read as it was saved.
    expect((await openFile('b.ts', 'src/b.ts')).state.doc.toString()).toBe('const b = 1')
  })

  it('closes a file with no changes at once', async () => {
    stubPackage()
    renderWorkspace(true)
    await screen.findByTestId('studio-package-tree')
    await openFile('b.ts', 'src/b.ts')

    await userEvent.click(screen.getByRole('button', { name: 'Close src/b.ts' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: /b\.ts/ })).not.toBeInTheDocument()
  })

  it('is read-only with an info alert without the developer role', async () => {
    stubPackage()
    renderWorkspace(false)
    await screen.findByTestId('studio-package-tree')
    const view = await openFile('b.ts', 'src/b.ts')

    typeAtStart(view)

    expect(view.state.readOnly).toBe(true)
    expect(view.state.doc.toString()).toBe('const b = 1')
    expect(screen.getByTestId('studio-package-read-only')).toHaveTextContent(
      'You can read these files. To edit them, you need the developer role.',
    )
    expect(screen.getByTestId('studio-package-status')).toHaveTextContent('No unsaved files')
  })

  it('accepts typing and shows no alert with the developer role', async () => {
    stubPackage()
    renderWorkspace(true)
    await screen.findByTestId('studio-package-tree')
    const view = await openFile('b.ts', 'src/b.ts')

    typeAtStart(view)

    expect(view.state.readOnly).toBe(false)
    expect(view.state.doc.toString()).toBe('xconst b = 1')
    expect(screen.queryByTestId('studio-package-read-only')).not.toBeInTheDocument()
  })
})
