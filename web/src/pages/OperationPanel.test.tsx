import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import raw from '../../../src/Axis.Configuration/Schemas/process.schema.json'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioNames } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, optionTexts, selectRoot } from '../test/antd'
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
    {
      id: '5',
      name: 'ApprovalNote',
      module: null,
      fields: ['text', 'author'].map((name) => ({
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
    {
      name: 'note',
      type: 'operation',
      operation: 'createRecord',
      entity: 'ApprovalNote',
      set: { text: 'status' },
      next: 'done',
      x: 1,
    },
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

function step(index: number) {
  return within(screen.getByTestId(`studio-step-${index}`))
}

type Conditional = {
  if: { properties?: { type?: { const?: string }; operation?: { const?: string; pattern?: string } } }
  then: { properties?: Record<string, unknown> }
}

const stepSchema = raw.$defs.step as unknown as { properties: Record<string, unknown>; allOf: Conditional[] }

const falseKeys = (conditional: Conditional) =>
  Object.entries(conditional.then.properties ?? {})
    .filter(([, value]) => value === false)
    .map(([key]) => key)

/** The operations of the process schema, a built-in one by its name and the package ones by a name with a slash. */
const schemaOperations = stepSchema.allOf.flatMap((conditional) => {
  const operation = conditional.if.properties?.operation
  if (operation === undefined) {
    return []
  }
  const name = operation.const ?? (operation.pattern === '/' ? 'package/operation' : undefined)
  return name === undefined
    ? []
    : [{ operation: name, builtIn: operation.const !== undefined, forbids: falseKeys(conditional).sort() }]
})

/** The keys a step of the operation type forbids, whatever its operation. */
const forbiddenForType = falseKeys(
  stepSchema.allOf.find((conditional) => conditional.if.properties?.type?.const === 'operation')!,
)

const sampleValues: Record<string, unknown> = {
  set: { status: "'x'" },
  connector: { name: 'C', operation: 'o' },
  input: { a: 'b' },
  output: { a: 'b' },
  retry: { attempts: 1 },
  event: 'E',
  entity: 'N',
  fields: { a: 'b' },
  to: [{ user: 'requester' }],
  channels: ['inApp'],
  message: { subject: { textKey: 's' }, body: { textKey: 'b' }, values: { n: 'number' } },
  open: { page: 'P' },
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
    expect(selectRoot(step(0).getByRole('combobox', { name: 'Operation' }))).toBeVisible()
    expect(
      within(screen.getByTestId('studio-field-/steps/0/set/status')).getByRole('textbox', { name: 'Expression' }),
    ).toBeVisible()
    expect(selectRoot(step(1).getByRole('combobox', { name: 'Connector' }))).toBeVisible()
    expect(step(1).getByRole('spinbutton', { name: 'Attempts' })).toBeVisible()
    expect(step(1).getByRole('textbox', { name: 'Backoff' })).toBeVisible()
    expect(selectRoot(step(2).getByRole('combobox', { name: 'Event' }))).toBeVisible()
    expect(step(3).getByRole('textbox', { name: 'Package operation' })).toBeVisible()
  })
})

describe('The operations of the process schema', () => {
  const builtIn = schemaOperations.filter((item) => item.builtIn)

  it('reads the operations and the operation keys from the schema', () => {
    expect(builtIn.map((item) => item.operation)).toEqual(expect.arrayContaining(['notify', 'updateRecord']))
    expect(schemaOperations.length).toBe(builtIn.length + 1)
  })

  it.each(schemaOperations)('offers $operation and shows every key it allows', async ({ operation, forbids }) => {
    const allowed = Object.keys(stepSchema.properties).filter(
      (key) =>
        !['name', 'type', 'operation', 'next'].includes(key) &&
        !forbiddenForType.includes(key) &&
        !forbids.includes(key),
    )
    expect(allowed.length).toBeGreaterThan(0)
    renderEditor(false, {
      ...process,
      steps: [
        {
          name: 'tell',
          type: 'operation',
          operation,
          ...Object.fromEntries(allowed.map((key) => [key, sampleValues[key]])),
          next: 'done',
        },
      ],
    })

    if (builtIn.some((item) => item.operation === operation)) {
      await userEvent.click(step(0).getByRole('combobox', { name: 'Operation' }))
      expect(optionTexts()).toContain(operation)
    }
    for (const key of allowed) {
      const fields = document.querySelectorAll(`[data-testid^="studio-field-/steps/0/${key}"]`)
      expect(fields.length, key).toBeGreaterThan(0)
      for (const field of fields) {
        expect(within(field as HTMLElement).queryByTestId('studio-json-only'), key).toBeNull()
      }
    }
  })
})

describe('A notify step', () => {
  const notify = {
    name: 'tell',
    type: 'operation',
    operation: 'notify',
    to: [{ user: 'requester' }],
    channels: ['inApp'],
    message: { subject: { textKey: 's' }, body: { textKey: 'b' }, values: { n: 'number' } },
    open: { page: 'P' },
    next: 'done',
  }
  const render = (value: Record<string, unknown> = notify) =>
    renderEditor(false, { ...process, steps: [value, { name: 'done', type: 'end' }] })
  const field = (pointer: string) => within(screen.getByTestId(`studio-field-${pointer}`))

  it('writes a recipient to its pointer and switches its kind', async () => {
    const onChange = render()

    await userEvent.type(field('/steps/0/to/0/user').getByRole('textbox', { name: 'User' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/to/0/user', 'requesterX')

    // The radio input of a button group has no pointer events, so the click goes to its label.
    const switchTo = (name: string) =>
      userEvent.click(field('/steps/0/to/0').getByRole('radio', { name }).closest('label')!)
    await switchTo('Role')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/to/0', { role: '' })
    await switchTo('Email')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/to/0', { email: '' })
  })

  it('types a role while the names are unknown, and shows an email recipient', async () => {
    const onChange = render({ ...notify, to: [{ role: 'manager' }, { email: 'a@b.c' }] })

    await userEvent.type(field('/steps/0/to/0/role').getByRole('textbox', { name: 'Role' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/to/0/role', 'managerX')
    expect(field('/steps/0/to/1/email').getByRole('textbox', { name: 'Email' })).toHaveValue('a@b.c')
  })

  it('adds a recipient to the array and removes one', async () => {
    const onChange = render()

    await userEvent.click(field('/steps/0/to').getByRole('button', { name: 'Add recipient' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/to', [{ user: 'requester' }, { user: '' }])
    await userEvent.click(field('/steps/0/to').getByRole('button', { name: 'Remove recipient' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/to/0', undefined)
  })

  it('adds the first recipient to a step that has none', async () => {
    const onChange = render({ ...notify, to: [] })

    await userEvent.click(field('/steps/0/to').getByRole('button', { name: 'Add recipient' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/to', [{ user: '' }])
  })

  it('writes the channels in a fixed order and keeps an empty list', async () => {
    const onChange = render()

    await userEvent.click(field('/steps/0/channels').getByRole('checkbox', { name: 'Email' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/channels', ['inApp', 'email'])
    await userEvent.click(field('/steps/0/channels').getByRole('checkbox', { name: 'In-app' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/channels', [])
  })

  it('writes the subject and body keys, and the placeholder values', async () => {
    const onChange = render()

    await userEvent.type(field('/steps/0/message/subject').getByRole('textbox', { name: 'Text key' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/message/subject/textKey', 'sX')
    await userEvent.type(field('/steps/0/message/body').getByRole('textbox', { name: 'Text key' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/message/body/textKey', 'bX')

    await userEvent.type(field('/steps/0/message/values/n').getByRole('textbox', { name: 'Expression' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/message/values/n', 'numberX')
    await userEvent.type(field('/steps/0/message/values/n').getByRole('textbox', { name: 'Placeholder' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/message/values', { nX: 'number' })
    await userEvent.click(field('/steps/0/message/values').getByRole('button', { name: 'Add mapping' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/message/values/', '')
  })

  it('opens a page and writes its name and parameters', async () => {
    const onChange = render()

    await userEvent.type(field('/steps/0/open/page').getByRole('textbox', { name: 'Page' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/open/page', 'PX')
    await userEvent.click(field('/steps/0/open/parameters').getByRole('button', { name: 'Add mapping' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/open/parameters/', '')
    await userEvent.click(field('/steps/0/open').getByRole('checkbox', { name: 'Open a page' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/open', undefined)
  })

  it('adds an open page with an empty name', async () => {
    const onChange = render({ ...notify, open: undefined })

    expect(screen.queryByTestId('studio-field-/steps/0/open/page')).toBeNull()
    await userEvent.click(field('/steps/0/open').getByRole('checkbox', { name: 'Open a page' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/0/open', { page: '' })
  })

  it('points to the JSON view for a value of the wrong shape', () => {
    render({ ...notify, to: 'x', channels: 'x', message: 'x', open: 'x' })

    for (const key of ['to', 'channels', 'message', 'open']) {
      expect(field(`/steps/0/${key}`).getByTestId('studio-json-only')).toBeVisible()
    }
  })

  it('shows no next control among its fields', () => {
    render()

    expect(screen.getByTestId('studio-field-/steps/0/next')).toBeVisible()
    for (const key of ['to', 'channels', 'message', 'open']) {
      expect(screen.getByTestId(`studio-field-/steps/0/${key}`).querySelector('[data-testid$="/next"]')).toBeNull()
    }
  })

  it('removes the notify keys and keeps next when the operation is changed to updateRecord', async () => {
    const onChange = render()

    await userEvent.click(step(0).getByRole('combobox', { name: 'Operation' }))
    await chooseOption('updateRecord')

    const [pointer, next] = onChange.mock.lastCall as [string, Record<string, unknown>]
    expect(pointer).toBe('/steps/0')
    for (const key of ['to', 'channels', 'message', 'open']) {
      expect(next).not.toHaveProperty(key)
    }
    expect(next).toEqual({ name: 'tell', type: 'operation', operation: 'updateRecord', next: 'done' })
  })
})

describe('An updateRecord step', () => {
  it('shows its set rows', async () => {
    renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    expect(selectRoot(step(0).getByRole('combobox', { name: 'Operation' }))).toBeVisible()
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
    const options = optionTexts()
    expect(options).toEqual(['purchaseOrderNumber'])
    await chooseOption('purchaseOrderNumber')

    expect(onChange).toHaveBeenLastCalledWith('/steps/0/set/purchaseOrderNumber', '')
  })

  it('renames a row in place and removes a row', async () => {
    const onChange = renderEditor()

    const row = within(screen.getByTestId('studio-field-/steps/0/set/status'))
    await userEvent.click(row.getByRole('combobox', { name: 'Field' }))
    await chooseOption('purchaseOrderNumber')
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
    await chooseOption('raiseEvent')

    const [pointer, next] = onChange.mock.lastCall as [string, Record<string, unknown>]
    expect(pointer).toBe('/steps/0')
    expect(next).toEqual({ name: 'update', type: 'operation', operation: 'raiseEvent', next: 'done', x: 1 })
  })

  it('switches to a package operation without keeping its forbidden keys and adds no empty key', async () => {
    const onChange = renderEditor()

    await userEvent.click(step(0).getByRole('combobox', { name: 'Operation' }))
    await chooseOption('Package operation')

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
    await chooseOption('cancelPurchaseOrder')

    expect(onChange).toHaveBeenLastCalledWith('/steps/1/connector/operation', 'cancelPurchaseOrder')
  })

  it('picks the entity field of an output', async () => {
    const onChange = renderEditor()

    const row = within(screen.getByTestId('studio-field-/steps/1/output/orderNumber'))
    await userEvent.click(row.getByRole('combobox', { name: 'Entity field' }))
    await chooseOption('status')

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
    const options = optionTexts()
    expect(options).toEqual(['amount'])
    await chooseOption('amount')
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

describe('A createRecord step', () => {
  it('shows the entity picker and the set rows of the picked entity', async () => {
    const onChange = renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    expect(within(screen.getByTestId('studio-field-/steps/5/entity')).getByText('ApprovalNote')).toBeVisible()
    const row = within(screen.getByTestId('studio-field-/steps/5/set/text'))
    expect(row.getByRole('combobox', { name: 'Field' })).toBeVisible()
    expect(row.getByRole('textbox', { name: 'Expression' })).toBeVisible()

    // The rows are keyed by the fields of the step's entity, not of the process's entity.
    await userEvent.click(step(5).getByRole('combobox', { name: 'Add mapping Set' }))
    expect(optionTexts()).toEqual(['author'])
    await chooseOption('author')
    expect(onChange).toHaveBeenLastCalledWith('/steps/5/set/author', '')
  })

  it('writes the picked entity to the draft', async () => {
    const onChange = renderEditor()

    await userEvent.click(step(5).getByRole('combobox', { name: 'Entity to create' }))
    await chooseOption('PurchaseRequest')
    expect(onChange).toHaveBeenLastCalledWith('/steps/5/entity', 'PurchaseRequest')
  })

  it('types the entity and its field names while the names are unknown', async () => {
    const onChange = renderEditor(false)

    await userEvent.type(step(5).getByRole('textbox', { name: 'Entity to create' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/5/entity', 'ApprovalNoteX')
    const row = within(screen.getByTestId('studio-field-/steps/5/set/text'))
    await userEvent.type(row.getByRole('textbox', { name: 'Expression' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/5/set/text', 'statusX')
    await userEvent.type(row.getByRole('textbox', { name: 'Field' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/steps/5/set', { textX: 'status' })
  })

  it('is picked from the operation list and keeps the keys it allows', async () => {
    const onChange = renderEditor()

    await userEvent.click(step(0).getByRole('combobox', { name: 'Operation' }))
    await chooseOption('createRecord')

    const [pointer, next] = onChange.mock.lastCall as [string, Record<string, unknown>]
    expect(pointer).toBe('/steps/0')
    expect(next).toEqual({
      name: 'update',
      type: 'operation',
      operation: 'createRecord',
      set: { status: "'submitted'", totalAmount: '1' },
      next: 'done',
      x: 1,
    })
  })

  it('points to the JSON view for a key its operation forbids', () => {
    renderEditor(true, {
      ...process,
      steps: [{ name: 'note', type: 'operation', operation: 'createRecord', entity: 'E', set: {}, event: 'E' }],
    })

    expect(within(screen.getByTestId('studio-field-/steps/0/event')).getByTestId('studio-json-only')).toBeVisible()
  })
})

describe('A package operation step', () => {
  it('shows its name box, input, output and retry, and no connector', async () => {
    renderEditor()
    await screen.findAllByTestId('studio-expression-type')

    expect(step(3).getByRole('textbox', { name: 'Package operation' })).toHaveValue(
      'payment-references/creditorReference',
    )
    expect(selectRoot(step(3).getByRole('combobox', { name: 'Operation' }))).toBeVisible()
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
    entity: 'N',
    fields: { a: 'b' },
    to: [{ user: 'requester' }],
    channels: ['inApp'],
    message: { subject: { textKey: 's' }, body: { textKey: 'b' } },
    open: { page: 'P' },
    next: 'done',
    x: 1,
  }
  const operations = ['updateRecord', 'callConnector', 'raiseEvent', 'createRecord', 'notify', 'package/operation']

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
    const forbidden = schemaOperations.map(({ operation, forbids }) => [operation, forbids])

    expect(forbidden.length).toBeGreaterThan(0)
    for (const [operation, forbids] of forbidden) {
      expect([...forbiddenOperationKeys(operation as string)].sort()).toEqual(forbids)
    }
    expect(forbiddenOperationKeys('updateRecord')).toEqual(
      expect.arrayContaining(['to', 'channels', 'message', 'open']),
    )
    expect(forbiddenOperationKeys('package/operation')).toEqual(
      expect.arrayContaining(['to', 'channels', 'message', 'open']),
    )
    expect(forbiddenOperationKeys('notify')).not.toEqual(expect.arrayContaining(['to']))
  })

  it('removes the notify keys when a notify step switches to another operation and keeps next', () => {
    for (const operation of ['updateRecord', 'callConnector', 'raiseEvent', 'createRecord', 'package/operation']) {
      const next = withOperation({ ...full, operation: 'notify' }, operation)

      for (const key of ['to', 'channels', 'message', 'open']) {
        expect(next).not.toHaveProperty(key)
      }
      expect(next.next).toBe('done')
    }
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
    expect(operationJsonOnlyKeys({ operation: 'updateRecord', set: {}, to: [] })).toEqual(['to'])
    expect(operationJsonOnlyKeys({ operation: 'notify', to: [], open: {} })).toEqual([])
  })
})
