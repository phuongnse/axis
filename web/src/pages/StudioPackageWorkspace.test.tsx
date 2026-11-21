import { EditorView } from '@codemirror/view'
import { ConfigProvider } from 'antd'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioDiagnostic, StudioRunInput } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioPackageWorkspace } from './StudioPackageWorkspace'

const files = '/api/studio/apps/Shop/package-files'
const fileRoute = (path: string) => `/api/studio/apps/Shop/packages/tools/files/${path}`
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

/**
 * Stubs a package `tools` with five files, and answers a file's read from `contents`. A write answers
 * `{ version: 'new-<path>' }` unless `writes` has a response for its path. The writes are recorded in `puts`. Each write
 * and each build post is also recorded in `calls`, in order, as `PUT <path>` and `POST build <body>`. A build post answers
 * the queued build `b1`, and its read answers `failedBuild` unless `readBuild` answers otherwise. The package's run
 * inputs start as `saved` at version `v0`, or have no version before the first save, and a write with a wrong version
 * answers `409`. A run answers `202`, and its request has ended with the run `greet`. Every request is recorded in
 * `requests`.
 */
function stubPackage(
  writes: Record<string, () => Response> = {},
  calls: string[] = [],
  readBuild: () => Response = () => json(failedBuild),
  saved: StudioRunInput[] | null = null,
) {
  const puts: { path: string; ifMatch: string | null; body: { content: string } }[] = []
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
      if (input === files) {
        return json({ items: Object.keys(contents).map((path) => ({ package: 'tools', path, version: `v-${path}` })) })
      }
      const path = Object.keys(contents).find((candidate) => input === fileRoute(candidate))
      if (path === undefined) {
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
      return json({ package: 'tools', path, version: `v-${path}`, content: contents[path] })
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

      await userEvent.click(screen.getByRole('button', { name: 'packages/tools/lib/x.py, line 3' }))

      const view = EditorView.findFromDOM(await screen.findByRole('textbox', { name: 'lib/x.py' }))!
      await waitFor(() =>
        expect(within(screen.getByTestId('studio-package-status')).getByText('Line 3, column 1')).toBeInTheDocument(),
      )
      expect(view.state.doc.lineAt(view.state.selection.main.head).number).toBe(3)

      // A file that is already open jumps again.
      await userEvent.click(screen.getByRole('button', { name: 'packages/tools/adapter.py' }))
      await screen.findByRole('textbox', { name: 'adapter.py' })
      await userEvent.click(screen.getByRole('button', { name: 'packages/tools/lib/x.py, line 3' }))
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

    it('says no build has run before the first one, and leaves Run tests off', async () => {
      stubPackage()
      renderWorkspace(true)
      await screen.findByTestId('studio-package-tree')

      await userEvent.click(screen.getByRole('tab', { name: 'Output' }))

      expect(await screen.findByText('No build has run yet.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Run tests' })).toBeDisabled()
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
})
