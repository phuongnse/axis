import { EditorView } from '@codemirror/view'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import purchaseRequestApproval from '../../../samples/apps/purchase-requests/processes/purchase-request-approval.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames, StudioResourceItem } from '../platform/studio'
import { setAt } from '../platform/jsonPatch'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { loadKindSchema } from '../platform/studioSchemas'
import { chooseOption } from '../test/antd'
import { buildProcessGraph } from './processGraph'
import { StudioEditor } from './StudioEditor'

const folder = '/api/studio/apps/Shop'
const seedId = '46e28f6b-ce3b-4a17-829e-92fb3a5930ad'
const entityId = 'ebe6bc20-4772-4d6c-839b-65d426ebeeca'

const resources: StudioResourceItem[] = [
  { id: seedId, kind: 'seed', name: 'Orders', file: 'seeds/orders.json', version: 's1', usedBy: 0 },
  { id: entityId, kind: 'entity', name: 'Order', file: 'entities/order.json', version: 'e1', usedBy: 1 },
]

const entityContent = {
  id: entityId,
  kind: 'entity',
  name: 'Order',
  formatVersion: 1,
  fields: [
    { name: 'code', type: 'text', required: true, maxLength: 20 },
    { name: 'size', type: 'integer' },
    { name: 'active', type: 'boolean' },
    { name: 'lines', type: 'child-collection', target: 'OrderLine' },
  ],
}

function seedContent(entity: string, records: unknown[]) {
  return { id: seedId, kind: 'seed', name: 'Orders', formatVersion: 1, entity, sync: true, records }
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stub(seed: unknown) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === `${folder}/resources/${seedId}` && init?.method === 'PATCH') {
      return json({ version: 's2', diagnostics: [] })
    }
    if (input === `${folder}/resources/${seedId}`) {
      return json({ ...resources[0], content: seed })
    }
    if (input === `${folder}/resources/${entityId}`) {
      return json({ ...resources[1], content: entityContent })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderEditor() {
  render(
    <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
      <StudioEditor
        application="Shop"
        item={resources[0]}
        resources={resources}
        path={null}
        diagnosticPaths={[]}
        onOpen={vi.fn()}
        onSaved={vi.fn()}
        onTextsWritten={vi.fn()}
        onDeleted={vi.fn()}
      />
    </TextProvider>,
  )
}

// The first lookup waits for the schema chunk and two fetches, which is slow while other test files run.
const slow = { timeout: 5000 }

async function openFormTab() {
  await userEvent.click(await screen.findByRole('tab', { name: 'Form' }, slow))
}

// The schema is a chunk of its own. Loading it here keeps the first test from racing its first lookup.
beforeAll(() =>
  Promise.all([
    loadKindSchema('seed'),
    loadKindSchema('connector'),
    loadKindSchema('endpoint'),
    loadKindSchema('text'),
    loadKindSchema('page'),
    loadKindSchema('entity'),
    loadKindSchema('form'),
    loadKindSchema('site'),
    import('./StudioFormDesign'),
  ]),
)

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('StudioEditor for a seed', () => {
  it('shows no process graph', async () => {
    stub(seedContent('order', []))
    renderEditor()

    await screen.findByTestId('studio-editor', {}, slow)
    expect(screen.queryByTestId('studio-process-graph')).not.toBeInTheDocument()
  })

  it('turns Save off while an integer cell holds a wrong value and on again once it is fixed', async () => {
    stub(seedContent('order', [{ id: 'a', values: { code: 'X1', size: 3 } }]))
    renderEditor()
    const size = await screen.findByRole('textbox', { name: 'size' }, slow)
    const save = screen.getByRole('button', { name: 'Save' })
    expect(save).toBeDisabled()

    await userEvent.clear(size)
    await userEvent.type(size, 'abc')
    expect(size).toHaveAttribute('aria-invalid', 'true')
    expect(save).toBeDisabled()

    await userEvent.clear(size)
    await userEvent.type(size, '12')
    expect(size).toHaveAttribute('aria-invalid', 'false')
    expect(save).toBeEnabled()
  })

  it('turns Save off for a loaded string "5" in an integer field, also for a change made elsewhere', async () => {
    stub(seedContent('Order', [{ id: 'a', values: { code: 'X1', size: '5' } }]))
    renderEditor()
    const code = await screen.findByRole('textbox', { name: 'code' }, slow)
    const save = screen.getByRole('button', { name: 'Save' })

    await userEvent.type(code, '2')

    expect(screen.getByRole('textbox', { name: 'size' })).toHaveAttribute('aria-invalid', 'true')
    expect(save).toBeDisabled()
  })

  it('turns Save off for a wrong value typed in the JSON view', async () => {
    stub(seedContent('Order', [{ id: 'a', values: { code: 'X1' } }]))
    renderEditor()
    await screen.findByRole('textbox', { name: 'code' }, slow)

    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const text = screen.getByRole('textbox', { name: 'JSON' })
    const edited = JSON.stringify(seedContent('Order', [{ id: 'a', values: { code: 5 } }]))
    await userEvent.clear(text)
    await userEvent.click(text)
    await userEvent.paste(edited)

    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('turns Save off for a new row until its required cell is filled', async () => {
    stub(seedContent('Order', []))
    renderEditor()

    await userEvent.click(await screen.findByRole('button', { name: 'Add record' }, slow))
    const save = screen.getByRole('button', { name: 'Save' })
    expect(save).toBeDisabled()

    await userEvent.type(screen.getByRole('textbox', { name: 'code' }), 'N1')
    expect(save).toBeEnabled()
  })

  it('saves a patch that keeps a child collection and an unknown key of the record', async () => {
    const original = { code: 'X1', size: 3, lines: [{ item: 'pen' }], note: 'kept' }
    const fetchMock = stub(seedContent('Order', [{ id: 'a', values: original }]))
    renderEditor()
    const size = await screen.findByRole('textbox', { name: 'size' }, slow)

    await userEvent.clear(size)
    await userEvent.type(size, '4')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const patches = fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')
    expect(patches).toHaveLength(1)
    expect(JSON.parse(patches[0][1]!.body as string)).toEqual([
      { op: 'replace', path: '/records/0/values/size', value: 4 },
    ])
  })

  it('keeps a child collection and an unknown key when a record is added', async () => {
    const original = { id: 'a', values: { code: 'X1', lines: [{ item: 'pen' }], note: 'kept' } }
    const fetchMock = stub(seedContent('Order', [original]))
    renderEditor()

    await userEvent.click(await screen.findByRole('button', { name: 'Add record' }, slow))
    await userEvent.type(screen.getAllByRole('textbox', { name: 'code' })[1], 'N1')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'PATCH')!
    const [operation] = JSON.parse(init!.body as string) as { op: string; path: string; value: unknown[] }[]
    expect(operation.path).toBe('/records')
    expect(operation.value[0]).toEqual(original)
    expect(operation.value[1]).toMatchObject({ values: { code: 'N1' } })
  })

  it('keeps an explicit false that the file did not have', async () => {
    const fetchMock = stub(seedContent('Order', [{ id: 'a', values: { code: 'X1' } }]))
    renderEditor()

    await userEvent.click(await screen.findByRole('combobox', { name: 'active' }, slow))
    await userEvent.click(await screen.findByTitle('false'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'PATCH')!
    expect(JSON.parse(init!.body as string)).toEqual([{ op: 'add', path: '/records/0/values/active', value: false }])
  })

  it('falls back to the generic form when the entity is not in the folder', async () => {
    stub(seedContent('Missing', [{ id: 'a', values: { code: 'X1' } }]))
    renderEditor()

    expect(await screen.findByTestId('studio-field-/entity', {}, slow)).toBeInTheDocument()
    expect(within(screen.getByTestId('studio-field-/entity')).getByRole('textbox')).toHaveValue('Missing')
    await waitFor(() => expect(screen.queryByTestId('studio-seed-grid')).not.toBeInTheDocument())
  })
})

describe('StudioEditor for a scenario', () => {
  const scenarioId = '6f0c1c3e-2a4b-4d7e-9c1f-5b8a7d6e4f21'
  const scenarioItem: StudioResourceItem = {
    id: scenarioId,
    kind: 'scenario',
    name: 'Approve',
    file: 'scenarios/approve.json',
    version: 'c1',
    usedBy: 0,
  }

  it('shows the typed editor and saves an edited step as one operation', async () => {
    const content = {
      id: scenarioId,
      kind: 'scenario',
      name: 'Approve',
      formatVersion: 1,
      users: { author: [] },
      steps: [{ type: 'advanceTime', by: 'PT1H' }],
    }
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input === `${folder}/resources/${scenarioId}` && init?.method === 'PATCH') {
        return json({ version: 'c2', diagnostics: [] })
      }
      if (input === `${folder}/resources/${scenarioId}`) {
        return json({ ...scenarioItem, content })
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetchMock)
    render(
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioEditor
          application="Shop"
          item={scenarioItem}
          resources={[scenarioItem]}
          path={null}
          diagnosticPaths={[]}
          onOpen={vi.fn()}
          onSaved={vi.fn()}
          onTextsWritten={vi.fn()}
          onDeleted={vi.fn()}
        />
      </TextProvider>,
    )

    expect(await screen.findByTestId('studio-scenario-step', {}, slow)).toBeInTheDocument()
    await userEvent.type(screen.getByRole('textbox', { name: 'by' }), '0')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'PATCH')!
    expect(JSON.parse(init!.body as string)).toEqual([{ op: 'replace', path: '/steps/0/by', value: 'PT1H0' }])
  })
})

const connectorId = '409bc638-0aec-4084-ada2-e3baed50057d'
const endpointId = 'bc241668-fe85-45a4-b7cf-d36da8d56b71'

const connectorItem: StudioResourceItem = {
  id: connectorId,
  kind: 'connector',
  name: 'PurchasingSystem',
  file: 'connectors/purchasing-system.json',
  version: 'c1',
  usedBy: 0,
}

const endpointItem: StudioResourceItem = {
  id: endpointId,
  kind: 'endpoint',
  name: 'SubmitPurchaseRequest',
  file: 'endpoints/submit-purchase-request.json',
  version: 'p1',
  usedBy: 0,
}

const connectorContent = {
  id: connectorId,
  kind: 'connector',
  name: 'PurchasingSystem',
  formatVersion: 1,
  baseUrl: { binding: 'PurchasingSystemUrl' },
  auth: { type: 'bearer', token: { binding: 'PurchasingSystemToken' } },
  timeout: 'PT30S',
  idempotency: 'Idempotency-Key',
  operations: [
    {
      name: 'createPurchaseOrder',
      method: 'POST',
      path: '/purchase-orders',
      input: [{ name: 'requestNumber', type: 'text', required: true }],
      output: [{ name: 'orderNumber', type: 'text', required: true }],
      lookup: 'findPurchaseOrder',
    },
    {
      name: 'findPurchaseOrder',
      method: 'GET',
      path: '/purchase-orders/by-key/{idempotencyKey}',
      input: [],
      output: [{ name: 'orderNumber', type: 'text', required: true }],
    },
  ],
}

const endpointContent = {
  id: endpointId,
  kind: 'endpoint',
  name: 'SubmitPurchaseRequest',
  formatVersion: 1,
  entity: 'PurchaseRequest',
  process: 'PurchaseRequestApproval',
  input: [{ name: 'title', type: 'text', required: true }],
  values: { title: 'title', status: "'draft'" },
}

const namesWithEntity = {
  locales: [],
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
  entities: [
    {
      id: 'e',
      name: 'PurchaseRequest',
      module: null,
      fields: ['title', 'status', 'department'].map((name) => ({ name, type: 'text', target: null, values: null })),
    },
  ],
} as unknown as StudioNames

/** A fetch stub that serves one resource and answers its PATCH, so the test can read what was sent. */
function stubResource(item: StudioResourceItem, content: unknown) {
  const url = `${folder}/resources/${item.id}`
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === url && init?.method === 'PATCH') {
      return json({ version: 'next', diagnostics: [] })
    }
    if (input === `${folder}/changes` && init?.method === 'POST') {
      return json({
        items: [
          { id: item.id, version: 'next' },
          { id: 'created', version: 'created-version' },
        ],
        diagnostics: [],
      })
    }
    if (input === url) {
      return json({ ...item, content })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock, url }
}

function renderResource(
  item: StudioResourceItem,
  names?: StudioNames,
  onTextsWritten = vi.fn(),
  path: string | null = null,
  diagnosticPaths: readonly string[] = [],
) {
  const editor = (current: string | null) => (
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <StudioEditor
              application="Shop"
              item={item}
              resources={[item]}
              path={current}
              diagnosticPaths={diagnosticPaths}
              onOpen={vi.fn()}
              onSaved={vi.fn()}
              onTextsWritten={onTextsWritten}
              onDeleted={vi.fn()}
            />
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>
  )
  const { rerender } = render(editor(path))
  return { setPath: (next: string) => rerender(editor(next)) }
}

type PatchOperation = { op: string; path: string; value?: unknown }

type SentChanges = { changes: ({ create: Record<string, unknown> } | { id: string; patch: PatchOperation[] })[] }

function sentChanges(fetchMock: ReturnType<typeof vi.fn>): SentChanges {
  const [, init] = fetchMock.mock.calls.find(
    ([input, call]) => input === `${folder}/changes` && call?.method === 'POST',
  )!
  return JSON.parse(init!.body as string) as SentChanges
}

function requests(fetchMock: ReturnType<typeof vi.fn>, method: string): number {
  return fetchMock.mock.calls.filter(([, call]) => call?.method === method).length
}

function sentPatch(fetchMock: ReturnType<typeof vi.fn>): PatchOperation[] {
  const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'PATCH')!
  return JSON.parse(init!.body as string) as PatchOperation[]
}

const siteId = '3a5c1f0e-6b7d-4e21-9c84-2f1d0a9b7c11'

const siteItem: StudioResourceItem = {
  id: siteId,
  kind: 'site',
  name: 'Purchasing',
  file: 'sites/purchasing.json',
  version: 'si1',
  usedBy: 0,
}

const siteContent = {
  id: siteId,
  kind: 'site',
  name: 'Purchasing',
  formatVersion: 1,
  path: 'purchasing',
  title: { textKey: 'site.title' },
  locales: { default: 'en', fallback: 'en', available: ['en'] },
  navigation: [{ page: 'PurchaseRequests', label: { textKey: 'nav.purchaseRequests' } }],
}

describe('StudioEditor for a site', () => {
  it('offers the icons of the list for a navigation entry and saves the chosen one', async () => {
    const { fetchMock } = stubResource(siteItem, siteContent)
    renderResource(siteItem)

    await chooseOption('tasks', await screen.findByTestId('studio-field-/navigation/0/icon', {}, slow))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([{ op: 'add', path: '/navigation/0/icon', value: 'tasks' }])
  })
})

describe('StudioEditor for a connector', () => {
  it('shows the binding names and saves only names, reading only the resource route', async () => {
    const { fetchMock, url } = stubResource(connectorItem, connectorContent)
    renderResource(connectorItem)

    const urlBinding = await screen.findByRole('textbox', { name: 'URL binding' }, slow)
    expect(urlBinding).toHaveValue('PurchasingSystemUrl')
    expect(screen.getByRole('textbox', { name: 'Secret binding' })).toHaveValue('PurchasingSystemToken')

    await userEvent.type(urlBinding, '2')
    await userEvent.type(screen.getByRole('textbox', { name: 'Secret binding' }), '2')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const operations = sentPatch(fetchMock)
    expect(operations).toEqual([
      { op: 'replace', path: '/baseUrl/binding', value: 'PurchasingSystemUrl2' },
      { op: 'replace', path: '/auth/token/binding', value: 'PurchasingSystemToken2' },
    ])
    for (const operation of operations) {
      expect(operation.path).not.toMatch(/(Value|Kind)$/)
      expect(
        typeof operation.value === 'string' || (operation.value as { binding?: unknown })?.binding !== undefined,
      ).toBe(true)
    }
    expect(new Set(fetchMock.mock.calls.map(([input]) => input))).toEqual(new Set([url]))
  })

  it('keeps an unknown key of the file, of an operation and of the auth object', async () => {
    const content = {
      ...connectorContent,
      'x-extra': 1,
      auth: { ...connectorContent.auth, 'x-hint': 'keep' },
      operations: [{ ...connectorContent.operations[0], 'x-note': 'keep' }, connectorContent.operations[1]],
    }
    const { fetchMock } = stubResource(connectorItem, content)
    renderResource(connectorItem)

    const timeout = await screen.findByRole('textbox', { name: 'Timeout' }, slow)
    await userEvent.clear(timeout)
    await userEvent.type(timeout, 'PT10S')
    await userEvent.click(screen.getByRole('button', { name: 'Add operation' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const operations = sentPatch(fetchMock)
    expect(operations.map((operation) => operation.path).sort()).toEqual(['/operations', '/timeout'])
    const replaced = operations.find((operation) => operation.path === '/operations')!.value as Record<
      string,
      unknown
    >[]
    expect(replaced).toHaveLength(3)
    expect(replaced[0]['x-note']).toBe('keep')
    expect(replaced[2]).toEqual({ name: '', method: 'POST', path: '/' })
  })

  it('removes the keys the new auth type forbids in one patch', async () => {
    const { fetchMock } = stubResource(connectorItem, connectorContent)
    renderResource(connectorItem)

    await userEvent.click(await screen.findByRole('combobox', { name: 'Auth type' }, slow))
    await userEvent.click(await screen.findByTitle('header'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/auth/type', value: 'header' },
      { op: 'remove', path: '/auth/token' },
      { op: 'add', path: '/auth/name', value: '' },
      { op: 'add', path: '/auth/value', value: { binding: '' } },
    ])
  })
})

describe('StudioEditor for an endpoint', () => {
  it('edits a mapping row and keeps an unknown key, patching only that value', async () => {
    const { fetchMock } = stubResource(endpointItem, { ...endpointContent, 'x-extra': { keep: true } })
    renderResource(endpointItem, namesWithEntity)

    const row = await screen.findByTestId('studio-field-/values/title', {}, slow)
    let view: EditorView | null = null
    // The editor chunk loads after the row shows a read-only box.
    await waitFor(() => {
      view = EditorView.findFromDOM(within(row).getByRole('textbox', { name: 'Expression' }))
      expect(view).not.toBeNull()
    }, slow)
    const editor = view as unknown as EditorView
    act(() => editor.dispatch({ changes: { from: 0, to: editor.state.doc.length, insert: 'trim(title)' } }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([{ op: 'replace', path: '/values/title', value: 'trim(title)' }])
  })

  it('shows the generic form for the rest of the endpoint and the mapping rows without a duplicate', async () => {
    stubResource(endpointItem, endpointContent)
    renderResource(endpointItem, namesWithEntity)

    expect(await screen.findByTestId('studio-field-/entity', {}, slow)).toBeInTheDocument()
    expect(screen.getAllByTestId('studio-field-/values')).toHaveLength(1)
    expect(screen.getByTestId('studio-field-/values/status')).toBeInTheDocument()
  })

  const namesWithEvent = {
    ...namesWithEntity,
    events: [
      {
        id: 'ev1',
        name: 'PaymentReceived',
        entity: 'PurchaseRequest',
        fields: [
          { name: 'amount', type: 'decimal' },
          { name: 'reference', type: 'text' },
          { name: 'note', type: 'text' },
        ],
      },
    ],
  } as unknown as StudioNames

  it('shows the event of an endpoint on the Form tab with no JSON view', async () => {
    stubResource(endpointItem, { ...endpointContent, event: { name: 'PaymentReceived', fields: { amount: 'amount' } } })
    renderResource(endpointItem, namesWithEvent)

    expect(await screen.findByTestId('studio-field-/entity', {}, slow)).toBeInTheDocument()
    expect(screen.queryByTestId('studio-json-only')).toBeNull()
    expect(screen.getAllByTestId('studio-field-/event/name')).toHaveLength(1)
    expect(screen.getByTestId('studio-field-/event/fields/amount')).toBeInTheDocument()
  })

  it('picks an event, maps its fields and saves the event as one add', async () => {
    const { fetchMock } = stubResource(endpointItem, endpointContent)
    renderResource(endpointItem, namesWithEvent)

    await screen.findByTestId('studio-field-/entity', {}, slow)
    await userEvent.click(screen.getByRole('combobox', { name: 'Event' }))
    await chooseOption('PaymentReceived')
    await userEvent.click(screen.getByRole('combobox', { name: 'Add mapping Event fields' }))
    await chooseOption('amount')
    await userEvent.click(screen.getByRole('combobox', { name: 'Add mapping Event fields' }))
    await chooseOption('reference')
    await userEvent.click(
      within(screen.getByTestId('studio-field-/event/fields/amount')).getByRole('combobox', { name: 'Event field' }),
    )
    await chooseOption('note')
    await userEvent.click(
      within(screen.getByTestId('studio-field-/event/fields/reference')).getByRole('button', { name: 'Remove' }),
    )

    const row = screen.getByTestId('studio-field-/event/fields/note')
    let view: EditorView | null = null
    // The editor chunk loads after the row shows a read-only box.
    await waitFor(() => {
      view = EditorView.findFromDOM(within(row).getByRole('textbox', { name: 'Expression' }))
      expect(view).not.toBeNull()
    }, slow)
    const editor = view as unknown as EditorView
    act(() => editor.dispatch({ changes: { from: 0, to: editor.state.doc.length, insert: 'trim(title)' } }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'add', path: '/event', value: { name: 'PaymentReceived', fields: { note: 'trim(title)' } } },
    ])
  })
})

const processId = purchaseRequestApproval.id

const processItem: StudioResourceItem = {
  id: processId,
  kind: 'process',
  name: 'PurchaseRequestApproval',
  file: 'processes/purchase-request-approval.json',
  version: 'r1',
  usedBy: 0,
}

/** The purchase request process with an unknown key on the operation `submit` and on the decision's first branch. */
const processContent = {
  ...purchaseRequestApproval,
  steps: purchaseRequestApproval.steps.map((step, index) => {
    if (index === 0) {
      return { ...step, 'x-note': 'keep' }
    }
    if (index === 2) {
      return {
        ...step,
        'x-note': 'keep',
        branches: [
          { when: 'totalAmount >= FinanceThreshold()', next: 'financeApproval', 'x-note': 'keep' },
          { when: 'totalAmount > 0', next: 'markApproved' },
        ],
      }
    }
    return step
  }),
}

/** The CodeMirror view of the expression editor that shows inside `field`. */
async function expressionView(field: HTMLElement): Promise<EditorView> {
  let view: EditorView | null = null
  // The editor chunk loads after the row shows a read-only box.
  await waitFor(() => {
    view = EditorView.findFromDOM(within(field).getByRole('textbox', { name: 'When' }))
    expect(view).not.toBeNull()
  }, slow)
  return view as unknown as EditorView
}

function sentSteps(fetchMock: ReturnType<typeof vi.fn>): Record<string, unknown>[] {
  return sentPatch(fetchMock).find((operation) => operation.path === '/steps')!.value as Record<string, unknown>[]
}

describe('StudioEditor for a process', () => {
  it('edits a branch and the otherwise of a decision, keeping the unknown keys', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const when = await screen.findByTestId('studio-field-/steps/2/branches/0/when', {}, slow)
    const view = await expressionView(when)
    act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: 'totalAmount > 5' } }))
    await chooseOption('markRejected', screen.getByTestId('studio-field-/steps/2/branches/1/next'))
    await chooseOption('markRejected', screen.getByTestId('studio-field-/steps/2/otherwise'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/steps/2/branches/0/when', value: 'totalAmount > 5' },
      { op: 'replace', path: '/steps/2/branches/1/next', value: 'markRejected' },
      { op: 'replace', path: '/steps/2/otherwise', value: 'markRejected' },
    ])
  })

  it('adds a branch, keeping the unknown keys of the others', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const branches = await screen.findByTestId('studio-field-/steps/2/branches', {}, slow)
    await userEvent.click(within(branches).getByRole('button', { name: 'Add branch' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      {
        op: 'replace',
        path: '/steps/2/branches',
        value: [...processContent.steps[2].branches!, { when: '', next: '' }],
      },
    ])
  })

  it('removes a branch, keeping the unknown keys of the others', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const second = await screen.findByTestId('studio-field-/steps/2/branches/1', {}, slow)
    await userEvent.click(within(second).getByRole('button', { name: 'Remove branch' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/steps/2/branches', value: [processContent.steps[2].branches![0]] },
    ])
  })

  it('renames the end step with a patch of only its name', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const name = within(await screen.findByTestId('studio-step-9', {}, slow)).getByRole('textbox', {
      name: 'Step name',
    })
    await userEvent.type(name, '2')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([{ op: 'replace', path: '/steps/9/name', value: 'done2' }])
  })

  it('adds a step of the chosen type with its required keys empty', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    await userEvent.click(await screen.findByRole('combobox', { name: 'Add step' }, slow))
    await chooseOption('decision')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const steps = sentSteps(fetchMock)
    expect(steps).toHaveLength(11)
    expect(steps.slice(0, 10)).toEqual(processContent.steps)
    expect(steps[10]).toEqual({ name: '', type: 'decision', branches: [], otherwise: '' })
  })

  it('removes a step and leaves the transitions that name it', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const card = await screen.findByTestId('studio-step-4', {}, slow)
    expect(card).toHaveTextContent('markApproved')
    await userEvent.click(within(card).getByRole('button', { name: 'Remove step' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const steps = sentSteps(fetchMock)
    expect(steps).toEqual(processContent.steps.filter((step) => step.name !== 'markApproved'))
    expect(steps[2].otherwise).toBe('markApproved')
    expect(sentPatch(fetchMock)).toHaveLength(1)
  })

  it('moves a step up and down in file order', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const first = await screen.findByTestId('studio-step-0', {}, slow)
    expect(within(first).getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(within(screen.getByTestId('studio-step-9')).getByRole('button', { name: 'Move down' })).toBeDisabled()
    await userEvent.click(within(first).getByRole('button', { name: 'Move down' }))
    await userEvent.click(within(screen.getByTestId('studio-step-9')).getByRole('button', { name: 'Move up' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    // Steps that swap places in an array of the same length are patched item by item, so apply the patch.
    let moved: unknown = processContent
    for (const operation of sentPatch(fetchMock)) {
      moved = setAt(moved, operation.path, operation.op === 'remove' ? undefined : operation.value)
    }
    const steps = (moved as typeof processContent).steps
    const [submit, managerApproval, ...rest] = processContent.steps
    const [markRejected, done] = [rest[rest.length - 2], rest[rest.length - 1]]
    expect(steps).toEqual([managerApproval, submit, ...rest.slice(0, -2), done, markRejected])
  })

  it.each(['/steps/1/outcomes/0/label/textKey', '/steps/1/label/textKey'])(
    'opens the form at the field for the path %s inside a task',
    async (path) => {
      stubResource(processItem, processContent)
      renderResource(processItem, undefined, vi.fn(), path)

      await screen.findByTestId('studio-step-1', {}, slow)
      await waitFor(() => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'))
    },
  )

  it('opens the form at the transition of an outcome for its path', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem, undefined, vi.fn(), '/steps/1/outcomes/0/next')

    await screen.findByTestId('studio-step-1', {}, slow)
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'))
  })

  it('changes a step type: forbidden keys go, required keys are added empty, the others stay', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const card = await screen.findByTestId('studio-step-0', {}, slow)
    await chooseOption('decision', within(card).getByTestId('studio-field-/steps/0/type'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/steps/0/type', value: 'decision' },
      { op: 'remove', path: '/steps/0/operation' },
      { op: 'remove', path: '/steps/0/set' },
      { op: 'remove', path: '/steps/0/next' },
      { op: 'add', path: '/steps/0/branches', value: [] },
      { op: 'add', path: '/steps/0/otherwise', value: '' },
    ])
  })

  it('changes a step to a task with its required keys empty and keeps the unknown key', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)
    await openFormTab()

    const card = await screen.findByTestId('studio-step-0', {}, slow)
    await chooseOption('task', within(card).getByTestId('studio-field-/steps/0/type'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const operations = sentPatch(fetchMock)
    expect(operations.map((operation) => operation.path)).toEqual([
      '/steps/0/type',
      '/steps/0/operation',
      '/steps/0/set',
      '/steps/0/next',
      '/steps/0/label',
      '/steps/0/assignee',
      '/steps/0/form',
      '/steps/0/outcomes',
    ])
    expect(operations.slice(4).map((operation) => operation.value)).toEqual([{ textKey: '' }, {}, '', []])
  })

  it('opens on the Design tab', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    expect(await screen.findByRole('tab', { name: 'Design' }, slow)).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'false')
  })

  it('shows a node for a step added in the JSON view, without saving it', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    await userEvent.click(await screen.findByRole('tab', { name: 'JSON' }, slow))
    const json = screen.getByRole('textbox', { name: 'JSON' })
    const withExtraStep = { ...processContent, steps: [...processContent.steps, { name: 'notify', type: 'end' }] }
    await userEvent.clear(json)
    await userEvent.click(json)
    await userEvent.paste(JSON.stringify(withExtraStep, null, 2))
    await userEvent.click(screen.getByRole('tab', { name: 'Design' }))

    expect(await screen.findByTestId('studio-graph-node-10', {}, slow)).toHaveAccessibleName('notify · end')
    expect(requests(fetchMock, 'PATCH')).toBe(0)
  })

  it.each(['light', 'dark'] as const)(
    'reaches the edges and then the nodes with Tab and opens a step with Enter in %s mode',
    async (mode) => {
      localStorage.setItem('axis.themeMode', mode)
      stubResource(processItem, processContent)
      renderResource(processItem)
      await screen.findByTestId('studio-graph-node-0', {}, slow)
      expect(document.querySelector('.react-flow')).toHaveClass(mode)
      // The Design tab is focused first, then its panel. React Flow puts the edges before the nodes.
      await userEvent.click(screen.getByRole('tab', { name: 'Design' }))
      await userEvent.tab()
      expect(screen.getByRole('tabpanel')).toHaveFocus()

      const edges = document.querySelectorAll('[data-testid^="rf__edge-"]')
      expect(edges).toHaveLength(buildProcessGraph(processContent, []).transitions.length)
      for (const edge of edges) {
        await userEvent.tab()
        expect(edge).toHaveFocus()
        expect(edge).toHaveAttribute('tabindex', '0')
      }

      for (const [index, step] of processContent.steps.entries()) {
        await userEvent.tab()
        const node = screen.getByTestId(`studio-graph-node-${index}`)
        expect(node).toHaveFocus()
        expect(node).toHaveAccessibleName(`${step.name} · ${step.type}`)
        await userEvent.keyboard('{Enter}')
        expect(
          within(screen.getByTestId('studio-process-design')).getByTestId(`studio-step-${index}`),
        ).toBeInTheDocument()
        expect(node).toHaveAttribute('aria-current', 'true')
      }
    },
  )

  it('shows a hint in the panel until a step is selected', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    const design = await screen.findByTestId('studio-process-design', {}, slow)
    expect(within(design).getByText('Select a step on the canvas.')).toBeInTheDocument()
    expect(within(design).queryByTestId('studio-step-0')).not.toBeInTheDocument()
  })

  it('selects a step when its node is clicked, and no other', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    fireEvent.click(await screen.findByTestId('studio-graph-node-1', {}, slow))
    expect(screen.getByTestId('studio-graph-node-1')).toHaveAttribute('aria-current', 'true')
    expect(screen.getByTestId('studio-graph-node-0')).not.toHaveAttribute('aria-current')
  })

  it('selects the step that a path inside it names, and opens the Form tab', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem, undefined, vi.fn(), '/steps/1/next')

    await waitFor(() => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'))
    await userEvent.click(screen.getByRole('tab', { name: 'Design' }))
    expect(await screen.findByTestId('studio-graph-node-1', {}, slow)).toHaveAttribute('aria-current', 'true')
    expect(screen.getByTestId('studio-graph-node-0')).not.toHaveAttribute('aria-current')
  })

  it('selects the step that an edge leaves when the edge is clicked', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    fireEvent.click(await screen.findByTestId('rf__edge-/steps/2/otherwise', {}, slow))
    const design = screen.getByTestId('studio-process-design')
    expect(within(design).getByTestId('studio-step-2')).toBeInTheDocument()
    expect(screen.getByTestId('studio-graph-node-2')).toHaveAttribute('aria-current', 'true')
  })

  it('moves the edge when the next step is changed in the panel, and Undo moves it back', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    fireEvent.click(await screen.findByTestId('studio-graph-node-0', {}, slow))
    const edge = () => screen.getByTestId('rf__edge-/steps/0/next')
    const oldTarget = processContent.steps.findIndex((step) => step.name === processContent.steps[0].next)
    const oldName = `submit to ${processContent.steps[oldTarget].name} · next`
    expect(edge()).toHaveAccessibleName(oldName)

    const panel = within(screen.getByTestId('studio-process-design'))
    await chooseOption('done', panel.getByTestId('studio-field-/steps/0/next'))
    expect(edge()).toHaveAccessibleName('submit to done · next')

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(edge()).toHaveAccessibleName(oldName)
  })

  it('removes the selected step with Delete, leaves the transitions that named it, and Undo restores both', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    const node = await screen.findByTestId('studio-graph-node-9', {}, slow)
    expect(node).toHaveAccessibleName('done · end')
    for (const source of [6, 7, 8]) {
      expect(screen.getByTestId(`rf__edge-/steps/${source}/next`)).toBeInTheDocument()
    }
    fireEvent.click(node)
    act(() => node.focus())
    await userEvent.keyboard('{Delete}')

    expect(screen.queryByTestId('studio-graph-node-9')).not.toBeInTheDocument()
    for (const source of [6, 7, 8]) {
      expect(screen.queryByTestId(`rf__edge-/steps/${source}/next`)).not.toBeInTheDocument()
    }
    expect(screen.getByTestId('studio-process-announcement')).toHaveTextContent('Removed step done')
    expect(screen.getByText('Select a step on the canvas.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const named = JSON.parse((screen.getByRole('textbox', { name: 'JSON' }) as HTMLTextAreaElement).value)
    expect(named.steps).toHaveLength(9)
    expect(named.steps[6].next).toBe('done')
    await userEvent.click(screen.getByRole('tab', { name: 'Design' }))

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(await screen.findByTestId('studio-graph-node-9')).toBeInTheDocument()
    for (const source of [6, 7, 8]) {
      expect(screen.getByTestId(`rf__edge-/steps/${source}/next`)).toBeInTheDocument()
    }
  })

  it('selects a focused edge with Enter and clears its transition with Delete, announcing it', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    const edge = await screen.findByTestId('rf__edge-/steps/2/otherwise', {}, slow)
    expect(edge).toHaveAttribute('tabindex', '0')
    expect(edge).toHaveAccessibleName('financeCheck to markApproved · otherwise')
    act(() => edge.focus())
    await userEvent.keyboard('{Enter}')

    const design = within(screen.getByTestId('studio-process-design'))
    expect(screen.getByTestId('rf__edge-/steps/2/otherwise')).toHaveAttribute('aria-current', 'true')
    expect(screen.getByTestId('studio-graph-node-2')).toHaveAttribute('aria-current', 'true')
    expect(design.getByRole('button', { name: 'Remove transition' })).toBeInTheDocument()

    await userEvent.keyboard('{Delete}')

    expect(screen.queryByTestId('rf__edge-/steps/2/otherwise')).not.toBeInTheDocument()
    expect(screen.getByTestId('studio-graph-node-4')).toBeInTheDocument()
    expect(within(design.getByTestId('studio-field-/steps/2/otherwise')).queryByText('markApproved')).toBeNull()
    expect(design.queryByRole('button', { name: 'Remove transition' })).not.toBeInTheDocument()
    expect(design.getByTestId('studio-step-2')).toBeInTheDocument()
    expect(design.getByTestId('studio-process-announcement')).toHaveTextContent(
      'Removed transition financeCheck to markApproved',
    )
  })

  it('clears the transition with the Remove transition button, and one Undo restores it', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    fireEvent.click(await screen.findByTestId('rf__edge-/steps/1/outcomes/0', {}, slow))
    const design = within(screen.getByTestId('studio-process-design'))
    await userEvent.click(design.getByRole('button', { name: 'Remove transition' }))

    expect(screen.queryByTestId('rf__edge-/steps/1/outcomes/0')).not.toBeInTheDocument()
    expect(screen.getByTestId('rf__edge-/steps/1/outcomes/1')).toBeInTheDocument()
    expect(design.getByTestId('studio-process-announcement')).toHaveTextContent(
      'Removed transition managerApproval to financeCheck · approve',
    )
    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(screen.getByTestId('rf__edge-/steps/1/outcomes/0')).toBeInTheDocument()
  })

  it('removes the selected step with Delete and announces it', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    const node = await screen.findByTestId('studio-graph-node-8', {}, slow)
    fireEvent.click(node)
    act(() => node.focus())
    const design = within(screen.getByTestId('studio-process-design'))
    expect(design.queryByRole('button', { name: 'Remove transition' })).not.toBeInTheDocument()
    await userEvent.keyboard('{Delete}')

    expect(screen.queryByRole('button', { name: 'markRejected · operation' })).not.toBeInTheDocument()
    expect(screen.getByTestId('studio-graph-node-8')).toHaveAccessibleName('done · end')
    expect(design.getByTestId('studio-process-announcement')).toHaveTextContent('Removed step markRejected')
  })

  it('does not remove a step when Delete is pressed outside the canvas', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem)

    fireEvent.click(await screen.findByTestId('studio-graph-node-8', {}, slow))
    const design = within(screen.getByTestId('studio-process-design'))
    await userEvent.click(design.getByRole('textbox', { name: 'Step name' }))
    await userEvent.keyboard('{Backspace}{Delete}')

    expect(screen.getByTestId('studio-graph-node-8')).toBeInTheDocument()
    expect(design.getByTestId('studio-process-announcement')).toHaveTextContent('')
  })

  it('marks the node of a step that a diagnostic path names', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem, undefined, vi.fn(), null, ['/steps/1/next'])

    const marked = await screen.findByTestId('studio-graph-node-1', {}, slow)
    expect(marked).toHaveAttribute('data-marked', 'true')
    expect(marked).toHaveAccessibleName(
      `${processContent.steps[1].name} · ${processContent.steps[1].type} · has diagnostics`,
    )
    const other = screen.getByTestId('studio-graph-node-0')
    expect(other).not.toHaveAttribute('data-marked')
    expect(other).not.toHaveAccessibleName(/has diagnostics/)
  })
})

describe('StudioEditor for a text resource', () => {
  const enId = '5b0f8c1e-27d7-4d4e-9d0f-0b1b8d7a1111'
  const viId = '5b0f8c1e-27d7-4d4e-9d0f-0b1b8d7a2222'
  const enItem: StudioResourceItem = {
    id: enId,
    kind: 'text',
    name: 'en',
    file: 'texts/en.json',
    version: 'e1',
    usedBy: 0,
  }
  const viItem: StudioResourceItem = { ...enItem, id: viId, name: 'vi', file: 'texts/vi.json', version: 'v1' }
  const enUrl = `${folder}/resources/${enId}`
  const viUrl = `${folder}/resources/${viId}`

  function textContent(id: string, locale: string, texts: Record<string, string>) {
    return { id, kind: 'text', name: locale, formatVersion: 1, locale, texts }
  }

  function stubTexts() {
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (input === enUrl && init?.method === 'PATCH') {
        return json({ version: 'e3', diagnostics: [] })
      }
      if (input === enUrl) {
        return json({ ...enItem, content: textContent(enId, 'en', { greeting: 'Hello' }) })
      }
      if (input === viUrl) {
        return json({ ...viItem, content: textContent(viId, 'vi', { greeting: 'Xin chào' }) })
      }
      if (input === `${folder}/texts/added` && init?.method === 'PUT') {
        return json({
          resources: [
            { id: enId, locale: 'en', version: 'e2' },
            { id: viId, locale: 'vi', version: 'v2' },
          ],
          diagnostics: [],
        })
      }
      return new Response(null, { status: 404 })
    })
    vi.stubGlobal('fetch', fetchMock)
    return fetchMock
  }

  function renderTexts() {
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioEditor
            application="Shop"
            item={enItem}
            resources={[enItem, viItem]}
            path={null}
            diagnosticPaths={[]}
            onOpen={vi.fn()}
            onSaved={vi.fn()}
            onTextsWritten={vi.fn()}
            onDeleted={vi.fn()}
          />
        </TextProvider>
      </ThemeModeProvider>,
    )
  }

  function calls(fetchMock: ReturnType<typeof vi.fn>, method: string) {
    return fetchMock.mock.calls.filter(([, init]) => init?.method === method) as [string, RequestInit][]
  }

  it('saves one patch to the open locale only, holding only the edited key', async () => {
    const fetchMock = stubTexts()
    renderTexts()

    const row = (await screen.findAllByTestId('studio-text-row', undefined, slow))[0]
    expect(within(row).getByText('Xin chào')).toBeVisible()
    await userEvent.clear(within(row).getByRole('textbox', { name: 'en' }))
    await userEvent.type(within(row).getByRole('textbox', { name: 'en' }), 'Hi')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-saved')

    const patches = calls(fetchMock, 'PATCH')
    expect(patches).toHaveLength(1)
    expect(patches[0][0]).toBe(enUrl)
    expect(new Headers(patches[0][1].headers).get('If-Match')).toBe('"e1"')
    expect(JSON.parse(patches[0][1].body as string)).toEqual([{ op: 'replace', path: '/texts/greeting', value: 'Hi' }])
    expect(calls(fetchMock, 'PUT')).toHaveLength(0)
    expect(fetchMock.mock.calls.some(([input, init]) => input === viUrl && init?.method !== undefined)).toBe(false)
  })

  it('sends the version of the texts route answer on the next save, without the added key', async () => {
    const fetchMock = stubTexts()
    renderTexts()

    await screen.findAllByTestId('studio-text-row', undefined, slow)
    const add = screen.getByTestId('studio-text-add')
    await userEvent.type(within(add).getByRole('textbox', { name: 'New key' }), 'added')
    await userEvent.type(within(add).getByRole('textbox', { name: 'en' }), 'A')
    await userEvent.type(within(add).getByRole('textbox', { name: 'vi' }), 'B')
    await userEvent.click(within(add).getByRole('button', { name: 'Add key' }))

    const added = await screen.findAllByTestId('studio-text-row')
    expect(added.map((row) => row.getAttribute('data-key'))).toEqual(['added', 'greeting'])
    const puts = calls(fetchMock, 'PUT')
    expect(puts).toHaveLength(1)
    expect(JSON.parse(puts[0][1].body as string)).toEqual({ texts: { en: 'A', vi: 'B' } })
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()

    const greeting = within(added[1]).getByRole('textbox', { name: 'en' })
    await userEvent.clear(greeting)
    await userEvent.type(greeting, 'Hi')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-saved')

    const patches = calls(fetchMock, 'PATCH')
    expect(patches).toHaveLength(1)
    expect(new Headers(patches[0][1].headers).get('If-Match')).toBe('"e2"')
    expect(JSON.parse(patches[0][1].body as string)).toEqual([{ op: 'replace', path: '/texts/greeting', value: 'Hi' }])
  })
})

const pageId = 'b5a5f1cb-4f0f-4f3d-9a54-9d6e3a1b7f10'
const pageItem: StudioResourceItem = {
  id: pageId,
  kind: 'page',
  name: 'MyTasks',
  file: 'pages/my-tasks.json',
  version: 'p1',
  usedBy: 0,
}
const pageContent = { id: pageId, kind: 'page', name: 'MyTasks', formatVersion: 1, widgets: [{ type: 'taskInbox' }] }

const namesWithTexts = {
  ...namesWithEntity,
  locales: ['en', 'vi'],
  texts: [{ key: 'pages.home.title', texts: { en: 'Home', vi: 'Trang chủ' }, usedBy: 3 }],
} as unknown as StudioNames

/** `stubResource` that also answers the text write with `textWrite`. */
function stubPage(content: unknown, textWrite: () => Response) {
  const { fetchMock } = stubResource(pageItem, content)
  const base = fetchMock.getMockImplementation()!
  fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
    init?.method === 'PUT' ? textWrite() : base(input, init),
  )
  return fetchMock
}

function sentTexts(fetchMock: ReturnType<typeof vi.fn>) {
  return fetchMock.mock.calls.filter(([, call]) => call?.method === 'PUT') as [string, RequestInit][]
}

describe('StudioEditor for a label', () => {
  it('writes the texts of every locale in one call and then saves the key as a new label', async () => {
    const fetchMock = stubPage(pageContent, () => json({ resources: [], diagnostics: [] }))
    const onTextsWritten = vi.fn()
    renderResource(pageItem, namesWithTexts, onTextsWritten)
    await openFormTab()

    const key = await screen.findByRole('textbox', { name: 'Text key' }, slow)
    expect(key).toHaveValue('pages.myTasks.title')
    await userEvent.type(screen.getByRole('textbox', { name: 'en' }), 'My tasks')
    await userEvent.type(screen.getByRole('textbox', { name: 'vi' }), 'Việc của tôi')
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Write texts' }))

    await screen.findByTestId('studio-label-written')
    const writes = sentTexts(fetchMock)
    expect(writes).toHaveLength(1)
    expect(writes[0][0]).toBe(`${folder}/texts/pages.myTasks.title`)
    expect(JSON.parse(writes[0][1].body as string)).toEqual({ texts: { en: 'My tasks', vi: 'Việc của tôi' } })
    expect(onTextsWritten).toHaveBeenCalledWith([])

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([{ op: 'add', path: '/title', value: { textKey: 'pages.myTasks.title' } }])
  })

  it('takes an empty key as no key and suggests one', async () => {
    stubPage({ ...pageContent, title: { textKey: '' } }, () => json({ resources: [], diagnostics: [] }))
    renderResource(pageItem, namesWithTexts)
    await openFormTab()

    expect(await screen.findByRole('textbox', { name: 'Text key' }, slow)).toHaveValue('pages.myTasks.title')
  })

  it('shows how many other labels use the key of a saved label', async () => {
    stubPage({ ...pageContent, title: { textKey: 'pages.home.title' } }, () => json({ resources: [], diagnostics: [] }))
    renderResource(pageItem, namesWithTexts)
    await openFormTab()

    expect(await screen.findByRole('textbox', { name: 'Text key' }, slow)).toHaveValue('pages.home.title')
    expect(screen.getByRole('textbox', { name: 'en' })).toHaveValue('Home')
    expect(screen.getByRole('textbox', { name: 'vi' })).toHaveValue('Trang chủ')
    expect(screen.getByTestId('studio-label-used-by')).toHaveTextContent('2')
  })

  it('shows the failure and leaves the draft unchanged when the write fails', async () => {
    const fetchMock = stubPage(pageContent, () => new Response(null, { status: 400 }))
    renderResource(pageItem, namesWithTexts)
    await openFormTab()

    await userEvent.type(await screen.findByRole('textbox', { name: 'en' }, slow), 'My tasks')
    await userEvent.click(screen.getByRole('button', { name: 'Write texts' }))

    expect(await screen.findByTestId('studio-label-write-error')).toHaveTextContent('The texts could not be written.')
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
    expect(sentTexts(fetchMock)).toHaveLength(1)
  })
})

const pageNames = {
  ...namesWithTexts,
  dataSources: [
    { id: 'd1', name: 'PurchaseRequestList', module: null, parameters: [], columns: [], groupBy: null },
    { id: 'd2', name: 'SupplierList', module: null, parameters: [], columns: [], groupBy: null },
  ],
  pages: [{ id: 'g1', name: 'ReviewForm', parameters: [] }],
  forms: [{ id: 'f1', name: 'RequestForm' }],
  texts: [{ key: 'pages.home.title', texts: { en: 'Home', vi: 'Trang chủ' }, usedBy: 1 }],
} as unknown as StudioNames

const tablePage = {
  id: pageId,
  kind: 'page',
  name: 'MyTasks',
  formatVersion: 1,
  title: { textKey: 'pages.home.title' },
  widgets: [
    {
      type: 'form',
      entity: 'PurchaseRequest',
      actions: [{ type: 'startProcess', process: 'Approval', label: { textKey: 'pages.home.title' } }],
      'x-note': 'keep',
    },
  ],
  'x-extra': 1,
}

describe('StudioEditor for a page', () => {
  it('opens the page editor with the widget list and the selected widget panel', async () => {
    stubResource(pageItem, tablePage)
    renderResource(pageItem, pageNames)
    await openFormTab()

    expect(await screen.findByTestId('studio-page-editor', {}, slow)).toBeInTheDocument()
    expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('form · PurchaseRequest')
    expect(screen.getByTestId('studio-field-/widgets/0/actions/0/process')).toBeInTheDocument()
  })

  it('opens on the Design tab', async () => {
    stubResource(pageItem, tablePage)
    renderResource(pageItem, pageNames)

    await waitFor(
      () => expect(screen.getByRole('tab', { name: 'Design' })).toHaveAttribute('aria-selected', 'true'),
      slow,
    )
  })

  it('selects the Form tab and scrolls to a field of a widget on a path', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubResource(pageItem, tablePage)
      renderResource(pageItem, pageNames, vi.fn(), '/widgets/0/actions/0/process')

      await waitFor(
        () => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'),
        slow,
      )
      const field = await screen.findByTestId('studio-field-/widgets/0/actions/0/process', {}, slow)
      await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' }))
      expect(scrollIntoView.mock.contexts).toEqual([field])
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('moves from the Design tab to the Form tab for a path opened later', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubResource(pageItem, tablePage)
      const { setPath } = renderResource(pageItem, pageNames)
      await waitFor(
        () => expect(screen.getByRole('tab', { name: 'Design' })).toHaveAttribute('aria-selected', 'true'),
        slow,
      )

      setPath('/widgets/0/actions/0/process')

      await waitFor(
        () => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'),
        slow,
      )
      const field = await screen.findByTestId('studio-field-/widgets/0/actions/0/process', {}, slow)
      await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' }))
      expect(scrollIntoView.mock.contexts).toEqual([field])
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('selects the JSON tab for a path the page editor cannot show', async () => {
    stubResource(pageItem, tablePage)
    renderResource(pageItem, pageNames, vi.fn(), '/x-extra')

    await waitFor(
      () => expect(screen.getByRole('tab', { name: 'JSON' })).toHaveAttribute('aria-selected', 'true'),
      slow,
    )
  })

  it('patches only the edit and keeps the unknown keys on the file and the widget', async () => {
    const content = { ...tablePage, widgets: [{ type: 'table', dataSource: 'PurchaseRequestList', 'x-note': 'keep' }] }
    const { fetchMock } = stubResource(pageItem, content)
    renderResource(pageItem, pageNames)
    await openFormTab()

    const field = await screen.findByTestId('studio-field-/widgets/0/dataSource', {}, slow)
    await userEvent.click(within(field).getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('SupplierList'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([{ op: 'replace', path: '/widgets/0/dataSource', value: 'SupplierList' }])
  })

  it('removes the keys of the old type in one patch and leaves an unknown key alone', async () => {
    const content = {
      ...tablePage,
      widgets: [{ type: 'form', form: 'RequestForm', actions: tablePage.widgets[0].actions, 'x-note': 'keep' }],
    }
    const { fetchMock } = stubResource(pageItem, content)
    renderResource(pageItem, pageNames)
    await openFormTab()

    const field = await screen.findByTestId('studio-field-/widgets/0/type', {}, slow)
    await userEvent.click(within(field).getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('table'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const operations = sentPatch(fetchMock)
    expect(operations).toEqual([
      { op: 'replace', path: '/widgets/0/type', value: 'table' },
      { op: 'remove', path: '/widgets/0/form' },
      { op: 'remove', path: '/widgets/0/actions' },
    ])
    expect(operations.some((operation) => operation.path.includes('x-note'))).toBe(false)
  })

  describe('undo and redo', () => {
    const undoButton = () => screen.getByRole('button', { name: 'Undo' })
    const redoButton = () => screen.getByRole('button', { name: 'Redo' })
    const tableAndInbox = {
      ...pageContent,
      widgets: [{ type: 'taskInbox' }, { type: 'table', dataSource: 'PurchaseRequestList' }],
    }

    it('starts with Undo and Redo off', async () => {
      stubResource(pageItem, pageContent)
      renderResource(pageItem, pageNames)
      await openFormTab()

      await screen.findByTestId('studio-page-editor', {}, slow)
      expect(undoButton()).toBeDisabled()
      expect(redoButton()).toBeDisabled()
    })

    it('undoes and redoes the typing in one box as one step', async () => {
      stubResource(pageItem, pageContent)
      renderResource(pageItem, pageNames)
      await openFormTab()

      const name = await screen.findByRole('textbox', { name: 'Name' }, slow)
      await userEvent.type(name, 'abc')
      expect(name).toHaveValue('MyTasksabc')

      await userEvent.click(undoButton())
      expect(name).toHaveValue('MyTasks')
      expect(undoButton()).toBeDisabled()
      expect(redoButton()).toBeEnabled()

      await userEvent.click(redoButton())
      expect(name).toHaveValue('MyTasksabc')
      expect(redoButton()).toBeDisabled()
    })

    it('makes the typing in a box after leaving it a new step', async () => {
      stubResource(pageItem, pageContent)
      renderResource(pageItem, pageNames)
      await openFormTab()

      const name = await screen.findByRole('textbox', { name: 'Name' }, slow)
      await userEvent.type(name, 'a')
      await userEvent.click(document.body)
      await userEvent.type(name, 'b')
      await userEvent.click(undoButton())

      expect(name).toHaveValue('MyTasksa')
    })

    it('undoes and redoes a data source pick with the buttons and with the keys', async () => {
      stubResource(pageItem, tableAndInbox)
      renderResource(pageItem, pageNames)
      await openFormTab()

      await userEvent.click(await screen.findByTestId('studio-widget-/widgets/1', {}, slow))
      await userEvent.click(within(screen.getByTestId('studio-field-/widgets/1/dataSource')).getByRole('combobox'))
      await userEvent.click(await screen.findByTitle('SupplierList'))
      expect(screen.getByTestId('studio-field-/widgets/1/dataSource')).toHaveTextContent('SupplierList')

      await userEvent.click(undoButton())
      expect(screen.getByTestId('studio-field-/widgets/1/dataSource')).toHaveTextContent('PurchaseRequestList')
      await userEvent.click(redoButton())
      expect(screen.getByTestId('studio-field-/widgets/1/dataSource')).toHaveTextContent('SupplierList')

      await userEvent.keyboard('{Control>}z{/Control}')
      expect(screen.getByTestId('studio-field-/widgets/1/dataSource')).toHaveTextContent('PurchaseRequestList')
      await userEvent.keyboard('{Control>}{Shift>}z{/Shift}{/Control}')
      expect(screen.getByTestId('studio-field-/widgets/1/dataSource')).toHaveTextContent('SupplierList')
    })

    it('undoes and redoes a move of a widget', async () => {
      stubResource(pageItem, tableAndInbox)
      renderResource(pageItem, pageNames)
      await openFormTab()

      await screen.findByTestId('studio-widget-/widgets/1', {}, slow)
      await userEvent.click(
        within(screen.getByTestId('studio-widget-row-/widgets/1')).getByRole('button', { name: 'Move up' }),
      )
      expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('table')

      await userEvent.click(undoButton())
      expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('taskInbox')
      await userEvent.click(redoButton())
      expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('table')

      await userEvent.keyboard('{Meta>}z{/Meta}')
      expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('taskInbox')
      await userEvent.keyboard('{Meta>}{Shift>}z{/Shift}{/Meta}')
      expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('table')
    })

    it('undoes and redoes an edit in the JSON view with the keys', async () => {
      stubResource(pageItem, pageContent)
      renderResource(pageItem, pageNames)
      await openFormTab()

      await screen.findByTestId('studio-page-editor', {}, slow)
      await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
      const view = screen.getByRole('textbox', { name: 'JSON' })
      const original = (view as HTMLTextAreaElement).value
      await userEvent.click(view)
      fireEvent.change(view, { target: { value: original.replace('MyTasks', 'Renamed') } })
      expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()

      await userEvent.keyboard('{Control>}z{/Control}')
      expect(view).toHaveValue(original)
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()

      await userEvent.keyboard('{Control>}{Shift>}z{/Shift}{/Control}')
      expect(view).toHaveValue(original.replace('MyTasks', 'Renamed'))
      expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
    })

    it('turns Save off again after undoing an invalid edit in the JSON view', async () => {
      stubResource(pageItem, pageContent)
      renderResource(pageItem, pageNames)
      await openFormTab()

      await screen.findByTestId('studio-page-editor', {}, slow)
      await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
      const view = screen.getByRole('textbox', { name: 'JSON' })
      await userEvent.click(view)
      fireEvent.change(view, { target: { value: '{' } })
      expect(screen.getByTestId('studio-json-error')).toBeInTheDocument()

      await userEvent.click(undoButton())
      expect(screen.queryByTestId('studio-json-error')).not.toBeInTheDocument()
    })

    it('keeps the history after a save, and the next save sends the patch that reverts the edit', async () => {
      const { fetchMock } = stubResource(pageItem, pageContent)
      renderResource(pageItem, pageNames)
      await openFormTab()

      const name = await screen.findByRole('textbox', { name: 'Name' }, slow)
      await userEvent.type(name, 'abc')
      await userEvent.click(screen.getByRole('button', { name: 'Save' }))
      await screen.findByTestId('studio-saved')
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
      expect(undoButton()).toBeEnabled()

      await userEvent.click(undoButton())
      expect(name).toHaveValue('MyTasks')
      expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
      await userEvent.click(screen.getByRole('button', { name: 'Save' }))

      await waitFor(() => expect(requests(fetchMock, 'PATCH')).toBe(2))
      const [, init] = fetchMock.mock.calls.filter(([, call]) => call?.method === 'PATCH')[1]
      expect(JSON.parse(init!.body as string)).toEqual([{ op: 'replace', path: '/name', value: 'MyTasks' }])
    })

    it('clears the history when the author reloads after a conflict', async () => {
      const { fetchMock } = stubResource(pageItem, pageContent)
      const base = fetchMock.getMockImplementation()!
      fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
        init?.method === 'PATCH' ? json({ title: 'Conflict.' }, 409) : base(input, init),
      )
      renderResource(pageItem, pageNames)
      await openFormTab()

      const name = await screen.findByRole('textbox', { name: 'Name' }, slow)
      await userEvent.type(name, 'abc')
      await userEvent.click(screen.getByRole('button', { name: 'Save' }))
      await screen.findByTestId('studio-conflict')
      expect(undoButton()).toBeEnabled()

      await userEvent.click(screen.getByRole('button', { name: 'Reload' }))

      await waitFor(() => expect(screen.queryByTestId('studio-conflict')).not.toBeInTheDocument())
      expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('MyTasks')
      expect(undoButton()).toBeDisabled()
      expect(redoButton()).toBeDisabled()
    })

    it('leaves the keys to the browser when the focus is outside the editor', async () => {
      stubResource(pageItem, pageContent)
      renderResource(pageItem, pageNames)
      await openFormTab()

      const name = await screen.findByRole('textbox', { name: 'Name' }, slow)
      await userEvent.type(name, 'abc')
      const outside = document.createElement('input')
      document.body.append(outside)
      outside.focus()

      await userEvent.keyboard('{Control>}z{/Control}')

      expect(name).toHaveValue('MyTasksabc')
      outside.remove()
    })
  })
})

describe('StudioEditor for a form', () => {
  const formId = '1db765b0-483d-456b-9939-17e2a36de3b6'
  const formItem: StudioResourceItem = {
    id: formId,
    kind: 'form',
    name: 'TicketEdit',
    file: 'forms/ticket-edit.json',
    version: 'f1',
    usedBy: 0,
  }
  const formContent = {
    id: formId,
    kind: 'form',
    name: 'TicketEdit',
    formatVersion: 1,
    entity: 'Ticket',
    sections: [{ name: 'problem', title: { textKey: 'ticket.sections.problem' }, fields: [{ field: 'title' }] }],
  }
  const formNames = {
    ...namesWithEntity,
    entities: [
      {
        id: 'e1',
        name: 'Ticket',
        module: null,
        fields: [
          { name: 'title', type: 'text', target: null, values: null },
          { name: 'resolution', type: 'text', target: null, values: null },
        ],
      },
    ],
  } as unknown as StudioNames

  const steppedContent = {
    ...formContent,
    sections: [
      { name: 'problem', title: { textKey: 'ticket.sections.problem' }, fields: [{ field: 'title' }] },
      { name: 'details', title: { textKey: 'ticket.sections.details' }, fields: [{ field: 'resolution' }] },
    ],
    steps: [
      { title: { textKey: 'ticket.steps.problem' }, sections: ['problem'] },
      { title: { textKey: 'ticket.steps.details' }, sections: ['details'] },
    ],
  }

  function stubForm(content: unknown = formContent) {
    const { fetchMock } = stubResource(formItem, content)
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input: string, init?: RequestInit) =>
      input === `${folder}/resources/${formId}/references` ? json({ usedBy: [] }) : base(input, init),
    )
  }

  function renderForm() {
    render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioNamesContext.Provider value={formNames}>
              <StudioEditor
                application="Shop"
                item={formItem}
                resources={[formItem]}
                path={null}
                diagnosticPaths={[]}
                onOpen={vi.fn()}
                onSaved={vi.fn()}
                onTextsWritten={vi.fn()}
                onDeleted={vi.fn()}
              />
            </StudioNamesContext.Provider>
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )
  }

  it('opens on the Design tab', async () => {
    stubForm()
    renderForm()

    await waitFor(
      () => expect(screen.getByRole('tab', { name: 'Design' })).toHaveAttribute('aria-selected', 'true'),
      slow,
    )
  })

  it('undoes and redoes the insert of a palette field', async () => {
    stubForm()
    renderForm()

    await userEvent.click(await screen.findByRole('tab', { name: 'Design' }, slow))
    await userEvent.click(await screen.findByTestId('studio-form-palette-resolution', {}, slow))
    expect(screen.queryByTestId('studio-form-palette-resolution')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Undo' })).toBeEnabled()

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(screen.getByTestId('studio-form-palette-resolution')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Redo' }))
    expect(screen.queryByTestId('studio-form-palette-resolution')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const view = screen.getByRole('textbox', { name: 'JSON' }) as HTMLTextAreaElement
    expect(JSON.parse(view.value).sections[0].fields).toEqual([{ field: 'title' }, { field: 'resolution' }])
  })

  it('undoes an Add step in one step, restoring sections and steps', async () => {
    stubForm()
    renderForm()

    await userEvent.click(await screen.findByRole('tab', { name: 'Design' }, slow))
    await userEvent.click(await screen.findByRole('button', { name: 'Add step' }, slow))
    expect(screen.getByRole('button', { name: 'Undo' })).toBeEnabled()

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled()

    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const draft = JSON.parse((screen.getByRole('textbox', { name: 'JSON' }) as HTMLTextAreaElement).value)
    expect(draft.sections).toEqual(formContent.sections)
    expect(draft.steps).toBeUndefined()
  })

  it('undoes an Add section in one step', async () => {
    stubForm()
    renderForm()

    await userEvent.click(await screen.findByRole('tab', { name: 'Design' }, slow))
    await userEvent.click(await screen.findByRole('button', { name: 'Add section' }, slow))
    expect(screen.getAllByTestId('studio-outline-section')).toHaveLength(2)

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(screen.getAllByTestId('studio-outline-section')).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled()
  })

  it('undoes a Remove section in one step, restoring sections and steps and returning the fields to the palette', async () => {
    stubForm(steppedContent)
    renderForm()

    await userEvent.click(await screen.findByRole('tab', { name: 'Design' }, slow))
    await userEvent.click(screen.getAllByTestId('studio-outline-section')[0])
    await userEvent.click(screen.getByRole('button', { name: 'Remove section' }))
    expect(screen.getAllByTestId('studio-outline-section')).toHaveLength(1)
    expect(screen.getByTestId('studio-form-palette-title')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Undo' })).toBeEnabled()

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const draft = JSON.parse((screen.getByRole('textbox', { name: 'JSON' }) as HTMLTextAreaElement).value)
    expect(draft.sections).toEqual(steppedContent.sections)
    expect(draft.steps).toEqual(steppedContent.steps)
  })

  it('undoes a Remove step in one step, restoring the step', async () => {
    stubForm(steppedContent)
    renderForm()

    await userEvent.click(await screen.findByRole('tab', { name: 'Design' }, slow))
    await userEvent.click(screen.getAllByTestId('studio-outline-step')[1])
    await userEvent.click(screen.getByRole('button', { name: 'Remove step' }))
    expect(screen.getAllByTestId('studio-outline-step')).toHaveLength(1)

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(screen.getAllByTestId('studio-outline-step')).toHaveLength(2)
    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))
    const draft = JSON.parse((screen.getByRole('textbox', { name: 'JSON' }) as HTMLTextAreaElement).value)
    expect(draft.sections).toEqual(steppedContent.sections)
    expect(draft.steps).toEqual(steppedContent.steps)
  })
})

describe('StudioEditor for an entity', () => {
  const designedItem: StudioResourceItem = { ...resources[1], id: 'a4e6d3a1-77a0-4b0d-8b0c-5c1d6f0a9b01' }
  const designed = {
    id: designedItem.id,
    kind: 'entity',
    name: 'Invoice',
    formatVersion: 1,
    label: { textKey: 'entity.invoice' },
    fields: [
      { name: 'total', type: 'decimal', unique: true, precision: 18, scale: 2 },
      { name: 'code', type: 'text', maxLength: 20 },
      { name: 'note', type: 'text' },
    ],
  }

  function sentFields(fetchMock: ReturnType<typeof vi.fn>): unknown {
    return sentPatch(fetchMock).find((operation) => operation.path === '/fields')!.value
  }

  it('opens on the Form tab', async () => {
    stubResource(resources[1], entityContent)
    renderResource(resources[1])

    await waitFor(
      () => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'),
      slow,
    )
  })

  it('opens in the designer and keeps the JSON tab with the draft', async () => {
    stubResource(designedItem, designed)
    renderResource(designedItem)

    const designer = await screen.findByTestId('studio-entity-designer', {}, slow)
    expect(designer).toBeVisible()
    await userEvent.click(screen.getByRole('tab', { name: 'JSON' }))

    expect(JSON.parse((screen.getByRole('textbox', { name: 'JSON' }) as HTMLTextAreaElement).value)).toEqual(designed)
  })

  it('changes a decimal field to enum: the type changes, precision and scale go and unique stays', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await chooseOption('enum', await screen.findByTestId('studio-field-/fields/0/type', {}, slow))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/fields/0/type', value: 'enum' },
      { op: 'remove', path: '/fields/0/precision' },
      { op: 'remove', path: '/fields/0/scale' },
    ])
  })

  it('adds a field', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await userEvent.click(await screen.findByRole('button', { name: 'Add field' }, slow))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentFields(fetchMock)).toEqual([...designed.fields, { name: '', type: 'text' }])
  })

  it('removes a field and, on Drop data, saves the entity and a dropField migration in one changes call', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/1', {}, slow)
    await userEvent.click(within(row).getByRole('button', { name: 'Remove field' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('code')
    await userEvent.click(screen.getByRole('button', { name: 'Drop data' }))

    await screen.findByTestId('studio-saved')
    expect(requests(fetchMock, 'PATCH')).toBe(0)
    const [entity, migration] = sentChanges(fetchMock).changes
    expect(entity).toEqual({
      id: designedItem.id,
      version: designedItem.version,
      patch: [{ op: 'replace', path: '/fields', value: [designed.fields[0], designed.fields[2]] }],
    })
    expect(migration).toEqual({
      create: {
        id: expect.any(String),
        kind: 'migration',
        name: expect.stringMatching(/^InvoiceChange\d{14}$/),
        formatVersion: 1,
        entity: 'Invoice',
        changes: [{ type: 'dropField', field: 'code' }],
      },
    })
  })

  it('keeps a removed field when the answer is Keep field and then saves nothing', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/2', {}, slow)
    await userEvent.click(within(row).getByRole('button', { name: 'Remove field' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Keep field' }))

    await waitFor(() => expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument())
    expect(requests(fetchMock, 'PATCH') + requests(fetchMock, 'POST')).toBe(0)
    expect(
      screen.getAllByRole('textbox', { name: 'Field name' }).map((box) => (box as HTMLInputElement).value),
    ).toEqual(['total', 'code', 'note'])
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('saves one patch and no changes call for a field added in the same session and then renamed', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await userEvent.click(await screen.findByRole('button', { name: 'Add field' }, slow))
    await userEvent.type(within(screen.getByTestId('studio-field-/fields/3/name')).getByRole('textbox'), 'ab')
    await userEvent.clear(within(screen.getByTestId('studio-field-/fields/3/name')).getByRole('textbox'))
    await userEvent.type(within(screen.getByTestId('studio-field-/fields/3/name')).getByRole('textbox'), 'memo')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument()
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
    expect((sentFields(fetchMock) as { name: string }[]).map((field) => field.name)).toEqual([
      'total',
      'code',
      'note',
      'memo',
    ])
  })

  it('asks Keep data for a renamed field, counts a chain of renames as one and adds renameField', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const name = within(await screen.findByTestId('studio-field-/fields/1/name', {}, slow)).getByRole('textbox')
    await userEvent.clear(name)
    await userEvent.type(name, 'abc')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('code → abc')
    await userEvent.click(screen.getByRole('button', { name: 'Keep data' }))

    await screen.findByTestId('studio-saved')
    const [, migration] = sentChanges(fetchMock).changes
    expect((migration as { create: { changes: unknown } }).create.changes).toEqual([
      { type: 'renameField', from: 'code', to: 'abc' },
    ])
  })

  function migrationChanges(fetchMock: ReturnType<typeof vi.fn>): unknown {
    const [, migration] = sentChanges(fetchMock).changes
    return (migration as { create: { changes: unknown } }).create.changes
  }

  it('asks for a convert expression for a retyped field and adds convert', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await chooseOption('integer', await screen.findByTestId('studio-field-/fields/2/type', {}, slow))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('note')
    expect(screen.getByRole('button', { name: 'Add convert' })).toBeDisabled()
    await userEvent.type(screen.getByRole('textbox', { name: 'Convert expression' }), 'length(note)')
    await userEvent.click(screen.getByRole('button', { name: 'Add convert' }))

    await screen.findByTestId('studio-saved')
    expect(migrationChanges(fetchMock)).toEqual([{ type: 'convert', field: 'note', value: 'length(note)' }])
  })

  it('asks for a fill expression for a field made required and adds fill', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/2', {}, slow)
    await userEvent.click(within(row).getByRole('checkbox', { name: 'Required' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Fill expression' }), "'none'")
    await userEvent.click(screen.getByRole('button', { name: 'Fill nulls' }))

    await screen.findByTestId('studio-saved')
    expect(migrationChanges(fetchMock)).toEqual([{ type: 'fill', field: 'note', value: "'none'" }])
  })

  it('adds nothing for a skipped convert and keeps the draft on Cancel', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await chooseOption('integer', await screen.findByTestId('studio-field-/fields/2/type', {}, slow))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument())
    expect(requests(fetchMock, 'PATCH') + requests(fetchMock, 'POST')).toBe(0)

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Skip' }))

    await screen.findByTestId('studio-saved')
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
  })

  it('warns in the row of a field switched to enum and saves one patch and no changes call', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    await chooseOption('enum', await screen.findByTestId('studio-field-/fields/2/type', {}, slow))
    expect(screen.getByTestId('studio-migration-warning-/fields/2')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-migration-warning-/fields/1')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument()
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
  })

  it('puts convert after renameField and uses the new name', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const name = within(await screen.findByTestId('studio-field-/fields/2/name', {}, slow)).getByRole('textbox')
    await userEvent.clear(name)
    await userEvent.type(name, 'remark')
    await chooseOption('integer', screen.getByTestId('studio-field-/fields/2/type'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('note → remark')
    await userEvent.click(screen.getByRole('button', { name: 'Keep data' }))
    expect(screen.getByTestId('studio-migration-field')).toHaveTextContent('remark')
    await userEvent.type(screen.getByRole('textbox', { name: 'Convert expression' }), 'length(remark)')
    await userEvent.click(screen.getByRole('button', { name: 'Add convert' }))

    await screen.findByTestId('studio-saved')
    expect(migrationChanges(fetchMock)).toEqual([
      { type: 'renameField', from: 'note', to: 'remark' },
      { type: 'convert', field: 'remark', value: 'length(remark)' },
    ])
  })

  it('asks nothing for a child-collection field', async () => {
    const withCollection = {
      ...designed,
      fields: [...designed.fields, { name: 'lines', type: 'child-collection', target: 'InvoiceLine' }],
    }
    const { fetchMock } = stubResource(designedItem, withCollection)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/3', {}, slow)
    await userEvent.click(within(row).getByRole('button', { name: 'Remove field' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(requests(fetchMock, 'PATCH')).toBe(1)
    expect(requests(fetchMock, 'POST')).toBe(0)
  })

  it('sends nothing and keeps the draft when the answer to a rename is Cancel', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const name = within(await screen.findByTestId('studio-field-/fields/2/name', {}, slow)).getByRole('textbox')
    await userEvent.type(name, 's')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByTestId('studio-migration-question')).not.toBeInTheDocument())
    expect(requests(fetchMock, 'PATCH') + requests(fetchMock, 'POST')).toBe(0)
    expect(name).toHaveValue('notes')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('moves a field up and down', async () => {
    const { fetchMock } = stubResource(designedItem, designed)
    renderResource(designedItem)

    const row = await screen.findByTestId('studio-field-/fields/1', {}, slow)
    expect(within(screen.getByTestId('studio-field-/fields/0')).getByRole('button', { name: 'Move up' })).toBeDisabled()
    await userEvent.click(within(row).getByRole('button', { name: 'Move up' }))
    await userEvent.click(
      within(screen.getByTestId('studio-field-/fields/1')).getByRole('button', { name: 'Move down' }),
    )
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    // Fields that swap places in an array of the same length are patched item by item, so apply the patch.
    let moved: unknown = designed
    for (const operation of sentPatch(fetchMock)) {
      moved = setAt(moved, operation.path, operation.op === 'remove' ? undefined : operation.value)
    }
    // The first move puts `code` first, the second moves `total` below `note`.
    expect((moved as typeof designed).fields).toEqual([designed.fields[1], designed.fields[2], designed.fields[0]])
  })
})
