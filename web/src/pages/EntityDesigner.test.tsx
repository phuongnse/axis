import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { useState, type ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import {
  editableSettings,
  fieldSettingNames,
  fieldSettings,
  fieldTypes,
  type FieldSetting,
} from '../platform/entityFields'
import { setAt } from '../platform/jsonPatch'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, clearPicker, clearSelect, openDropdown, optionTexts, removeTag } from '../test/antd'
import { EntityDesigner } from './EntityDesigner'
import { ExpressionCheckContext } from './expressionCheckContext'
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
  minLength: 2,
  min: 0,
  max: 10,
  searchable: true,
  default: '1',
  precision: 18,
  scale: 2,
  target: 'Supplier',
  values: ['open', 'closed'],
  expression: 'total * 2',
  sequence: 'InvoiceNumbers',
  creator: true,
  sensitive: true,
  maxSize: 1048576,
  accept: ['application/pdf'],
  currency: 'VND',
  currencyField: 'code',
  valueLabels: { open: { textKey: 'field.total' }, closed: { textKey: 'entity.invoice' } },
}

/** The limit of a date or date-time field is a string, and the one of a number field is a number. */
function valueOf(type: string, setting: keyof typeof everySetting) {
  if (setting === 'min' || setting === 'max') {
    return type === 'date' ? '2026-01-05' : type === 'date-time' ? '2026-01-05T10:30:00Z' : everySetting[setting]
  }
  return everySetting[setting]
}

function fixture(skipped: FieldSetting[]) {
  return fieldTypes.map((type) => ({
    name: `${type.replace('-', '')}Field`,
    type,
    ...Object.fromEntries(
      fieldSettings(type)
        .filter((setting) => !skipped.includes(setting))
        .map((setting) => [setting, valueOf(type, setting)]),
    ),
  }))
}

/** One field of each type, with every setting its type takes. Its computed fields hide `default` and the limits. */
const typed = fixture([])

/** One field of each type without `expression`, `sequence` or `creator`, so each type shows all its other settings. */
const plain = fixture(['expression', 'sequence', 'creator'])

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
    <Providers loaded={loaded}>
      <EntityDesigner value={value} onChange={onChange} readOnly={readOnly} />
    </Providers>,
  )
  return onChange
}

function Providers({ loaded, children }: { loaded: StudioNames | null; children: ReactNode }) {
  return (
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={loaded ?? undefined}>
            <LabelTextsContext.Provider value={labelContext}>{children}</LabelTextsContext.Provider>
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>
  )
}

/**
 * The designer over an entity that it edits, as the editor does: each change is applied with `setAt`. It returns the
 * whole content of the entity.
 */
function renderContent(initial: Record<string, unknown>) {
  function Harness() {
    const [value, setValue] = useState(initial)
    return (
      <>
        <EntityDesigner
          value={value}
          onChange={(pointer, next) => setValue((current) => setAt(current, pointer, next) as Record<string, unknown>)}
        />
        <output data-testid="entity-content">{JSON.stringify(value)}</output>
      </>
    )
  }
  render(
    <Providers loaded={names}>
      <Harness />
    </Providers>,
  )
  return () => JSON.parse(screen.getByTestId('entity-content').textContent ?? '') as Record<string, unknown>
}

/** The fields of the entity that the designer edits. */
function renderEditing(initial: Record<string, unknown>) {
  const content = renderContent(initial)
  return () => content().fields as Record<string, unknown>[]
}

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe.each(['light', 'dark'])('EntityDesigner in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it.each([
    ['with a computed field of each type', typed],
    ['with no computed field', plain],
  ])('shows, for each type, inputs only for the settings the type takes, %s', (_, fields) => {
    renderDesigner(entity({ fields, displayField: undefined }))

    const designer = screen.getByTestId('studio-entity-designer')
    expect(designer.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(designer).toBeVisible()
    fields.forEach((field, index) => {
      const row = screen.getByTestId(`studio-field-/fields/${index}`)
      expect(row).toBeVisible()
      for (const setting of fieldSettingNames) {
        const present = screen.queryByTestId(`studio-field-/fields/${index}/${setting}`) !== null
        // A money field shows exactly one of its two currency keys, the one the field holds.
        const expected =
          field.type === 'money' && (setting === 'currency' || setting === 'currencyField')
            ? setting === (Object.hasOwn(field, 'currencyField') ? 'currencyField' : 'currency')
            : editableSettings(field).includes(setting)
        expect(present, `${field.type} ${setting}`).toBe(expected)
        if (present) {
          expect(row.contains(screen.getByTestId(`studio-field-/fields/${index}/${setting}`))).toBe(true)
        }
      }
    })
  })

  it('shows default, min, max and minLength on every type that takes them when the field computes nothing', () => {
    renderDesigner(entity({ fields: plain, displayField: undefined }))

    for (const setting of ['default', 'min', 'max', 'minLength'] as const) {
      const types = plain.flatMap((field, index) =>
        screen.queryByTestId(`studio-field-/fields/${index}/${setting}`) === null ? [] : [field.type],
      )
      expect(types, setting).toEqual(
        plain.filter((field) => fieldSettings(field.type).includes(setting)).map((f) => f.type),
      )
      expect(types.length, setting).toBeGreaterThan(0)
    }
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

  it('switches a money field between a fixed code and a currency field', async () => {
    const fields = renderEditing(
      entity({
        fields: [
          { name: 'code', type: 'text' },
          { name: 'status', type: 'enum', values: ['open'] },
          { name: 'amount', type: 'money', currency: 'USD' },
        ],
      }),
    )
    const amount = within(screen.getByTestId('studio-field-/fields/2'))

    await userEvent.click(amount.getByRole('radio', { name: 'Field' }))
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money', currencyField: '' })

    await userEvent.click(amount.getByRole('combobox', { name: 'Currency field' }))
    expect(optionTexts(await openDropdown())).toEqual(['code', 'status'])
    await chooseOption('code')
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money', currencyField: 'code' })

    await userEvent.click(amount.getByRole('radio', { name: 'Fixed code' }))
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money' })
    await userEvent.type(amount.getByRole('textbox', { name: 'Currency' }), 'vnd')
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money', currency: 'VND' })
    await userEvent.clear(amount.getByRole('textbox', { name: 'Currency' }))
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money' })
  })

  it('sets and clears the max size and the accepted media types of a file field', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'receipt', type: 'file' }] }))
    const receipt = within(screen.getByTestId('studio-field-/fields/0'))

    await userEvent.type(receipt.getByRole('spinbutton', { name: 'Max size (bytes)' }), '1048576')
    expect(fields()[0]).toEqual({ name: 'receipt', type: 'file', maxSize: 1048576 })

    const accept = receipt.getByRole('combobox', { name: 'Accepted media types' })
    await userEvent.type(accept, 'application/pdf')
    await chooseOption('application/pdf')
    await userEvent.type(accept, 'image/*')
    await chooseOption('image/*')
    expect(fields()[0]).toEqual({
      name: 'receipt',
      type: 'file',
      maxSize: 1048576,
      accept: ['application/pdf', 'image/*'],
    })

    await userEvent.clear(receipt.getByRole('spinbutton', { name: 'Max size (bytes)' }))
    expect(fields()[0]).toEqual({ name: 'receipt', type: 'file', accept: ['application/pdf', 'image/*'] })
  })

  it('sets the label of each enum value and removes valueLabels with the last one', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'status', type: 'enum', values: ['open', 'closed'] }] }))
    const status = within(screen.getByTestId('studio-field-/fields/0'))
    const picker = (value: string) => status.getByTestId(`studio-field-/fields/0/valueLabels/${value}`)

    expect(status.getByRole('combobox', { name: 'Label of open' })).toBeInTheDocument()
    await chooseOption('entity.invoice', picker('closed'))
    expect(fields()[0].valueLabels).toEqual({ closed: { textKey: 'entity.invoice' } })
    await chooseOption('field.total', picker('open'))
    expect(fields()[0].valueLabels).toEqual({ open: { textKey: 'field.total' }, closed: { textKey: 'entity.invoice' } })
    expect(Object.keys(fields()[0].valueLabels as object)).toEqual(['open', 'closed'])

    await clearSelect(picker('open'))
    await clearSelect(picker('closed'))
    expect(fields()[0]).toEqual({ name: 'status', type: 'enum', values: ['open', 'closed'] })
    expect(Object.hasOwn(fields()[0], 'valueLabels')).toBe(false)
  })

  it('sets and clears the default of a field, with no key left after it is cleared', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'state', type: 'enum', values: ['open'] }] }))
    const state = within(screen.getByTestId('studio-field-/fields/0'))

    await userEvent.type(state.getByRole('textbox', { name: 'Default' }), "'open'")
    expect(fields()[0]).toEqual({ name: 'state', type: 'enum', values: ['open'], default: "'open'" })
    await userEvent.clear(state.getByRole('textbox', { name: 'Default' }))
    expect(fields()[0]).toEqual({ name: 'state', type: 'enum', values: ['open'] })
    expect(Object.hasOwn(fields()[0], 'default')).toBe(false)
  })

  it('sets and clears min and max of an integer, a decimal and a money field', async () => {
    const fields = renderEditing(
      entity({
        fields: [
          { name: 'count', type: 'integer' },
          { name: 'total', type: 'decimal' },
          { name: 'price', type: 'money', currency: 'VND' },
        ],
      }),
    )
    const row = (index: number) => within(screen.getByTestId(`studio-field-/fields/${index}`))

    await userEvent.type(row(0).getByRole('spinbutton', { name: 'Min' }), '0')
    await userEvent.type(row(0).getByRole('spinbutton', { name: 'Max' }), '10')
    expect(fields()[0]).toEqual({ name: 'count', type: 'integer', min: 0, max: 10 })
    await userEvent.clear(row(0).getByRole('spinbutton', { name: 'Min' }))
    expect(fields()[0]).toEqual({ name: 'count', type: 'integer', max: 10 })
    await userEvent.clear(row(0).getByRole('spinbutton', { name: 'Max' }))
    expect(fields()[0]).toEqual({ name: 'count', type: 'integer' })

    await userEvent.type(row(1).getByRole('spinbutton', { name: 'Min' }), '-1.5')
    expect(fields()[1]).toEqual({ name: 'total', type: 'decimal', min: -1.5 })
    await userEvent.type(row(2).getByRole('spinbutton', { name: 'Max' }), '99.25')
    expect(fields()[2]).toEqual({ name: 'price', type: 'money', currency: 'VND', max: 99.25 })
    await userEvent.clear(row(2).getByRole('spinbutton', { name: 'Max' }))
    expect(fields()[2]).toEqual({ name: 'price', type: 'money', currency: 'VND' })
  })

  it('sets and clears min of a date field as yyyy-MM-dd and of a date-time field as an RFC 3339 UTC string', async () => {
    const fields = renderEditing(
      entity({
        fields: [
          { name: 'day', type: 'date' },
          { name: 'at', type: 'date-time' },
        ],
      }),
    )
    const day = within(screen.getByTestId('studio-field-/fields/0')).getByRole('textbox', { name: 'Min' })
    const at = within(screen.getByTestId('studio-field-/fields/1')).getByRole('textbox', { name: 'Min' })

    await userEvent.type(day, '2026-01-05{Enter}')
    expect(fields()[0]).toEqual({ name: 'day', type: 'date', min: '2026-01-05' })
    await clearPicker(screen.getByTestId('studio-field-/fields/0/min'))
    expect(fields()[0]).toEqual({ name: 'day', type: 'date' })

    await userEvent.type(at, '2026-01-05 10:30:00{Enter}')
    const written = fields()[1].min as string
    expect(written).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00Z$/)
    expect(new Date(written).getTime()).toBe(new Date(2026, 0, 5, 10, 30, 0).getTime())
  })

  it('shows the date and date-time limits of the file in their pickers', () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'day', type: 'date', min: '2026-01-05', max: '2026-02-01' },
          { name: 'at', type: 'date-time', min: '2026-01-05T10:30:00Z' },
        ],
      }),
    )

    expect(within(screen.getByTestId('studio-field-/fields/0/min')).getByRole('textbox', { name: 'Min' })).toHaveValue(
      '2026-01-05',
    )
    expect(within(screen.getByTestId('studio-field-/fields/0/max')).getByRole('textbox', { name: 'Max' })).toHaveValue(
      '2026-02-01',
    )
    expect(within(screen.getByTestId('studio-field-/fields/1/min')).getByRole('textbox', { name: 'Min' })).toHaveValue(
      dayjs('2026-01-05T10:30:00Z').format('YYYY-MM-DD HH:mm:ss'),
    )
  })

  it('sets and clears minLength and searchable of a text field, and removes the key for a switch turned off', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'code', type: 'text' }] }))
    const code = within(screen.getByTestId('studio-field-/fields/0'))

    await userEvent.type(code.getByRole('spinbutton', { name: 'Min length' }), '2')
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', minLength: 2 })
    await userEvent.click(code.getByRole('switch', { name: 'Searchable' }))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', minLength: 2, searchable: true })
    expect(code.getByRole('switch', { name: 'Searchable' })).toBeChecked()

    await userEvent.clear(code.getByRole('spinbutton', { name: 'Min length' }))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', searchable: true })
    await userEvent.click(code.getByRole('switch', { name: 'Searchable' }))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text' })
    expect(Object.hasOwn(fields()[0], 'searchable')).toBe(false)
  })

  it('reaches the switch and the limit inputs with the keyboard', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'code', type: 'text' }] }))

    screen.getByRole('switch', { name: 'Searchable' }).focus()
    await userEvent.keyboard(' ')
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', searchable: true })
  })

  it('hides default, min, max and minLength next to an expression, and keeps a key that is already there', () => {
    renderDesigner(
      entity({
        fields: [{ name: 'code', type: 'text', sequence: 'InvoiceNumbers', minLength: 2, default: "'x'" }],
      }),
    )
    const code = within(screen.getByTestId('studio-field-/fields/0'))

    for (const name of ['Default', 'Min length']) {
      expect(code.queryByRole('textbox', { name })).toBeNull()
      expect(code.queryByRole('spinbutton', { name })).toBeNull()
    }
    expect(code.getByRole('switch', { name: 'Searchable' })).toBeInTheDocument()
  })

  it('keeps the other keys of the entity when it sets a setting', async () => {
    const content = renderContent(entity({ fields: [{ name: 'count', type: 'integer' }] }))

    await userEvent.type(screen.getByRole('spinbutton', { name: 'Min' }), '0')

    expect(content()).toEqual(entity({ fields: [{ name: 'count', type: 'integer', min: 0 }] }))
  })

  it('adds an index, picks two fields in order, marks it unique, moves it and removes it', async () => {
    const content = renderContent(
      entity({
        fields: [
          { name: 'stage', type: 'enum', values: ['open'] },
          { name: 'count', type: 'integer' },
          { name: 'lines', type: 'child-collection', target: 'Line' },
          { name: 'tags', type: 'references', target: 'Supplier' },
          { name: 'receipt', type: 'file' },
        ],
      }),
    )
    const indexes = () => content().indexes
    const row = (index: number) => within(screen.getByTestId(`studio-field-/indexes/${index}`))

    expect(screen.getByTestId('studio-field-/indexes')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Add index' }))
    expect(indexes()).toEqual([{ fields: [] }])

    await userEvent.click(row(0).getByRole('combobox', { name: 'Index fields' }))
    expect(optionTexts(await openDropdown())).toEqual(['stage', 'count'])
    await chooseOption('count')
    await chooseOption('stage')
    expect(indexes()).toEqual([{ fields: ['count', 'stage'] }])
    await userEvent.keyboard('{Escape}')

    await userEvent.click(row(0).getByRole('checkbox', { name: 'Unique index' }))
    expect(indexes()).toEqual([{ fields: ['count', 'stage'], unique: true }])

    await userEvent.click(screen.getByRole('button', { name: 'Add index' }))
    expect(indexes()).toEqual([{ fields: ['count', 'stage'], unique: true }, { fields: [] }])
    expect(row(0).getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(row(1).getByRole('button', { name: 'Move down' })).toBeDisabled()
    await userEvent.click(row(1).getByRole('button', { name: 'Move up' }))
    expect(indexes()).toEqual([{ fields: [] }, { fields: ['count', 'stage'], unique: true }])

    await userEvent.click(row(1).getByRole('checkbox', { name: 'Unique index' }))
    expect(indexes()).toEqual([{ fields: [] }, { fields: ['count', 'stage'] }])
    expect(Object.hasOwn((indexes() as object[])[1], 'unique')).toBe(false)

    await userEvent.click(row(0).getByRole('button', { name: 'Remove index' }))
    expect(indexes()).toEqual([{ fields: ['count', 'stage'] }])
    await userEvent.click(row(0).getByRole('button', { name: 'Remove index' }))
    expect(Object.hasOwn(content(), 'indexes')).toBe(false)
  })

  it('points to the JSON view for indexes that are not an array and hides the index buttons when it is read-only', () => {
    renderDesigner(entity({ indexes: 'x' }))
    expect(within(screen.getByTestId('studio-field-/indexes')).getByTestId('studio-json-only')).toBeInTheDocument()
  })

  it('offers no index button and turns the index inputs off when it is read-only', () => {
    renderDesigner(entity({ indexes: [{ fields: ['code'], unique: true }] }), vi.fn(), names, true)

    for (const name of ['Add index', 'Remove index']) {
      expect(screen.queryByRole('button', { name })).toBeNull()
    }
    expect(screen.getByRole('combobox', { name: 'Index fields' })).toBeDisabled()
    expect(screen.getByRole('checkbox', { name: 'Unique index' })).toBeDisabled()
  })

  it('checks a default at /fields/{i}/default, offers no field name and shows the unknown name error', async () => {
    const fetcher = vi.fn(
      async (_input: string, _init?: RequestInit) =>
        new Response(
          JSON.stringify({
            type: null,
            diagnostics: [{ code: 'AXC0046', message: "Unknown field 'total'", start: 0, end: 5 }],
          }),
          { status: 200 },
        ),
    )
    vi.stubGlobal('fetch', fetcher)
    const draft = entity({
      fields: [
        { name: 'total', type: 'decimal' },
        { name: 'tax', type: 'decimal' },
      ],
    })
    render(
      <Providers loaded={names}>
        <ExpressionCheckContext.Provider
          value={{ application: 'Shop', resourceId: 'b5a5f1cb', kind: 'entity', draft, names }}
        >
          <EntityDesigner value={draft} onChange={vi.fn()} />
        </ExpressionCheckContext.Provider>
      </Providers>,
    )
    const row = within(screen.getByTestId('studio-field-/fields/1/default'))

    // The editor loads lazily, and a plain read-only box stands in until it has.
    await waitFor(() =>
      expect(screen.getByTestId('studio-field-/fields/1/default').querySelector('.cm-editor')).not.toBeNull(),
    )
    const editor = row.getByRole('textbox', { name: 'Default' })
    // The completions are the rules and the functions: no field of the entity and no aggregate.
    await userEvent.click(editor)
    await userEvent.keyboard('{Control>}[Space]{/Control}')
    const completions = (await screen.findAllByRole('option')).map((option) => option.textContent ?? '')
    expect(completions.some((text) => text.includes('length()'))).toBe(true)
    for (const name of ['total', 'tax', 'count()', 'sum()']) {
      expect(
        completions.some((text) => text.startsWith(name)),
        name,
      ).toBe(false)
    }
    await userEvent.keyboard('{Escape}')

    await userEvent.type(editor, 'total')

    await waitFor(() => expect(screen.getByTestId('studio-expression-error')).toHaveTextContent('AXC0046'))
    const bodies = fetcher.mock.calls.map(([, init]) => JSON.parse(init!.body as string))
    expect(bodies.at(-1)).toMatchObject({ path: '/fields/1/default', expression: 'total' })
    expect(bodies.every((body) => body.path === '/fields/1/default')).toBe(true)
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
          {
            name: 'status',
            type: 'enum',
            values: ['open', 'closed'],
            valueLabels: { open: { textKey: 'field.total' } },
          },
          { name: 'total', type: 'decimal', precision: 18, scale: 2, expression: 'code' },
          { name: 'price', type: 'money', currency: 'VND' },
          { name: 'receipt', type: 'file', maxSize: 1024, accept: ['image/*'] },
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
      ...screen.getAllByRole('radio'),
    ]
    expect(screen.getAllByRole('spinbutton').length).toBeGreaterThan(0)
    expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(0)
    for (const name of ['Fixed code', 'Field']) {
      expect(screen.getByRole('radio', { name })).toBeDisabled()
    }
    for (const name of ['Max size (bytes)']) {
      expect(screen.getByRole('spinbutton', { name })).toBeDisabled()
    }
    for (const name of ['Accepted media types', 'Label of open', 'Label of closed']) {
      expect(screen.getByRole('combobox', { name })).toBeDisabled()
    }
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
