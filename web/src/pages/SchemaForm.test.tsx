import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { designTokensFor } from '../platform/designTokens'
import { policyKeyedLists } from '../platform/layerMarks'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema, resolveSchema, type JsonSchema } from '../platform/studioSchemas'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { LabelTextsContext } from './labelTextsContext'
import { SchemaForm, type ChangeHandler } from './SchemaForm'

const english: Record<string, string> = platformEnglish
const vietnamese: Record<string, string> = platformVietnamese

const names = {
  locales: ['en', 'vi'],
  entities: [],
  dataSources: [],
  processes: [],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [],
  forms: [],
  pages: [],
  texts: [],
} as unknown as StudioNames

const labelContext = {
  application: 'Shop',
  file: 'roles/approver.json',
  name: 'Approver',
  saved: {},
  texts: {},
  onTextsChange: vi.fn(),
}

/**
 * The kinds that SchemaForm draws, with the root properties that a dedicated editor draws instead: StudioEditor's
 * processSchema, siteSchema, endpointSchema and formSchema memos, and ProcessEditor's withoutSubject.
 */
const formKinds: Record<string, string[]> = {
  application: [],
  role: [],
  policy: [],
  rule: [],
  sequence: [],
  event: [],
  trigger: [],
  dataSource: [],
  webhook: [],
  migration: [],
  seed: [],
  site: ['signIn'],
  endpoint: ['values', 'event'],
  form: ['steps'],
  process: ['entity', 'parameters', 'start', 'steps'],
}
const hiddenRootProperties = ['id', 'kind', 'formatVersion']

function renderForm(
  schema: JsonSchema,
  value: Record<string, unknown>,
  onChange: ChangeHandler = vi.fn(),
  extra: Partial<ComponentProps<typeof SchemaForm>> & { names?: StudioNames } = {},
) {
  const { names: shownNames = names, ...formProps } = extra
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={shownNames}>
          <LabelTextsContext.Provider value={labelContext}>
            <SchemaForm schema={schema} value={value} onChange={onChange} {...formProps} />
          </LabelTextsContext.Provider>
        </StudioNamesContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

/** The missing `.help` of each field text in a catalogue. */
function missingHelp(catalogue: Record<string, string>): string[] {
  return Object.keys(catalogue).filter(
    (key) => key.startsWith('shell.studio.field.') && !key.endsWith('.help') && catalogue[`${key}.help`] === undefined,
  )
}

/** The paths of a schema's fields, through nested objects and list items, stopping at labels and maps. */
function fieldPaths(node: JsonSchema, root: JsonSchema, prefix: string[]): string[] {
  return Object.entries(resolveSchema(node, root).properties ?? {}).flatMap(([name, child]) => {
    if (prefix.length === 0 && hiddenRootProperties.includes(name)) {
      return []
    }
    const path = [...prefix, name]
    const schema = resolveSchema(child, root)
    if (Object.keys(schema.properties ?? {}).join() === 'textKey') {
      return [path.join('.')]
    }
    const item = schema.type === 'array' && schema.items !== undefined ? resolveSchema(schema.items, root) : schema
    const nested = item.type === 'object' && item.properties !== undefined ? fieldPaths(item, root, path) : []
    return [path.join('.'), ...nested]
  })
}

/** The paths of the lists of objects that SchemaForm draws as tables: not inside a list item, and not lists of labels. */
function objectListPaths(node: JsonSchema, root: JsonSchema, prefix: string[]): string[] {
  return Object.entries(resolveSchema(node, root).properties ?? {}).flatMap(([name, child]) => {
    if (prefix.length === 0 && hiddenRootProperties.includes(name)) {
      return []
    }
    const path = [...prefix, name]
    const schema = resolveSchema(child, root)
    const item = schema.type === 'array' && schema.items !== undefined ? resolveSchema(schema.items, root) : undefined
    if (item !== undefined) {
      const isLabel = Object.keys(item.properties ?? {}).join() === 'textKey'
      return item.type === 'object' && item.properties !== undefined && !isLabel ? [path.join('.')] : []
    }
    return schema.type === 'object' && schema.properties !== undefined ? objectListPaths(schema, root, path) : []
  })
}

afterEach(() => localStorage.clear())

describe('SchemaForm field texts', () => {
  beforeEach(() => localStorage.setItem('axis.themeMode', 'light'))

  it('names a role in words, with its help, and shows no raw property name', async () => {
    renderForm((await loadKindSchema('role'))!, { name: 'Approver', label: { textKey: 'roles.approver' } })

    for (const field of ['name', 'label']) {
      const key = `shell.studio.field.role.${field}`
      const row = screen.getByTestId(`studio-field-/${field}`)
      expect(within(row).getByText(english[key])).toBeVisible()
      expect(within(row).getByText(english[`${key}.help`])).toBeVisible()
    }
    for (const raw of ['name', 'label', 'textKey']) {
      expect(screen.queryByText(raw)).toBeNull()
    }
  })

  it('hides the file identity and shows name with its label and help', async () => {
    renderForm((await loadKindSchema('role'))!, {
      id: 'role-approver',
      kind: 'role',
      name: 'Approver',
      formatVersion: 1,
      label: { textKey: '' },
    })

    for (const hidden of hiddenRootProperties) {
      expect(screen.queryByTestId(`studio-field-/${hidden}`)).toBeNull()
    }
    const name = screen.getByTestId('studio-field-/name')
    expect(within(name).getByText(english['shell.studio.field.role.name'])).toBeVisible()
    expect(within(name).getByText(english['shell.studio.field.role.name.help'])).toBeVisible()
  })

  it('keeps a property name as the label of a kind with no field texts', () => {
    renderForm(
      { type: 'object', properties: { kind: { const: 'example' }, note: { type: 'string' } } },
      { kind: 'example', note: 'Draft' },
    )

    expect(within(screen.getByTestId('studio-field-/note')).getByText('note')).toBeVisible()
  })

  it('names a data source in words, with its help, and shows no raw property name', async () => {
    renderForm((await loadKindSchema('dataSource'))!, {
      kind: 'dataSource',
      name: 'Orders',
      formatVersion: 1,
      entity: 'Order',
      fields: [{ name: 'Title', path: 'title' }],
      parameters: [{ name: 'Status', type: 'text' }],
      filter: 'x',
      aggregate: { groupBy: [], measures: [{ name: 'Total', function: 'count' }] },
      sort: 'Title',
      pageSize: 10,
      search: true,
    })

    for (const field of [
      'name',
      'entity',
      'fields',
      'parameters',
      'filter',
      'aggregate',
      'sort',
      'pageSize',
      'search',
    ]) {
      const key = `shell.studio.field.dataSource.${field}`
      const row = screen.getByTestId(`studio-field-/${field}`)
      expect(within(row).getByText(english[key])).toBeVisible()
      expect(within(row).getByText(english[`${key}.help`])).toBeVisible()
    }
    for (const raw of [
      'entity',
      'fields',
      'parameters',
      'filter',
      'aggregate',
      'groupBy',
      'measures',
      'sort',
      'pageSize',
      'search',
      'path',
      'function',
    ]) {
      expect(screen.queryByText(raw)).toBeNull()
    }
  })

  it('sits the fields of an object behind a subtle rule on its start side', async () => {
    renderForm((await loadKindSchema('dataSource'))!, {
      name: 'Orders',
      aggregate: { groupBy: [], measures: [] },
    })

    const { colour } = designTokensFor('light')
    expect(screen.getByTestId('studio-group-/aggregate')).toHaveStyle({
      borderInlineStart: `1px solid ${colour.borderSubtle}`,
    })
  })
})

describe('SchemaForm sections', () => {
  beforeEach(() => localStorage.setItem('axis.themeMode', 'light'))

  const applicationValue = { name: 'Shop', modules: [{ id: 'Sales', version: '^1.0.0' }] }

  function expectHeading(section: HTMLElement, key: string) {
    expect(within(section).getByRole('heading', { name: english[key] })).toBeVisible()
    expect(within(section).getByText(english[`${key}.line`])).toBeVisible()
  }

  it('shows a role in a General section with its heading, line, name and label', async () => {
    renderForm((await loadKindSchema('role'))!, { name: 'Approver', label: { textKey: 'roles.approver' } })

    const section = screen.getByTestId('studio-section-general')
    expectHeading(section, 'shell.studio.section.role.general')
    expect(within(section).getByTestId('studio-field-/name')).toBeVisible()
    expect(within(section).getByTestId('studio-field-/label')).toBeVisible()
    expect(screen.queryByTestId('studio-section-rest')).toBeNull()
  })

  it('shows the application in General, Modules, Packages and File storage, in that order', async () => {
    renderForm((await loadKindSchema('application'))!, applicationValue)

    const sections = screen.getAllByTestId(/^studio-section-/)
    expect(sections.map((section) => section.dataset.testid)).toEqual([
      'studio-section-general',
      'studio-section-modules',
      'studio-section-packages',
      'studio-section-fileStorage',
    ])
    const keys = ['general', 'modules', 'packages', 'fileStorage']
    sections.forEach((section, index) => expectHeading(section, `shell.studio.section.application.${keys[index]}`))
  })

  it('puts a property that no section names in a last section with no heading', async () => {
    const schema = (await loadKindSchema('role'))!
    renderForm(
      { ...schema, properties: { ...schema.properties, extra: { type: 'string' } } },
      { name: 'Approver', extra: 'x' },
    )

    const rest = screen.getByTestId('studio-section-rest')
    expect(within(rest).getByTestId('studio-field-/extra')).toBeVisible()
    expect(within(rest).queryByRole('heading')).toBeNull()
    const sections = screen.getAllByTestId(/^studio-section-/)
    expect(sections[sections.length - 1]).toBe(rest)
  })

  it('shows one panel with no heading for a kind that has no sections', async () => {
    renderForm((await loadKindSchema('trigger'))!, { kind: 'trigger', name: 'Daily', formatVersion: 1, parameters: {} })

    expect(screen.getAllByTestId(/^studio-section-/).map((section) => section.dataset.testid)).toEqual([
      'studio-section-rest',
    ])
    expect(screen.queryByRole('heading')).toBeNull()
  })

  it('puts the Add module… of the Modules list in the section heading, with no toolbar in the table', async () => {
    renderForm((await loadKindSchema('application'))!, applicationValue)

    const section = screen.getByTestId('studio-section-modules')
    expect(within(section).getAllByRole('button', { name: 'Add module…' })).toHaveLength(1)
    expect(
      within(screen.getByTestId('studio-field-/modules')).queryByRole('button', { name: 'Add module…' }),
    ).toBeNull()
  })

  it('adds a module only on Done of the Add module dialog', async () => {
    const onChange = vi.fn()
    renderForm((await loadKindSchema('application'))!, applicationValue, onChange)

    await userEvent.click(
      within(screen.getByTestId('studio-section-modules')).getByRole('button', { name: 'Add module…' }),
    )
    const dialog = await screen.findByRole('dialog', { name: 'Add module' })
    await userEvent.type(within(within(dialog).getByTestId('studio-field-/modules/1/id')).getByRole('textbox'), 'hr')
    await userEvent.type(
      within(within(dialog).getByTestId('studio-field-/modules/1/version')).getByRole('textbox'),
      '^1.0.0',
    )
    expect(onChange).not.toHaveBeenCalled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('/modules', [
      { id: 'Sales', version: '^1.0.0' },
      { id: 'hr', version: '^1.0.0' },
    ])
  })

  it('draws a module as a row that opens Edit module, a large dialog with its relaxed points as rows of inputs', async () => {
    const onChange = vi.fn()
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'Sales', version: '^1.0.0', relax: [{ point: 'a.b' }] }] },
      onChange,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Sales' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit module' })

    expect(dialog.closest('.ant-modal')).toHaveStyle({ width: `${designTokensFor('light').size.dialog.large}px` })
    const point = within(dialog).getByTestId('studio-field-/modules/0/relax/0/point')
    expect(within(point).getByRole('textbox')).toHaveValue('a.b')
    expect(within(dialog).queryByRole('table')).toBeNull()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Add row' }))
    expect(within(dialog).getByTestId('studio-field-/modules/0/relax/1/point')).toBeVisible()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('shows the fields of the File storage object without a label or group of its own', async () => {
    renderForm((await loadKindSchema('application'))!, { name: 'Shop', fileStorage: {} })

    const section = screen.getByTestId('studio-section-fileStorage')
    expect(within(section).getByTestId('studio-field-/fileStorage/binding')).toBeVisible()
    expect(screen.queryByTestId('studio-group-/fileStorage')).toBeNull()
    expect(within(section).queryByText(english['shell.studio.field.application.fileStorage.help'])).toBeNull()
  })

  it('draws a section as a surface panel with a subtle border, spacing-md apart', async () => {
    renderForm((await loadKindSchema('application'))!, applicationValue)

    const { colour, spacing } = designTokensFor('light')
    const section = screen.getByTestId('studio-section-modules')
    expect(section).toHaveStyle({ background: colour.surface, border: `1px solid ${colour.borderSubtle}` })
    expect(section.parentElement).toHaveStyle({ gap: `${spacing.md}px` })
  })
})

describe('SchemaForm lists of objects', () => {
  beforeEach(() => localStorage.setItem('axis.themeMode', 'light'))

  const listSchema: JsonSchema = {
    type: 'object',
    properties: {
      kind: { const: 'example' },
      rows: {
        type: 'array',
        items: {
          type: 'object',
          properties: {
            name: { type: 'string' },
            active: { type: 'boolean' },
            label: { $ref: '#/$defs/textReference' },
            tags: { type: 'array', items: { type: 'string' } },
            limits: { type: 'object', additionalProperties: { type: 'integer' } },
          },
        },
      },
      plain: { type: 'array', items: { type: 'string' } },
    },
    $defs: {
      textReference: { type: 'object', required: ['textKey'], properties: { textKey: { type: 'string' } } },
    },
  }
  const namesWithTexts = {
    ...names,
    texts: [{ key: 'k.budget', texts: { en: 'Budget', vi: 'Ngân sách' }, usedBy: 1 }],
  } as unknown as StudioNames
  const rowOf = (index: number) => screen.getAllByRole('row')[index + 1]

  it('shows a value, Yes or No, a label text, the items of a list and the entries of a map in the cells', () => {
    renderForm(
      listSchema,
      {
        kind: 'example',
        rows: [
          {
            name: 'budget',
            active: true,
            label: { textKey: 'k.budget' },
            tags: ['a', 'b'],
            limits: { max: 5, min: 1 },
          },
          { name: 'leave', active: false },
        ],
        plain: ['x', 'y'],
      },
      vi.fn(),
      { names: namesWithTexts },
    )

    const first = within(rowOf(0))
    expect(first.getByRole('button', { name: 'budget' })).toBeVisible()
    expect(first.getByText('Yes')).toBeVisible()
    expect(first.getByText('Budget')).toBeVisible()
    expect(first.getByText('a')).toBeVisible()
    expect(first.getByText('b')).toBeVisible()
    expect(first.getByText('max: 5')).toBeVisible()
    expect(first.getByText('min: 1')).toBeVisible()
    expect(within(rowOf(1)).getByText('No')).toBeVisible()
  })

  it('keeps a list of plain values as inline rows of inputs', () => {
    renderForm(listSchema, { kind: 'example', rows: [], plain: ['x', 'y'] })

    const plain = screen.getByTestId('studio-field-/plain')
    expect(within(plain).getAllByRole('textbox')).toHaveLength(2)
    expect(within(plain).queryByRole('table')).toBeNull()
    expect(within(plain).getByRole('button', { name: 'Add' })).toBeVisible()
  })

  it('shows the layer tag in the first cell and the removed rows below the table', async () => {
    renderForm(
      (await loadKindSchema('policy'))!,
      { name: 'Shop', roles: ['Approver'], grants: [{ name: 'Read orders', actions: ['read'] }] },
      vi.fn(),
      {
        keyedLists: policyKeyedLists,
        layer: {
          elements: [
            { list: '/grants', key: 'Read orders', mark: 'added' },
            { list: '/grants', key: 'Old grant', mark: 'removed' },
          ],
          disabled: false,
          onReset: vi.fn(),
        },
      },
    )

    const list = screen.getByTestId('studio-field-/grants')
    const firstCell = within(rowOf(0)).getAllByRole('cell')[0]
    expect(within(firstCell).getByRole('button', { name: 'Read orders' })).toBeVisible()
    expect(within(firstCell).getByTestId('studio-layer-mark-/grants/0')).toBeVisible()
    const removed = within(list).getByTestId('studio-layer-removed-/grants/Old grant')
    expect(removed).toBeVisible()
    expect(screen.getByRole('table').compareDocumentPosition(removed) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})

describe('field text catalogues', () => {
  it('reports a field text that has no help', () => {
    expect(
      missingHelp({
        'shell.studio.field.role.name': 'Name',
        'shell.studio.field.role.label': 'Label',
        'shell.studio.field.role.label.help': 'The text people see for this role.',
      }),
    ).toEqual(['shell.studio.field.role.name'])
  })

  it('finds a help for every field text in English and Vietnamese', () => {
    expect(missingHelp(english)).toEqual([])
    expect(missingHelp(vietnamese)).toEqual([])
  })

  it.each(Object.entries(formKinds))(
    'gives every field of %s a text in English and Vietnamese',
    async (kind, dedicated) => {
      const schema = (await loadKindSchema(kind))!
      const paths = fieldPaths(schema, schema, []).filter((path) => !dedicated.includes(path.split('.')[0]))

      expect(paths.length).toBeGreaterThan(0)
      const missing = paths.flatMap((path) =>
        [english, vietnamese]
          .filter((catalogue) => catalogue[`shell.studio.field.${kind}.${path}`] === undefined)
          .map(() => `${kind}.${path}`),
      )
      expect(missing).toEqual([])
    },
  )
})

describe('item name catalogues', () => {
  it.each(Object.entries(formKinds))(
    'gives every list of objects of %s an item name in English and Vietnamese',
    async (kind, dedicated) => {
      const schema = (await loadKindSchema(kind))!
      const paths = objectListPaths(schema, schema, []).filter((path) => !dedicated.includes(path.split('.')[0]))

      const missing = paths.flatMap((path) =>
        [english, vietnamese]
          .filter((catalogue) => catalogue[`shell.studio.item.${kind}.${path}`] === undefined)
          .map(() => `${kind}.${path}`),
      )
      expect(missing).toEqual([])
    },
  )
})
