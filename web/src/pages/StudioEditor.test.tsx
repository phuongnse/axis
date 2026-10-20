import { EditorView } from '@codemirror/view'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames, StudioResourceItem } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { loadKindSchema } from '../platform/studioSchemas'
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
        names={null}
        path={null}
        onSaved={vi.fn()}
        onDeleted={vi.fn()}
      />
    </TextProvider>,
  )
}

// The first lookup waits for the schema chunk and two fetches, which is slow while other test files run.
const slow = { timeout: 5000 }

// The schema is a chunk of its own. Loading it here keeps the first test from racing its first lookup.
beforeAll(() => Promise.all([loadKindSchema('seed'), loadKindSchema('connector'), loadKindSchema('endpoint')]))

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('StudioEditor for a seed', () => {
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
    if (input === url) {
      return json({ ...item, content })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { fetchMock, url }
}

function renderResource(item: StudioResourceItem, names?: StudioNames) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={names}>
          <StudioEditor
            application="Shop"
            item={item}
            resources={[item]}
            names={names ?? null}
            path={null}
            onSaved={vi.fn()}
            onDeleted={vi.fn()}
          />
        </StudioNamesContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

type PatchOperation = { op: string; path: string; value?: unknown }

function sentPatch(fetchMock: ReturnType<typeof vi.fn>): PatchOperation[] {
  const [, init] = fetchMock.mock.calls.find(([, call]) => call?.method === 'PATCH')!
  return JSON.parse(init!.body as string) as PatchOperation[]
}

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
})
