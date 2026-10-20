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
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [{ id: '3', name: 'manager' }],
  sequences: [],
  forms: [],
  pages: [],
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

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderForm(value: Record<string, unknown>, namesLoaded = true) {
  const onChange = vi.fn()
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={namesLoaded ? names : undefined}>
            <SchemaForm schema={schema} value={value} onChange={onChange} />
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
})
