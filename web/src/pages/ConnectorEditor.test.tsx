import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
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

function renderEditor(value: Record<string, unknown> = connector, onChange = vi.fn()) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ConnectorEditor value={value} onChange={onChange} />
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('ConnectorEditor in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with the bindings as names and the operations as cards', () => {
    renderEditor()

    const editor = screen.getByTestId('studio-connector-editor')
    expect(editor.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(editor).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'URL binding' })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'URL binding' })).toHaveValue('PurchasingSystemUrl')
    expect(screen.getByRole('textbox', { name: 'Secret binding' })).toHaveValue('PurchasingSystemToken')
    expect(screen.getByRole('textbox', { name: 'Timeout' })).toHaveValue('PT30S')
    expect(screen.getByRole('textbox', { name: 'Idempotency header' })).toHaveValue('Idempotency-Key')
    const first = screen.getByTestId('studio-operation-0')
    expect(first).toBeVisible()
    expect(within(first).getByRole('textbox', { name: 'Operation name' })).toHaveValue('createPurchaseOrder')
    expect(within(first).getByRole('textbox', { name: 'Path' })).toBeVisible()
    expect(within(first).getAllByRole('textbox', { name: 'Input name' })).toHaveLength(2)
    expect(screen.getAllByText(/Only the name is stored/)[0]).toBeVisible()
    expect(screen.getByRole('button', { name: 'Add operation' })).toBeVisible()
  })
})

describe('ConnectorEditor', () => {
  it('shows only a binding name, never a value, whatever else the file holds', () => {
    renderEditor({ ...connector, baseUrl: { binding: 'PurchasingSystemUrl', value: 'https://secret.example' } })

    expect(screen.queryByDisplayValue('https://secret.example')).not.toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'URL binding' })).toHaveValue('PurchasingSystemUrl')
  })

  it('edits a binding by its name at the binding pointer', async () => {
    const onChange = renderEditor()

    await userEvent.type(screen.getByRole('textbox', { name: 'URL binding' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/baseUrl/binding', 'PurchasingSystemUrlX')
  })

  it('removes the timeout key when it is cleared and the auth when its type is cleared', async () => {
    const onChange = renderEditor()

    await userEvent.clear(screen.getByRole('textbox', { name: 'Timeout' }))
    expect(onChange).toHaveBeenLastCalledWith('/timeout', undefined)

    await userEvent.hover(screen.getByRole('combobox', { name: 'Auth type' }))
    await userEvent.click(document.querySelector('.ant-select-clear')!)
    expect(onChange).toHaveBeenLastCalledWith('/auth', undefined)
  })

  it('drops the keys the new auth type forbids and sets the keys it requires', async () => {
    const onChange = renderEditor()

    await userEvent.click(screen.getByRole('combobox', { name: 'Auth type' }))
    await userEvent.click(await screen.findByTitle('header'))

    expect(onChange.mock.calls).toEqual([
      ['/auth/type', 'header'],
      ['/auth/token', undefined],
      ['/auth/name', ''],
      ['/auth/value', { binding: '' }],
    ])
  })

  it('sets a whole auth object when the connector has none', async () => {
    const { auth: _auth, ...withoutAuth } = connector
    const onChange = renderEditor(withoutAuth)

    await userEvent.click(screen.getByRole('combobox', { name: 'Auth type' }))
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

    await userEvent.type(row.getByRole('textbox', { name: 'Input name' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/1/name', 'supplierCodeX')
    await userEvent.click(row.getByRole('checkbox', { name: 'Required' }))
    expect(onChange).toHaveBeenLastCalledWith('/operations/0/input/1/required', true)
  })

  it('offers only the other operations as a lookup', async () => {
    renderEditor()

    await userEvent.click(within(screen.getByTestId('studio-field-/operations/0/lookup')).getByRole('combobox'))
    const options = [...document.querySelectorAll('.ant-select-item-option')].map((option) => option.textContent)
    expect(options).toEqual(['findPurchaseOrder'])
  })

  it('keeps the rows of a GET with input and says that a GET sends no body', () => {
    const operations = [{ name: 'find', method: 'GET', path: '/x', input: [{ name: 'a', type: 'text' }] }]
    renderEditor({ ...connector, operations })

    expect(screen.getByRole('textbox', { name: 'Input name' })).toHaveValue('a')
    expect(screen.getByTestId('studio-connector-get-no-input')).toBeVisible()
  })

  it('shows the secret binding and the header name of a header auth', () => {
    renderEditor({ ...connector, auth: { type: 'header', name: 'X-Api-Key', value: { binding: 'ApiKey' } } })

    expect(screen.getByRole('textbox', { name: 'Header name' })).toHaveValue('X-Api-Key')
    expect(screen.getByRole('textbox', { name: 'Secret binding' })).toHaveValue('ApiKey')
  })
})
