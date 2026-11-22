import { EditorView } from '@codemirror/view'
import { ConfigProvider } from 'antd'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioDiagnostic, StudioRunInput } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioPackageWorkspace } from './StudioPackageWorkspace'

const files = '/api/studio/apps/Shop/package-files'
const fileRoutePrefix = '/api/studio/apps/Shop/packages/tools/files/'
const fileRoute = (path: string) => `${fileRoutePrefix}${path}`
const moveRoute = '/api/studio/apps/Shop/packages/tools/move'
const contents: Record<string, string> = {
  'src/b.ts': 'const b = 1',
  'src/a.cs': 'class A {}',
  'lib/x.py': 'x = 1\ny = 2\nz = 3',
  'adapter.py': 'print(1)',
  'axis-package.json': JSON.stringify({
    id: 'tools',
    label: 'Purchasing tools',
    version: '1.2.0',
    operations: [
      {
        name: 'greet',
        type: 'greet',
        idempotent: true,
        input: [
          { name: 'name', type: 'text', required: true },
          { name: 'count', type: 'integer' },
        ],
      },
    ],
  }),
}
const runInputs = '/api/studio/apps/Shop/packages/tools/run-inputs'
const runRoute = '/api/studio/apps/Shop/run'
const requestRoute = '/api/studio/apps/Shop/requests/r1'

/** Every request of the last stubbed package, in order, with its method, URL, headers and body. */
let requests: { method: string; url: string; headers: Headers; body: unknown }[] = []
const callsTo = (method: string, url: string) => requests.filter((call) => call.method === method && call.url === url)

afterEach(() => vi.unstubAllGlobals())

const problem = (file: string, extra: Partial<StudioDiagnostic> = {}): StudioDiagnostic => ({
  code: 'AXC0132',
  severity: 'error',
  message: `Problem in ${file}`,
  file,
  path: '',
  resourceId: null,
  ...extra,
})

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const buildRoute = '/api/studio/apps/Shop/build'
const queuedBuild = { id: 'b1', application: 'Shop', kind: 'build', status: 'queued', package: 'tools', output: [] }
const failedBuild = {
  ...queuedBuild,
  status: 'failed',
  output: ['Compiled 1 Python file.', 'lib/x.py: invalid syntax'],
  diagnostics: [problem('packages/tools/lib/x.py', { line: 2, column: 1 })],
}

/** The requests of the last stubbed package that are not reads: the explorer's creates, moves and deletes. */
const writesSent = () => requests.filter((call) => call.method !== 'GET')

const testRoute = '/api/studio/apps/Shop/test'
const queuedTest = { id: 't1', application: 'Shop', kind: 'test', status: 'queued', package: 'tools', output: [] }
const failedTest = {
  ...queuedTest,
  status: 'failed',
  diagnostics: [],
  scenarios: null,
  packageTests: [
    { name: 'test_adds', outcome: 'passed', message: null, durationMs: 12 },
    {
      name: 'test_greets',
      outcome: 'failed',
      message: 'AssertionError: Hello Ada != Hi Ada',
      durationMs: 30,
      file: 'lib/x.py',
      line: 3,
      column: 5,
    },
  ],
  output: ['collected 2 tests', '1 failed, 1 passed'],
}

/**
 * Stubs a package `tools` with five files, and answers a file's read from a copy of `contents` that moves, deletes and
 * creates change, and the list of its files. A move answers `{ diagnostics: [] }`, and a delete too. A write answers
 * `{ version: 'new-<path>' }` unless `writes` has a response for its path. The writes are recorded in `puts`. Each write
 * and each build post is also recorded in `calls`, in order, as `PUT <path>` and `POST build <body>`. A build post answers
 * the queued build `b1`, and its read answers `failedBuild` unless `readBuild` answers otherwise. A test post is
 * recorded as `POST test <body>` and answers the queued package test `t1`, and its read answers `failedTest` unless
 * `readTest` answers otherwise. A move answers `moveStatus` with a problem when it is not 200, and moves nothing. The
 * package's run inputs start as `saved` at version `v0`, or have no version before the first save, and a write with a wrong version
 * answers `409`. A run answers `202`, and its request has ended with the run `greet`. Every request is recorded in
 * `requests`, and `writesSent` has those that are not reads.
 */
function stubPackage(
  writes: Record<string, () => Response> = {},
  calls: string[] = [],
  readBuild: () => Response = () => json(failedBuild),
  saved: StudioRunInput[] | null = null,
  readTest: () => Response = () => json(failedTest),
  moveStatus = 200,
) {
  const puts: { path: string; ifMatch: string | null; body: { content: string } }[] = []
  const store = { ...contents }
  let stored = { items: saved ?? [], version: saved === null ? null : 0 }
  requests = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init?: RequestInit): Promise<Response> => {
      const headers = new Headers(init?.headers)
      requests.push({
        method: init?.method ?? 'GET',
        url: input,
        headers,
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      })
      if (input === runInputs && init?.method === 'PUT') {
        const current = stored.version === null ? null : `"v${stored.version}"`
        if (headers.get('If-None-Match') === '*' ? current !== null : headers.get('If-Match') !== current) {
          return json({ title: 'Changed.' }, 409)
        }
        const version = (stored.version ?? -1) + 1
        stored = { items: (JSON.parse(init.body as string) as { items: StudioRunInput[] }).items, version }
        return json({ version: `v${version}` })
      }
      if (input === runInputs) {
        return new Response(JSON.stringify({ items: stored.items }), {
          status: 200,
          headers: {
            'Content-Type': 'application/json',
            ...(stored.version === null ? {} : { ETag: `"v${stored.version}"` }),
          },
        })
      }
      if (input === runRoute) {
        return json({ id: 'r1', status: 'queued', kind: 'run' }, 202)
      }
      if (input === requestRoute) {
        return json({
          id: 'r1',
          kind: 'run',
          status: 'succeeded',
          diagnostics: [],
          run: {
            operation: 'greet',
            outcome: 'succeeded',
            output: { greeting: 'Hello Ada' },
            error: null,
            durationMs: 1234,
          },
          output: ['hello from greet', 'done'],
        })
      }
      if (input === buildRoute) {
        calls.push(`POST build ${init?.body as string}`)
        return json(queuedBuild, 202)
      }
      if (input === '/api/studio/apps/Shop/requests/b1') {
        return readBuild()
      }
      if (input === testRoute) {
        calls.push(`POST test ${init?.body as string}`)
        return json(queuedTest, 202)
      }
      if (input === '/api/studio/apps/Shop/requests/t1') {
        return readTest()
      }
      if (input === files) {
        return json({ items: Object.keys(store).map((path) => ({ package: 'tools', path, version: `v-${path}` })) })
      }
      if (input === moveRoute) {
        const request = { body: JSON.parse(init?.body as string) }
        if (moveStatus !== 200) {
          return json({ title: 'Changed.' }, moveStatus)
        }
        for (const { from, to } of (request.body as { moves: { from: string; to: string }[] }).moves) {
          store[to] = store[from]
          delete store[from]
        }
        return json({ diagnostics: [] })
      }
      const path = input.startsWith(fileRoutePrefix) ? input.slice(fileRoutePrefix.length) : undefined
      if (path !== undefined && init?.method === 'PUT' && new Headers(init.headers).get('If-None-Match') === '*') {
        store[path] = (JSON.parse(init.body as string) as { content: string }).content
        return json({ version: `v-${path}`, diagnostics: [] })
      }
      if (path !== undefined && init?.method === 'DELETE') {
        delete store[path]
        return json({ diagnostics: [] })
      }
      if (path === undefined || !(path in store)) {
        return new Response(null, { status: 404 })
      }
      if (init?.method === 'PUT') {
        calls.push(`PUT ${path}`)
        puts.push({
          path,
          ifMatch: new Headers(init.headers).get('If-Match'),
          body: JSON.parse(init.body as string) as { content: string },
        })
        return writes[path]?.() ?? json({ version: `new-${path}`, diagnostics: [] })
      }
      return json({ package: 'tools', path, version: `v-${path}`, content: store[path] })
    }),
  )
  return puts
}

function renderWorkspace(canDevelop: boolean, packageId = 'tools', diagnostics: StudioDiagnostic[] = []) {
  const onWritten = vi.fn()
  render(
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
              diagnostics={diagnostics}
              onWritten={onWritten}
            />
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>
    </ConfigProvider>,
  )
  return onWritten
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

  /** Opens `src/b.ts` and `adapter.py` and types at the start of both, so both are unsaved. */
  async function editTwoFiles() {
    await screen.findByTestId('studio-package-tree')
    const b = await openFile('b.ts', 'src/b.ts')
    const adapter = await openFile('adapter.py', 'adapter.py')
    typeAtStart(b)
    typeAtStart(adapter)
    await waitFor(() => expect(screen.getAllByTestId('studio-package-unsaved')).toHaveLength(4))
    return { b, adapter }
  }

  describe('saving', () => {
    it('saves every changed file with the version it was read at on Ctrl+S, and clears each dot', async () => {
      const puts = stubPackage()
      const onWritten = renderWorkspace(true)
      await editTwoFiles()
      expect(screen.getByTestId('studio-package-save-bar')).toBeInTheDocument()

      await userEvent.keyboard('{Control>}s{/Control}')

      await waitFor(() => expect(screen.queryByTestId('studio-package-unsaved')).not.toBeInTheDocument())
      expect(puts).toEqual([
        { path: 'src/b.ts', ifMatch: '"v-src/b.ts"', body: { content: 'xconst b = 1' } },
        { path: 'adapter.py', ifMatch: '"v-adapter.py"', body: { content: 'xprint(1)' } },
      ])
      expect(onWritten).toHaveBeenCalledTimes(1)
      expect(screen.getByTestId('studio-package-status')).toHaveTextContent('No unsaved files')
      await waitFor(() => expect(screen.queryByTestId('studio-package-save-bar')).not.toBeInTheDocument())
    })

    it('saves with the next version, so a second edit is sent with the version of the first save', async () => {
      const puts = stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      const view = await openFile('b.ts', 'src/b.ts')
      typeAtStart(view)
      await userEvent.keyboard('{Control>}s{/Control}')
      await waitFor(() => expect(screen.queryByTestId('studio-package-unsaved')).not.toBeInTheDocument())

      typeAtStart(view)
      await userEvent.keyboard('{Control>}s{/Control}')

      await waitFor(() => expect(puts).toHaveLength(2))
      expect(puts[1].ifMatch).toBe('"new-src/b.ts"')
    })

    it('saves the same files from Save on the save bar', async () => {
      const puts = stubPackage()
      renderWorkspace(true)
      await editTwoFiles()

      await userEvent.click(screen.getByTestId('studio-package-save'))

      await waitFor(() => expect(screen.queryByTestId('studio-package-unsaved')).not.toBeInTheDocument())
      expect(puts.map((put) => put.path)).toEqual(['src/b.ts', 'adapter.py'])
    })

    it('keeps the text and the dot of a file that changed elsewhere, shows its notice, and saves the other file', async () => {
      const puts = stubPackage({ 'src/b.ts': () => json({ title: 'Changed.' }, 409) })
      const onWritten = renderWorkspace(true)
      const { b } = await editTwoFiles()

      await userEvent.keyboard('{Control>}s{/Control}')

      expect(await screen.findByTestId('studio-package-conflict')).toHaveTextContent('Someone else changed this file.')
      expect(puts.map((put) => put.path)).toEqual(['src/b.ts', 'adapter.py'])
      expect(b.state.doc.toString()).toBe('xconst b = 1')
      await waitFor(() => expect(screen.getAllByTestId('studio-package-unsaved')).toHaveLength(2))
      expect(
        within(screen.getByRole('tab', { name: /b\.ts/ })).getByTestId('studio-package-unsaved'),
      ).toBeInTheDocument()
      expect(
        within(screen.getByRole('tab', { name: /adapter\.py/ })).queryByTestId('studio-package-unsaved'),
      ).toBeNull()
      expect(onWritten).toHaveBeenCalledTimes(1)
      expect(screen.getByRole('tab', { name: /b\.ts/ })).toHaveAttribute('aria-selected', 'true')
      expect(screen.getByTestId('studio-package-save')).toBeDisabled()
    })

    it('reads a conflicting file again on Reload and drops the notice and the unsaved text', async () => {
      stubPackage({ 'src/b.ts': () => json({ title: 'Changed.' }, 409) })
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      const view = await openFile('b.ts', 'src/b.ts')
      typeAtStart(view)
      await userEvent.keyboard('{Control>}s{/Control}')

      await userEvent.click(
        within(await screen.findByTestId('studio-package-conflict')).getByRole('button', { name: 'Reload' }),
      )

      await waitFor(() => expect(screen.queryByTestId('studio-package-conflict')).not.toBeInTheDocument())
      await waitFor(() => expect(screen.queryByTestId('studio-package-unsaved')).not.toBeInTheDocument())
      expect(EditorView.findFromDOM(screen.getByRole('textbox', { name: 'src/b.ts' }))!.state.doc.toString()).toBe(
        'const b = 1',
      )
    })

    it('shows the quota refusal under the heading and keeps the dot of a file it did not save', async () => {
      const puts = stubPackage({
        'src/b.ts': () => json({ reason: 'quota-exceeded', title: 'Storage is full.' }, 403),
      })
      const onWritten = renderWorkspace(true)
      await editTwoFiles()

      await userEvent.keyboard('{Control>}s{/Control}')

      expect(await screen.findByTestId('studio-package-save-error')).toHaveTextContent('Storage is full.')
      expect(puts.map((put) => put.path)).toEqual(['src/b.ts'])
      expect(screen.getAllByTestId('studio-package-unsaved')).toHaveLength(4)
      expect(onWritten).not.toHaveBeenCalled()
    })

    it('puts every unsaved file back to its saved text on Discard, after it asks', async () => {
      const puts = stubPackage()
      renderWorkspace(true)
      const { b, adapter } = await editTwoFiles()

      await userEvent.click(
        within(screen.getByTestId('studio-package-save-bar')).getByRole('button', { name: 'Discard' }),
      )
      await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Discard' }))

      await waitFor(() => expect(screen.queryByTestId('studio-package-unsaved')).not.toBeInTheDocument())
      expect(b.state.doc.toString()).toBe('const b = 1')
      expect(adapter.state.doc.toString()).toBe('print(1)')
      expect(puts).toEqual([])
    })

    it('shows no save bar and sends nothing on Ctrl+S without the developer role', async () => {
      const puts = stubPackage()
      renderWorkspace(false)
      await screen.findByTestId('studio-package-tree')
      const view = await openFile('b.ts', 'src/b.ts')
      typeAtStart(view)

      await userEvent.keyboard('{Control>}s{/Control}')

      expect(screen.queryByTestId('studio-package-save-bar')).not.toBeInTheDocument()
      expect(puts).toEqual([])
    })
  })

  describe('the explorer’s menus', () => {
    const tree = () => screen.getByTestId('studio-package-tree')
    const menuNames = async () => (await screen.findAllByRole('menuitem')).map((item) => item.textContent)
    const filesPane = () => screen.getByRole('region', { name: 'Files' })
    /** Opens a node's menu with a right click and chooses `item`. */
    async function choose(node: string, item: string) {
      fireEvent.contextMenu(within(tree()).getByText(node, { exact: true }))
      await userEvent.click(await screen.findByRole('menuitem', { name: item }))
    }
    /** Types `name` in the open name dialog and sends it. */
    async function submitName(name: string, button: string) {
      const dialog = await screen.findByRole('dialog')
      const field = within(dialog).getByRole('textbox')
      await userEvent.clear(field)
      await userEvent.type(field, name)
      await userEvent.click(within(dialog).getByRole('button', { name: button }))
    }

    it('offers New file…, New folder…, Rename…, Delete… and Copy path on a right click, and Rename… on F2 with the name filled in', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      fireEvent.contextMenu(within(tree()).getByText('src', { exact: true }))
      expect(await menuNames()).toEqual(['New file…', 'New folder…', 'Rename…', 'Delete…', 'Copy path'])
      await userEvent.keyboard('{Escape}')

      await userEvent.click(within(tree()).getByText('src', { exact: true }))
      fireEvent.keyDown(tree(), { key: 'F2' })

      const dialog = await screen.findByRole('dialog', { name: 'Rename src' })
      expect(within(dialog).getByRole('textbox')).toHaveValue('src')
    })

    it('opens the node’s menu on Shift+F10 and on the menu key', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      await userEvent.click(within(tree()).getByText('adapter.py', { exact: true }))

      fireEvent.keyDown(tree(), { key: 'F10', shiftKey: true })
      expect(await menuNames()).toHaveLength(5)
      await userEvent.keyboard('{Escape}')
      await waitFor(() => expect(screen.queryByRole('menuitem')).not.toBeInTheDocument())

      fireEvent.keyDown(tree(), { key: 'ContextMenu' })
      expect(await menuNames()).toHaveLength(5)
    })

    it('has New and Refresh in the title row with the developer role, and each a name as its tooltip', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      expect(
        within(filesPane())
          .getAllByRole('button')
          .map((button) => button.getAttribute('aria-label')),
      ).toEqual(['New', 'Refresh'])
      await userEvent.click(within(filesPane()).getByRole('button', { name: 'New' }))
      expect(await menuNames()).toEqual(['New file…', 'New folder…'])
    })

    it('offers only Copy path and Refresh without the developer role, and F2 does nothing', async () => {
      stubPackage()
      renderWorkspace(false)
      await screen.findByTestId('studio-package-tree')

      expect(
        within(filesPane())
          .getAllByRole('button')
          .map((button) => button.getAttribute('aria-label')),
      ).toEqual(['Refresh'])
      fireEvent.contextMenu(within(tree()).getByText('src', { exact: true }))
      expect(await menuNames()).toEqual(['Copy path'])
      await userEvent.keyboard('{Escape}')

      await userEvent.click(within(tree()).getByText('adapter.py', { exact: true }))
      fireEvent.keyDown(tree(), { key: 'F2' })
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    it('copies packages/<id>/<path> on Copy path', async () => {
      stubPackage()
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
      renderWorkspace(false)
      await screen.findByTestId('studio-package-tree')

      await choose('a.cs', 'Copy path')

      await waitFor(() => expect(writeText).toHaveBeenCalledWith('packages/tools/src/a.cs'))
    })

    it('reads the list again on Refresh', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      const listed = () => vi.mocked(fetch).mock.calls.filter(([url]) => url === files).length
      const before = listed()

      await userEvent.click(
        within(screen.getByRole('region', { name: 'Files' })).getByRole('button', { name: 'Refresh' }),
      )

      await waitFor(() => expect(listed()).toBe(before + 1))
    })

    it('renames a folder with one move that lists each of its files with its version, and shows them under the new name', async () => {
      stubPackage()
      const onWritten = renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await choose('src', 'Rename…')
      await submitName('code', 'Rename')

      await waitFor(() =>
        expect(treeNames()).toEqual(['code', 'a.cs', 'b.ts', 'lib', 'x.py', 'adapter.py', 'axis-package.json']),
      )
      expect(writesSent()).toHaveLength(1)
      expect(writesSent()[0].method).toBe('POST')
      expect(writesSent()[0].url).toBe(moveRoute)
      expect(writesSent()[0].body).toEqual({
        moves: [
          { from: 'src/a.cs', to: 'code/a.cs', version: 'v-src/a.cs' },
          { from: 'src/b.ts', to: 'code/b.ts', version: 'v-src/b.ts' },
        ],
      })
      expect(onWritten).toHaveBeenCalledWith([])
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    })

    it('keeps the unsaved text of an open file that moves, under its new path', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      typeAtStart(await openFile('b.ts', 'src/b.ts'))
      await screen.findAllByTestId('studio-package-unsaved')

      await choose('src', 'Rename…')
      await submitName('code', 'Rename')

      const editor = await screen.findByRole('textbox', { name: 'code/b.ts' })
      expect(EditorView.findFromDOM(editor)!.state.doc.toString()).toBe('xconst b = 1')
      expect(writesSent()[0].body).toEqual({
        moves: [
          { from: 'src/a.cs', to: 'code/a.cs', version: 'v-src/a.cs' },
          { from: 'src/b.ts', to: 'code/b.ts', version: 'v-src/b.ts' },
        ],
      })
      expect(
        within(screen.getByRole('tab', { name: /b\.ts/ })).getByTestId('studio-package-unsaved'),
      ).toBeInTheDocument()
    })

    it('renames a file with one move', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await choose('adapter.py', 'Rename…')
      await submitName('a.py', 'Rename')

      await waitFor(() => expect(treeNames()).toContain('a.py'))
      expect(writesSent()[0].body).toEqual({ moves: [{ from: 'adapter.py', to: 'a.py', version: 'v-adapter.py' }] })
    })

    it('says in the dialog that the name is taken or a file changed on a 409, and reads the list again', async () => {
      stubPackage({}, [], undefined, null, undefined, 409)
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      const listed = () => vi.mocked(fetch).mock.calls.filter(([url]) => url === files).length
      const before = listed()

      await choose('src', 'Rename…')
      await submitName('code', 'Rename')

      expect(await within(await screen.findByRole('dialog')).findByText(/This name is taken/)).toBeInTheDocument()
      await waitFor(() => expect(listed()).toBe(before + 1))
      expect(treeNames()).toContain('src')
    })

    it('says the name is taken for a folder renamed onto an existing folder, and sends nothing', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await choose('src', 'Rename…')
      await submitName('lib', 'Rename')

      expect(await within(await screen.findByRole('dialog')).findByText(/This name is taken/)).toBeInTheDocument()
      expect(writesSent()).toEqual([])
      expect(treeNames()).toContain('src')
    })

    it('shows the problem in the dialog for a refused name and sends nothing', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await choose('adapter.py', 'Rename…')
      await submitName('a/b.py', 'Rename')

      expect(await within(await screen.findByRole('dialog')).findByText(/This name is not allowed/)).toBeInTheDocument()
      expect(writesSent()).toEqual([])
    })

    it('adds an empty folder with New folder…, and New file… in it creates the file with If-None-Match', async () => {
      stubPackage()
      const onWritten = renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(within(screen.getByRole('region', { name: 'Files' })).getByRole('button', { name: 'New' }))
      await userEvent.click(await screen.findByRole('menuitem', { name: 'New folder…' }))
      await submitName('lib2', 'Create')

      await waitFor(() => expect(treeNames()).toContain('lib2'))
      expect(writesSent()).toEqual([])

      await choose('lib2', 'New file…')
      await submitName('x.ts', 'Create')

      await waitFor(() => expect(writesSent()).toHaveLength(1))
      expect(writesSent()[0].method).toBe('PUT')
      expect(writesSent()[0].url).toBe(fileRoute('lib2/x.ts'))
      expect(writesSent()[0].headers.get('If-None-Match')).toBe('*')
      expect(writesSent()[0].body).toEqual({ content: '' })
      expect(onWritten).toHaveBeenCalledWith([])
      expect(await screen.findByRole('textbox', { name: 'lib2/x.ts' })).toBeInTheDocument()
      await waitFor(() => expect(treeNames()).toContain('x.ts'))
    })

    it('refuses a new folder whose name is taken in the dialog', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await choose('lib', 'New folder…')
      await submitName('x.py', 'Create')

      expect(await within(await screen.findByRole('dialog')).findByText(/This name is taken/)).toBeInTheDocument()
    })

    it('asks before Delete…, then deletes each file of the folder with its version and closes their tabs', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      await openFile('b.ts', 'src/b.ts')

      await choose('src', 'Delete…')
      const dialog = await screen.findByRole('dialog')
      expect(within(dialog).getByText('Delete the folder src and the 2 files in it?')).toBeInTheDocument()
      expect(writesSent()).toEqual([])
      await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

      await waitFor(() => expect(writesSent()).toHaveLength(2))
      expect(writesSent().map((request) => [request.method, request.url, request.headers.get('If-Match')])).toEqual([
        ['DELETE', fileRoute('src/a.cs'), '"v-src/a.cs"'],
        ['DELETE', fileRoute('src/b.ts'), '"v-src/b.ts"'],
      ])
      await waitFor(() => expect(screen.queryByRole('tab', { name: /b\.ts/ })).not.toBeInTheDocument())
      await waitFor(() => expect(treeNames()).not.toContain('src'))
    })

    it('asks about a folder of one file in the singular', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await choose('lib', 'Delete…')

      expect(
        within(await screen.findByRole('dialog')).getByText('Delete the folder lib and its 1 file?'),
      ).toBeInTheDocument()
    })

    it('sends nothing when Delete… is cancelled', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await choose('adapter.py', 'Delete…')
      await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(writesSent()).toEqual([])
      expect(treeNames()).toContain('adapter.py')
    })

    describe('dragging', () => {
      const row = (name: string) => within(tree()).getAllByText(name, { exact: true })[0].closest('.ant-tree-treenode')!
      const dataTransfer = { setData: vi.fn(), getData: vi.fn(), effectAllowed: 'move', dropEffect: 'move' }
      /** Drags the node `from` and drops it on `onto`, a row of the tree or the tree's free space. */
      function drag(from: string, onto: Element) {
        fireEvent.dragStart(row(from), { dataTransfer, clientX: 0 })
        fireEvent.dragEnter(onto, { dataTransfer, clientX: 100 })
        fireEvent.dragOver(onto, { dataTransfer, clientX: 100 })
        fireEvent.drop(onto, { dataTransfer, clientX: 100 })
        fireEvent.dragEnd(row(from), { dataTransfer })
      }

      it('moves a file dropped on a folder into it with one move', async () => {
        stubPackage()
        renderWorkspace(true)
        await screen.findByTestId('studio-package-tree')

        drag('adapter.py', row('src'))

        await waitFor(() => expect(writesSent()).toHaveLength(1))
        expect(writesSent()[0].method).toBe('POST')
        expect(writesSent()[0].url).toBe(moveRoute)
        expect(writesSent()[0].body).toEqual({
          moves: [{ from: 'adapter.py', to: 'src/adapter.py', version: 'v-adapter.py' }],
        })
        await waitFor(() =>
          expect(treeNames()).toEqual(['lib', 'x.py', 'src', 'a.cs', 'adapter.py', 'b.ts', 'axis-package.json']),
        )
      })

      it('moves a folder dropped on a folder, and then on the free space to the root, with one move each', async () => {
        stubPackage()
        renderWorkspace(true)
        await screen.findByTestId('studio-package-tree')

        drag('src', row('lib'))
        await waitFor(() => expect(writesSent()).toHaveLength(1))
        await waitFor(() =>
          expect(treeNames()).toEqual(['lib', 'src', 'a.cs', 'b.ts', 'x.py', 'adapter.py', 'axis-package.json']),
        )
        expect(writesSent()[0].body).toEqual({
          moves: [
            { from: 'src/a.cs', to: 'lib/src/a.cs', version: 'v-src/a.cs' },
            { from: 'src/b.ts', to: 'lib/src/b.ts', version: 'v-src/b.ts' },
          ],
        })

        drag('src', tree())

        await waitFor(() => expect(writesSent()).toHaveLength(2))
        expect(writesSent()[1].url).toBe(moveRoute)
        expect(writesSent()[1].body).toEqual({
          moves: [
            { from: 'lib/src/a.cs', to: 'src/a.cs', version: 'v-lib/src/a.cs' },
            { from: 'lib/src/b.ts', to: 'src/b.ts', version: 'v-lib/src/b.ts' },
          ],
        })
      })

      it('sends nothing for a node dropped where it already is, or a folder dropped into itself', async () => {
        stubPackage()
        renderWorkspace(true)
        await screen.findByTestId('studio-package-tree')

        drag('x.py', row('lib'))
        drag('lib', row('lib'))
        drag('adapter.py', tree())
        drag('adapter.py', row('b.ts'))

        await new Promise((resolve) => setTimeout(resolve, 50))
        expect(writesSent()).toEqual([])
      })

      it('says above the workspace that a file was not moved on a 409, and reads the list again', async () => {
        stubPackage({}, [], undefined, null, undefined, 409)
        renderWorkspace(true)
        await screen.findByTestId('studio-package-tree')
        const listed = () => vi.mocked(fetch).mock.calls.filter(([url]) => url === files).length
        const before = listed()

        drag('adapter.py', row('src'))

        expect(await screen.findByText(/adapter\.py was not moved/)).toBeInTheDocument()
        await waitFor(() => expect(listed()).toBe(before + 1))
      })

      it('says a file was not moved when the target already holds that name, and sends nothing', async () => {
        stubPackage()
        renderWorkspace(true)
        await screen.findByTestId('studio-package-tree')
        await userEvent.click(
          within(screen.getByRole('region', { name: 'Files' })).getByRole('button', { name: 'New' }),
        )
        await userEvent.click(await screen.findByRole('menuitem', { name: 'New folder…' }))
        await submitName('x.py', 'Create')
        await waitFor(() => expect(treeNames()).toContain('x.py'))

        drag('x.py', tree())

        expect(await screen.findByText(/x\.py was not moved/)).toBeInTheDocument()
        expect(writesSent()).toEqual([])
      })

      it('cannot drag any node without the developer role, and a drag sends nothing', async () => {
        stubPackage()
        renderWorkspace(false)
        await screen.findByTestId('studio-package-tree')

        expect(tree().querySelectorAll('[draggable="true"]')).toHaveLength(0)
        drag('adapter.py', row('src'))
        drag('src', tree())

        await new Promise((resolve) => setTimeout(resolve, 50))
        expect(writesSent()).toEqual([])
      })
    })
  })

  describe('Problems tab', () => {
    const output = () => screen.getByTestId('studio-package-output')

    it("lists only the package's problems, and a problem's file opens its tab", async () => {
      stubPackage()
      renderWorkspace(true, 'tools', [
        problem('packages/tools/axis-package.json'),
        problem('packages/other/axis-package.json'),
        problem('entities/a.json', { resourceId: 'e1' }),
      ])
      await screen.findByTestId('studio-package-tree')

      const panel = within(output()).getByRole('tabpanel')
      expect(within(output()).getByRole('tab', { name: 'Problems' })).toBeInTheDocument()
      expect(within(panel).getAllByRole('listitem')).toHaveLength(1)
      expect(within(panel).queryByText('Problem in packages/other/axis-package.json')).not.toBeInTheDocument()
      expect(within(panel).queryByText('Problem in entities/a.json')).not.toBeInTheDocument()

      await userEvent.click(within(panel).getByRole('button', { name: 'packages/tools/axis-package.json' }))

      expect(await screen.findByRole('textbox', { name: 'axis-package.json' })).toBeInTheDocument()
      expect(screen.getByRole('tab', { name: /axis-package\.json/, selected: true })).toBeInTheDocument()
    })

    it('opens the file at the problem’s line, and at line 1 when it has none', async () => {
      stubPackage()
      renderWorkspace(true, 'tools', [
        problem('packages/tools/lib/x.py', { line: 3 }),
        problem('packages/tools/adapter.py', { severity: 'warning' }),
      ])
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(screen.getByRole('link', { name: 'packages/tools/lib/x.py, line 3' }))

      const view = EditorView.findFromDOM(await screen.findByRole('textbox', { name: 'lib/x.py' }))!
      await waitFor(() =>
        expect(within(screen.getByTestId('studio-package-status')).getByText('Line 3, column 1')).toBeInTheDocument(),
      )
      expect(view.state.doc.lineAt(view.state.selection.main.head).number).toBe(3)

      // A file that is already open jumps again.
      await userEvent.click(screen.getByRole('link', { name: 'packages/tools/adapter.py' }))
      await screen.findByRole('textbox', { name: 'adapter.py' })
      await userEvent.click(screen.getByRole('link', { name: 'packages/tools/lib/x.py, line 3' }))
      await waitFor(() =>
        expect(within(screen.getByTestId('studio-package-status')).getByText('Line 3, column 1')).toBeInTheDocument(),
      )
    })

    it('says there are no problems when the package has none', async () => {
      stubPackage()
      renderWorkspace(true, 'tools', [problem('packages/other/axis-package.json')])
      await screen.findByTestId('studio-package-tree')

      expect(within(within(output()).getByRole('tabpanel')).getByText('No problems.')).toBeInTheDocument()
    })
  })
  describe('Run tab', () => {
    const runPanel = () => within(screen.getByTestId('studio-package-output')).getByRole('tabpanel', { name: 'Run' })
    const jsonOf = async () =>
      EditorView.findFromDOM(await screen.findByRole('textbox', { name: 'JSON' }))!.state.doc.toString()

    async function openRun() {
      await screen.findByTestId('studio-package-tree')
      await userEvent.click(await screen.findByRole('button', { name: 'Run…' }))
      return runPanel()
    }

    it('opens from Run… with the input pane, and after a run shows the outcome, duration, output and log', async () => {
      stubPackage()
      renderWorkspace(true)
      const panel = await openRun()

      expect(within(screen.getByTestId('studio-package-output')).getByRole('tab', { name: 'Run' })).toHaveAttribute(
        'aria-selected',
        'true',
      )
      expect(within(panel).getByRole('heading', { level: 2, name: 'Input' })).toBeInTheDocument()
      expect(within(panel).getByRole('combobox', { name: 'Operation' })).toBeInTheDocument()
      expect(within(panel).getByRole('combobox', { name: 'Saved input' })).toBeInTheDocument()
      expect(within(panel).getByRole('tab', { name: 'Form' })).toBeInTheDocument()
      expect(within(panel).getByRole('tab', { name: 'JSON' })).toBeInTheDocument()
      expect(within(panel).getByRole('textbox', { name: /^name/ })).toBeInTheDocument()
      expect(within(panel).getByRole('heading', { level: 2, name: 'Output' })).toBeInTheDocument()

      await userEvent.type(within(panel).getByRole('textbox', { name: /^name/ }), 'Ada')
      await userEvent.click(within(panel).getByRole('button', { name: 'Run' }))

      expect(await within(panel).findByTestId('studio-run-outcome', {}, { timeout: 3000 })).toHaveTextContent(
        'Succeeded',
      )
      expect(within(panel).getByTestId('studio-run-duration')).toHaveTextContent('Duration: 1.2 sec')
      expect(within(panel).getByTestId('studio-run-log')).toHaveTextContent('hello from greet')
      const output = EditorView.findFromDOM(await within(panel).findByRole('textbox', { name: 'Output' }))!
      expect(JSON.parse(output.state.doc.toString())).toEqual({ greeting: 'Hello Ada' })
      expect(output.state.readOnly).toBe(true)
      expect(callsTo('POST', runRoute)[0].body).toEqual({
        package: 'tools',
        operation: 'greet',
        input: { name: 'Ada', count: null },
      })
    })

    it('shows each input field that has no value as null in the JSON, and follows the form', async () => {
      stubPackage()
      renderWorkspace(true)
      const panel = await openRun()

      await userEvent.click(within(panel).getByRole('tab', { name: 'JSON' }))
      expect(JSON.parse(await jsonOf())).toEqual({ name: null, count: null })
      expect(await jsonOf()).toBe('{\n  "name": null,\n  "count": null\n}')

      await userEvent.click(within(panel).getByRole('tab', { name: 'Form' }))
      await userEvent.type(within(panel).getByRole('textbox', { name: /^count/ }), '3')
      await userEvent.click(within(panel).getByRole('tab', { name: 'JSON' }))
      expect(JSON.parse(await jsonOf())).toEqual({ name: null, count: 3 })
    })

    it('saves a changed file before it posts the run', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      typeAtStart(await openFile('b.ts', 'src/b.ts'))
      await screen.findAllByTestId('studio-package-unsaved')
      await userEvent.click(screen.getByRole('button', { name: 'Run…' }))

      await userEvent.click(within(runPanel()).getByRole('button', { name: 'Run' }))

      await within(runPanel()).findByTestId('studio-run-outcome', {}, { timeout: 3000 })
      const sent = requests.filter((call) => call.method !== 'GET').map((call) => `${call.method} ${call.url}`)
      expect(sent).toEqual([`PUT ${fileRoute('src/b.ts')}`, `POST ${runRoute}`])
    })

    it('posts no run and shows the file’s conflict notice when its save answers 409', async () => {
      stubPackage({ 'src/b.ts': () => json({ title: 'Changed.' }, 409) })
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')
      typeAtStart(await openFile('b.ts', 'src/b.ts'))
      await screen.findAllByTestId('studio-package-unsaved')
      await userEvent.click(screen.getByRole('button', { name: 'Run…' }))

      await userEvent.click(within(runPanel()).getByRole('button', { name: 'Run' }))

      expect(await screen.findByTestId('studio-package-conflict')).toHaveTextContent('Someone else changed this file.')
      expect(callsTo('POST', runRoute)).toHaveLength(0)
      expect(within(runPanel()).queryByTestId('studio-run-outcome')).not.toBeInTheDocument()

      // The file is still in conflict, so a second Run stops too.
      await userEvent.click(within(runPanel()).getByRole('button', { name: 'Run' }))
      await waitFor(() => expect(screen.getByRole('tab', { name: /b\.ts/ })).toHaveAttribute('aria-selected', 'true'))
      expect(callsTo('POST', runRoute)).toHaveLength(0)
    })

    it('keeps saved inputs in the saved items dialog and writes them with the version', async () => {
      stubPackage()
      renderWorkspace(true)
      const panel = await openRun()
      await waitFor(() => expect(callsTo('GET', runInputs)).toHaveLength(1))
      await userEvent.type(within(panel).getByRole('textbox', { name: /^name/ }), 'Ada')

      const manage = async () => {
        await userEvent.click(within(panel).getByRole('combobox', { name: 'Saved input' }))
        await userEvent.click(await screen.findByRole('menuitem', { name: 'Manage saved inputs…' }))
        return within(await screen.findByRole('dialog', { name: 'Saved inputs' }))
      }
      const lastPut = () => callsTo('PUT', runInputs).at(-1)!

      // The first save has no version.
      let dialog = await manage()
      await userEvent.type(dialog.getByRole('textbox', { name: 'Name' }), 'Ada case')
      await userEvent.click(dialog.getByRole('button', { name: 'Save input' }))
      await waitFor(() => expect(callsTo('PUT', runInputs)).toHaveLength(1))
      expect(lastPut().headers.get('If-None-Match')).toBe('*')
      expect(lastPut().headers.get('If-Match')).toBeNull()
      expect(lastPut().body).toEqual({
        items: [{ name: 'Ada case', operation: 'greet', input: { name: 'Ada', count: null } }],
      })
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(within(panel).getByRole('combobox', { name: 'Saved input' })).toBeInTheDocument()

      // Overwrite takes what the form shows now.
      await userEvent.type(within(panel).getByRole('textbox', { name: /^count/ }), '2')
      dialog = await manage()
      await userEvent.click(dialog.getByRole('button', { name: 'Overwrite Ada case' }))
      await userEvent.click(dialog.getByRole('button', { name: 'Overwrite' }))
      await waitFor(() => expect(callsTo('PUT', runInputs)).toHaveLength(2))
      expect(lastPut().headers.get('If-Match')).toBe('"v0"')
      expect(lastPut().body).toEqual({
        items: [{ name: 'Ada case', operation: 'greet', input: { name: 'Ada', count: 2 } }],
      })

      // Rename.
      await userEvent.click(dialog.getByRole('button', { name: 'More actions Ada case' }))
      await userEvent.click(await screen.findByRole('menuitem', { name: 'Rename' }))
      // The line's own Name field comes before the one that saves a new input.
      const name = dialog.getAllByRole('textbox', { name: 'Name' })[0]
      await userEvent.clear(name)
      await userEvent.type(name, 'Ada two')
      await userEvent.click(dialog.getByRole('button', { name: 'Save' }))
      await waitFor(() => expect(callsTo('PUT', runInputs)).toHaveLength(3))
      expect(lastPut().headers.get('If-Match')).toBe('"v1"')
      expect(lastPut().body).toMatchObject({ items: [{ name: 'Ada two' }] })

      // Delete.
      await userEvent.click(await dialog.findByRole('button', { name: 'More actions Ada two' }))
      await userEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }))
      await userEvent.click(dialog.getByRole('button', { name: 'Delete' }))
      await waitFor(() => expect(callsTo('PUT', runInputs)).toHaveLength(4))
      expect(lastPut().headers.get('If-Match')).toBe('"v2"')
      expect(lastPut().body).toEqual({ items: [] })
    })

    it('opens a saved input with its operation and values from the Saved input list', async () => {
      stubPackage({}, [], undefined, [{ name: 'Grace case', operation: 'greet', input: { name: 'Grace', count: 5 } }])
      renderWorkspace(true)
      const panel = await openRun()
      await waitFor(() => expect(callsTo('GET', runInputs)).toHaveLength(1))

      await userEvent.click(within(panel).getByRole('combobox', { name: 'Saved input' }))
      await userEvent.click(await screen.findByText('Grace case'))

      await waitFor(() => expect(within(panel).getByRole('textbox', { name: /^name/ })).toHaveValue('Grace'))
      expect(within(panel).getByRole('textbox', { name: /^count/ })).toHaveValue('5')
    })

    it('shows no Run… and no Run tab, and asks for no run inputs, without the developer role', async () => {
      stubPackage()
      renderWorkspace(false)
      await screen.findByTestId('studio-package-tree')

      expect(screen.queryByRole('button', { name: 'Run…' })).not.toBeInTheDocument()
      expect(within(screen.getByTestId('studio-package-output')).queryByRole('tab', { name: 'Run' })).toBeNull()
      expect(callsTo('GET', runInputs)).toHaveLength(0)
    })
  })

  describe('Build', () => {
    const buildButton = () => screen.getByRole('button', { name: 'Build' })

    it('saves every changed file before it posts the build of the package, and opens the Output tab', async () => {
      const calls: string[] = []
      stubPackage({}, calls)
      renderWorkspace(true)
      await editTwoFiles()

      await userEvent.click(buildButton())

      await waitFor(() => expect(calls).toHaveLength(3))
      expect(calls).toEqual(['PUT src/b.ts', 'PUT adapter.py', 'POST build {"package":"tools"}'])
      expect(await screen.findByRole('tab', { name: 'Output', selected: true })).toBeInTheDocument()
    })

    it('posts nothing when a file changed elsewhere, and opens that file with its notice', async () => {
      const calls: string[] = []
      stubPackage({ 'src/b.ts': () => json({ title: 'Changed.' }, 409) }, calls)
      renderWorkspace(true)
      await editTwoFiles()
      await userEvent.click(screen.getByRole('tab', { name: /adapter\.py/ }))

      await userEvent.click(buildButton())

      expect(await screen.findByTestId('studio-package-conflict')).toHaveTextContent('Someone else changed this file.')
      expect(screen.getByRole('tab', { name: /b\.ts/ })).toHaveAttribute('aria-selected', 'true')
      expect(calls).toEqual(['PUT src/b.ts', 'PUT adapter.py'])
      await waitFor(() => expect(buildButton()).toBeEnabled())

      // The file stays in conflict, so a second build stops before any request.
      await userEvent.click(buildButton())
      await waitFor(() => expect(buildButton()).toBeEnabled())
      expect(calls).toEqual(['PUT src/b.ts', 'PUT adapter.py'])
    })

    it('shows the failed build’s output under its title row and state, and its problem opens the file at its line', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(buildButton())

      const panel = within(screen.getByTestId('studio-package-output'))
      await waitFor(() => expect(panel.getByTestId('studio-package-build-state')).toHaveTextContent('Failed'), {
        timeout: 3000,
      })
      expect(panel.getByRole('heading', { level: 2, name: 'Build' })).toBeInTheDocument()
      expect(panel.getByTestId('studio-package-build-output')).toHaveTextContent(
        'Compiled 1 Python file. lib/x.py: invalid syntax',
      )

      await userEvent.click(panel.getByRole('tab', { name: 'Problems' }))
      await userEvent.click(await screen.findByRole('button', { name: 'packages/tools/lib/x.py, line 2' }))

      const view = EditorView.findFromDOM(await screen.findByRole('textbox', { name: 'lib/x.py' }))!
      await waitFor(() =>
        expect(within(screen.getByTestId('studio-package-status')).getByText('Line 2, column 1')).toBeInTheDocument(),
      )
      expect(view.state.doc.lineAt(view.state.selection.main.head).number).toBe(2)
    })

    it('shows the alert and turns Build back on when reading the build fails', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      stubPackage({}, [], () => new Response(null, { status: 500 }))
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(buildButton())

      expect(
        await screen.findByText('The request could not be sent or checked.', {}, { timeout: 3000 }),
      ).toBeInTheDocument()
      await waitFor(() => expect(buildButton()).toBeEnabled())
      expect(buildButton()).not.toHaveAttribute('aria-busy', 'true')
    })

    it('says nothing has run before the first build or test, and leaves Run tests on', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(screen.getByRole('tab', { name: 'Output' }))

      expect(await screen.findByText('Nothing has been built or tested yet.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Run tests' })).toBeEnabled()
      expect(screen.getByRole('group', { name: 'Build and test' })).toBeInTheDocument()
    })

    it('builds on Ctrl+Shift+B', async () => {
      const calls: string[] = []
      stubPackage({}, calls)
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.keyboard('{Control>}{Shift>}b{/Shift}{/Control}')

      await waitFor(() => expect(calls).toEqual(['POST build {"package":"tools"}']))
    })

    it('shows no Build and Run tests group and posts nothing on Ctrl+Shift+B without the developer role', async () => {
      const calls: string[] = []
      stubPackage({}, calls)
      renderWorkspace(false)
      await screen.findByTestId('studio-package-tree')

      await userEvent.keyboard('{Control>}{Shift>}b{/Shift}{/Control}')

      expect(screen.queryByRole('group', { name: 'Build and test' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Build' })).not.toBeInTheDocument()
      expect(calls).toEqual([])
    })
  })

  describe('Run tests', () => {
    // A running button also names its loading icon, so the names match as a pattern.
    const testButton = () => screen.getByRole('button', { name: /Run tests/ })
    const buildButton = () => screen.getByRole('button', { name: /Build/ })

    it('saves every changed file before it posts the test of the package, and opens the Tests tab', async () => {
      const calls: string[] = []
      stubPackage({}, calls)
      renderWorkspace(true)
      await editTwoFiles()

      await userEvent.click(testButton())

      await waitFor(() => expect(calls).toHaveLength(3))
      expect(calls).toEqual(['PUT src/b.ts', 'PUT adapter.py', 'POST test {"package":"tools"}'])
      expect(await screen.findByRole('tab', { name: 'Tests', selected: true })).toBeInTheDocument()
    })

    it('posts nothing when a file changed elsewhere, and opens that file with its notice', async () => {
      const calls: string[] = []
      stubPackage({ 'src/b.ts': () => json({ title: 'Changed.' }, 409) }, calls)
      renderWorkspace(true)
      await editTwoFiles()
      await userEvent.click(screen.getByRole('tab', { name: /adapter\.py/ }))

      await userEvent.click(testButton())

      expect(await screen.findByTestId('studio-package-conflict')).toHaveTextContent('Someone else changed this file.')
      expect(screen.getByRole('tab', { name: /b\.ts/ })).toHaveAttribute('aria-selected', 'true')
      expect(calls).toEqual(['PUT src/b.ts', 'PUT adapter.py'])
      await waitFor(() => expect(testButton()).toBeEnabled())

      // The file stays in conflict, so a second run stops before any request.
      await userEvent.click(testButton())
      await waitFor(() => expect(testButton()).toBeEnabled())
      expect(calls).toEqual(['PUT src/b.ts', 'PUT adapter.py'])
    })

    it('says no test has run before the first one', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(screen.getByRole('tab', { name: 'Tests' }))

      expect(await screen.findByText('No tests have run yet.')).toBeInTheDocument()
    })

    it('lists each test with its result, name, duration and message, and the failed place opens its line', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(testButton())

      const panel = within(screen.getByTestId('studio-package-output'))
      await waitFor(() => expect(panel.getAllByTestId('studio-test-result')).toHaveLength(2), { timeout: 3000 })
      const [passed, failed] = panel.getAllByTestId('studio-test-result')
      expect(passed).toHaveAttribute('data-outcome', 'passed')
      expect(passed).toHaveTextContent('passedtest_adds< 1 sec')
      expect(failed).toHaveAttribute('data-outcome', 'failed')
      expect(failed).toHaveTextContent('failedtest_greets< 1 sec')
      expect(failed).toHaveTextContent('AssertionError: Hello Ada != Hi Ada')
      expect(panel.queryByRole('heading', { name: 'Test results' })).not.toBeInTheDocument()

      await userEvent.click(within(failed).getByRole('button', { name: 'packages/tools/lib/x.py, line 3' }))

      const view = EditorView.findFromDOM(await screen.findByRole('textbox', { name: 'lib/x.py' }))!
      await waitFor(() =>
        expect(within(screen.getByTestId('studio-package-status')).getByText('Line 3, column 1')).toBeInTheDocument(),
      )
      expect(view.state.doc.lineAt(view.state.selection.main.head).number).toBe(3)
    })

    it('turns Build and Run tests off while the test is followed, and on when it ends', async () => {
      let ended = false
      stubPackage({}, [], undefined, null, () => (ended ? json(failedTest) : json(queuedTest)))
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(testButton())

      await waitFor(() => expect(testButton()).toBeDisabled())
      expect(buildButton()).toBeDisabled()
      expect(screen.getByText('The results show when the run ends.')).toBeInTheDocument()
      ended = true
      await waitFor(() => expect(testButton()).toBeEnabled(), { timeout: 3000 })
      expect(buildButton()).toBeEnabled()
    })

    it('shows the test’s output under the Tests title row with its state', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(testButton())
      const panel = within(screen.getByTestId('studio-package-output'))
      await waitFor(() => expect(panel.getAllByTestId('studio-test-result')).toHaveLength(2), { timeout: 3000 })
      await userEvent.click(panel.getByRole('tab', { name: 'Output' }))

      expect(panel.getByRole('heading', { level: 2, name: 'Tests' })).toBeInTheDocument()
      expect(panel.getByTestId('studio-package-build-state')).toHaveTextContent('Failed')
      expect(panel.getByTestId('studio-package-build-output')).toHaveTextContent('collected 2 tests 1 failed, 1 passed')
    })

    it('shows the problems of a refused test copy in Problems', async () => {
      stubPackage({}, [], undefined, null, () =>
        json({
          ...failedTest,
          packageTests: [],
          diagnostics: [problem('packages/tools/lib/x.py', { line: 2, column: 1 })],
        }),
      )
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(testButton())
      const panel = within(screen.getByTestId('studio-package-output'))
      expect(await panel.findByText('No test ran. See the problems.', {}, { timeout: 3000 })).toBeInTheDocument()
      await userEvent.click(panel.getByRole('tab', { name: 'Problems' }))

      expect(await screen.findByRole('button', { name: 'packages/tools/lib/x.py, line 2' })).toBeInTheDocument()
    })
  })
})
