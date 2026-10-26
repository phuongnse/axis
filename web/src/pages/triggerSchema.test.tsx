import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect, useState } from 'react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { setAt } from '../platform/jsonPatch'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema, type JsonSchema } from '../platform/studioSchemas'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { openDropdown, optionTexts } from '../test/antd'
import { ExpressionCheckContext } from './expressionCheckContext'
import { SchemaForm } from './SchemaForm'
import { triggerSchema } from './triggerSchema'

// The expression editor is CodeMirror. The mock shows the names it is offered, which is what these tests check.
vi.mock('./ExpressionEditor', () => ({
  default: ({ name, value, options }: { name: string; value: string; options: { label: string }[] }) => (
    <div
      role="group"
      aria-label={name}
      data-value={value}
      data-options={options.map((option) => option.label).join(',')}
    />
  ),
}))

const names = {
  locales: ['en'],
  entities: [
    {
      id: 'e1',
      name: 'PurchaseRequest',
      module: null,
      fields: [{ name: 'title', type: 'text', target: null, values: null }],
    },
  ],
  dataSources: [
    {
      id: 'd1',
      name: 'Overdue',
      module: null,
      parameters: [{ name: 'days', type: 'integer' }],
      columns: [],
      groupBy: null,
    },
  ],
  processes: [
    {
      id: 'p1',
      name: 'DailyReminder',
      parameters: [
        { name: 'daysOverdue', type: 'integer' },
        { name: 'label', type: 'text' },
      ],
      steps: [],
    },
    { id: 'p2', name: 'PurchaseRequestApproval', parameters: [], steps: [] },
  ],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [],
  forms: [],
  pages: [],
  texts: [],
} as unknown as StudioNames

let schema: JsonSchema

/** Applies each change to its own copy of the draft, as the Studio editor does, and exposes the latest draft. */
function renderTrigger(value: Record<string, unknown>) {
  const latest = { value }
  function Harness() {
    const [draft, setDraft] = useState(value)
    useEffect(() => {
      latest.value = draft
    })
    return (
      <ExpressionCheckContext.Provider
        value={{ application: 'Shop', resourceId: '4b60c03e', kind: 'trigger', draft, names }}
      >
        <SchemaForm
          schema={triggerSchema(schema, draft)}
          value={draft}
          onChange={(pointer, next) => setDraft((current) => setAt(current, pointer, next) as Record<string, unknown>)}
        />
      </ExpressionCheckContext.Provider>
    )
  }
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={names}>
          <Harness />
        </StudioNamesContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return latest
}

const base = {
  id: '4b60c03e-02d0-4329-80c6-75f972190399',
  kind: 'trigger',
  formatVersion: 1,
  process: 'DailyReminder',
}

const schedule = {
  ...base,
  name: 'EveryMorning',
  type: 'schedule',
  schedule: '0 8 * * 1-5',
  timeZone: 'Asia/Ho_Chi_Minh',
}

const deletion = {
  ...base,
  name: 'RequestDeleted',
  type: 'dataChange',
  entity: 'PurchaseRequest',
  on: ['delete'],
}

beforeAll(async () => {
  schema = (await loadKindSchema('trigger'))!
})

beforeEach(() => localStorage.setItem('axis.themeMode', 'light'))

afterEach(() => {
  localStorage.clear()
  vi.unstubAllGlobals()
})

describe('triggerSchema', () => {
  it('leaves the schema as it is for an event trigger, a data change without delete and a scheduled job with a data source', () => {
    for (const draft of [
      { type: 'event', event: 'Paid' },
      { type: 'dataChange', entity: 'PurchaseRequest', on: ['create', 'update'] },
      { type: 'schedule', dataSource: 'Overdue' },
      {},
    ]) {
      expect(triggerSchema(schema, draft)).toBe(schema)
    }
  })

  it('hides fields on a delete trigger unless the file has the list, and keeps the other properties in order', () => {
    const without = Object.keys(triggerSchema(schema, deletion).properties ?? {})
    const kept = Object.keys(triggerSchema(schema, { ...deletion, fields: ['title'] }).properties ?? {})

    expect(without).not.toContain('fields')
    expect(kept).toEqual(Object.keys(schema.properties ?? {}))
    expect(without).toEqual(kept.filter((key) => key !== 'fields'))
  })
})

describe('A scheduled job without a data source', () => {
  it('offers the parameters of its process as row keys and writes a number for an integer', async () => {
    const latest = renderTrigger({ ...schedule, parameters: {} })
    const field = screen.getByTestId('studio-field-/parameters')

    await userEvent.click(within(field).getByRole('button', { name: 'Add' }))
    expect(latest.value.parameters).toEqual({ daysOverdue: 0 })

    await userEvent.click(within(field).getByRole('combobox', { name: 'parameters 1' }))
    const dropdown = await openDropdown('label')
    expect(optionTexts(dropdown)).toEqual(['daysOverdue', 'label'])
    await userEvent.keyboard('{Escape}')

    const value = within(field).getByRole('spinbutton', { name: 'daysOverdue' })
    await userEvent.clear(value)
    await userEvent.type(value, '1')
    expect(latest.value.parameters).toEqual({ daysOverdue: 1 })
    expect(JSON.stringify(latest.value.parameters)).toBe('{"daysOverdue":1}')

    await userEvent.click(within(field).getByRole('button', { name: 'Add' }))
    expect(latest.value.parameters).toEqual({ daysOverdue: 1, label: '' })
    expect(within(field).getByRole('textbox', { name: 'label' })).toBeVisible()
  })

  it('offers no parameter when its process has none', async () => {
    renderTrigger({ ...schedule, process: 'PurchaseRequestApproval', parameters: {} })

    expect(within(screen.getByTestId('studio-field-/parameters')).getByRole('button', { name: 'Add' })).toBeDisabled()
  })

  it('keeps the data source parameters when it has a data source', async () => {
    renderTrigger({ ...schedule, dataSource: 'Overdue', parameters: {} })

    await userEvent.click(within(screen.getByTestId('studio-field-/parameters')).getByRole('button', { name: 'Add' }))
    expect(screen.getByRole('spinbutton', { name: 'days' })).toBeVisible()
  })
})

describe('A delete trigger', () => {
  it('shows an expression editor over the deleted record for each parameter and no fields', async () => {
    const latest = renderTrigger({ ...deletion, parameters: {} })
    const field = screen.getByTestId('studio-field-/parameters')
    expect(screen.queryByTestId('studio-field-/fields')).toBeNull()

    await userEvent.click(within(field).getByRole('button', { name: 'Add' }))
    expect(latest.value.parameters).toEqual({ daysOverdue: '' })

    const editor = await within(field).findByRole('group', { name: 'daysOverdue' })
    const options = (editor.getAttribute('data-options') ?? '').split(',')
    expect(options.slice(0, 2)).toEqual(['id', 'title'])
  })

  it('keeps fields visible when the file already has the list', () => {
    renderTrigger({ ...deletion, fields: ['title'], parameters: {} })

    expect(screen.getByTestId('studio-field-/fields')).toBeVisible()
  })

  it('offers the entity fields and id in the when expression of a data change trigger', async () => {
    renderTrigger({ ...deletion, on: ['create'], when: 'x' })

    const editor = await within(screen.getByTestId('studio-field-/when')).findByRole('group', { name: 'when' })
    expect((editor.getAttribute('data-options') ?? '').split(',').slice(0, 2)).toEqual(['id', 'title'])
  })
})
