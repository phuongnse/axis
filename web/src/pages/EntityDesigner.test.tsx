import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { fieldSettingNames, fieldSettings, fieldTypes } from '../platform/entityFields'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, clearSelect, openDropdown, optionTexts, removeTag } from '../test/antd'
import { EntityDesigner } from './EntityDesigner'
import { LabelTextsContext } from './labelTextsContext'

const names = {
  locales: ['en', 'vi'],
  entities: [
    { id: 'e1', name: 'Invoice', module: null, fields: [] },
    { id: 'e2', name: 'Supplier', module: null, fields: [] },
  ],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [{ id: 's1', name: 'InvoiceNumbers' }],
  forms: [],
  pages: [],
  texts: [
    { key: 'entity.invoice', texts: { en: 'Invoice', vi: 'Hóa đơn' }, usedBy: 1 },
    { key: 'field.total', texts: { en: 'Total', vi: 'Tổng' }, usedBy: 1 },
    { key: 'validation.total', texts: { en: 'Total must be positive', vi: 'Tổng phải dương' }, usedBy: 1 },
  ],
} as unknown as StudioNames

const labelContext = {
  application: 'Shop',
  file: 'entities/invoice.json',
  name: 'Invoice',
  saved: {},
  onTextsWritten: vi.fn(),
}

/** One field of each type, with every setting its type takes. */
const everySetting = {
  required: true,
  unique: true,
  maxLength: 20,
  precision: 18,
  scale: 2,
  target: 'Supplier',
  values: ['open', 'closed'],
  expression: 'total * 2',
  sequence: 'InvoiceNumbers',
  creator: true,
  sensitive: true,
}

const typed = fieldTypes.map((type) => ({
  name: `${type.replace('-', '')}Field`,
  type,
  ...Object.fromEntries(fieldSettings(type).map((setting) => [setting, everySetting[setting]])),
}))

function entity(extra: Record<string, unknown> = {}) {
  return {
    id: 'b5a5f1cb-4f0f-4f3d-9a54-9d6e3a1b7f10',
    kind: 'entity',
    name: 'Invoice',
    formatVersion: 1,
    label: { textKey: 'entity.invoice' },
    fields: [
      { name: 'total', type: 'decimal', label: { textKey: 'field.total' }, unique: true, precision: 18, scale: 2 },
      { name: 'code', type: 'text', maxLength: 20 },
      { name: 'note', type: 'text' },
    ],
    displayField: 'code',
    ...extra,
  }
}

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderDesigner(
  value: Record<string, unknown>,
  onChange = vi.fn(),
  loaded: StudioNames | null = names,
  readOnly = false,
) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={loaded ?? undefined}>
            <LabelTextsContext.Provider value={labelContext}>
              <EntityDesigner value={value} onChange={onChange} readOnly={readOnly} />
            </LabelTextsContext.Provider>
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('EntityDesigner in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('shows, for each type, inputs only for the settings the type takes', () => {
    renderDesigner(entity({ fields: typed, displayField: undefined }))

    const designer = screen.getByTestId('studio-entity-designer')
    expect(designer.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(designer).toBeVisible()
    typed.forEach((field, index) => {
      const row = screen.getByTestId(`studio-field-/fields/${index}`)
      expect(row).toBeVisible()
      for (const setting of fieldSettingNames) {
        const present = screen.queryByTestId(`studio-field-/fields/${index}/${setting}`) !== null
        expect(present, `${field.type} ${setting}`).toBe(fieldSettings(field.type).includes(setting))
        if (present) {
          expect(row.contains(screen.getByTestId(`studio-field-/fields/${index}/${setting}`))).toBe(true)
        }
      }
    })
  })
})

describe('EntityDesigner', () => {
  it('shows the header and the fields of the entity', () => {
    renderDesigner(entity())

    expect(screen.getByRole('textbox', { name: 'Entity name' })).toHaveValue('Invoice')
    expect(within(screen.getByTestId('studio-field-/label')).getByRole('textbox', { name: 'en' })).toHaveValue(
      'Invoice',
    )
    expect(within(screen.getByTestId('studio-field-/displayField')).getByText('code')).toBeInTheDocument()
    expect(within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')).toHaveValue('20')
    expect(within(screen.getByTestId('studio-field-/fields/0/name')).getByRole('textbox')).toHaveValue('total')
  })

  it('lists the field names of the draft in the display field picker and clears it with undefined', async () => {
    const onChange = renderDesigner(entity())
    const field = screen.getByTestId('studio-field-/displayField')

    await userEvent.click(within(field).getByRole('combobox'))
    expect(optionTexts(await openDropdown())).toEqual(['total', 'code', 'note'])
    await userEvent.keyboard('{Escape}')
    await clearSelect(field)

    expect(onChange).toHaveBeenCalledWith('/displayField', undefined)
  })

  it('changes the type of a field without precision and scale and keeps unique', async () => {
    const onChange = renderDesigner(entity())

    await chooseOption('enum', screen.getByTestId('studio-field-/fields/0/type'))

    expect(onChange).toHaveBeenCalledWith('/fields/0', {
      name: 'total',
      type: 'enum',
      label: { textKey: 'field.total' },
      unique: true,
    })
  })

  it('removes a field label with undefined and writes a picked key under textKey', async () => {
    const onChange = renderDesigner(entity())
    const label = screen.getByTestId('studio-field-/fields/0/label')

    await clearSelect(label)
    expect(onChange).toHaveBeenCalledWith('/fields/0/label', undefined)

    await chooseOption('field.total', screen.getByTestId('studio-field-/fields/1/label'))
    expect(onChange).toHaveBeenCalledWith('/fields/1/label/textKey', 'field.total')
  })

  it('sends undefined for a cleared maxLength and the number for a typed one', async () => {
    const onChange = renderDesigner(entity())
    const maxLength = within(screen.getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton')

    await userEvent.clear(maxLength)
    expect(onChange).toHaveBeenLastCalledWith('/fields/1/maxLength', undefined)
    await userEvent.type(maxLength, '5')
    expect(onChange).toHaveBeenLastCalledWith('/fields/1/maxLength', 5)
  })

  it('sends undefined when every enum value is cleared', async () => {
    const onChange = renderDesigner(entity({ fields: [{ name: 'state', type: 'enum', values: ['open'] }] }))
    const values = screen.getByTestId('studio-field-/fields/0/values')

    await removeTag(values)

    expect(onChange).toHaveBeenCalledWith('/fields/0/values', undefined)
  })

  it('moves, removes and adds a field by replacing the whole array', async () => {
    const value = entity()
    const onChange = renderDesigner(value)
    const fields = value.fields
    const row = (index: number) => within(screen.getByTestId(`studio-field-/fields/${index}`))

    expect(row(0).getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(row(2).getByRole('button', { name: 'Move down' })).toBeDisabled()

    await userEvent.click(row(1).getByRole('button', { name: 'Move up' }))
    expect(onChange).toHaveBeenLastCalledWith('/fields', [fields[1], fields[0], fields[2]])
    await userEvent.click(row(1).getByRole('button', { name: 'Move down' }))
    expect(onChange).toHaveBeenLastCalledWith('/fields', [fields[0], fields[2], fields[1]])
    await userEvent.click(row(0).getByRole('button', { name: 'Remove field' }))
    expect(onChange).toHaveBeenLastCalledWith('/fields', [fields[1], fields[2]])
    await userEvent.click(screen.getByRole('button', { name: 'Add field' }))
    expect(onChange).toHaveBeenLastCalledWith('/fields', [...fields, { name: '', type: 'text' }])
  })

  it('adds and removes a validation, with a message key and a field of the draft', async () => {
    const validation = { expression: 'total > 0', message: { textKey: 'validation.total' }, field: 'total' }
    const value = entity({ validations: [validation] })
    const onChange = renderDesigner(value)

    const row = screen.getByTestId('studio-field-/validations/0')
    expect(within(row).getByRole('textbox', { name: 'Validation expression' })).toHaveValue('total > 0')
    expect(
      within(screen.getByTestId('studio-field-/validations/0/message/textKey')).getByText('validation.total'),
    ).toBeInTheDocument()
    await chooseOption('code', screen.getByTestId('studio-field-/validations/0/field'))
    expect(onChange).toHaveBeenLastCalledWith('/validations/0/field', 'code')

    await userEvent.click(within(row).getByRole('button', { name: 'Remove validation' }))
    expect(onChange).toHaveBeenLastCalledWith('/validations/0', undefined)
    await userEvent.click(screen.getByRole('button', { name: 'Add validation' }))
    expect(onChange).toHaveBeenLastCalledWith('/validations', [
      validation,
      { expression: '', message: { textKey: '' }, field: '' },
    ])
  })

  it('points to the JSON view for fields or validations that are not arrays', () => {
    renderDesigner(entity({ fields: 'x', validations: 'y' }))

    expect(within(screen.getByTestId('studio-field-/fields')).getByTestId('studio-json-only')).toBeInTheDocument()
    expect(within(screen.getByTestId('studio-field-/validations')).getByTestId('studio-json-only')).toBeInTheDocument()
  })

  it('turns every input off and offers no edit button when it is read-only', async () => {
    const onChange = renderDesigner(
      entity({
        fields: [
          {
            name: 'code',
            type: 'text',
            required: true,
            unique: true,
            maxLength: 20,
            label: { textKey: 'field.total' },
          },
          { name: 'status', type: 'enum', values: ['open', 'closed'] },
          { name: 'total', type: 'decimal', precision: 18, scale: 2, expression: 'code' },
        ],
        validations: [{ expression: 'total > 0', message: { textKey: 'validation.total' }, field: 'total' }],
      }),
      vi.fn(),
      names,
      true,
    )

    const inputs = [
      ...screen.getAllByRole('textbox'),
      ...screen.getAllByRole('combobox'),
      ...screen.getAllByRole('checkbox'),
      ...screen.getAllByRole('spinbutton'),
    ]
    expect(screen.getAllByRole('spinbutton').length).toBeGreaterThan(0)
    expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(0)
    for (const input of inputs) {
      expect(input).toBeDisabled()
    }
    expect(within(screen.getByTestId('studio-field-/label')).getByRole('textbox')).toHaveValue('entity.invoice')
    for (const name of ['Add field', 'Remove field', 'Move up', 'Move down', 'Remove validation', 'Add validation']) {
      expect(screen.queryByRole('button', { name })).toBeNull()
    }

    await userEvent.type(within(screen.getByTestId('studio-field-/fields/0/name')).getByRole('textbox'), 'x')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('shows plain boxes until the names have loaded', () => {
    renderDesigner(
      entity({ validations: [{ expression: 'total > 0', message: { textKey: 'v' }, field: 'total' }] }),
      vi.fn(),
      null,
    )

    expect(within(screen.getByTestId('studio-field-/label/textKey')).getByRole('textbox')).toHaveValue('entity.invoice')
    expect(within(screen.getByTestId('studio-field-/fields/0/label')).getByRole('textbox')).toHaveValue('field.total')
    expect(within(screen.getByTestId('studio-field-/validations/0/message/textKey')).getByRole('textbox')).toHaveValue(
      'v',
    )
  })
})
