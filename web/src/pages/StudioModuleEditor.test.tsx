import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { StudioConnectionContext } from '../platform/studioConnection'
import type { ModuleResourceItem, StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { followLink, renderGuarded } from '../test/leaveGuard'
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

function renderEditor(
  respond: (url: string, init?: RequestInit) => Response,
  editing: ModuleResourceItem = item,
  names?: StudioNames,
  mount?: typeof renderGuarded,
) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) =>
    String(input).endsWith('/page-metadata')
      ? new Response(JSON.stringify({ page: null, diagnostics: [] }))
      : respond(String(input), init),
  )
  vi.stubGlobal('fetch', fetchMock)
  const onWritten = vi.fn()
  const ui = (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioConnectionContext.Provider value={{ generation: 0, reconnecting: false, lost: () => {} }}>
          <StudioNamesContext.Provider value={names}>
            <StudioModuleEditor application="Leave Requests" item={editing} onWritten={onWritten} />
          </StudioNamesContext.Provider>
        </StudioConnectionContext.Provider>
      </TextProvider>
    </ThemeModeProvider>
  )
  const router = mount ? mount(ui) : render(<MemoryRouter>{ui}</MemoryRouter>) && undefined
  return { fetchMock, onWritten, router }
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
  it('asks before a link leaves an edited entity, and not before one leaves an untouched entity', async () => {
    const router = renderEditor(() => read(null), item, undefined, renderGuarded).router!
    await screen.findByTestId('studio-entity-designer')

    await followLink()
    expect(router.state.location.pathname).toBe('/elsewhere')
    await act(() => router.navigate('/studio'))

    await rename()
    await followLink()

    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/studio')
  })

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
      await screen.findByRole('button', { name: 'submit, operation, Added' }, { timeout: 5000 }),
    ).toBeInTheDocument()
  })

  it('opens a module page in the page designer, marking a changed nested widget and a removed one', async () => {
    const page = {
      id: 'page-id',
      kind: 'page',
      name: 'Budgets',
      widgets: [{ type: 'section', name: 'main', widgets: [{ type: 'table', name: 'list', dataSource: 'X' }] }],
    }
    const { fetchMock } = renderEditor(
      (_url, init) =>
        init?.method === 'POST'
          ? new Response(
              JSON.stringify({ file: 'customizations/budgets/Budgets.json', version: 'v2', diagnostics: [] }),
            )
          : read(
              layer,
              page,
              [
                { path: '/widgets/0', mark: 'changed' },
                { path: '/widgets/0/widgets/0', mark: 'changed' },
              ],
              [{ list: '/widgets/0/widgets', key: 'old', content: { type: 'table', name: 'old' } }],
            ),
      { id: 'page-id', kind: 'page', name: 'budgets/Budgets', module: 'budgets' },
    )

    expect(await screen.findByTestId('studio-page-design', undefined, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.getByTestId('studio-module-reset-all')).toBeInTheDocument()
    expect(screen.getByTestId('studio-save')).toBeDisabled()
    expect(screen.getByRole('note', { name: 'Changed list' })).toBeInTheDocument()
    expect(screen.getByRole('note', { name: 'Changed main' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reset to module list' })).toBeEnabled()
    const removed = screen.getByTestId('studio-layer-removed-/widgets/0/widgets/old')
    expect(removed).toHaveAttribute('data-muted')
    expect(within(removed).getByRole('note', { name: 'Removed old' })).toBeInTheDocument()

    await userEvent.click(within(removed).getByRole('button', { name: 'Reset to module old' }))

    await waitFor(() => expect(calls(fetchMock, 'POST')).toHaveLength(1))
    expect(JSON.parse(calls(fetchMock, 'POST')[0][1].body as string)).toEqual({
      list: '/widgets/0/widgets',
      key: 'old',
    })
  })

  it('opens a module text in the text table and resets one key through the reset route', async () => {
    let reset = false
    const text = {
      id: 'text-id',
      kind: 'text',
      name: 'TextsEn',
      locale: 'en',
      texts: { 'a.label': 'Mine', 'a.name': 'Name' },
    }
    const { fetchMock } = renderEditor(
      (_url, init) => {
        if (init?.method === 'POST') {
          reset = true
          return new Response(JSON.stringify({ file: null, version: null, diagnostics: [] }))
        }
        return reset ? read(null, text) : read(layer, text, [{ path: '/texts/a.label', mark: 'changed' }])
      },
      { id: 'text-id', kind: 'text', name: 'checklists/TextsEn', module: 'checklists' },
    )

    expect(await screen.findByTestId('studio-text-table')).toBeInTheDocument()
    expect(screen.getByRole('note', { name: 'Changed a.label' })).toBeInTheDocument()
    expect(screen.queryByRole('note', { name: 'Changed a.name' })).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Reset to module a.label' }))

    await waitFor(() => expect(screen.queryByRole('note')).toBeNull())
    const [[, init]] = calls(fetchMock, 'POST')
    expect(JSON.parse(init.body as string)).toEqual({ list: '/texts', key: 'a.label' })
  })

  describe('a module policy', () => {
    const policy = {
      id: 'policy-id',
      kind: 'policy',
      name: 'ChecklistAccess',
      roles: ['reviewer'],
      grants: [
        { name: 'checklistRead', entity: 'Checklist', actions: ['read'] },
        { name: 'checklistsRead', dataSource: 'Checklists', actions: ['read'] },
      ],
    }
    const policyItem: ModuleResourceItem = {
      id: 'policy-id',
      kind: 'policy',
      name: 'checklists/ChecklistAccess',
      module: 'checklists',
    }
    const policyPath = '/api/studio/apps/Leave%20Requests/modules/checklists/resources/policy-id'
    const removedGrant = { name: 'checklistsExport', dataSource: 'Checklists', actions: ['read'] }

    it('marks an added grant and lists a removed grant as a muted row with Reset to module', async () => {
      renderEditor(
        () =>
          read(
            layer,
            policy,
            [{ path: '/grants/1', mark: 'added' }],
            [{ list: '/grants', key: 'checklistsExport', content: removedGrant }],
          ),
        policyItem,
      )

      expect(await screen.findByTestId('studio-module-policy-grant-1')).toBeInTheDocument()
      expect(screen.getByRole('note', { name: 'Added checklistsRead' })).toBeInTheDocument()
      expect(screen.queryByRole('note', { name: 'Added checklistRead' })).toBeNull()
      const removed = screen.getByTestId('studio-layer-removed-/grants/checklistsExport')
      expect(within(removed).getByRole('note', { name: 'Removed checklistsExport' })).toBeInTheDocument()
      expect(within(removed).getByRole('button', { name: 'Reset to module checklistsExport' })).toBeEnabled()
    })

    it('resets a removed grant through the reset route', async () => {
      let reset = false
      const { fetchMock } = renderEditor((_url, init) => {
        if (init?.method === 'POST') {
          reset = true
          return new Response(JSON.stringify({ file: null, version: null, diagnostics: [] }))
        }
        return reset
          ? read(null, policy)
          : read(layer, policy, [], [{ list: '/grants', key: 'checklistsExport', content: removedGrant }])
      }, policyItem)

      await userEvent.click(await screen.findByRole('button', { name: 'Reset to module checklistsExport' }))

      await waitFor(() => expect(screen.queryByTestId('studio-layer-removed-/grants/checklistsExport')).toBeNull())
      const [[url, init]] = calls(fetchMock, 'POST')
      expect(url).toBe(`${policyPath}/reset`)
      expect(JSON.parse(init.body as string)).toEqual({ list: '/grants', key: 'checklistsExport' })
    })

    it('saves a ticked action through the module save route', async () => {
      let written = false
      const { fetchMock } = renderEditor((_url, init) => {
        if (init?.method === 'PUT') {
          written = true
          return new Response(
            JSON.stringify({ file: 'customizations/checklists/ChecklistAccess.json', version: 'v1', diagnostics: [] }),
          )
        }
        return read(written ? layer : null, policy)
      }, policyItem)

      const grant = await screen.findByTestId('studio-module-policy-grant-1')
      await userEvent.click(within(grant).getByRole('checkbox', { name: 'export' }))
      await userEvent.click(screen.getByTestId('studio-save'))

      expect(await screen.findByTestId('studio-saved')).toBeInTheDocument()
      const [[url, init]] = calls(fetchMock, 'PUT')
      expect(url).toBe(policyPath)
      const body = JSON.parse(init.body as string)
      expect(body.grants[1].actions).toEqual(['read', 'export'])
      expect(body.grants[0].actions).toEqual(['read'])
    })
  })
  describe('a module form', () => {
    const form = {
      id: 'form-id',
      kind: 'form',
      name: 'BudgetReview',
      formatVersion: 1,
      entity: 'Budget',
      sections: [
        {
          name: 'main',
          title: { textKey: 'department-budgets.budgetReview.title' },
          fields: [{ field: 'name' }, { field: 'amount' }, { field: 'department' }, { field: 'year' }],
        },
      ],
    }
    const formItem: ModuleResourceItem = {
      id: 'form-id',
      kind: 'form',
      name: 'department-budgets/BudgetReview',
      module: 'department-budgets',
    }
    const formPath = '/api/studio/apps/Leave%20Requests/modules/department-budgets/resources/form-id'

    /** Answers the canvas's draft route, and passes every other URL to `respond`. */
    function renderForm(respond: (url: string, init?: RequestInit) => Response) {
      return renderEditor(
        (url, init) =>
          url.endsWith('/page-metadata')
            ? new Response(JSON.stringify({ widget: null, diagnostics: [] }))
            : respond(url, init),
        formItem,
      )
    }

    function resets(fetchMock: ReturnType<typeof vi.fn>) {
      return calls(fetchMock, 'POST').filter(([url]) => url.endsWith('/reset'))
    }

    it('opens in the form designer with the layer applied and marks a moved field as changed', async () => {
      let reset = false
      const { fetchMock } = renderForm((_url, init) => {
        if (init?.method === 'POST') {
          reset = true
          return new Response(JSON.stringify({ file: null, version: null, diagnostics: [] }))
        }
        return reset ? read(null, form) : read(layer, form, [{ path: '/sections/0/fields/1', mark: 'changed' }])
      })

      expect(await screen.findByTestId('studio-form-design', undefined, { timeout: 5000 })).toBeInTheDocument()
      expect(screen.getByTestId('studio-module-layer')).toBeInTheDocument()
      expect(screen.queryByTestId('studio-form-used-on')).toBeNull()
      expect(screen.getByRole('note', { name: 'Changed amount' })).toBeInTheDocument()
      expect(screen.queryByRole('note', { name: 'Changed name' })).toBeNull()
      await userEvent.click(screen.getByRole('button', { name: 'Reset to module amount' }))

      await waitFor(() => expect(screen.queryByRole('note')).toBeNull())
      const [[url, init]] = resets(fetchMock)
      expect(url).toBe(`${formPath}/reset`)
      expect(JSON.parse(init.body as string)).toEqual({ list: '/sections/0/fields', key: 'amount' })
    })

    it('draws the draft through the module draft route', async () => {
      const { fetchMock } = renderForm(() => read(null, form))

      await screen.findByTestId('studio-form-design', undefined, { timeout: 5000 })

      await waitFor(() => expect(calls(fetchMock, 'POST').map(([url]) => url)).toContain(`${formPath}/page-metadata`))
    })

    it('marks a changed section and lists a removed field as a muted row with Reset to module', async () => {
      const { fetchMock } = renderForm(() =>
        read(
          layer,
          form,
          [{ path: '/sections/0', mark: 'changed' }],
          [{ list: '/sections/0/fields', key: 'code', content: { field: 'code' } }],
        ),
      )

      expect(await screen.findByTestId('studio-form-design', undefined, { timeout: 5000 })).toBeInTheDocument()
      expect(screen.getByRole('note', { name: 'Changed main' })).toBeInTheDocument()
      const removed = screen.getByTestId('studio-layer-removed-/sections/0/fields/code')
      expect(within(removed).getByRole('note', { name: 'Removed code' })).toBeInTheDocument()
      expect(within(removed).getByRole('button', { name: 'Reset to module code' })).toBeEnabled()
      expect(resets(fetchMock)).toEqual([])
    })
  })

  describe('a module data source and a module seed', () => {
    const dataSourceItem: ModuleResourceItem = {
      id: 'orders-id',
      kind: 'dataSource',
      name: 'organization/Orders',
      module: 'organization',
    }
    const seedItem: ModuleResourceItem = {
      id: 'departments-id',
      kind: 'seed',
      name: 'organization/Departments',
      module: 'organization',
    }
    const moduleNames = {
      locales: [],
      entities: [{ id: 'department-id', name: 'organization/Department', module: 'organization', fields: [] }],
      dataSources: [],
      processes: [],
      rules: [],
      connectors: [],
      events: [],
      roles: [],
      sequences: [],
      forms: [],
      pages: [],
      texts: [],
    } as unknown as StudioNames

    function readOf(
      kind: string,
      content: Record<string, unknown>,
      marks: { path: string; mark: 'added' | 'changed' }[],
    ) {
      return new Response(
        JSON.stringify({
          id: 'resource-id',
          kind,
          name: 'Resource',
          file: 'modules/organization/resource.json',
          version: 'm1',
          content: { id: 'resource-id', kind, name: 'Resource', ...content },
          layer,
          marks,
          removed: [],
        }),
      )
    }

    it('marks a changed field Changed with Reset to module', async () => {
      renderEditor(
        () =>
          readOf('dataSource', { entity: 'Department', fields: [{ name: 'title', path: 'title' }] }, [
            { path: '/fields/0', mark: 'changed' },
          ]),
        dataSourceItem,
      )

      expect(await screen.findByRole('note', { name: 'Changed title' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Reset to module title' })).toBeEnabled()
      expect(screen.getByTestId('studio-module-layer')).toBeInTheDocument()
    })

    it('marks an added record Added in the seed grid', async () => {
      renderEditor(
        (url) =>
          url.endsWith('/department-id')
            ? read(null, entity)
            : readOf(
                'seed',
                {
                  entity: 'Department',
                  records: [
                    { id: 'r1', values: { title: 'Sales' } },
                    { id: 'r2', values: { title: 'Support' } },
                  ],
                },
                [{ path: '/records/1', mark: 'added' }],
              ),
        seedItem,
        moduleNames,
      )

      const rows = await screen.findAllByTestId('studio-seed-row')
      expect(within(rows[1]).getByRole('note', { name: 'Added r2' })).toBeInTheDocument()
      expect(within(rows[0]).queryByRole('note')).toBeNull()
    })
  })
})
