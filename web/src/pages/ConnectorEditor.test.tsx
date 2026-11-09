import { type BoundFunctions, type ByRoleMatcher, type queries, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { type ReactNode, useEffect, useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import oauth2Fixture from '../../../tests/e2e/fixtures/studio-connector-oauth2.json'
import { setAt } from '../platform/jsonPatch'
import type { StudioBindings } from '../platform/studio'
import { StudioBindingsContext } from '../platform/studioCatalog'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, clearSelect, openDropdown, optionTexts, selectRoot, shownDropdown } from '../test/antd'
import { ConnectorEditor } from './ConnectorEditor'

const connector = {
  id: '409bc638-0aec-4084-ada2-e3baed50057d',
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
      input: [
        { name: 'requestNumber', type: 'text', required: true },
        { name: 'supplierCode', type: 'text' },
      ],
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

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderEditor(value: Record<string, unknown> = connector, onChange = vi.fn(), bindings?: StudioBindings) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioBindingsContext.Provider value={bindings}>
            <ConnectorEditor value={value} onChange={onChange} />
          </StudioBindingsContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

/** The starter of a connector with the identity of the fixture, as Studio creates it. */
const starter = {
  id: oauth2Fixture.id,
  kind: 'connector',
  name: oauth2Fixture.name,
  formatVersion: 1,
  baseUrl: { binding: '' },
  operations: [{ name: 'Operation', method: 'GET', path: '/' }],
}

/** Holds the draft as Studio does: each change is applied to it, and `onDraft` gets every new draft. */
function renderStateful(onDraft: (draft: Record<string, unknown>) => void) {
  function Harness() {
    const [draft, setDraft] = useState<Record<string, unknown>>(starter)
    useEffect(() => onDraft(draft), [draft])
    return (
      <ConnectorEditor
        value={draft}
        onChange={(pointer, next) => setDraft((current) => setAt(current, pointer, next) as Record<string, unknown>)}
      />
    )
  }
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <Harness />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

/** The label and the help of a connector field's text, in English. */
function texts(path: string) {
  const key = `shell.studio.field.connector.${path}`
  const catalogue: Record<string, string> = platformEnglish
  return { label: catalogue[key], help: catalogue[`${key}.help`] }
}

/** The input with the label of a connector field, which has the field's help as its description. */
function field(path: string, role: ByRoleMatcher = 'textbox', scope: BoundFunctions<typeof queries> = screen) {
  const { label, help } = texts(path)
  const input = scope.getByRole(role, { name: label })
  expect(input).toHaveAccessibleDescription(help)
  return input
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('ConnectorEditor in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with the bindings as names and the operations as cards', () => {
    renderEditor()

    const editor = screen.getByTestId('studio-connector-editor')
    expect(editor.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(editor).toBeVisible()
    expect(field('baseUrl')).toBeVisible()
    expect(field('baseUrl')).toHaveValue('PurchasingSystemUrl')
    expect(field('auth.token')).toHaveValue('PurchasingSystemToken')
    expect(field('timeout')).toHaveValue('PT30S')
    expect(field('idempotency')).toHaveValue('Idempotency-Key')
    const first = screen.getByTestId('studio-operation-0')
    expect(first).toBeVisible()
    expect(field('operations.name', 'textbox', within(first))).toHaveValue('createPurchaseOrder')
    expect(field('operations.path', 'textbox', within(first))).toBeVisible()
    expect(within(first).getAllByRole('textbox', { name: texts('operations.input.name').label })).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Add operation' })).toBeVisible()
  })
})

describe('ConnectorEditor', () => {
  it('shows the description after the name, and writes its key', async () => {
    const onChange = renderEditor({ ...connector, description: { textKey: 'connectors.purchasing.description' } })

    const description = screen.getByTestId('studio-field-/description')
    const box = within(description).getByRole('textbox', { name: 'Text key' })
    expect(box).toHaveValue('connectors.purchasing.description')
    expect(
      screen.getByTestId('studio-field-/name').compareDocumentPosition(description) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0)
    expect(
      description.compareDocumentPosition(screen.getByTestId('studio-field-/baseUrl/binding')) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).not.toBe(0)

    await userEvent.type(box, 'x')
    expect(onChange).toHaveBeenLastCalledWith('/description/textKey', 'connectors.purchasing.descriptionx')
  })

  it('shows only a binding name, never a value, whatever else the file holds', () => {
    renderEditor({ ...connector, baseUrl: { binding: 'PurchasingSystemUrl', value: 'https://secret.example' } })

    expect(screen.queryByDisplayValue('https://secret.example')).not.toBeInTheDocument()
    expect(field('baseUrl')).toHaveValue('PurchasingSystemUrl')
  })

  it('edits a binding by its name at the binding pointer', async () => {
    const onChange = renderEditor()

    await userEvent.type(field('baseUrl'), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/baseUrl/binding', 'PurchasingSystemUrlX')
  })

  it('removes the timeout key when it is cleared and the auth when its type is cleared', async () => {
    const onChange = renderEditor()

    await userEvent.clear(field('timeout'))
    expect(onChange).toHaveBeenLastCalledWith('/timeout', undefined)

    await clearSelect(selectRoot(field('auth.type', 'combobox')))
    expect(onChange).toHaveBeenLastCalledWith('/auth', undefined)
  })

  it('drops the keys the new auth type forbids and sets the keys it requires', async () => {
    const onChange = renderEditor()

    await userEvent.click(field('auth.type', 'combobox'))
    await userEvent.click(await screen.findByTitle('header'))

    expect(onChange.mock.calls).toEqual([
      ['/auth/type', 'header'],
      ['/auth/token', undefined],
      ['/auth/tokenUrl', undefined],
      ['/auth/clientId', undefined],
      ['/auth/clientSecret', undefined],
      ['/auth/scope', undefined],
      ['/auth/audience', undefined],
      ['/auth/name', ''],
      ['/auth/value', { binding: '' }],
    ])
  })

  it('sets a whole auth object when the connector has none', async () => {
    const { auth: _auth, ...withoutAuth } = connector
    const onChange = renderEditor(withoutAuth)

    await userEvent.click(field('auth.type', 'combobox'))
    await userEvent.click(await screen.findByTitle('bearer'))

    expect(onChange).toHaveBeenLastCalledWith('/auth', { type: 'bearer', token: { binding: '' } })
  })

  it('adds an operation after the others and removes one by its index', async () => {
    const onChange = renderEditor()

    await userEvent.click(screen.getByRole('button', { name: 'Add operation' }))
    expect(onChange).toHaveBeenLastCalledWith('/operations', [
      ...connector.operations,
      { name: '', method: 'POST', path: '/' },
    ])

    await userEvent.click(
      within(screen.getByTestId('studio-operation-1')).getByRole('button', { name: 'Remove operation' }),
    )
    expect(onChange).toHaveBeenLastCalledWith('/operations/1', undefined)
  })

  it('adds and removes an input row and an output row', async () => {
    const onChange = renderEditor()
    const first = within(screen.getByTestId('studio-operation-0'))

    await userEvent.click(first.getByRole('button', { name: 'Add input field' }))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input', [
      ...connector.operations[0].input,
      { name: '', type: 'text' },
    ])
    await userEvent.click(first.getByRole('button', { name: 'Add output field' }))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/output', [
      ...connector.operations[0].output,
      { name: '', type: 'text' },
    ])

    const row = within(screen.getByTestId('studio-field-/operations/0/input/1'))
    await userEvent.click(row.getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/1', undefined)
  })

  it('edits a field row by name, type and required', async () => {
    const onChange = renderEditor()
    const row = within(screen.getByTestId('studio-field-/operations/0/input/1'))

    await userEvent.type(field('operations.input.name', 'textbox', row), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/1/name', 'supplierCodeX')
    await userEvent.click(field('operations.input.required', 'checkbox', row))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/1/required', true)
  })

  it('offers only the other operations as a lookup', async () => {
    renderEditor()

    await userEvent.click(within(screen.getByTestId('studio-field-/operations/0/lookup')).getByRole('combobox'))
    const options = optionTexts()
    expect(options).toEqual(['findPurchaseOrder'])
  })

  it('keeps the rows of a GET with input and says that a GET sends no body', () => {
    const operations = [{ name: 'find', method: 'GET', path: '/x', input: [{ name: 'a', type: 'text' }] }]
    renderEditor({ ...connector, operations })

    expect(field('operations.input.name')).toHaveValue('a')
    expect(screen.getByTestId('studio-connector-get-no-input')).toBeVisible()
  })

  it('does not say that a GET sends no body when its rows all go in the path, the query or a header', () => {
    const operations = [
      {
        name: 'find',
        method: 'GET',
        path: '/x/{a}',
        input: [
          { name: 'a', type: 'text', required: true, in: 'path' },
          { name: 'b', type: 'text', in: 'query' },
        ],
      },
    ]
    renderEditor({ ...connector, operations })

    expect(screen.getAllByRole('textbox', { name: texts('operations.input.name').label })).toHaveLength(2)
    expect(screen.queryByTestId('studio-connector-get-no-input')).toBeNull()
  })

  it('shows the secret binding and the header name of a header auth', () => {
    renderEditor({ ...connector, auth: { type: 'header', name: 'X-Api-Key', value: { binding: 'ApiKey' } } })

    expect(field('auth.name')).toHaveValue('X-Api-Key')
    expect(field('auth.value')).toHaveValue('ApiKey')
  })

  it('builds an oauth2 connector with path, query, nested list and source in the form controls', async () => {
    let latest: Record<string, unknown> = starter
    renderStateful((draft) => {
      latest = draft
    })
    const user = userEvent.setup()

    await user.type(field('baseUrl'), 'PurchasingSystemUrl')
    await chooseOption('oauth2', screen.getByTestId('studio-field-/auth/type'))
    await user.type(field('auth.tokenUrl'), 'PurchasingSystemTokenUrl')
    await user.type(field('auth.clientId'), 'PurchasingSystemClientId')
    await user.type(field('auth.clientSecret'), 'PurchasingSystemClientSecret')
    await user.type(field('auth.scope'), 'shipments.write')
    expect(field('auth.audience')).toHaveValue('')

    const operation = within(screen.getByTestId('studio-operation-0'))
    await user.clear(field('operations.name', 'textbox', operation))
    await user.type(field('operations.name', 'textbox', operation), 'createShipment')
    await chooseOption('POST', screen.getByTestId('studio-field-/operations/0/method'))
    await user.clear(field('operations.path', 'textbox', operation))
    await user.type(field('operations.path', 'textbox', operation), '/warehouses/{{warehouse}/shipments')

    for (let index = 0; index < 3; index++) {
      await user.click(operation.getByRole('button', { name: 'Add input field' }))
    }
    const row = (index: number) => `studio-field-/operations/0/input/${index}`
    const warehouse = within(screen.getByTestId(row(0)))
    await user.type(field('operations.input.name', 'textbox', warehouse), 'warehouse')
    await chooseOption('path', screen.getByTestId(`${row(0)}/in`))
    await user.click(field('operations.input.required', 'checkbox', warehouse))

    const dryRun = within(screen.getByTestId(row(1)))
    await user.type(field('operations.input.name', 'textbox', dryRun), 'dryRun')
    await chooseOption('boolean', screen.getByTestId(`${row(1)}/type`))
    await chooseOption('query', screen.getByTestId(`${row(1)}/in`))

    await user.type(field('operations.input.name', 'textbox', within(screen.getByTestId(`${row(2)}/name`))), 'lines')
    await chooseOption('list', screen.getByTestId(`${row(2)}/type`))
    expect(screen.queryByTestId(`${row(2)}/in`)).toBeNull()
    const sku = within(screen.getByTestId(`${row(2)}/fields/0`))
    await user.type(field('operations.input.fields.name', 'textbox', sku), 'sku')
    await user.click(field('operations.input.fields.required', 'checkbox', sku))
    await user.click(within(screen.getByTestId(row(2))).getByRole('button', { name: 'Add nested field' }))
    const quantity = within(screen.getByTestId(`${row(2)}/fields/1`))
    await user.type(field('operations.input.fields.name', 'textbox', quantity), 'quantity')
    await chooseOption('integer', screen.getByTestId(`${row(2)}/fields/1/type`))
    await user.click(field('operations.input.fields.required', 'checkbox', quantity))

    await user.click(operation.getByRole('button', { name: 'Add output field' }))
    const output = within(screen.getByTestId('studio-field-/operations/0/output/0'))
    await user.type(field('operations.output.name', 'textbox', output), 'trackingNumber')
    await user.type(field('operations.output.source', 'textbox', output), '/shipment/tracking/number')

    expect(latest).toEqual(oauth2Fixture)
  })

  it('swaps the keys of oauth2 and bearer both ways', async () => {
    const onChange = renderEditor({ ...connector, auth: oauth2Fixture.auth })

    await userEvent.click(field('auth.type', 'combobox'))
    await userEvent.click(await screen.findByTitle('bearer'))
    expect(onChange.mock.calls).toEqual([
      ['/auth/type', 'bearer'],
      ['/auth/name', undefined],
      ['/auth/value', undefined],
      ['/auth/tokenUrl', undefined],
      ['/auth/clientId', undefined],
      ['/auth/clientSecret', undefined],
      ['/auth/scope', undefined],
      ['/auth/audience', undefined],
      ['/auth/token', { binding: '' }],
    ])
  })

  it('adds the oauth2 bindings and removes the bearer token when the type becomes oauth2', async () => {
    const onChange = renderEditor()

    await userEvent.click(field('auth.type', 'combobox'))
    await userEvent.click(await screen.findByTitle('oauth2'))
    expect(onChange.mock.calls).toEqual([
      ['/auth/type', 'oauth2'],
      ['/auth/token', undefined],
      ['/auth/name', undefined],
      ['/auth/value', undefined],
      ['/auth/tokenUrl', { binding: '' }],
      ['/auth/clientId', { binding: '' }],
      ['/auth/clientSecret', { binding: '' }],
    ])
  })

  it("lists only the tenant's URL bindings for the base URL, and its secret bindings for the token", async () => {
    const bindings = { url: ['ShopUrl', 'ShopTokenUrl'], secret: ['ShopToken'], storage: ['Files'] }
    const onChange = renderEditor(
      { ...connector, baseUrl: { binding: 'ShopUrl' }, auth: { type: 'bearer', token: { binding: 'ShopToken' } } },
      vi.fn(),
      bindings,
    )

    expect(screen.queryByRole('textbox', { name: texts('baseUrl').label })).toBeNull()
    await userEvent.click(field('baseUrl', 'combobox'))
    expect(optionTexts(await openDropdown())).toEqual(['ShopUrl', 'ShopTokenUrl'])
    await chooseOption('ShopTokenUrl')
    expect(onChange).toHaveBeenLastCalledWith('/baseUrl/binding', 'ShopTokenUrl')
    expect(screen.queryByTestId('studio-tenant-binding-missing')).toBeNull()

    await userEvent.click(field('auth.token', 'combobox'))
    expect(optionTexts(await openDropdown('ShopToken'))).toEqual(['ShopToken'])
  })

  it('removes the scope key when the box is cleared', async () => {
    const onChange = renderEditor({ ...connector, auth: oauth2Fixture.auth })

    expect(field('auth.tokenUrl')).toHaveValue('PurchasingSystemTokenUrl')
    expect(field('auth.clientId')).toHaveValue('PurchasingSystemClientId')
    expect(screen.queryByRole('textbox', { name: texts('auth.token').label })).toBeNull()
    await userEvent.clear(field('auth.scope'))
    expect(onChange).toHaveBeenLastCalledWith('/auth/scope', undefined)
  })

  it('shows body for an input without in, and removes the key when body is chosen', async () => {
    const operations = [
      {
        name: 'find',
        method: 'POST',
        path: '/x',
        input: [
          { name: 'a', type: 'text' },
          { name: 'b', type: 'text', in: 'query' },
        ],
      },
    ]
    const onChange = renderEditor({ ...connector, operations })

    const placements = screen.getAllByRole('combobox', { name: texts('operations.input.in').label })
    expect(placements).toHaveLength(2)
    expect(within(screen.getByTestId('studio-field-/operations/0/input/0/in')).getByTitle('body')).toBeVisible()
    expect(within(screen.getByTestId('studio-field-/operations/0/input/1/in')).getByTitle('query')).toBeVisible()

    await chooseOption('body', screen.getByTestId('studio-field-/operations/0/input/1/in'))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/1/in', undefined)
    await chooseOption('header', screen.getByTestId('studio-field-/operations/0/input/0/in'))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/0/in', 'header')
  })

  it('adds fields and removes in when an input becomes a list, and removes fields when it becomes text again', async () => {
    const operations = [{ name: 'find', method: 'POST', path: '/x', input: [{ name: 'a', type: 'text', in: 'query' }] }]
    const onChange = renderEditor({ ...connector, operations })

    await chooseOption('list', screen.getByTestId('studio-field-/operations/0/input/0/type'))
    expect(onChange.mock.calls).toEqual([
      ['/operations/0/input/0/type', 'list'],
      ['/operations/0/input/0/in', undefined],
      ['/operations/0/input/0/fields', [{ name: '', type: 'text' }]],
    ])
  })

  it('removes fields when a list input becomes a scalar type', async () => {
    const operations = [
      {
        name: 'find',
        method: 'POST',
        path: '/x',
        input: [{ name: 'lines', type: 'list', fields: [{ name: 'sku', type: 'text' }] }],
      },
    ]
    const onChange = renderEditor({ ...connector, operations })

    expect(screen.queryByRole('combobox', { name: texts('operations.input.in').label })).toBeNull()
    expect(field('operations.input.fields.name')).toHaveValue('sku')
    await chooseOption('text', screen.getByTestId('studio-field-/operations/0/input/0/type'))
    expect(onChange.mock.calls).toEqual([
      ['/operations/0/input/0/type', 'text'],
      ['/operations/0/input/0/fields', undefined],
    ])
  })

  it('adds, edits and removes a nested field', async () => {
    const operations = [
      {
        name: 'find',
        method: 'POST',
        path: '/x',
        input: [{ name: 'lines', type: 'list', fields: [{ name: 'sku', type: 'text' }] }],
      },
    ]
    const onChange = renderEditor({ ...connector, operations })

    await userEvent.click(screen.getByRole('button', { name: 'Add nested field' }))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/0/fields', [
      { name: 'sku', type: 'text' },
      { name: '', type: 'text' },
    ])
    const nested = within(screen.getByTestId('studio-field-/operations/0/input/0/fields/0'))
    await userEvent.click(field('operations.input.fields.required', 'checkbox', nested))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/0/fields/0/required', true)
    await userEvent.click(nested.getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/0/fields/0', undefined)
  })

  it('offers object and list in a nested row only down to the fourth level', async () => {
    const level5 = { name: 'leaf', type: 'text' }
    const level4 = { name: 'deep', type: 'object', fields: [level5] }
    const level3 = { name: 'inner', type: 'list', fields: [level4] }
    const level2 = { name: 'mid', type: 'object', fields: [level3] }
    const operations = [
      { name: 'find', method: 'POST', path: '/x', input: [{ name: 'top', type: 'list', fields: [level2] }] },
    ]
    renderEditor({ ...connector, operations })
    const level4Row = 'studio-field-/operations/0/input/0/fields/0/fields/0/fields/0'

    await userEvent.click(within(screen.getByTestId(`${level4Row}/type`)).getByRole('combobox'))
    expect(optionTexts(shownDropdown()!)).toContain('object')
    expect(optionTexts(shownDropdown()!)).toContain('list')
    await userEvent.keyboard('{Escape}')

    await userEvent.click(within(screen.getByTestId(`${level4Row}/fields/0/type`)).getByRole('combobox'))
    expect(optionTexts(shownDropdown()!)).toEqual(['text', 'integer', 'decimal', 'boolean', 'date', 'date-time'])
  })

  it('offers object and list in a nested row at the third level', async () => {
    const level3 = { name: 'inner', type: 'text' }
    const level2 = { name: 'mid', type: 'object', fields: [level3] }
    const operations = [
      { name: 'find', method: 'POST', path: '/x', input: [{ name: 'top', type: 'list', fields: [level2] }] },
    ]
    renderEditor({ ...connector, operations })

    await userEvent.click(
      within(screen.getByTestId('studio-field-/operations/0/input/0/fields/0/fields/0/type')).getByRole('combobox'),
    )
    expect(optionTexts(shownDropdown()!)).toContain('list')
  })

  it('sets an output source from its box', async () => {
    const onChange = renderEditor()
    const output = within(screen.getByTestId('studio-field-/operations/0/output/0'))

    await userEvent.type(field('operations.output.source', 'textbox', output), 'x')
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/output/0/source', 'x')
  })

  it('removes the source key of an output when its box is cleared', async () => {
    const operations = [
      { name: 'find', method: 'GET', path: '/x', output: [{ name: 'n', type: 'text', source: '/a/b' }] },
    ]
    const onChange = renderEditor({ ...connector, operations })

    expect(field('operations.output.source')).toHaveValue('/a/b')
    expect(screen.queryByRole('combobox', { name: texts('operations.input.in').label })).toBeNull()
    await userEvent.clear(field('operations.output.source'))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/output/0/source', undefined)
  })

  describe('draws every field through AxisField with its label and help', () => {
    const operations = [
      {
        name: 'find',
        method: 'POST',
        path: '/x',
        input: [
          { name: 'a', type: 'text' },
          { name: 'lines', type: 'list', fields: [{ name: 'sku', type: 'text' }] },
        ],
        output: [{ name: 'n', type: 'text' }],
      },
      { name: 'other', method: 'GET', path: '/y' },
    ]
    const common: [string, string, ByRoleMatcher][] = [
      ['name', '/name', 'textbox'],
      ['baseUrl', '/baseUrl/binding', 'textbox'],
      ['timeout', '/timeout', 'textbox'],
      ['idempotency', '/idempotency', 'textbox'],
      ['auth.type', '/auth/type', 'combobox'],
      ['operations.name', '/operations/0/name', 'textbox'],
      ['operations.method', '/operations/0/method', 'combobox'],
      ['operations.path', '/operations/0/path', 'textbox'],
      ['operations.lookup', '/operations/0/lookup', 'combobox'],
      ['operations.input.name', '/operations/0/input/0/name', 'textbox'],
      ['operations.input.type', '/operations/0/input/0/type', 'combobox'],
      ['operations.input.in', '/operations/0/input/0/in', 'combobox'],
      ['operations.input.required', '/operations/0/input/0/required', 'checkbox'],
      ['operations.input.fields.name', '/operations/0/input/1/fields/0/name', 'textbox'],
      ['operations.input.fields.type', '/operations/0/input/1/fields/0/type', 'combobox'],
      ['operations.input.fields.required', '/operations/0/input/1/fields/0/required', 'checkbox'],
      ['operations.output.name', '/operations/0/output/0/name', 'textbox'],
      ['operations.output.type', '/operations/0/output/0/type', 'combobox'],
      ['operations.output.required', '/operations/0/output/0/required', 'checkbox'],
      ['operations.output.source', '/operations/0/output/0/source', 'textbox'],
    ]
    const groups: [string, string][] = [
      ['description', '/description'],
      ['operations.input', '/operations/0/input'],
      ['operations.input.fields', '/operations/0/input/1/fields'],
      ['operations.output', '/operations/0/output'],
    ]
    const variants: { auth: Record<string, unknown>; fields: [string, string, ByRoleMatcher][] }[] = [
      { auth: { type: 'bearer', token: { binding: 'T' } }, fields: [['auth.token', '/auth/token/binding', 'textbox']] },
      {
        auth: { type: 'header', name: 'X-Api-Key', value: { binding: 'K' } },
        fields: [
          ['auth.name', '/auth/name', 'textbox'],
          ['auth.value', '/auth/value/binding', 'textbox'],
        ],
      },
      {
        auth: oauth2Fixture.auth,
        fields: [
          ['auth.tokenUrl', '/auth/tokenUrl/binding', 'textbox'],
          ['auth.clientId', '/auth/clientId/binding', 'textbox'],
          ['auth.clientSecret', '/auth/clientSecret/binding', 'textbox'],
          ['auth.scope', '/auth/scope', 'textbox'],
          ['auth.audience', '/auth/audience', 'textbox'],
        ],
      },
    ]

    it.each(variants)('finds each field of the $auth.type auth and of the rest', ({ auth, fields }) => {
      renderEditor({ ...connector, description: { textKey: 'k' }, auth, operations })

      for (const [path, pointer, role] of [...common, ...fields]) {
        field(path, role, within(screen.getByTestId(`studio-field-${pointer}`)))
      }
      for (const [path, pointer] of groups) {
        const { label, help } = texts(path)
        const group = within(screen.getByTestId(`studio-field-${pointer}`)).getAllByRole('group', { name: label })[0]
        expect(group).toHaveAccessibleDescription(help)
      }
    })
  })

  it('lays the editor out in the General, Service, Authentication and Operations sections', () => {
    renderEditor()

    const sections = [
      ...screen.getByTestId('studio-connector-editor').querySelectorAll('[data-testid^="studio-section-"]'),
    ]
    expect(sections.map((section) => section.getAttribute('data-testid'))).toEqual([
      'studio-section-general',
      'studio-section-service',
      'studio-section-auth',
      'studio-section-operations',
    ])
    for (const name of ['general', 'service', 'auth', 'operations']) {
      const prefix = `shell.studio.section.connector.${name}`
      const section = within(screen.getByTestId(`studio-section-${name}`))
      expect(
        section.getByRole('heading', { name: platformEnglish[prefix as keyof typeof platformEnglish] }),
      ).toBeVisible()
      expect(section.getByText(platformEnglish[`${prefix}.line` as keyof typeof platformEnglish])).toBeVisible()
    }
    expect(
      within(screen.getByTestId('studio-section-operations')).getByRole('button', { name: 'Add operation' }),
    ).toBeVisible()
    expect(
      within(screen.getByTestId('studio-section-general')).queryByRole('button', { name: 'Add operation' }),
    ).toBeNull()
  })

  it('has no separate line under a binding, because its help says it all', () => {
    renderEditor()

    expect(screen.queryByText(/Only the name is stored/)).toBeNull()
    expect(field('baseUrl')).toHaveAccessibleDescription(texts('baseUrl').help)
  })
})
