import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { StudioConnectionContext } from '../platform/studioConnection'
import type { ModuleResourceItem } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { StudioModuleEditor } from './StudioModuleEditor'

afterEach(() => vi.unstubAllGlobals())

const item: ModuleResourceItem = {
  id: 'department-id',
  kind: 'entity',
  name: 'organization/Department',
  module: 'organization',
}
const entity = {
  id: 'department-id',
  kind: 'entity',
  name: 'Department',
  fields: [{ name: 'title', type: 'string' }],
}
const layer = { id: 'layer-id', file: 'customizations/organization/Department.json', version: 'v1' }
const readPath = '/api/studio/apps/Leave%20Requests/modules/organization/resources/department-id'

function read(
  layerAnswer: typeof layer | null,
  content: Record<string, unknown> = entity,
  marks: { path: string; mark: 'added' | 'changed' }[] = [],
  removed: { list: string; key: string; content: unknown }[] = [],
) {
  return new Response(
    JSON.stringify({
      id: 'department-id',
      kind: 'entity',
      name: 'Department',
      file: 'modules/organization/entities/department.json',
      version: 'm1',
      content,
      layer: layerAnswer,
      marks,
      removed,
    }),
  )
}

function problem(status: number, title = 'Problem') {
  return new Response(JSON.stringify({ title }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

function renderEditor(respond: (url: string, init?: RequestInit) => Response, editing: ModuleResourceItem = item) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => respond(String(input), init))
  vi.stubGlobal('fetch', fetchMock)
  const onWritten = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioConnectionContext.Provider value={{ generation: 0, reconnecting: false, lost: () => {} }}>
          <StudioModuleEditor application="Leave Requests" item={editing} onWritten={onWritten} />
        </StudioConnectionContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return { fetchMock, onWritten }
}

function calls(fetchMock: ReturnType<typeof vi.fn>, method: string) {
  return fetchMock.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === method) as [
    string,
    RequestInit,
  ][]
}

async function rename() {
  const input = within(await screen.findByTestId('studio-field-/fields/0/name')).getByRole('textbox')
  await userEvent.clear(input)
  await userEvent.type(input, 'heading')
}

describe('StudioModuleEditor', () => {
  it('opens the entity editable, with Save off and no layer tag', async () => {
    renderEditor(() => read(null))

    expect(await screen.findByTestId('studio-entity-designer')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-module-read-only')).toBeNull()
    expect(screen.queryByTestId('studio-module-layer')).toBeNull()
    expect(screen.getByTestId('studio-save')).toBeDisabled()
    expect(within(screen.getByTestId('studio-field-/fields/0/name')).getByRole('textbox')).toBeEnabled()
  })

  it('saves with If-None-Match: * and no If-Match when the entity has no layer', async () => {
    let written = false
    const { fetchMock, onWritten } = renderEditor((_url, init) => {
      if (init?.method === 'PUT') {
        written = true
        return new Response(
          JSON.stringify({ file: 'customizations/organization/Department.json', version: 'v1', diagnostics: [] }),
        )
      }
      return read(written ? layer : null)
    })

    await rename()
    await userEvent.click(screen.getByTestId('studio-save'))

    expect(await screen.findByTestId('studio-saved')).toBeInTheDocument()
    const [[url, init]] = calls(fetchMock, 'PUT')
    expect(url).toBe(readPath)
    const headers = new Headers(init.headers)
    expect(headers.get('If-None-Match')).toBe('*')
    expect(headers.get('If-Match')).toBeNull()
    expect(JSON.parse(init.body as string).fields[0].name).toBe('heading')
    expect(onWritten).toHaveBeenCalledWith([], {
      added: {
        id: 'layer-id',
        kind: 'layer',
        name: 'organization/Department',
        file: 'customizations/organization/Department.json',
        version: 'v1',
        usedBy: 0,
      },
    })
    expect(await screen.findByTestId('studio-module-layer')).toBeInTheDocument()
  })

  it('saves with the quoted layer version in If-Match when the entity has a layer', async () => {
    const { fetchMock, onWritten } = renderEditor((_url, init) =>
      init?.method === 'PUT'
        ? new Response(
            JSON.stringify({ file: 'customizations/organization/Department.json', version: 'v2', diagnostics: [] }),
          )
        : read(layer),
    )

    expect(await screen.findByTestId('studio-module-layer')).toBeInTheDocument()
    await rename()
    await userEvent.click(screen.getByTestId('studio-save'))

    await waitFor(() => expect(onWritten).toHaveBeenCalledWith([], {}))
    const [[, init]] = calls(fetchMock, 'PUT')
    const headers = new Headers(init.headers)
    expect(headers.get('If-Match')).toBe('"v1"')
    expect(headers.get('If-None-Match')).toBeNull()
  })

  it('shows the conflict alert on a 409 and turns Save off', async () => {
    renderEditor((_url, init) => (init?.method === 'PUT' ? problem(409) : read(layer)))

    await rename()
    await userEvent.click(screen.getByTestId('studio-save'))

    expect(await screen.findByTestId('studio-conflict')).toBeInTheDocument()
    expect(screen.getByTestId('studio-save')).toBeDisabled()
  })

  it('shows the problem title when the save route refuses the edit', async () => {
    renderEditor((_url, init) =>
      init?.method === 'PUT' ? problem(400, 'The edit cannot be saved as a layer.') : read(null),
    )

    await rename()
    await userEvent.click(screen.getByTestId('studio-save'))

    expect(await screen.findByTestId('studio-save-error')).toHaveTextContent('The edit cannot be saved as a layer.')
  })

  it('deletes the layer with its version after Reset all is confirmed', async () => {
    let deleted = false
    const { fetchMock, onWritten } = renderEditor((_url, init) => {
      if (init?.method === 'DELETE') {
        deleted = true
        return new Response(JSON.stringify({ diagnostics: [] }))
      }
      return read(deleted ? null : layer)
    })

    await userEvent.click(await screen.findByTestId('studio-module-reset-all'))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Reset all' }))

    await waitFor(() => expect(screen.queryByTestId('studio-module-layer')).toBeNull())
    const [[url, init]] = calls(fetchMock, 'DELETE')
    expect(url).toBe('/api/studio/apps/Leave%20Requests/resources/layer-id')
    expect(new Headers(init.headers).get('If-Match')).toBe('"v1"')
    expect(onWritten).toHaveBeenCalledWith([], { removedId: 'layer-id' })
  })

  it('resets one element with the quoted layer version, then reads the entity again', async () => {
    let reset = false
    const marks = [
      { path: '/fields/0', mark: 'added' as const },
      { path: '/fields/0/label', mark: 'changed' as const },
    ]
    const { fetchMock, onWritten } = renderEditor((_url, init) => {
      if (init?.method === 'POST') {
        reset = true
        return new Response(JSON.stringify({ file: null, version: null, diagnostics: [] }))
      }
      return reset ? read(null) : read(layer, entity, marks)
    })

    expect(await screen.findAllByRole('note', { name: /^(Added|Changed) / })).toHaveLength(1)
    expect(screen.getByRole('note', { name: 'Added title' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Reset to module title' }))

    await waitFor(() => expect(screen.queryByTestId('studio-module-layer')).toBeNull())
    const [[url, init]] = calls(fetchMock, 'POST')
    expect(url).toBe(`${readPath}/reset`)
    expect(JSON.parse(init.body as string)).toEqual({ list: '/fields', key: 'title' })
    const headers = new Headers(init.headers)
    expect(headers.get('If-Match')).toBe('"v1"')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(fetchMock.mock.calls.filter(([target, init]) => target === readPath && !init?.method)).toHaveLength(2)
    expect(screen.queryByRole('note')).toBeNull()
    expect(onWritten).toHaveBeenCalledWith([], { removedId: 'layer-id' })
  })

  it('keeps the layer when the reset leaves a file', async () => {
    const { onWritten } = renderEditor((_url, init) =>
      init?.method === 'POST'
        ? new Response(JSON.stringify({ file: layer.file, version: 'v2', diagnostics: [] }))
        : read(layer, entity, [{ path: '/fields/0', mark: 'changed' }]),
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Reset to module title' }))

    await waitFor(() => expect(onWritten).toHaveBeenCalledWith([], {}))
    expect(screen.getByTestId('studio-module-layer')).toBeInTheDocument()
  })

  it('shows the conflict alert when the reset answers 409', async () => {
    renderEditor((_url, init) =>
      init?.method === 'POST' ? problem(409) : read(layer, entity, [{ path: '/fields/0', mark: 'added' }]),
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Reset to module title' }))

    expect(await screen.findByTestId('studio-conflict')).toBeInTheDocument()
  })

  it('shows the reason in the save error when the reset answers 404', async () => {
    renderEditor((_url, init) =>
      init?.method === 'POST'
        ? problem(404, 'The element is not in the resource.')
        : read(layer, entity, [{ path: '/fields/0', mark: 'added' }]),
    )

    await userEvent.click(await screen.findByRole('button', { name: 'Reset to module title' }))

    expect(await screen.findByTestId('studio-save-error')).toHaveTextContent('The element is not in the resource.')
  })

  it('turns Reset to module off after an unsaved edit', async () => {
    renderEditor(() => read(layer, entity, [{ path: '/fields/0', mark: 'added' }]))

    expect(await screen.findByRole('button', { name: 'Reset to module title' })).toBeEnabled()
    await userEvent.type(screen.getByRole('textbox', { name: 'Entity name' }), 'X')

    expect(screen.getByRole('button', { name: 'Reset to module title' })).toBeDisabled()
  })

  it('opens a module process in the process designer with the layer applied', async () => {
    const process = {
      id: 'process-id',
      kind: 'process',
      name: 'ChecklistReview',
      start: 'submit',
      steps: [
        { name: 'submit', type: 'operation', operation: 'updateRecord', set: {}, next: 'done' },
        { name: 'done', type: 'end' },
      ],
    }
    renderEditor(() => read(layer, process, [{ path: '/steps/0', mark: 'added' }]), {
      id: 'process-id',
      kind: 'process',
      name: 'checklists/ChecklistReview',
      module: 'checklists',
    })

    expect(await screen.findByTestId('studio-process-design', undefined, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.getByTestId('studio-module-reset-all')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-entity-designer')).toBeNull()
    expect(
      await screen.findByRole('button', { name: 'submit · operation · Added' }, { timeout: 5000 }),
    ).toBeInTheDocument()
  })
})
