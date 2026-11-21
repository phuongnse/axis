import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioNames } from '../platform/studio'
import { colourTokens } from '../platform/designTokens'
import { studioResourceAddress } from '../platform/studioAddress'
import { TextProvider } from '../platform/TextProvider'
import { StudioApplicationContext, StudioNamesContext } from '../platform/studioNames'
import type { JsonSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { ReferencePicker } from './ReferencePicker'
import { SchemaForm } from './SchemaForm'

const names: StudioNames = {
  locales: ['en'],
  entities: [
    {
      id: '1',
      name: 'Order',
      module: null,
      fields: [
        { name: 'Id', type: 'integer', target: null, values: null, unique: false },
        { name: 'Total', type: 'decimal', target: null, values: null, unique: false },
      ],
    },
    {
      id: '2',
      name: 'Customer',
      module: null,
      fields: [{ name: 'Email', type: 'text', target: null, values: null, unique: false }],
    },
    { id: '5', name: 'Supplier', labelKey: 'entities.supplier.label', module: null, fields: [] },
    { id: '6', name: 'Vendor', labelKey: 'entities.vendor.label', module: null, fields: [] },
    { id: '7', name: 'Invoice', labelKey: 'entities.invoice.label', module: null, fields: [] },
  ],
  dataSources: [
    {
      id: '4',
      name: 'Orders',
      module: null,
      parameters: [
        { name: 'status', type: 'text' },
        { name: 'limit', type: 'integer' },
      ],
      columns: [
        { name: 'openCount', type: 'integer' },
        { name: 'region', type: 'text' },
      ],
      groupBy: null,
    },
  ],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [{ id: '3', name: 'manager' }],
  sequences: [],
  forms: [],
  pages: [],
  policies: [],
  contracts: [],
  texts: [
    { key: 'entities.supplier.label', texts: { en: 'Partner' }, usedBy: 1 },
    { key: 'entities.vendor.label', texts: { en: 'Partner' }, usedBy: 1 },
    { key: 'entities.invoice.label', texts: { en: 'Bill' }, usedBy: 1 },
  ],
  seeds: [],
  triggers: [],
  scenarios: [],
  textResources: [],
}

// A grant names its entity at `/grants/<n>/entity`, so a field of a grant reads the entity two levels up.
const schema: JsonSchema = {
  type: 'object',
  properties: {
    entity: { type: 'string', 'x-axis-reference': 'entity' },
    grants: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          entity: { type: 'string', 'x-axis-reference': 'entity' },
          fields: {
            type: 'array',
            items: { type: 'string', 'x-axis-reference': { part: 'field', of: '2/entity' } },
          },
        },
      },
    },
  },
}

// A widget names its data source at `dataSource`. Its measure and the keys of its parameters name parts of it.
const widgetSchema: JsonSchema = {
  type: 'object',
  properties: {
    dataSource: { type: 'string', 'x-axis-reference': 'dataSource' },
    measure: { type: 'string', 'x-axis-reference': { part: 'column', of: '1/dataSource' } },
    parameters: {
      type: 'object',
      propertyNames: { type: 'string', 'x-axis-reference': { part: 'parameter', of: '2/dataSource' } },
    },
  },
}

// A group entry is a plain name or an object with a bucket, as in the data source schema.
const groupSchema: JsonSchema = {
  type: 'object',
  properties: {
    groupBy: { type: 'array', items: { $ref: '#/$defs/groupEntry' } },
  },
  $defs: {
    name: { type: 'string' },
    groupEntry: {
      oneOf: [
        { $ref: '#/$defs/name' },
        {
          type: 'object',
          properties: { field: { $ref: '#/$defs/name' }, bucket: { enum: ['day', 'week', 'month', 'year'] } },
        },
      ],
    },
  },
}

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

/** The address the probe shows, so a test can see where an Open link led. */
function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}

/** Renders the form. With `application`, Studio's editor is open on it, inside a router with a location probe. */
function renderForm(
  value: Record<string, unknown>,
  namesLoaded = true,
  formSchema: JsonSchema = schema,
  application?: string,
) {
  const onChange = vi.fn()
  const form = (
    <StudioNamesContext.Provider value={namesLoaded ? names : undefined}>
      <SchemaForm schema={formSchema} value={value} onChange={onChange} />
    </StudioNamesContext.Provider>
  )
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          {application === undefined ? (
            form
          ) : (
            <MemoryRouter initialEntries={['/studio?app=Shop']}>
              <StudioApplicationContext.Provider value={application}>
                {form}
                <LocationProbe />
              </StudioApplicationContext.Provider>
            </MemoryRouter>
          )}
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('ReferencePicker in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('offers the application names and reports the one that is picked', async () => {
    const onChange = renderForm({ entity: 'Order', grants: [] })

    const picker = screen.getByRole('combobox', { name: 'entity' })
    expect(picker).toBeVisible()
    await userEvent.click(picker)
    expect(screen.getByRole('option', { name: 'Order' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('option', { name: 'Customer' }))

    expect(onChange).toHaveBeenLastCalledWith('/entity', 'Customer')
  })

  it('filters the names by what is typed', async () => {
    renderForm({ grants: [] })

    await userEvent.type(screen.getByRole('combobox', { name: 'entity' }), 'cus')

    expect(screen.getByRole('option', { name: 'Customer' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Order' })).not.toBeInTheDocument()
  })

  it('shows a choice by its label, and a choice with no label by its name', async () => {
    renderForm({ grants: [] })

    await userEvent.click(screen.getByRole('combobox', { name: 'entity' }))

    expect(screen.getByRole('option', { name: 'Bill' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Invoice' })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Order' })).toBeInTheDocument()
  })

  it('shows the name after a label that choices share, in the muted colour', async () => {
    renderForm({ grants: [] })

    await userEvent.click(screen.getByRole('combobox', { name: 'entity' }))

    const supplier = screen.getByRole('option', { name: 'Partner Supplier' })
    const name = within(supplier).getByText('Supplier')
    expect(screen.getByRole('option', { name: 'Partner Vendor' })).toBeInTheDocument()
    expect(within(screen.getByRole('option', { name: 'Bill' })).queryByText('Invoice')).not.toBeInTheDocument()
    expect(name).toHaveStyle({ color: colourTokens.textMuted[mode as 'light' | 'dark'] })
  })

  it('finds a choice by its label or by its name, and writes the name', async () => {
    const onChange = renderForm({ grants: [] })
    const picker = screen.getByRole('combobox', { name: 'entity' })

    await userEvent.type(picker, 'bil')
    expect(screen.getByRole('option', { name: 'Bill' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Order' })).not.toBeInTheDocument()

    await userEvent.clear(picker)
    await userEvent.type(picker, 'invo')
    await userEvent.click(screen.getByRole('option', { name: 'Bill' }))

    expect(onChange).toHaveBeenLastCalledWith('/entity', 'Invoice')
  })

  it('shows a choice by its ready label, finds it by that label and writes its name', async () => {
    const onChange = vi.fn()
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ReferencePicker
            name="Find a step"
            placeholder="Find a step"
            width={200}
            required
            value={undefined}
            options={[
              { name: '0', label: 'Submit request' },
              { name: '1', label: 'Check the amount' },
            ]}
            onChange={onChange}
          />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const picker = screen.getByRole('combobox', { name: 'Find a step' })
    await userEvent.type(picker, 'amou')
    expect(screen.queryByRole('option', { name: 'Submit request' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('option', { name: 'Check the amount' }))

    expect(onChange).toHaveBeenLastCalledWith('1')
  })

  it('keeps a value that is not a name and tags it unknown', async () => {
    renderForm({ entity: 'Gone', grants: [] })

    const unknown = screen.getByTestId('studio-reference-unknown')
    expect(unknown).toHaveTextContent('Gone')
    expect(unknown).toHaveTextContent('unknown')

    await userEvent.click(screen.getByRole('combobox', { name: 'entity' }))
    expect(screen.getAllByTestId('studio-reference-unknown')).toHaveLength(2)
  })

  it('matches a value to a name without regard to case', () => {
    renderForm({ entity: 'order', grants: [] })

    expect(screen.queryByTestId('studio-reference-unknown')).not.toBeInTheDocument()
    expect(screen.getByText('Order')).toBeInTheDocument()
  })

  it('lists the fields of the entity that the grant names, from the draft, in the grant dialog', async () => {
    const onChange = renderForm({ grants: [{ entity: 'Order', fields: ['Total'] }] })

    await userEvent.click(screen.getByRole('button', { name: 'Order' }))
    const dialog = await screen.findByRole('dialog')
    const fields = within(dialog).getByTestId('studio-field-/grants/0/fields/0')
    await userEvent.click(within(fields).getByRole('combobox', { name: 'fields 1' }))

    expect(screen.getByRole('option', { name: 'Id' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Total' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Email' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('option', { name: 'Id' }))
    expect(onChange).not.toHaveBeenCalled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))
    expect(onChange).toHaveBeenLastCalledWith('/grants/0', { entity: 'Order', fields: ['Id'] })
  })

  it('opens a chosen entity in Studio from an Open link joined to the select', async () => {
    renderForm({ entity: 'Order', grants: [] }, true, schema, 'Shop')

    const open = screen.getByRole('link', { name: 'Open entity' })
    expect(open).toHaveAttribute('href', studioResourceAddress('Shop', '1'))

    await userEvent.click(open)
    expect(screen.getByTestId('location').textContent).toBe(studioResourceAddress('Shop', '1'))
  })

  it('opens a chosen role from an Open link to its resource', () => {
    const roleSchema: JsonSchema = {
      type: 'object',
      properties: { role: { type: 'string', 'x-axis-reference': 'role' } },
    }
    renderForm({ role: 'manager' }, true, roleSchema, 'Shop')

    expect(screen.getByRole('link', { name: 'Open role' })).toHaveAttribute('href', studioResourceAddress('Shop', '3'))
  })

  it.each([
    ['empty', { grants: [] }],
    ['unknown', { entity: 'Gone', grants: [] }],
  ])('shows no Open link when the value is %s', (_, value) => {
    renderForm(value, true, schema, 'Shop')

    expect(screen.queryByRole('link', { name: 'Open entity' })).not.toBeInTheDocument()
  })

  it('shows no Open link on a text key, which names no resource', () => {
    const textSchema: JsonSchema = {
      type: 'object',
      properties: { text: { type: 'string', 'x-axis-reference': 'text' } },
    }
    renderForm({ text: 'entities.supplier.label' }, true, textSchema, 'Shop')

    expect(screen.getByRole('combobox', { name: 'text' })).toBeVisible()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('shows no Open link on a field of the grant dialog, which is part of a resource', async () => {
    renderForm({ grants: [{ entity: 'Order', fields: ['Total'] }] }, true, schema, 'Shop')

    await userEvent.click(screen.getByRole('button', { name: 'Order' }))
    const dialog = await screen.findByRole('dialog')
    const fields = within(dialog).getByTestId('studio-field-/grants/0/fields/0')

    expect(within(fields).getByRole('combobox', { name: 'fields 1' })).toBeVisible()
    expect(within(fields).queryByRole('link')).not.toBeInTheDocument()
  })

  it('shows no Open link outside the Studio editor, even for a chosen entity', () => {
    renderForm({ entity: 'Order', grants: [] })

    expect(screen.getByRole('combobox', { name: 'entity' })).toBeVisible()
    expect(screen.queryByRole('link', { name: 'Open entity' })).not.toBeInTheDocument()
  })

  it('shows a text box until the names load', () => {
    renderForm({ entity: 'Order', grants: [] }, false)

    expect(screen.getByRole('textbox', { name: 'entity' })).toHaveValue('Order')
    expect(screen.queryByRole('combobox', { name: 'entity' })).not.toBeInTheDocument()
  })

  it('lists the columns of the data source that the widget names', async () => {
    const onChange = renderForm({ dataSource: 'Orders', measure: 'openCount' }, true, widgetSchema)

    await userEvent.click(screen.getByRole('combobox', { name: 'measure' }))
    expect(screen.getByRole('option', { name: 'region' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'status' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('option', { name: 'region' }))
    expect(onChange).toHaveBeenLastCalledWith('/measure', 'region')
  })

  it('shows a map with marked keys as rows with a key picker', async () => {
    const onChange = renderForm({ dataSource: 'Orders', parameters: { status: 'open' } }, true, widgetSchema)

    expect(screen.getByRole('combobox', { name: 'parameters 1' })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'status' })).toHaveValue('open')
    expect(screen.queryByTestId('studio-json-only')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('combobox', { name: 'parameters 1' }))
    // The key that another row uses is not offered twice, and this row's own key stays.
    expect(screen.getAllByRole('option', { name: 'status' }).length).toBeGreaterThan(0)
    await userEvent.click(screen.getByRole('option', { name: 'limit' }))
    expect(onChange).toHaveBeenLastCalledWith('/parameters', { limit: 0 })
  })

  it('adds a row with the first unused name from the title row, and removes a row from its Actions menu', async () => {
    const onChange = renderForm({ dataSource: 'Orders', parameters: { status: 'open' } }, true, widgetSchema)
    const group = screen.getByRole('group', { name: 'parameters' })

    await userEvent.click(within(group).getByRole('button', { name: 'Add' }))
    expect(onChange).toHaveBeenLastCalledWith('/parameters', { status: 'open', limit: 0 })
    expect(within(group).getByRole('button', { name: 'Add' }).closest('legend')).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull()

    await userEvent.click(within(group).getByRole('button', { name: 'Actions' }))
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/parameters/status', undefined)
  })

  it('shows a number box for an integer parameter and reports the typed number', async () => {
    const onChange = renderForm({ dataSource: 'Orders', parameters: { limit: 3 } }, true, widgetSchema)

    const box = screen.getByRole('spinbutton', { name: 'limit' })
    expect(box).toHaveValue('3')
    await userEvent.clear(box)
    await userEvent.type(box, '7')

    expect(onChange).toHaveBeenLastCalledWith('/parameters/limit', 7)
  })

  it('keeps a map with marked keys in the JSON view until the names load', () => {
    renderForm({ dataSource: 'Orders', parameters: { status: 'open' } }, false, widgetSchema)

    expect(screen.getByTestId('studio-json-only')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'parameters 1' })).not.toBeInTheDocument()
  })

  it('disables Add when every name is used', () => {
    renderForm({ dataSource: 'Orders', parameters: { status: 'a', limit: 'b' } }, true, widgetSchema)

    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
  })

  it('edits a name entry of alternatives as a text box and sends an object entry to the JSON view', async () => {
    const onChange = renderForm({ groupBy: ['status', { field: 'placedOn', bucket: 'month' }] }, true, groupSchema)

    expect(screen.getByRole('textbox', { name: 'groupBy 1' })).toHaveValue('status')
    expect(screen.getAllByTestId('studio-json-only')).toHaveLength(1)
    expect(screen.getByTestId('studio-field-/groupBy/1')).toContainElement(screen.getByTestId('studio-json-only'))

    await userEvent.type(screen.getByRole('textbox', { name: 'groupBy 1' }), 'x')
    expect(onChange).toHaveBeenLastCalledWith('/groupBy/0', 'statusx')

    await userEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(onChange).toHaveBeenLastCalledWith('/groupBy', ['status', { field: 'placedOn', bucket: 'month' }, ''])
  })
})

describe('ReferencePicker in a field', () => {
  it('fills the field with a chosen resource, its select and its Open link joined together', () => {
    renderForm({ entity: 'Order', grants: [] }, true, schema, 'Shop')

    const group = screen.getByRole('link', { name: 'Open entity' }).closest<HTMLElement>('.ant-space-compact')
    expect(group?.closest('.axis-field')).not.toBeNull()
    expect(group).toHaveStyle({ width: '100%' })
    expect(group?.querySelector('.ant-select')).toHaveStyle({ flexGrow: '1' })
  })
})
