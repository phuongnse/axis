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
