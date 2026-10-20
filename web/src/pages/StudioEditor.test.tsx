import { EditorView } from '@codemirror/view'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import purchaseRequestApproval from '../../../samples/apps/purchase-requests/processes/purchase-request-approval.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames, StudioResourceItem } from '../platform/studio'
import { setAt } from '../platform/jsonPatch'
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
        path={null}
        onSaved={vi.fn()}
        onTextsWritten={vi.fn()}
        onDeleted={vi.fn()}
      />
    </TextProvider>,
  )
}

// The first lookup waits for the schema chunk and two fetches, which is slow while other test files run.
const slow = { timeout: 5000 }

// The schema is a chunk of its own. Loading it here keeps the first test from racing its first lookup.
beforeAll(() =>
  Promise.all([
    loadKindSchema('seed'),
    loadKindSchema('connector'),
    loadKindSchema('endpoint'),
    loadKindSchema('text'),
    loadKindSchema('page'),
  ]),
)

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
) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={names}>
          <StudioEditor
            application="Shop"
            item={item}
            resources={[item]}
            path={path}
            onSaved={vi.fn()}
            onTextsWritten={onTextsWritten}
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

/** The option of the dropdown opened last. A title also marks a closed select that shows the same value. */
async function optionOf(name: string): Promise<HTMLElement> {
  return waitFor(() => {
    const found = [...document.querySelectorAll<HTMLElement>('.ant-select-item-option')].filter(
      (element) => element.title === name && !element.closest('.ant-select-dropdown-hidden'),
    )
    expect(found.length).toBeGreaterThan(0)
    return found[found.length - 1]
  })
}

async function pick(field: HTMLElement, option: string) {
  await userEvent.click(within(field).getByRole('combobox'))
  await userEvent.click(await optionOf(option))
}

function sentSteps(fetchMock: ReturnType<typeof vi.fn>): Record<string, unknown>[] {
  return sentPatch(fetchMock).find((operation) => operation.path === '/steps')!.value as Record<string, unknown>[]
}

describe('StudioEditor for a process', () => {
  it('edits a branch and the otherwise of a decision, keeping the unknown keys', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const when = await screen.findByTestId('studio-field-/steps/2/branches/0/when', {}, slow)
    const view = await expressionView(when)
    act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: 'totalAmount > 5' } }))
    await pick(screen.getByTestId('studio-field-/steps/2/branches/1/next'), 'markRejected')
    await pick(screen.getByTestId('studio-field-/steps/2/otherwise'), 'markRejected')
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

    await userEvent.click(await screen.findByRole('combobox', { name: 'Add step' }, slow))
    await userEvent.click(await optionOf('decision'))
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

    const card = await screen.findByTestId('studio-step-0', {}, slow)
    await pick(within(card).getByTestId('studio-field-/steps/0/type'), 'decision')
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

    const card = await screen.findByTestId('studio-step-0', {}, slow)
    await pick(within(card).getByTestId('studio-field-/steps/0/type'), 'task')
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

    expect(await screen.findByRole('textbox', { name: 'Text key' }, slow)).toHaveValue('pages.myTasks.title')
  })

  it('shows how many other labels use the key of a saved label', async () => {
    stubPage({ ...pageContent, title: { textKey: 'pages.home.title' } }, () => json({ resources: [], diagnostics: [] }))
    renderResource(pageItem, namesWithTexts)

    expect(await screen.findByRole('textbox', { name: 'Text key' }, slow)).toHaveValue('pages.home.title')
    expect(screen.getByRole('textbox', { name: 'en' })).toHaveValue('Home')
    expect(screen.getByRole('textbox', { name: 'vi' })).toHaveValue('Trang chủ')
    expect(screen.getByTestId('studio-label-used-by')).toHaveTextContent('2')
  })

  it('shows the failure and leaves the draft unchanged when the write fails', async () => {
    const fetchMock = stubPage(pageContent, () => new Response(null, { status: 400 }))
    renderResource(pageItem, namesWithTexts)

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
    { id: 'd1', name: 'PurchaseRequestList', module: null },
    { id: 'd2', name: 'SupplierList', module: null },
  ],
  pages: [{ id: 'g1', name: 'ReviewForm' }],
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

    expect(await screen.findByTestId('studio-page-editor', {}, slow)).toBeInTheDocument()
    expect(screen.getByTestId('studio-widget-0')).toHaveTextContent('form · PurchaseRequest')
    expect(screen.getByTestId('studio-field-/widgets/0/actions/0/process')).toBeInTheDocument()
  })

  it('patches only the edit and keeps the unknown keys on the file and the widget', async () => {
    const content = { ...tablePage, widgets: [{ type: 'table', dataSource: 'PurchaseRequestList', 'x-note': 'keep' }] }
    const { fetchMock } = stubResource(pageItem, content)
    renderResource(pageItem, pageNames)

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
})
