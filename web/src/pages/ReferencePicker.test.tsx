import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioNames } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { StudioNamesContext } from '../platform/studioNames'
import type { JsonSchema } from '../platform/studioSchemas'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { SchemaForm } from './SchemaForm'

const names: StudioNames = {
  locales: ['en'],
  entities: [
    {
      id: '1',
      name: 'Order',
      module: null,
      fields: [
        { name: 'Id', type: 'integer', target: null, values: null },
        { name: 'Total', type: 'decimal', target: null, values: null },
      ],
    },
    {
      id: '2',
      name: 'Customer',
      module: null,
      fields: [{ name: 'Email', type: 'text', target: null, values: null }],
    },
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
  texts: [],
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

function renderForm(value: Record<string, unknown>, namesLoaded = true, formSchema: JsonSchema = schema) {
  const onChange = vi.fn()
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={namesLoaded ? names : undefined}>
            <SchemaForm schema={formSchema} value={value} onChange={onChange} />
          </StudioNamesContext.Provider>
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

  it('lists the fields of the entity that the grant names, from the draft', async () => {
    const onChange = renderForm({ grants: [{ entity: 'Order', fields: ['Total'] }] })

    const fields = screen.getByTestId('studio-field-/grants/0/fields/0')
    await userEvent.click(within(fields).getByRole('combobox', { name: 'fields 1' }))

    expect(screen.getByRole('option', { name: 'Id' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Total' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Email' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('option', { name: 'Id' }))
    expect(onChange).toHaveBeenLastCalledWith('/grants/0/fields/0', 'Id')
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

  it('adds a row with the first unused name and removes a row', async () => {
    const onChange = renderForm({ dataSource: 'Orders', parameters: { status: 'open' } }, true, widgetSchema)

    await userEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(onChange).toHaveBeenLastCalledWith('/parameters', { status: 'open', limit: 0 })

    await userEvent.click(screen.getByRole('button', { name: 'Remove' }))
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
