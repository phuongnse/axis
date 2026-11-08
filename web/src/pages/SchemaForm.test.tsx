import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { designTokensFor } from '../platform/designTokens'
import { policyKeyedLists } from '../platform/layerMarks'
import type { StudioCatalog, StudioFolderRelaxedPoint, StudioNames } from '../platform/studio'
import { StudioCatalogContext, StudioRelaxedContext } from '../platform/studioCatalog'
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
  extra: Partial<ComponentProps<typeof SchemaForm>> & {
    names?: StudioNames
    catalog?: StudioCatalog
    relaxed?: StudioFolderRelaxedPoint[]
  } = {},
) {
  const { names: shownNames = names, catalog, relaxed, ...formProps } = extra
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={shownNames}>
            <StudioCatalogContext.Provider value={catalog}>
              <StudioRelaxedContext.Provider value={relaxed && { application: 'Shop', points: relaxed }}>
                <LabelTextsContext.Provider value={labelContext}>
                  <SchemaForm schema={schema} value={value} onChange={onChange} {...formProps} />
                </LabelTextsContext.Provider>
              </StudioRelaxedContext.Provider>
            </StudioCatalogContext.Provider>
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
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

  it('draws a module as a row that opens Edit module, a large dialog with no relaxed points to edit', async () => {
    const onChange = vi.fn()
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'Sales', version: '^1.0.0', relax: [{ point: 'a.b', reason: 'Small team' }] }] },
      onChange,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Sales' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit module' })

    expect(dialog.closest('.ant-modal')).toHaveStyle({ width: `${designTokensFor('light').size.dialog.large}px` })
    expect(within(dialog).queryByTestId('studio-field-/modules/0/relax')).toBeNull()
    expect(within(dialog).queryByTestId('studio-field-/modules/0/relax/0/point')).toBeNull()
    expect(within(dialog).queryByRole('button', { name: 'Add row' })).toBeNull()
    expect(onChange).not.toHaveBeenCalled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))
    expect(onChange).toHaveBeenCalledWith('/modules/0', {
      id: 'Sales',
      version: '^1.0.0',
      relax: [{ point: 'a.b', reason: 'Small team' }],
    })
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

  it('names the item of a list with no item text as item in Add… and the dialog titles', async () => {
    renderForm(listSchema, { kind: 'example', rows: [{ name: 'budget' }] })

    await userEvent.click(screen.getByRole('button', { name: 'Add item…' }))
    expect(await screen.findByRole('dialog', { name: 'Add item' })).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    await userEvent.click(within(rowOf(0)).getByRole('button', { name: 'budget' }))
    expect(await screen.findByRole('dialog', { name: 'Edit item' })).toBeVisible()
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

describe('SchemaForm controls that know what they hold', () => {
  beforeEach(() => localStorage.setItem('axis.themeMode', 'light'))

  const catalog: StudioCatalog = {
    modules: [
      {
        id: 'sales',
        label: 'Sales',
        versions: ['1.2.0', '1.1.0'],
        roles: [{ name: 'approver', titles: { en: 'Budget approver', vi: 'Người duyệt ngân sách' } }],
        connectors: [{ name: 'mail', titles: { en: 'Mail server' } }],
      },
      { id: 'hr', label: 'People', versions: ['2.0.0'], roles: [], connectors: [] },
    ],
    packages: [{ id: 'finance', label: 'Finance', versions: ['3.1.0'] }],
    // The server sends the zones by offset, then id.
    timeZones: [
      { id: 'America/Sao_Paulo', offset: -180 },
      { id: 'Asia/Kolkata', offset: 330 },
      { id: 'Asia/Bangkok', offset: 420 },
      { id: 'Asia/Ho_Chi_Minh', offset: 420 },
    ],
  }
  const catalogNames = {
    ...names,
    roles: [{ name: 'DepartmentManager' }, { name: 'Clerk' }],
    connectors: [{ name: 'Smtp', operations: [] }],
  } as unknown as StudioNames

  // In tests every select has the id `test-id`, as the dialog's title does, so the dialog is found by its title.
  async function findDialog(title: string): Promise<HTMLElement> {
    return (await screen.findByText(title)).closest<HTMLElement>('[role="dialog"]')!
  }

  const relaxedPoint: StudioFolderRelaxedPoint = {
    module: 'sales',
    point: 'Budget/transitions/approve',
    reason: 'Small team',
    moduleTitle: 'Sales',
    part: 'transition',
    entityTitles: { en: 'Budget' },
    titles: { en: 'Approve a budget' },
    contractId: '7c1d2e3f-0000-4000-8000-000000000001',
  }

  async function openAddModule(value: Record<string, unknown>, onChange: ChangeHandler = vi.fn()) {
    renderForm((await loadKindSchema('application'))!, value, onChange, { catalog, names: catalogNames })
    await userEvent.click(
      within(screen.getByTestId('studio-section-modules')).getByRole('button', { name: 'Add module…' }),
    )
    return findDialog('Add module')
  }

  it('fills an empty version range with ^ and the newest version when a module is chosen', async () => {
    const onChange = vi.fn()
    const dialog = await openAddModule({ name: 'Shop' }, onChange)

    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Module' }))
    await userEvent.click(screen.getByRole('option', { name: 'Sales' }))

    expect(within(dialog).getByRole('combobox', { name: 'Version' })).toBeVisible()
    expect(within(dialog).getByText('1.2.0 (newest)')).toBeVisible()
    expect(within(dialog).getByText('Compatible updates (1.x)')).toBeVisible()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))
    expect(onChange).toHaveBeenCalledWith('/modules', [{ id: 'sales', version: '^1.2.0' }])
  })

  it('keeps the version range that a module already has when another module is chosen', async () => {
    const onChange = vi.fn()
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '~1.1.0' }] },
      onChange,
      { catalog, names: catalogNames },
    )
    await userEvent.click(screen.getByRole('button', { name: 'Sales' }))
    const dialog = await findDialog('Edit module')

    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Module' }))
    await userEvent.click(screen.getByRole('option', { name: 'People' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenCalledWith('/modules/0', { id: 'hr', version: '~1.1.0' })
  })

  it('writes the range as the operator and the version that the two selects choose', async () => {
    const onChange = vi.fn()
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.2.0' }] },
      onChange,
      { catalog, names: catalogNames },
    )
    await userEvent.click(screen.getByRole('button', { name: 'Sales' }))
    const dialog = await findDialog('Edit module')

    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Updates' }))
    await userEvent.click(screen.getByRole('option', { name: 'Fixes only (1.2.x)' }))
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Version' }))
    await userEvent.click(screen.getByRole('option', { name: '1.1.0' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenCalledWith('/modules/0', { id: 'sales', version: '~1.1.0' })
  })

  it('shows one select for each slot of the chosen module, labelled by the slot title', async () => {
    const onChange = vi.fn()
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.2.0', roles: { approver: 'Clerk' } }] },
      onChange,
      { catalog, names: catalogNames },
    )
    await userEvent.click(screen.getByRole('button', { name: 'Sales' }))
    const dialog = await findDialog('Edit module')

    const approver = within(dialog).getByRole('combobox', { name: 'Budget approver' })
    expect(within(dialog).getByTestId('studio-slot-approver')).toHaveTextContent('Budget approver')
    expect(within(dialog).getByText('Clerk')).toBeVisible()
    await userEvent.click(approver)
    expect(screen.getByRole('option', { name: 'DepartmentManager' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('option', { name: 'DepartmentManager' }))
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Mail server' }))
    await userEvent.click(screen.getByRole('option', { name: 'Smtp' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenCalledWith('/modules/0', {
      id: 'sales',
      version: '^1.2.0',
      roles: { approver: 'DepartmentManager' },
      connectors: { mail: 'Smtp' },
    })
  })

  it('says that a chosen module has no slots, once', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'hr', version: '^2.0.0' }] },
      vi.fn(),
      { catalog, names: catalogNames },
    )
    await userEvent.click(screen.getByRole('button', { name: 'People' }))
    const dialog = await findDialog('Edit module')

    expect(within(dialog).getAllByText(english['shell.studio.module.noSlots'])).toHaveLength(1)
    expect(within(dialog).queryByTestId(/^studio-slot-/)).toBeNull()
  })

  it('keeps a version that is no longer installed selected, with a warning line', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.0.0' }] },
      vi.fn(),
      { catalog, names: catalogNames },
    )
    await userEvent.click(screen.getByRole('button', { name: 'Sales' }))
    const dialog = await findDialog('Edit module')

    const version = within(dialog).getByTestId('studio-field-/modules/0/version')
    expect(within(version).getByText('1.0.0')).toBeVisible()
    const warning = within(version).getByTestId('studio-version-not-installed')
    expect(warning).toHaveTextContent(english['shell.studio.versionRange.notInstalled'])
    expect(warning).toHaveStyle({ color: designTokensFor('light').colour.warningText })
  })

  it('keeps the text input for a range in no valid form and for a module that is not installed', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      {
        name: 'Shop',
        modules: [
          { id: 'sales', version: 'latest' },
          { id: 'gone', version: '^1.0.0' },
        ],
      },
      vi.fn(),
      { catalog, names: catalogNames },
    )
    await userEvent.click(screen.getByRole('button', { name: 'Sales' }))
    let dialog = await findDialog('Edit module')
    expect(within(within(dialog).getByTestId('studio-field-/modules/0/version')).getByRole('textbox')).toHaveValue(
      'latest',
    )
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await userEvent.click(screen.getByRole('button', { name: 'gone' }))
    dialog = await findDialog('Edit module')
    expect(within(within(dialog).getByTestId('studio-field-/modules/1/version')).getByRole('textbox')).toHaveValue(
      '^1.0.0',
    )
  })

  it('shows the module and the package in the table by the label of their manifest', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      {
        name: 'Shop',
        modules: [{ id: 'sales', version: '^1.2.0' }],
        packages: [{ id: 'finance', version: '^3.1.0' }],
      },
      vi.fn(),
      { catalog, names: catalogNames },
    )

    const modules = screen.getByTestId('studio-field-/modules')
    expect(within(modules).getByRole('button', { name: 'Sales' })).toBeVisible()
    expect(within(modules).queryByText('sales')).toBeNull()
    expect(within(screen.getByTestId('studio-field-/packages')).getByRole('button', { name: 'Finance' })).toBeVisible()
  })

  it('shows each role slot of the installed module as a line, and "Not mapped" in warning colour for one no role plays', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.2.0' }] },
      vi.fn(),
      { catalog, names: catalogNames },
    )

    const modules = screen.getByTestId('studio-field-/modules')
    const line = within(modules).getByText(/^Budget approver:/)
    expect(line).toHaveTextContent('Budget approver: Not mapped')
    expect(within(line).getByText('Not mapped')).toHaveStyle({ color: designTokensFor('light').colour.warningText })
  })

  it('shows the role that plays a slot by its name, without the warning', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.2.0', roles: { approver: 'department-manager' } }] },
      vi.fn(),
      { catalog, names: catalogNames },
    )

    const line = within(screen.getByTestId('studio-field-/modules')).getByText(/^Budget approver:/)
    expect(line).toHaveTextContent('Budget approver: department-manager')
    expect(within(line).queryByText('Not mapped')).toBeNull()
  })

  it('shows a relaxed point as a link to its contract page, then its reason, with no input in the cell', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.2.0', relax: [{ point: relaxedPoint.point }] }] },
      vi.fn(),
      {
        catalog,
        names: catalogNames,
        relaxed: [relaxedPoint, { ...relaxedPoint, module: 'hr', point: 'Other/point' }],
      },
    )

    const cells = screen.getAllByTestId('studio-relaxed-point')
    expect(cells).toHaveLength(1)
    const link = within(cells[0]).getByRole('link', { name: 'Approve a budget' })
    expect(link).toHaveAttribute('href', `/studio?app=Shop&resource=${relaxedPoint.contractId}`)
    expect(cells[0]).toHaveTextContent('Approve a budgetSmall team')
    expect(within(cells[0]).queryByRole('textbox')).toBeNull()
    expect(within(cells[0]).queryByRole('combobox')).toBeNull()
  })

  it('shows a relaxed point whose contract is not found as plain text', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.2.0' }] },
      vi.fn(),
      { catalog, names: catalogNames, relaxed: [{ ...relaxedPoint, contractId: null }] },
    )

    const cell = screen.getByTestId('studio-relaxed-point')
    expect(within(cell).queryByRole('link')).toBeNull()
    expect(cell).toHaveTextContent('Approve a budgetSmall team')
  })

  it('leaves the Relaxed points column out when no module has a relaxed point', async () => {
    renderForm(
      (await loadKindSchema('application'))!,
      { name: 'Shop', modules: [{ id: 'sales', version: '^1.2.0' }] },
      vi.fn(),
      { catalog, names: catalogNames, relaxed: [] },
    )

    expect(
      within(screen.getByTestId('studio-field-/modules')).queryByRole('columnheader', { name: 'Relaxed points' }),
    ).toBeNull()
  })

  it('keeps the text inputs until the catalog loads', async () => {
    renderForm((await loadKindSchema('application'))!, { name: 'Shop', timeZone: 'Asia/Ho_Chi_Minh' })

    expect(within(screen.getByTestId('studio-field-/timeZone')).getByRole('textbox')).toHaveValue('Asia/Ho_Chi_Minh')
  })

  it('lists the time zones as the UTC offset and the id, by offset then id', async () => {
    renderForm((await loadKindSchema('application'))!, { name: 'Shop' }, vi.fn(), { catalog, names: catalogNames })

    await userEvent.click(screen.getByRole('combobox', { name: 'Time zone' }))

    expect(
      Array.from(document.querySelectorAll('.ant-select-item-option-content'), (node) => node.textContent),
    ).toEqual([
      '(UTC-03:00) America/Sao Paulo',
      '(UTC+05:30) Asia/Kolkata',
      '(UTC+07:00) Asia/Bangkok',
      '(UTC+07:00) Asia/Ho Chi Minh',
    ])
  })

  it('chooses a time zone by its id, and clears it to UTC', async () => {
    const onChange = vi.fn()
    renderForm((await loadKindSchema('application'))!, { name: 'Shop', timeZone: 'Asia/Kolkata' }, onChange, {
      catalog,
      names: catalogNames,
    })

    expect(screen.getByText('(UTC+05:30) Asia/Kolkata')).toBeVisible()
    await userEvent.click(screen.getByRole('combobox', { name: 'Time zone' }))
    await userEvent.click(screen.getByRole('option', { name: '(UTC+07:00) Asia/Ho Chi Minh' }))
    expect(onChange).toHaveBeenLastCalledWith('/timeZone', 'Asia/Ho_Chi_Minh')

    const field = screen.getByTestId('studio-field-/timeZone')
    await userEvent.hover(field)
    await userEvent.click(field.querySelector('.ant-select-clear')!)
    expect(onChange).toHaveBeenLastCalledWith('/timeZone', undefined)
  })

  it('gives a schedule trigger the same time zone select', async () => {
    renderForm(
      (await loadKindSchema('trigger'))!,
      { kind: 'trigger', name: 'Daily', formatVersion: 1, type: 'schedule', schedule: '0 6 * * *' },
      vi.fn(),
      { catalog, names: catalogNames },
    )

    await userEvent.click(screen.getByRole('combobox', { name: 'Time zone' }))

    expect(screen.getByRole('option', { name: '(UTC+07:00) Asia/Bangkok' })).toBeInTheDocument()
  })

  it('chooses a package by label and fills its empty version', async () => {
    const onChange = vi.fn()
    renderForm((await loadKindSchema('application'))!, { name: 'Shop' }, onChange, { catalog, names: catalogNames })
    await userEvent.click(
      within(screen.getByTestId('studio-section-packages')).getByRole('button', { name: 'Add package…' }),
    )
    const dialog = await findDialog('Add package')

    await userEvent.click(within(dialog).getByRole('combobox', { name: 'Package' }))
    await userEvent.click(screen.getByRole('option', { name: 'Finance' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onChange).toHaveBeenCalledWith('/packages', [{ id: 'finance', version: '^3.1.0' }])
  })

  it('shows a version range in words in the Modules and Packages tables', async () => {
    renderForm((await loadKindSchema('application'))!, {
      name: 'Shop',
      modules: [
        { id: 'sales', version: '^1.2.0' },
        { id: 'hr', version: '1.2.0' },
        { id: 'ops', version: '~1.2.3' },
      ],
      packages: [{ id: 'finance', version: '^3.1.0' }],
    })

    const modules = within(screen.getByTestId('studio-field-/modules'))
    expect(modules.getByText('1.2.0 and later 1.x')).toBeVisible()
    expect(modules.getByText('1.2.0 only')).toBeVisible()
    expect(modules.getByText('1.2.3 and later 1.2.x')).toBeVisible()
    expect(within(screen.getByTestId('studio-field-/packages')).getByText('3.1.0 and later 3.x')).toBeVisible()
  })
})
