import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioNames } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { ExpressionCheckContext } from './expressionCheckContext'
import { forbiddenOperationKeys, operationJsonOnlyKeys, withOperation } from './operationKeys'
import { ProcessEditor } from './ProcessEditor'

const names: StudioNames = {
  locales: ['en'],
  entities: [
    {
      id: '1',
      name: 'PurchaseRequest',
      module: null,
      fields: ['status', 'totalAmount', 'purchaseOrderNumber'].map((name) => ({
        name,
        type: 'text',
        target: null,
        values: null,
      })),
    },
  ],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [
    {
      id: '2',
      name: 'PurchasingSystem',
      operations: [{ name: 'createPurchaseOrder' }, { name: 'cancelPurchaseOrder' }],
    },
  ],
  events: [
    {
      id: '3',
      name: 'PurchaseRequestApproved',
      fields: [
        { name: 'reason', type: 'text' },
        { name: 'amount', type: 'decimal' },
      ],
    },
  ],
  roles: [],
  sequences: [],
  forms: [],
  pages: [],
  texts: [],
}

const process = {
  id: '4b60c03e-02d0-4329-80c6-75f972190399',
  kind: 'process',
  name: 'PurchaseRequestApproval',
  formatVersion: 1,
  entity: 'PurchaseRequest',
  start: 'update',
  steps: [
    {
      name: 'update',
      type: 'operation',
      operation: 'updateRecord',
      set: { status: "'submitted'", totalAmount: '1' },
      next: 'done',
      x: 1,
    },
    {
      name: 'call',
      type: 'operation',
      operation: 'callConnector',
      connector: { name: 'PurchasingSystem', operation: 'createPurchaseOrder' },
      input: { amount: 'totalAmount' },
      output: { orderNumber: 'purchaseOrderNumber' },
      retry: { attempts: 3, backoff: 'PT30S' },
      next: 'done',
      x: 1,
    },
    {
      name: 'raise',
      type: 'operation',
      operation: 'raiseEvent',
      event: 'PurchaseRequestApproved',
      fields: { reason: 'status' },
      next: 'done',
      x: 1,
    },
    {
      name: 'pay',
      type: 'operation',
      operation: 'payment-references/creditorReference',
      input: { reference: 'number' },
      output: { creditorReference: 'purchaseOrderNumber' },
      retry: { attempts: 2 },
      next: 'done',
      x: 1,
    },
    { name: 'done', type: 'end' },
  ],
}

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

let schema: JsonSchema

/** Renders the editor. With `known` false it has no check context, so the names are unknown and every picker is a name box. */
function renderEditor(known = true, value: Record<string, unknown> = process, onChange = vi.fn()) {
  const editor = <ProcessEditor schema={schema} value={value} onChange={onChange} />
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          {known ? (
            <ExpressionCheckContext.Provider
              value={{ application: 'Shop', resourceId: '4b60c03e', kind: 'process', draft: value, names }}
            >
              {editor}
            </ExpressionCheckContext.Provider>
          ) : (
            editor
          )}
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

/** Picks an option of the open dropdown. A selected value elsewhere has the same title, so only an option counts. */
async function choose(title: string) {
  const option = await waitFor(() => {
    const found = document.querySelector(`.ant-select-item-option[title="${title}"]`)
    expect(found).not.toBeNull()
    return found!
  })
  await userEvent.click(option)
}

function step(index: number) {
  return within(screen.getByTestId(`studio-step-${index}`))
}

beforeAll(async () => {
  const full = (await loadKindSchema('process'))!
  const { start: _start, steps: _steps, ...properties } = full.properties ?? {}
  schema = { ...full, properties, required: full.required?.filter((name) => name !== 'start' && name !== 'steps') }
})

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ type: 'text', diagnostics: [] }), { status: 200 })),
  ),
)

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe.each(['light', 'dark'])('The operation panel in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with its controls visible', async () => {
    renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    const root = screen.getByTestId('studio-field-/steps/0/operation')
    expect(root.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(root).toBeVisible()
    expect(step(0).getByRole('combobox', { name: 'Operation' }).closest('.ant-select')).toBeVisible()
    expect(
      within(screen.getByTestId('studio-field-/steps/0/set/status')).getByRole('textbox', { name: 'Expression' }),
    ).toBeVisible()
    expect(step(1).getByRole('combobox', { name: 'Connector' }).closest('.ant-select')).toBeVisible()
    expect(step(1).getByRole('spinbutton', { name: 'Attempts' })).toBeVisible()
    expect(step(1).getByRole('textbox', { name: 'Backoff' })).toBeVisible()
    expect(step(2).getByRole('combobox', { name: 'Event' }).closest('.ant-select')).toBeVisible()
    expect(step(3).getByRole('textbox', { name: 'Package operation' })).toBeVisible()
  })
})

describe('An updateRecord step', () => {
  it('shows its set rows', async () => {
    renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    expect(step(0).getByRole('combobox', { name: 'Operation' }).closest('.ant-select')).toBeVisible()
    for (const key of ['status', 'totalAmount']) {
      const row = within(screen.getByTestId(`studio-field-/steps/0/set/${key}`))
      expect(row.getByRole('combobox', { name: 'Field' })).toBeVisible()
      expect(row.getByRole('textbox', { name: 'Expression' })).toBeVisible()
      expect(row.getByRole('button', { name: 'Remove' })).toBeVisible()
    }
    expect(step(0).queryByTestId('studio-json-only')).toBeNull()
  })

  it('adds a row for an unmapped field of the entity', async () => {
    const onChange = renderEditor()

    await userEvent.click(step(0).getByRole('combobox', { name: 'Add mapping Set' }))
    const options = [...document.querySelectorAll('.ant-select-item-option')].map((option) => option.textContent)
    expect(options).toEqual(['purchaseOrderNumber'])
    await choose('purchaseOrderNumber')

    expect(onChange).toHaveBeenLastCalledWith('/steps/0/set/purchaseOrderNumber', '')
  })

  it('renames a row in place and removes a row', async () => {
    const onChange = renderEditor()

    const row = within(screen.getByTestId('studio-field-/steps/0/set/status'))
    await userEvent.click(row.getByRole('combobox', { name: 'Field' }))
    await choose('purchaseOrderNumber')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/set', {
      purchaseOrderNumber: "'submitted'",
      totalAmount: '1',
    })
    expect(Object.keys(onChange.mock.calls[0][1] as object)).toEqual(['purchaseOrderNumber', 'totalAmount'])

    await userEvent.click(row.getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/set/status', undefined)
  })

  it('edits an expression, a field name and adds a row while the names are unknown', async () => {
    const onChange = renderEditor(false)

    const row = within(screen.getByTestId('studio-field-/steps/0/set/totalAmount'))
    await userEvent.type(row.getByRole('textbox', { name: 'Expression' }), '2')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/set/totalAmount', '12')
    await userEvent.type(row.getByRole('textbox', { name: 'Field' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/set', { status: "'submitted'", totalAmountX: '1' })
    await userEvent.click(step(0).getByRole('button', { name: 'Add mapping' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/set/', '')
  })

  it('never writes the unknown key or the other steps', async () => {
    const onChange = renderEditor(false)

    const row = within(screen.getByTestId('studio-field-/steps/0/set/status'))
    await userEvent.type(row.getByRole('textbox', { name: 'Expression' }), 'a')
    await userEvent.click(row.getByRole('button', { name: 'Remove' }))

    const pointers = onChange.mock.calls.map(([pointer]) => pointer as string)
    expect(pointers.length).toBeGreaterThan(0)
    expect(pointers.every((pointer) => pointer.startsWith('/steps/0/set'))).toBe(true)
  })

  it('changes the operation and keeps what the new one allows', async () => {
    const onChange = renderEditor()

    await userEvent.click(step(0).getByRole('combobox', { name: 'Operation' }))
    await choose('raiseEvent')

    const [pointer, next] = onChange.mock.lastCall as [string, Record<string, unknown>]
    expect(pointer).toBe('/steps/0')
    expect(next).toEqual({ name: 'update', type: 'operation', operation: 'raiseEvent', next: 'done', x: 1 })
  })

  it('switches to a package operation without keeping its forbidden keys and adds no empty key', async () => {
    const onChange = renderEditor()

    await userEvent.click(step(0).getByRole('combobox', { name: 'Operation' }))
    await choose('Package operation')

    expect(onChange).toHaveBeenLastCalledWith('/steps/0', {
      name: 'update',
      type: 'operation',
      operation: '',
      next: 'done',
      x: 1,
    })
  })
})

describe('A callConnector step', () => {
  it('shows the connector and operation pickers, its mappings and its retry', async () => {
    renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    expect(
      within(screen.getByTestId('studio-field-/steps/1/connector/name')).getByText('PurchasingSystem'),
    ).toBeVisible()
    expect(
      within(screen.getByTestId('studio-field-/steps/1/connector/operation')).getByText('createPurchaseOrder'),
    ).toBeVisible()
    const input = within(screen.getByTestId('studio-field-/steps/1/input/amount'))
    expect(input.getByRole('textbox', { name: 'Input name' })).toHaveValue('amount')
    expect(input.getByRole('textbox', { name: 'Expression' })).toBeVisible()
    const output = within(screen.getByTestId('studio-field-/steps/1/output/orderNumber'))
    expect(output.getByRole('textbox', { name: 'Connector output' })).toHaveValue('orderNumber')
    expect(output.getByRole('combobox', { name: 'Entity field' })).toBeVisible()
    expect(step(1).getByRole('spinbutton', { name: 'Attempts' })).toHaveValue('3')
    expect(step(1).getByRole('textbox', { name: 'Backoff' })).toHaveValue('PT30S')
  })

  it('picks the connector operation from the operations of the connector', async () => {
    const onChange = renderEditor()

    await userEvent.click(step(1).getByRole('combobox', { name: 'Connector operation' }))
    await choose('cancelPurchaseOrder')

    expect(onChange).toHaveBeenLastCalledWith('/steps/1/connector/operation', 'cancelPurchaseOrder')
  })

  it('picks the entity field of an output', async () => {
    const onChange = renderEditor()

    const row = within(screen.getByTestId('studio-field-/steps/1/output/orderNumber'))
    await userEvent.click(row.getByRole('combobox', { name: 'Entity field' }))
    await choose('status')

    expect(onChange).toHaveBeenLastCalledWith('/steps/1/output/orderNumber', 'status')
  })

  it('types the connector, its operation and the names of the input and the output while the names are unknown', async () => {
    const onChange = renderEditor(false)

    await userEvent.type(step(1).getByRole('textbox', { name: 'Connector' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/connector/name', 'PurchasingSystemX')
    await userEvent.type(step(1).getByRole('textbox', { name: 'Connector operation' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/connector/operation', 'createPurchaseOrderX')
    const input = within(screen.getByTestId('studio-field-/steps/1/input/amount'))
    await userEvent.type(input.getByRole('textbox', { name: 'Input name' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/input', { amountX: 'totalAmount' })
    await userEvent.type(input.getByRole('textbox', { name: 'Expression' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/input/amount', 'totalAmountX')
    const output = within(screen.getByTestId('studio-field-/steps/1/output/orderNumber'))
    await userEvent.type(output.getByRole('textbox', { name: 'Connector output' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/output', { orderNumberX: 'purchaseOrderNumber' })
    await userEvent.type(output.getByRole('textbox', { name: 'Entity field' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/output/orderNumber', 'purchaseOrderNumberX')
  })

  it('adds and removes input rows', async () => {
    const onChange = renderEditor(false)

    await userEvent.click(
      within(screen.getByTestId('studio-field-/steps/1/input')).getByRole('button', { name: 'Add mapping' }),
    )
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/input/', '')
    await userEvent.click(
      within(screen.getByTestId('studio-field-/steps/1/input/amount')).getByRole('button', { name: 'Remove' }),
    )
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/input/amount', undefined)
  })

  it('saves the attempts and the backoff of the retry, and removes a cleared one', async () => {
    const onChange = renderEditor(false)

    await userEvent.type(step(1).getByRole('textbox', { name: 'Backoff' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/retry/backoff', 'PT30SX')
    await userEvent.clear(step(1).getByRole('textbox', { name: 'Backoff' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/retry/backoff', undefined)
    await userEvent.clear(step(1).getByRole('spinbutton', { name: 'Attempts' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/retry/attempts', undefined)
  })

  it('removes retry when its last value is cleared', async () => {
    const onChange = renderEditor(false)

    await userEvent.clear(step(3).getByRole('spinbutton', { name: 'Attempts' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/3/retry', undefined)
  })
})

describe('A raiseEvent step', () => {
  it('shows the event and offers its payload fields as row keys', async () => {
    const onChange = renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    expect(within(screen.getByTestId('studio-field-/steps/2/event')).getByText('PurchaseRequestApproved')).toBeVisible()
    const row = within(screen.getByTestId('studio-field-/steps/2/fields/reason'))
    expect(row.getByRole('combobox', { name: 'Event field' })).toBeVisible()
    expect(row.getByRole('textbox', { name: 'Expression' })).toBeVisible()

    await userEvent.click(step(2).getByRole('combobox', { name: 'Add mapping Event fields' }))
    const options = [...document.querySelectorAll('.ant-select-item-option')].map((option) => option.textContent)
    expect(options).toEqual(['amount'])
    await choose('amount')
    expect(onChange).toHaveBeenLastCalledWith('/steps/2/fields/amount', '')
  })

  it('types the event and its field names while the names are unknown', async () => {
    const onChange = renderEditor(false)

    await userEvent.type(step(2).getByRole('textbox', { name: 'Event' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/2/event', 'PurchaseRequestApprovedX')
    const row = within(screen.getByTestId('studio-field-/steps/2/fields/reason'))
    await userEvent.type(row.getByRole('textbox', { name: 'Event field' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/2/fields', { reasonX: 'status' })
  })

  it('points to the JSON view for a key its operation forbids', () => {
    renderEditor(true, {
      ...process,
      steps: [{ name: 'raise', type: 'operation', operation: 'raiseEvent', event: 'E', fields: {}, set: { a: 'b' } }],
    })

    expect(within(screen.getByTestId('studio-field-/steps/0/set')).getByTestId('studio-json-only')).toBeVisible()
  })
})

describe('A package operation step', () => {
  it('shows its name box, input, output and retry, and no connector', async () => {
    renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    expect(step(3).getByRole('textbox', { name: 'Package operation' })).toHaveValue(
      'payment-references/creditorReference',
    )
    expect(step(3).getByRole('combobox', { name: 'Operation' }).closest('.ant-select')).toBeVisible()
    expect(screen.getByTestId('studio-field-/steps/3/input/reference')).toBeVisible()
    expect(screen.getByTestId('studio-field-/steps/3/output/creditorReference')).toBeVisible()
    expect(step(3).getByRole('spinbutton', { name: 'Attempts' })).toHaveValue('2')
    expect(step(3).queryByRole('combobox', { name: 'Connector' })).toBeNull()
    expect(step(3).queryByRole('combobox', { name: 'Event' })).toBeNull()
  })

  it('saves its name and each mapping at its pointer', async () => {
    const onChange = renderEditor(false)

    await userEvent.type(step(3).getByRole('textbox', { name: 'Package operation' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/3/operation', 'payment-references/creditorReferenceX')
    await userEvent.type(
      within(screen.getByTestId('studio-field-/steps/3/input/reference')).getByRole('textbox', { name: 'Expression' }),
      'X',
    )
    expect(onChange).toHaveBeenLastCalledWith('/steps/3/input/reference', 'numberX')
    await userEvent.type(
      within(screen.getByTestId('studio-field-/steps/3/output/creditorReference')).getByRole('textbox', {
        name: 'Entity field',
      }),
      'X',
    )
    expect(onChange).toHaveBeenLastCalledWith('/steps/3/output/creditorReference', 'purchaseOrderNumberX')
    await userEvent.type(step(3).getByRole('textbox', { name: 'Backoff' }), 'P')
    expect(onChange).toHaveBeenLastCalledWith('/steps/3/retry/backoff', 'P')
  })
})

describe('withOperation', () => {
  const full = {
    name: 's',
    type: 'operation',
    operation: 'updateRecord',
    set: { a: 'b' },
    connector: { name: 'C', operation: 'o' },
    input: { a: 'b' },
    output: { a: 'b' },
    retry: { attempts: 2 },
    event: 'E',
    fields: { a: 'b' },
    next: 'done',
    x: 1,
  }
  const operations = ['updateRecord', 'callConnector', 'raiseEvent', 'package/operation']

  it.each(operations)('removes exactly the keys that %s forbids', (operation) => {
    const next = withOperation(full, operation)

    const forbidden = forbiddenOperationKeys(operation)
    expect(forbidden.length).toBeGreaterThan(0)
    const expected = Object.fromEntries(Object.entries(full).filter(([key]) => !forbidden.includes(key)))
    expect(next).toEqual({ ...expected, operation })
    expect(next.next).toBe('done')
    expect(next.x).toBe(1)
    expect(Object.keys(next)).toEqual(Object.keys(expected))
  })

  it('removes the keys the schema forbids for each operation', () => {
    expect(forbiddenOperationKeys('updateRecord')).toEqual(['connector', 'input', 'output', 'retry', 'event', 'fields'])
    expect(forbiddenOperationKeys('callConnector')).toEqual(['set', 'event', 'fields'])
    expect(forbiddenOperationKeys('raiseEvent')).toEqual(['set', 'connector', 'input', 'output', 'retry'])
    expect(forbiddenOperationKeys('package/operation')).toEqual(['set', 'connector', 'event', 'fields'])
  })

  it('removes nothing for an operation the schema has no rule for and adds no key', () => {
    expect(withOperation(full, '')).toEqual({ ...full, operation: '' })
    expect(withOperation({ name: 's', type: 'operation' }, 'raiseEvent')).toEqual({
      name: 's',
      type: 'operation',
      operation: 'raiseEvent',
    })
  })

  it('lists the keys an operation forbids that the step has, and all of them for an unknown operation', () => {
    expect(operationJsonOnlyKeys({ operation: 'raiseEvent', set: {}, event: 'E' })).toEqual(['set'])
    expect(operationJsonOnlyKeys({ operation: 'callConnector', set: {}, input: {} })).toEqual(['set'])
    expect(operationJsonOnlyKeys({ operation: 'nope', set: {}, event: 'E' })).toEqual(['set', 'event'])
    expect(operationJsonOnlyKeys({ operation: 'updateRecord', set: {} })).toEqual([])
  })
})
