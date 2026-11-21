import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { ExpressionCheckContext } from './expressionCheckContext'
import { MappingRows } from './MappingRows'
import type { ChangeHandler } from './SchemaForm'

const title = 'Output'
const value = { orderNumber: 'purchaseOrderNumber', total: 'totalAmount' }

/** Renders the rows as a connector call step draws its output: typed keys, or picked from `keyOptions`. */
function renderRows({
  data = value,
  keyOptions,
  readOnly = false,
  onChange = vi.fn<ChangeHandler>(),
}: {
  data?: unknown
  keyOptions?: string[]
  readOnly?: boolean
  onChange?: ChangeHandler
} = {}) {
  render(
    <ConfigProvider componentDisabled={readOnly}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ExpressionCheckContext.Provider
            value={{ application: 'Shop', resourceId: '4b60c03e', kind: 'process', draft: () => ({}), names: null }}
          >
            <MappingRows
              pointer="/steps/1/output"
              title={title}
              help="What the connector returns."
              value={data}
              keyLabel="Connector output"
              keyOptions={keyOptions}
              target={{ kind: 'field', label: 'Entity field' }}
              onChange={onChange}
            />
          </ExpressionCheckContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </ConfigProvider>,
  )
  return onChange
}

beforeEach(() =>
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(null, { status: 404 })),
  ),
)

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe('MappingRows', () => {
  it('puts Add row on the title row as a text button and Remove in each row’s menu', async () => {
    const onChange = renderRows()

    const group = within(screen.getByRole('group', { name: title }))
    const add = group.getByRole('button', { name: 'Add row' })
    expect(add.closest('legend')).not.toBeNull()
    expect(add).toHaveClass('ant-btn-variant-text')
    expect(group.getAllByRole('button', { name: 'Add row' })).toHaveLength(1)
    expect(group.queryByRole('button', { name: 'Remove' })).toBeNull()

    for (const key of Object.keys(value)) {
      expect(
        within(screen.getByTestId(`studio-field-/steps/1/output/${key}`)).getByRole('button', { name: 'Actions' }),
      ).toBeVisible()
    }
    await userEvent.click(
      within(screen.getByTestId('studio-field-/steps/1/output/total')).getByRole('button', { name: 'Actions' }),
    )
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/output/total', undefined)

    await userEvent.click(add)
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/output/', '')
  })

  it('puts the add choice of the unmapped keys on the title row while the keys are known', async () => {
    const onChange = renderRows({ keyOptions: ['orderNumber', 'total', 'currency'] })

    const add = screen.getByRole('combobox', { name: `Add ${title}` })
    expect(add.closest('legend')).not.toBeNull()
    await userEvent.click(add)
    await userEvent.click(await screen.findByTitle('currency'))
    expect(onChange).toHaveBeenLastCalledWith('/steps/1/output/currency', '')
  })

  it.each([
    { when: 'typed', keyOptions: undefined },
    { when: 'known', keyOptions: ['orderNumber', 'total', 'currency'] },
  ])('shows no Add and no row menu when the editor is read-only and the keys are $when', ({ keyOptions }) => {
    renderRows({ keyOptions, readOnly: true })

    expect(screen.getByTestId('studio-field-/steps/1/output/total')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Add row' })).toBeNull()
    expect(screen.queryByRole('combobox', { name: `Add ${title}` })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Actions' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull()
  })

  it('points to the JSON view for a value that is not an object', () => {
    renderRows({ data: ['x'] })

    expect(screen.getByTestId('studio-json-only')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Add row' })).toBeNull()
  })
})
