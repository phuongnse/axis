import { render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { designTokensFor } from '../platform/designTokens'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { loadKindSchema, resolveSchema, type JsonSchema } from '../platform/studioSchemas'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { LabelTextsContext } from './labelTextsContext'
import { SchemaForm } from './SchemaForm'

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

function renderForm(schema: JsonSchema, value: Record<string, unknown>) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={names}>
          <LabelTextsContext.Provider value={labelContext}>
            <SchemaForm schema={schema} value={value} onChange={vi.fn()} />
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

  it('sits a grant behind a subtle rule on its start side', async () => {
    renderForm((await loadKindSchema('policy'))!, {
      name: 'Shop',
      roles: ['Approver'],
      grants: [{ name: 'Read orders', actions: ['read'] }],
    })

    const { colour } = designTokensFor('light')
    expect(screen.getByTestId('studio-group-/grants/0')).toHaveStyle({
      borderInlineStart: `1px solid ${colour.borderSubtle}`,
    })
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
