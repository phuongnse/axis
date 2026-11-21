import { act, render, screen, waitFor, within } from '@testing-library/react'
import { EditorView } from '@codemirror/view'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { useCallback, useLayoutEffect, useMemo, useRef, useState, type ComponentProps, type ReactNode } from 'react'
import { createMemoryRouter, RouterProvider, type DataRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import {
  editableSettings,
  fieldSettingNames,
  fieldSettings,
  fieldTypes,
  type FieldSetting,
} from '../platform/entityFields'
import { ContentInsetContext } from '../platform/contentInsetContext'
import { setAt } from '../platform/jsonPatch'
import type { LayerElement } from '../platform/layerMarks'
import { studioResourceAddress } from '../platform/studioAddress'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { PlatformContext, type Platform } from './context'
import { chooseOption, clearPicker, clearSelect, openDropdown, optionTexts, removeTag } from '../test/antd'
import {
  addEntityField,
  entityFieldAction,
  entityFieldMenuItem,
  entityFieldNames,
  openEntityField,
} from '../test/entityFields'
import { clickInDialog, dialog, pick } from '../test/tableFilter'
import { EntityDesigner } from './EntityDesigner'
import { ExpressionCheckContext } from './expressionCheckContext'
import { LabelTextsContext } from './labelTextsContext'

// The pointer of each expression editor that draws, in order. The tests that count draws read it.
const draws = vi.hoisted(() => [] as string[])

vi.mock('./ProcessFields', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./ProcessFields')>()
  const { Profiler } = await import('react')
  // A plain function, not a memo, so a draw of its parent always draws it and a skipped parent never does.
  return {
    ...actual,
    Expression: (props: ComponentProps<typeof actual.Expression>) => (
      <Profiler id={props.pointer} onRender={(id) => draws.push(id)}>
        <actual.Expression {...props} />
      </Profiler>
    ),
  }
})

// A group draws a hidden reader of the content inset it is given, so a test reads the inset of a group on the page and of one in a drawer.
vi.mock('../platform/AxisFieldGroup', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../platform/AxisFieldGroup')>()
  const { useContext } = await import('react')
  const { ContentInsetContext } = await import('../platform/contentInsetContext')
  return {
    ...actual,
    AxisFieldGroup: (props: ComponentProps<typeof actual.AxisFieldGroup>) => {
      const inset = useContext(ContentInsetContext)
      return (
        <>
          <span hidden data-testid="content-inset">
            {inset}
          </span>
          <actual.AxisFieldGroup {...props} />
        </>
      )
    },
  }
})

const english: Record<string, string> = platformEnglish

const labelOf = (path: string) => english[`shell.studio.field.entity.${path}`]
const helpOf = (path: string) => english[`shell.studio.field.entity.${path}.help`]
/** The title of a section, or its line when the path ends in `.line`, from `shell.studio.section.entity.`. */
const sectionOf = (path: string) => english[`shell.studio.section.entity.${path}`]

/** The paths under `shell.studio.field.entity.` that the designer draws, each with a label and a help. */
const drawnPaths = [
  'name',
  'label',
  'description',
  'displayField',
  'fields.name',
  'fields.label',
  'fields.type',
  'fields.required',
  'fields.unique',
  'fields.helpText',
  'fields.maxLength',
  'fields.minLength',
  'fields.min',
  'fields.max',
  'fields.precision',
  'fields.scale',
  'fields.maxSize',
  'fields.accept',
  'fields.target',
  'fields.currencySource',
  'fields.currencySource.code',
  'fields.currencySource.field',
  'fields.currency',
  'fields.currencyField',
  'fields.values',
  'fields.valueLabels',
  'fields.expression',
  'fields.default',
  'fields.sequence',
  'fields.valueSource',
  'fields.valueSource.entered',
  'fields.valueSource.computed',
  'fields.valueSource.sequence',
  'fields.valueSource.creator',
  'fields.valueSource.matched',
  'fields.userId',
  'fields.match.field',
  'fields.match.from',
  'fields.searchable',
  'fields.sensitive',
  'fields.exactLookup',
  'indexes.fields',
  'indexes.unique',
  'validations.expression',
  'validations.message',
  'validations.field',
  'validations.messageText',
  'fields.group.general',
  'fields.group.value',
  'fields.group.validation',
  'fields.group.searchPrivacy',
  'fields.precisionScale',
  'fields.limits',
]

/** The types whose settings include one that the type's group holds, which is titled by the type. */
const typesWithOwnGroup = fieldTypes.filter((type) =>
  fieldSettings(type).some((setting) =>
    ['target', 'values', 'valueLabels', 'currency', 'precision', 'scale', 'maxLength'].includes(setting),
  ),
)

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
    {
      key: 'entity.invoice.description',
      texts: { en: 'What an invoice is for', vi: 'Hóa đơn dùng để làm gì' },
      usedBy: 1,
    },
    { key: 'field.total', texts: { en: 'Total', vi: 'Tổng' }, usedBy: 1 },
    { key: 'validation.total', texts: { en: 'Total must be positive', vi: 'Tổng phải dương' }, usedBy: 1 },
  ],
} as unknown as StudioNames

/**
 * The names for the targets of a reference: an entity labelled Department, a module's entity labelled Department, a
 * module's entity with no label, and the texts of two field labels.
 */
const withTargets = {
  ...names,
  entities: [
    ...names.entities,
    { id: 'e3', name: 'OrgUnit', labelKey: 'entity.department', module: null, fields: [] },
    { id: 'e4', name: 'organization/Department', labelKey: 'entity.department', module: 'organization', fields: [] },
    { id: 'e5', name: 'organization/Employee', labelKey: null, module: 'organization', fields: [] },
  ],
  texts: [
    ...names.texts,
    { key: 'entity.department', texts: { en: 'Department', vi: 'Phòng ban' }, usedBy: 1 },
    { key: 'field.size', texts: { en: 'Quantity', vi: 'Số lượng' }, usedBy: 1 },
  ],
} as unknown as StudioNames

/** The platform context of a shell that shows `locale`, which the designer reads the author's language from. */
function platformIn(locale: string): Platform {
  return {
    site: {
      name: 'platform',
      titleKey: 'shell.title',
      locales: { default: 'en', fallback: 'en', available: ['en', 'vi'] },
      navigation: [],
    },
    fallbackTexts: platformEnglish,
    current: { locale, texts: platformEnglish },
    setCurrent: vi.fn(),
    development: false,
  }
}

const labelContext = {
  application: 'Shop',
  file: 'entities/invoice.json',
  name: 'Invoice',
  saved: {},
  texts: {},
  onTextsChange: vi.fn(),
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
  userId: true,
  match: { field: 'code', from: 'code' },
  sensitive: true,
  exactLookup: true,
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

/** One field of each type, with every setting its type takes. A field with a way other than entered hides the entered value's settings. */
const typed = fixture([])

/** One field of each type without `expression`, `sequence`, `creator` or `match`, so each type shows all its other settings. */
const plain = fixture(['expression', 'sequence', 'creator', 'match'])

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
      { name: 'note', type: 'text', help: { textKey: 'field.total' } },
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
      <EntityDesigner value={value} onChange={onChange} application="Shop" readOnly={readOnly} />
    </Providers>,
  )
  return onChange
}

/** The text of each column header of the Fields table, in column order, without the header of the row menu. */
function columnHeaders(): string[] {
  return screen
    .getAllByRole('columnheader')
    .map((header) => header.textContent ?? '')
    .filter((text) => text !== '' && text !== 'Actions')
}

/** The text of each cell of field `index`'s row in the Fields table, in column order, without the row menu's cell. */
function fieldCells(index: number): string[] {
  return within(screen.getByTestId(`studio-field-/fields/${index}`))
    .getAllByRole('cell')
    .slice(0, 6)
    .map((cell) => cell.textContent ?? '')
}

/** The tags in the Properties cell of field `index`'s row, in order. */
function fieldTags(index: number): string[] {
  const cell = within(screen.getByTestId(`studio-field-/fields/${index}`)).getAllByRole('cell')[5]
  return Array.from(cell.querySelectorAll('.ant-tag'), (tag) => tag.textContent ?? '')
}

/**
 * The providers of the designer's tests. The designer sits in a data router, so a test moves through the address with
 * `router`. Without one, the providers make a router at `/` that draws `children`.
 */
function Providers({
  loaded,
  router,
  children,
}: {
  loaded: StudioNames | null
  router?: DataRouter
  children?: ReactNode
}) {
  const [fallback] = useState(() =>
    router === undefined ? createMemoryRouter([{ path: '*', element: children }]) : undefined,
  )
  return (
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={loaded ?? undefined}>
            <LabelTextsContext.Provider value={labelContext}>
              <RouterProvider router={(router ?? fallback)!} />
            </LabelTextsContext.Provider>
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>
  )
}

/** Renders `element` in a data router at `initialEntries`, and returns the router, so a test moves through its history. */
function renderRouted(element: ReactNode, initialEntries: string[] = ['/']) {
  const router = createMemoryRouter([{ path: '*', element }], { initialEntries })
  render(<Providers loaded={names} router={router} />)
  return router
}

/**
 * The designer over an entity that it edits, as the editor does: each change is applied with `setAt`, and the test
 * moves through the address at `initialEntries`. It returns the whole content of the entity, and the router.
 */
function renderAddressed(initial: Record<string, unknown>, initialEntries: string[] = ['/']) {
  function Harness() {
    const [value, setValue] = useState(initial)
    return (
      <>
        <EntityDesigner
          value={value}
          application="Shop"
          onChange={(pointer, next) => setValue((current) => setAt(current, pointer, next) as Record<string, unknown>)}
        />
        <output data-testid="entity-content">{JSON.stringify(value)}</output>
      </>
    )
  }
  const router = renderRouted(<Harness />, initialEntries)
  const content = () => JSON.parse(screen.getByTestId('entity-content').textContent ?? '') as Record<string, unknown>
  return { content, router }
}

/** The designer over an entity that it edits, as `renderAddressed` does, at the address `/`. */
function renderContent(initial: Record<string, unknown>) {
  return renderAddressed(initial).content
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
  ])('shows, for each type, inputs only for the settings the type takes, %s', async (_, fields) => {
    renderDesigner(entity({ fields, displayField: undefined }))

    const designer = screen.getByTestId('studio-entity-designer')
    expect(designer.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(designer).toBeVisible()
    for (const [index, field] of fields.entries()) {
      const row = await openEntityField(index)
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
    }
  })

  it('shows default, min, max and minLength on every type that takes them when the field computes nothing', async () => {
    renderDesigner(entity({ fields: plain, displayField: undefined }))

    // Each field opens once, because opening a drawer is the slow step of this test.
    const settings = ['default', 'min', 'max', 'minLength'] as const
    const typesBySetting = new Map<string, string[]>(settings.map((setting) => [setting, []]))
    for (const [index, field] of plain.entries()) {
      await openEntityField(index)
      for (const setting of settings) {
        if (screen.queryByTestId(`studio-field-/fields/${index}/${setting}`) !== null) {
          typesBySetting.get(setting)?.push(field.type)
        }
      }
    }
    for (const setting of settings) {
      const types = typesBySetting.get(setting) ?? []
      expect(types, setting).toEqual(
        plain.filter((field) => fieldSettings(field.type).includes(setting)).map((f) => f.type),
      )
      expect(types.length, setting).toBeGreaterThan(0)
    }
  })

  it('offers no sensitive setting on reference, references and file fields, and still offers it on text', async () => {
    renderDesigner(entity({ fields: plain, displayField: undefined }))

    // Each field opens once, because opening a drawer is the slow step of this test.
    const offeredOn = new Map<string, number[]>()
    for (const [index, field] of plain.entries()) {
      await openEntityField(index)
      if (screen.queryByTestId(`studio-field-/fields/${index}/sensitive`) !== null) {
        offeredOn.set(field.type, [...(offeredOn.get(field.type) ?? []), index])
      }
    }
    for (const type of ['reference', 'references', 'file']) {
      expect(
        plain.some((field) => field.type === type),
        type,
      ).toBe(true)
      expect(offeredOn.get(type) ?? [], type).toEqual([])
    }
    expect((offeredOn.get('text') ?? []).length).toBeGreaterThan(0)
  })
})

const follows = (first: HTMLElement, second: HTMLElement) =>
  (first.compareDocumentPosition(second) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0

describe('EntityDesigner', () => {
  it('shows the header and the fields of the entity', async () => {
    renderDesigner(entity())

    expect(screen.getByRole('textbox', { name: 'Entity name' })).toHaveValue('Invoice')
    expect(within(screen.getByTestId('studio-field-/label')).getByRole('textbox', { name: 'English' })).toHaveValue(
      'Invoice',
    )
    expect(within(screen.getByTestId('studio-field-/displayField')).getByText('code')).toBeInTheDocument()
    expect(
      within(within(await openEntityField(1)).getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton'),
    ).toHaveValue('20')
    expect(
      within(within(await openEntityField(0)).getByTestId('studio-field-/fields/0/name')).getByRole('textbox'),
    ).toHaveValue('total')
  })

  it('shows the columns Name, Label, Type, Required, Unique and Properties, with Yes, No or the dash for Required and Unique', () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'total', type: 'decimal', required: true, unique: true },
          { name: 'code', type: 'text', maxLength: 20 },
          { name: 'lines', type: 'child-collection', target: 'Supplier' },
          { name: 'scan', type: 'file' },
        ],
      }),
    )

    expect(columnHeaders()).toEqual(['Field name', 'Field label', 'Field type', 'Required', 'Unique', 'Properties'])
    expect(fieldCells(0)).toEqual(['total', '—', 'Decimal', 'Yes', 'Yes', '—'])
    expect(fieldCells(1)).toEqual(['code', '—', 'Text', 'No', 'No', '—'])
    expect(fieldCells(2)).toEqual(['lines', '—', 'Child collection of Supplier', '—', '—', '—'])
    expect(fieldCells(3)).toEqual(['scan', '—', 'File', 'No', '—', '—'])
  })

  it('reads a reference as Reference to its target label, its name where the entity has no label, and a text as Text', () => {
    const withLabel = {
      ...names,
      entities: [
        ...names.entities,
        { id: 'e3', name: 'OrgUnit', labelKey: 'entity.department', module: null, fields: [] },
      ],
      texts: [...names.texts, { key: 'entity.department', texts: { en: 'Department', vi: 'Phòng ban' }, usedBy: 1 }],
    } as unknown as StudioNames
    renderDesigner(
      entity({
        fields: [
          { name: 'unit', type: 'reference', target: 'OrgUnit' },
          { name: 'vendor', type: 'reference', target: 'Supplier' },
          { name: 'lower', type: 'reference', target: 'orgunit' },
          { name: 'empty', type: 'reference' },
          { name: 'note', type: 'text' },
        ],
      }),
      vi.fn(),
      withLabel,
    )

    expect(fieldCells(0)[2]).toBe('Reference to Department')
    expect(fieldCells(1)[2]).toBe('Reference to Supplier')
    expect(fieldCells(2)[2]).toBe('Reference to Department')
    expect(fieldCells(3)[2]).toBe('Reference to —')
    expect(fieldCells(4)[2]).toBe('Text')
  })

  it('links the target of a reference to its entity, and leaves the name of the field a button', () => {
    renderDesigner(entity({ fields: [{ name: 'unit', type: 'reference', target: 'OrgUnit' }] }), vi.fn(), withTargets)

    const row = screen.getByTestId('studio-field-/fields/0')
    expect(within(row).getByRole('link', { name: 'Department' })).toHaveAttribute(
      'href',
      studioResourceAddress('Shop', 'e3'),
    )
    expect(within(row).getByRole('button', { name: 'unit' })).toBeInTheDocument()
    expect(within(row).queryByRole('link', { name: 'unit' })).toBeNull()
  })

  it('links a target with no label by its name, and a module entity by its own id at its module view', () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'vendor', type: 'reference', target: 'Supplier' },
          { name: 'unit', type: 'reference', target: 'organization/Department' },
          { name: 'staff', type: 'child-collection', target: 'organization/Employee' },
        ],
      }),
      vi.fn(),
      withTargets,
    )

    expect(
      within(screen.getByTestId('studio-field-/fields/0')).getByRole('link', { name: 'Supplier' }),
    ).toHaveAttribute('href', studioResourceAddress('Shop', 'e2'))
    expect(
      within(screen.getByTestId('studio-field-/fields/1')).getByRole('link', { name: 'Department' }),
    ).toHaveAttribute('href', studioResourceAddress('Shop', 'e4'))
    expect(
      within(screen.getByTestId('studio-field-/fields/2')).getByRole('link', { name: 'Employee' }),
    ).toHaveAttribute('href', studioResourceAddress('Shop', 'e5'))
  })

  it('shows a target the application does not have, and a reference with none, with no link', () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'ghost', type: 'reference', target: 'Missing' },
          { name: 'empty', type: 'reference' },
        ],
      }),
      vi.fn(),
      withTargets,
    )

    expect(fieldCells(0)[2]).toBe('Reference to Missing')
    expect(fieldCells(1)[2]).toBe('Reference to —')
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('shows a target as text, with no link, until the names have loaded', () => {
    renderDesigner(entity({ fields: [{ name: 'vendor', type: 'reference', target: 'Supplier' }] }), vi.fn(), null)

    expect(fieldCells(0)[2]).toBe('Reference to Supplier')
    expect(screen.queryByRole('link')).toBeNull()
  })

  describe('the search of the Fields table', () => {
    const searchedFields = [
      { name: 'total', type: 'decimal', label: { textKey: 'field.total' } },
      { name: 'code', type: 'text' },
      { name: 'count', type: 'integer', label: { textKey: 'field.size' } },
    ]

    function renderSearched(locale: string) {
      render(
        <PlatformContext.Provider value={platformIn(locale)}>
          <Providers loaded={withTargets}>
            <EntityDesigner value={entity({ fields: searchedFields })} onChange={vi.fn()} application="Shop" />
          </Providers>
        </PlatformContext.Provider>,
      )
    }

    it('narrows the rows by a field name, as the search box is typed in and entered', async () => {
      renderSearched('en')

      await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'cod{Enter}')

      expect(entityFieldNames()).toEqual(['code'])
    })

    it('narrows the rows by the label as the Label cell shows it, and not by its text key', async () => {
      renderSearched('en')

      await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'quantity{Enter}')
      expect(entityFieldNames()).toEqual(['count'])

      await userEvent.clear(screen.getByRole('searchbox', { name: 'Search' }))
      await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'field.size{Enter}')
      expect(screen.queryAllByTestId(/^studio-field-\/fields\/\d+$/)).toHaveLength(0)
    })

    it("narrows the rows by the label in the author's language", async () => {
      renderSearched('vi')

      await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'số lượng{Enter}')

      expect(entityFieldNames()).toEqual(['count'])
    })
  })

  it('shows the properties of a field as tags in their order, with one source of its value, and the dash for none', () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'stamp', type: 'text', default: "'draft'", searchable: true, sensitive: true },
          { name: 'total', type: 'decimal', expression: 'total * 2', default: '1' },
          { name: 'number', type: 'text', sequence: 'InvoiceNumbers', creator: true },
          { name: 'plain', type: 'text' },
          { name: 'author', type: 'text', creator: true },
          { name: 'owner', type: 'text', userId: true, default: "'x'" },
          { name: 'supplier', type: 'reference', target: 'Supplier', match: { field: 'code', from: 'code' } },
        ],
      }),
    )

    expect(fieldTags(0)).toEqual(['Default', 'Searchable', 'Sensitive'])
    expect(fieldTags(1)).toEqual(['Computed'])
    expect(fieldTags(2)).toEqual(['Sequence'])
    expect(fieldTags(3)).toEqual([])
    expect(fieldCells(3)[5]).toBe('—')
    expect(fieldTags(4)).toEqual(['Creator'])
    expect(fieldTags(5)).toEqual(['User'])
    expect(fieldTags(6)).toEqual(['Matched'])
  })

  it('names each entity input by its label and describes it by its help', async () => {
    renderDesigner(entity({ description: { textKey: 'entity.invoice.description' } }))

    expect(screen.getByRole('textbox', { name: labelOf('name') })).toHaveAccessibleDescription(helpOf('name'))
    expect(screen.getByRole('group', { name: labelOf('label') })).toHaveAccessibleDescription(helpOf('label'))
    expect(screen.getByRole('group', { name: labelOf('description') })).toHaveAccessibleDescription(
      helpOf('description'),
    )
    expect(
      within(screen.getByTestId('studio-field-/displayField')).getByRole('combobox', {
        name: labelOf('displayField'),
      }),
    ).toHaveAccessibleDescription(helpOf('displayField'))
    expect(
      within(within(await openEntityField(1)).getByTestId('studio-field-/fields/1/maxLength')).getByRole('spinbutton', {
        name: labelOf('fields.maxLength'),
      }),
    ).toHaveAccessibleDescription(helpOf('fields.maxLength'))
  })

  it('names the currency inputs by their labels and describes them by their help', async () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'code', type: 'text' },
          { name: 'fixed', type: 'money', currency: 'USD' },
          { name: 'variable', type: 'money', currencyField: 'code' },
        ],
      }),
    )

    const fixed = within(await openEntityField(1))
    expect(fixed.getByRole('radiogroup', { name: labelOf('fields.currencySource') })).toHaveAccessibleDescription(
      helpOf('fields.currencySource'),
    )
    expect(fixed.getByRole('textbox', { name: labelOf('fields.currency') })).toHaveAccessibleDescription(
      helpOf('fields.currency'),
    )
    expect(
      within(await openEntityField(2)).getByRole('combobox', {
        name: labelOf('fields.currencyField'),
      }),
    ).toHaveAccessibleDescription(helpOf('fields.currencyField'))
  })

  it('describes each option of the currency source by its help', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'amount', type: 'money', currency: 'USD' }] }))
    const amount = within(await openEntityField(0))

    for (const [option, label] of [
      ['code', /^Fixed code/],
      ['field', /^From a field/],
    ] as const) {
      const radio = amount.getByRole('radio', { name: label })
      const description = radio.getAttribute('aria-describedby')
      expect(description).not.toBeNull()
      expect(document.getElementById(description!)).toHaveTextContent(helpOf(`fields.currencySource.${option}`))
      expect(radio).toHaveAccessibleDescription(helpOf(`fields.currencySource.${option}`))
    }
    expect(amount.getByRole('radio', { name: /^Fixed code/ })).toBeChecked()
    expect(amount.getByText(labelOf('fields.currencySource.field'))).toBeVisible()

    await userEvent.click(amount.getByRole('radio', { name: /^From a field/ }))
    expect(fields()[0]).toEqual({ name: 'amount', type: 'money', currencyField: '' })
    expect(amount.getByRole('radio', { name: /^From a field/ })).toBeChecked()
  })

  it.each([
    ['searchable', 'switch', 'Searchable', undefined],
    ['sensitive', 'checkbox', 'Sensitive', false],
    ['userId', 'checkbox', 'Holds a user', undefined],
  ] as const)(
    'draws %s as a field with its help and writes its pointer when it is toggled',
    async (setting, role, label, off) => {
      const fields = renderEditing(entity({ fields: [{ name: 'code', type: 'text' }] }))
      const code = within(await openEntityField(0))

      const input = code.getByRole(role, { name: label })
      expect(label).toBe(labelOf(`fields.${setting}`))
      expect(input).toHaveAccessibleDescription(helpOf(`fields.${setting}`))
      expect(input).not.toBeChecked()

      await userEvent.click(input)
      expect(fields()[0]).toEqual({ name: 'code', type: 'text', [setting]: true })
      expect(code.getByRole(role, { name: label })).toBeChecked()

      await userEvent.click(code.getByRole(role, { name: label }))
      expect(fields()[0]).toEqual({ name: 'code', type: 'text', ...(off === undefined ? {} : { [setting]: off }) })
    },
  )

  it('shows Exact lookup only on a sensitive field, and unticking Sensitive removes it in one change', async () => {
    const onChange = renderDesigner(
      entity({ fields: [{ name: 'code', type: 'text', sensitive: true, exactLookup: true, searchable: true }] }),
    )
    const code = within(await openEntityField(0))

    const lookup = code.getByRole('checkbox', { name: labelOf('fields.exactLookup') })
    expect(lookup).toHaveAccessibleDescription(helpOf('fields.exactLookup'))
    expect(lookup).toBeChecked()

    await userEvent.click(code.getByRole('checkbox', { name: labelOf('fields.sensitive') }))
    expect(onChange).toHaveBeenCalledTimes(1)
    const [pointer, next] = onChange.mock.calls[0]
    expect(pointer).toBe('/fields/0')
    expect(next).toEqual({ name: 'code', type: 'text', sensitive: false, searchable: true })
    expect(Object.keys(next)).toEqual(['name', 'type', 'sensitive', 'searchable'])
  })

  it('offers Exact lookup once Sensitive is ticked, and writes it as a toggle', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'code', type: 'text' }] }))
    const code = within(await openEntityField(0))

    expect(code.queryByRole('checkbox', { name: labelOf('fields.exactLookup') })).toBeNull()
    expect(code.queryByTestId('studio-field-/fields/0/exactLookup')).toBeNull()
    await userEvent.click(code.getByRole('checkbox', { name: labelOf('fields.sensitive') }))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', sensitive: true })

    await userEvent.click(code.getByRole('checkbox', { name: labelOf('fields.exactLookup') }))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', sensitive: true, exactLookup: true })
    await userEvent.click(code.getByRole('checkbox', { name: labelOf('fields.exactLookup') }))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', sensitive: true })
  })

  it('shows General, Fields, Validations and Indexes as sections in that order, each with its line and a rule before all but the first', () => {
    renderDesigner(entity())

    const names = ['general', 'fields', 'validations', 'indexes'] as const
    const regions = screen.getAllByRole('region')
    expect(regions.map((region) => region.dataset.testid)).toEqual(names.map((name) => `studio-section-${name}`))
    names.forEach((name, position) => {
      const region = screen.getByRole('region', { name: sectionOf(name) })
      expect(region).toBe(regions[position])
      expect(within(region).getByText(sectionOf(`${name}.line`))).toBeVisible()
      const previous = region.previousElementSibling
      if (position === 0) {
        expect(previous).toBeNull()
      } else {
        expect(previous).toHaveAttribute('role', 'separator')
      }
    })
  })

  it('shows the name, label, description and display field in General, and no id, kind or formatVersion', () => {
    renderDesigner(entity({ description: { textKey: 'entity.invoice.description' } }))

    const general = within(screen.getByTestId('studio-section-general'))
    expect(general.getByRole('textbox', { name: labelOf('name') })).toHaveAccessibleDescription(helpOf('name'))
    expect(general.getByRole('group', { name: labelOf('label') })).toHaveAccessibleDescription(helpOf('label'))
    expect(general.getByRole('group', { name: labelOf('description') })).toHaveAccessibleDescription(
      helpOf('description'),
    )
    expect(general.getByRole('combobox', { name: labelOf('displayField') })).toHaveAccessibleDescription(
      helpOf('displayField'),
    )
    for (const key of ['id', 'kind', 'formatVersion']) {
      expect(screen.queryByTestId(`studio-field-/${key}`), key).toBeNull()
    }
  })

  it('puts Add field on the right of the Fields heading, and a click appends an empty text field', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'a', type: 'text' }] }))
    const section = screen.getByTestId('studio-section-fields')
    const heading = within(section).getByRole('heading', { name: sectionOf('fields') })
    const add = within(section).getByRole('button', { name: 'Add field' })

    expect(follows(heading, add)).toBe(true)
    expect(follows(add, screen.getByTestId('studio-field-/fields'))).toBe(true)
    await userEvent.click(add)
    expect(fields()).toEqual([
      { name: 'a', type: 'text' },
      { name: '', type: 'text' },
    ])
  })

  it('opens a field in a drawer named by its name, with its inputs at their ids, and keeps every row', async () => {
    renderDesigner(entity())
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByTestId('studio-field-/fields/1/maxLength')).toBeNull()

    const drawer = await openEntityField(1)

    expect(drawer).toBe(screen.getByRole('dialog', { name: 'code' }))
    for (const id of ['name', 'label', 'type', 'help', 'required', 'unique', 'maxLength']) {
      expect(within(drawer).getByTestId(`studio-field-/fields/1/${id}`), id).toBeInTheDocument()
    }
    for (const [id, role, label] of [
      ['name', 'textbox', 'Name'],
      ['type', 'combobox', 'Type'],
    ]) {
      const area = within(within(drawer).getByTestId(`studio-field-/fields/1/${id}`))
      expect(area.getByText('*'), id).toBeInTheDocument()
      expect(area.getByRole(role, { name: label }), id).toHaveAttribute('aria-required', 'true')
    }
    expect(within(drawer).getByRole('textbox', { name: 'Name' })).toHaveValue('code')
    // The table keeps every row, and only the row has a `studio-field-` id in it.
    expect(screen.getAllByTestId(/^studio-field-\/fields\/\d+$/)).toHaveLength(3)
    expect(entityFieldNames()).toEqual(['total', 'code', 'note'])
  })

  describe('the address of the field drawer', () => {
    it('opens a field by its name as &field=<name>, with focus in its name, and Back closes it', async () => {
      const { router } = renderAddressed(entity(), ['/studio?app=Shop'])

      const drawer = await openEntityField(1)

      expect(drawer).toBe(screen.getByRole('dialog', { name: 'code' }))
      expect(router.state.location.search).toBe('?app=Shop&field=code')
      expect(router.state.historyAction).toBe('PUSH')
      expect(within(drawer).getByRole('textbox', { name: 'Name' })).toHaveFocus()

      await act(() => router.navigate(-1))
      await waitFor(() => expect(screen.queryByTestId('studio-field-drawer')).toBeNull())
      expect(router.state.location.search).toBe('?app=Shop')
    })

    it('opens the drawer of the field at an address that names it, and of no other', async () => {
      renderAddressed(entity(), ['/studio?app=Shop&field=code'])

      expect(await screen.findByRole('dialog', { name: 'code' })).toBeVisible()
    })

    it('opens nothing for a name that no field has, and drops it from the address with a replace', async () => {
      const { router } = renderAddressed(entity(), ['/studio?app=Shop&field=nope'])

      await waitFor(() => expect(router.state.location.search).toBe('?app=Shop'))
      expect(router.state.historyAction).toBe('REPLACE')
      expect(screen.queryByRole('dialog')).toBeNull()
    })

    it('removes field from the address when Close is pressed, and returns focus to the name of the field', async () => {
      const { router } = renderAddressed(entity(), ['/studio?app=Shop'])
      const drawer = await openEntityField(1)

      await userEvent.click(within(drawer).getByRole('button', { name: 'Close' }))

      await waitFor(() => expect(screen.queryByTestId('studio-field-drawer')).toBeNull())
      expect(router.state.location.search).toBe('?app=Shop')
      expect(within(screen.getByTestId('studio-field-/fields/1')).getAllByRole('button')[0]).toHaveFocus()
    })

    it('follows a rename of the open field in the address, and keeps the drawer open', async () => {
      const { router } = renderAddressed(entity(), ['/studio?app=Shop'])
      const drawer = await openEntityField(1)
      const name = within(drawer).getByRole('textbox', { name: 'Name' })

      await userEvent.clear(name)
      await userEvent.type(name, 'sku')

      await waitFor(() => expect(router.state.location.search).toBe('?app=Shop&field=sku'))
      expect(router.state.historyAction).toBe('REPLACE')
      expect(screen.getByRole('dialog', { name: 'sku' })).toBe(drawer)
    })

    it('opens the drawer of an added field, which has no name, with no field in the address', async () => {
      const { router } = renderAddressed(entity(), ['/studio?app=Shop'])

      await addEntityField()

      expect(await screen.findByRole('dialog', { name: 'Unnamed field' })).toBeVisible()
      expect(router.state.location.search).toBe('?app=Shop')
    })

    it('opens the drawer of a diagnostic inside a field, with focus at that setting', async () => {
      const fields = entity().fields
      const value = entity({ fields: [...fields.slice(0, 2), { name: 'note', type: 'text', maxLength: 30 }] })
      const router = renderRouted(
        <EntityDesigner value={value} onChange={vi.fn()} application="Shop" path="/fields/2/maxLength" />,
        ['/studio?app=Shop'],
      )

      const drawer = await screen.findByRole('dialog', { name: 'note' })

      expect(router.state.location.search).toBe('?app=Shop&field=note')
      expect(within(drawer).getByRole('spinbutton', { name: 'Max length' })).toHaveFocus()
    })

    it('focuses the drawer when the drawer shows no element for the path of a diagnostic', async () => {
      const value = entity({ fields: [...entity().fields.slice(0, 2), { name: 'note', type: 'text' }] })
      renderRouted(<EntityDesigner value={value} onChange={vi.fn()} application="Shop" path="/fields/2/bogus" />, [
        '/studio?app=Shop',
      ])

      expect(await screen.findByTestId('studio-field-drawer')).toHaveFocus()
    })

    it('gives the groups of the field drawer no page inset, and the groups of the General section the inset of the page', async () => {
      const value = entity({ fields: [...entity().fields.slice(0, 2), { name: 'note', type: 'text' }] })
      renderRouted(
        <ContentInsetContext.Provider value={24}>
          <EntityDesigner value={value} onChange={vi.fn()} application="Shop" />
        </ContentInsetContext.Provider>,
        ['/studio?app=Shop&field=note'],
      )

      const drawer = await screen.findByTestId('studio-field-drawer')
      const drawerInsets = within(drawer).getAllByTestId('content-inset')
      const generalInsets = within(screen.getByTestId('studio-section-general')).getAllByTestId('content-inset')

      expect(drawerInsets.length).toBeGreaterThan(0)
      drawerInsets.forEach((inset) => expect(inset).toHaveTextContent(/^0$/))
      expect(generalInsets.length).toBeGreaterThan(0)
      generalInsets.forEach((inset) => expect(inset).toHaveTextContent(/^24$/))
    })
  })

  it('reads an empty name as Unnamed field in the table and in the drawer title', async () => {
    renderDesigner(entity({ fields: [{ name: '', type: 'text' }] }))

    expect(entityFieldNames()).toEqual(['Unnamed field'])
    const drawer = await openEntityField(0)

    expect(drawer).toBe(screen.getByRole('dialog', { name: 'Unnamed field' }))
  })

  it('marks the keys that the entity schema requires and no others, and announces them as required', () => {
    renderDesigner(
      entity({
        indexes: [{ name: 'index1', fields: ['code'], unique: true }],
        validations: [
          { name: 'totalPositive', expression: 'total > 0', message: { textKey: 'validation.total' }, field: 'total' },
        ],
      }),
    )

    const marked = (testId: string, role: string, label: string) => {
      const area = within(screen.getByTestId(testId))
      expect(area.getByText('*'), label).toBeInTheDocument()
      expect(area.getByRole(role, { name: label }), label).toHaveAttribute('aria-required', 'true')
    }
    const unmarked = (testId: string, role: string, label: string) => {
      const area = within(screen.getByTestId(testId))
      expect(area.queryByText('*'), label).toBeNull()
      expect(area.getByRole(role, { name: label }), label).not.toHaveAttribute('aria-required')
    }
    marked('studio-field-/name', 'textbox', labelOf('name'))
    marked('studio-field-/indexes/0/fields', 'combobox', labelOf('indexes.fields'))
    marked('studio-field-/validations/0/expression', 'textbox', labelOf('validations.expression'))
    marked('studio-field-/validations/0/message/textKey', 'combobox', labelOf('validations.message'))
    marked('studio-field-/validations/0/field', 'combobox', labelOf('validations.field'))
    unmarked('studio-field-/displayField', 'combobox', labelOf('displayField'))
    unmarked('studio-field-/indexes/0/unique', 'checkbox', labelOf('indexes.unique'))
  })

  it('marks the target of a reference field and the currency field of a money field as required', async () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'code', type: 'text' },
          { name: 'supplier', type: 'reference', target: 'Supplier' },
          { name: 'amount', type: 'money', currencyField: 'code' },
        ],
      }),
    )

    const target = within(within(await openEntityField(1)).getByTestId('studio-field-/fields/1/target'))
    expect(target.getByText('*')).toBeInTheDocument()
    expect(target.getByRole('combobox', { name: labelOf('fields.target') })).toHaveAttribute('aria-required', 'true')
    const currency = within(within(await openEntityField(2)).getByTestId('studio-field-/fields/2/currencyField'))
    expect(currency.getByText('*')).toBeInTheDocument()
    expect(currency.getByRole('combobox', { name: labelOf('fields.currencyField') })).toHaveAttribute(
      'aria-required',
      'true',
    )
  })

  it('lays each index and validation out as a column: its name, one field to a line, then its buttons', () => {
    renderDesigner(
      entity({
        indexes: [{ name: 'index1', fields: ['code'] }],
        validations: [
          { name: 'totalPositive', expression: 'total > 0', message: { textKey: 'validation.total' }, field: 'total' },
        ],
      }),
    )

    const order = (row: HTMLElement, ids: string[]) => {
      const parts = ids.map((id) => within(row).getByTestId(id))
      parts.slice(1).forEach((part, position) => expect(follows(parts[position], part)).toBe(true))
    }
    order(screen.getByTestId('studio-field-/indexes/0'), [
      'studio-field-/indexes/0/name',
      'studio-field-/indexes/0/fields',
      'studio-field-/indexes/0/unique',
    ])
    order(screen.getByTestId('studio-field-/validations/0'), [
      'studio-field-/validations/0/name',
      'studio-field-/validations/0/expression',
      'studio-field-/validations/0/message/textKey',
      'studio-field-/validations/0/field',
    ])
    const index = within(screen.getByTestId('studio-field-/indexes/0'))
    expect(
      follows(index.getByTestId('studio-field-/indexes/0/unique'), index.getByRole('button', { name: 'Move up' })),
    ).toBe(true)
    const validation = within(screen.getByTestId('studio-field-/validations/0'))
    expect(
      follows(
        validation.getByTestId('studio-field-/validations/0/field'),
        validation.getByRole('button', { name: 'Remove validation' }),
      ),
    ).toBe(true)
  })

  it('names the index inputs by their labels and describes them by their help', () => {
    renderDesigner(entity({ indexes: [{ name: 'index1', fields: ['code'], unique: true }] }))

    const index = within(screen.getByTestId('studio-field-/indexes/0'))
    expect(index.getByRole('combobox', { name: labelOf('indexes.fields') })).toHaveAccessibleDescription(
      helpOf('indexes.fields'),
    )
    expect(index.getByRole('checkbox', { name: labelOf('indexes.unique') })).toHaveAccessibleDescription(
      helpOf('indexes.unique'),
    )
  })

  it('names the validation inputs by their labels and shows the help of the expression', () => {
    renderDesigner(
      entity({
        validations: [
          { name: 'totalPositive', expression: 'total > 0', message: { textKey: 'validation.total' }, field: 'total' },
        ],
      }),
    )

    const row = within(screen.getByTestId('studio-field-/validations/0'))
    expect(
      within(screen.getByTestId('studio-field-/validations/0/message/textKey')).getByRole('combobox', {
        name: labelOf('validations.message'),
      }),
    ).toHaveAccessibleDescription(helpOf('validations.message'))
    expect(
      within(screen.getByTestId('studio-field-/validations/0/field')).getByRole('combobox', {
        name: labelOf('validations.field'),
      }),
    ).toHaveAccessibleDescription(helpOf('validations.field'))
    expect(row.getByRole('textbox', { name: labelOf('validations.expression') })).toHaveAccessibleDescription(
      helpOf('validations.expression'),
    )
    expect(row.getByText(helpOf('validations.expression'))).toBeVisible()
  })

  it('shows the help of a computed field expression', async () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'total', type: 'integer' },
          { name: 'double', type: 'integer', expression: 'total * 2' },
        ],
      }),
    )

    expect(within(await openEntityField(1)).getByText(helpOf('fields.expression'))).toBeVisible()
  })

  it('shows the description after the name and the label and writes its texts', async () => {
    renderDesigner(entity({ description: { textKey: 'entity.invoice.description' } }))

    const description = screen.getByTestId('studio-field-/description')
    const box = within(description).getByRole('textbox', { name: 'English' })
    expect(box).toHaveValue('What an invoice is for')
    expect(follows(screen.getByTestId('studio-field-/name'), description)).toBe(true)
    expect(follows(screen.getByTestId('studio-field-/label'), description)).toBe(true)
    expect(follows(description, screen.getByTestId('studio-field-/displayField'))).toBe(true)

    await userEvent.type(box, '!')
    expect(labelContext.onTextsChange).toHaveBeenCalledWith('/description', 'entity.invoice.description', {
      en: 'What an invoice is for!',
      vi: 'Hóa đơn dùng để làm gì',
    })
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

    await chooseOption('enum', within(await openEntityField(0)).getByTestId('studio-field-/fields/0/type'))

    expect(onChange).toHaveBeenCalledWith('/fields/0', {
      name: 'total',
      type: 'enum',
      label: { textKey: 'field.total' },
      unique: true,
    })
  })

  it('removes a field label with undefined and writes a picked key under textKey', async () => {
    const onChange = renderDesigner(entity())
    const label = within(await openEntityField(0)).getByTestId('studio-field-/fields/0/label')

    await clearSelect(label)
    expect(onChange).toHaveBeenCalledWith('/fields/0/label', undefined)

    await chooseOption('field.total', within(await openEntityField(1)).getByTestId('studio-field-/fields/1/label'))
    expect(onChange).toHaveBeenCalledWith('/fields/1/label/textKey', 'field.total')
  })

  it('removes a field help with undefined and writes a picked key under textKey', async () => {
    const onChange = renderDesigner(entity())

    await chooseOption('field.total', within(await openEntityField(1)).getByTestId('studio-field-/fields/1/help'))
    expect(onChange).toHaveBeenCalledWith('/fields/1/help/textKey', 'field.total')

    await clearSelect(within(await openEntityField(2)).getByTestId('studio-field-/fields/2/help'))
    expect(onChange).toHaveBeenCalledWith('/fields/2/help', undefined)
  })

  it('keeps the help of a field when its type changes', async () => {
    const onChange = renderDesigner(entity())

    await chooseOption('integer', within(await openEntityField(2)).getByTestId('studio-field-/fields/2/type'))

    expect(onChange).toHaveBeenCalledWith('/fields/2', {
      name: 'note',
      type: 'integer',
      help: { textKey: 'field.total' },
    })
  })

  it('sends undefined for a cleared maxLength and the number for a typed one', async () => {
    const onChange = renderDesigner(entity())
    const maxLength = within(
      within(await openEntityField(1)).getByTestId('studio-field-/fields/1/maxLength'),
    ).getByRole('spinbutton')

    await userEvent.clear(maxLength)
    expect(onChange).toHaveBeenLastCalledWith('/fields/1/maxLength', undefined)
    await userEvent.type(maxLength, '5')
    expect(onChange).toHaveBeenLastCalledWith('/fields/1/maxLength', 5)
  })

  it('sends undefined when every enum value is cleared', async () => {
    const onChange = renderDesigner(entity({ fields: [{ name: 'state', type: 'enum', values: ['open'] }] }))
    const values = within(await openEntityField(0)).getByTestId('studio-field-/fields/0/values')

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
    const amount = within(await openEntityField(2))

    await userEvent.click(amount.getByRole('radio', { name: /^From a field/ }))
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money', currencyField: '' })

    await userEvent.click(amount.getByRole('combobox', { name: 'Currency field' }))
    expect(optionTexts(await openDropdown())).toEqual(['code', 'status'])
    await chooseOption('code')
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money', currencyField: 'code' })

    await userEvent.click(amount.getByRole('radio', { name: /^Fixed code/ }))
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money' })
    await userEvent.type(amount.getByRole('textbox', { name: 'Currency' }), 'vnd')
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money', currency: 'VND' })
    await userEvent.clear(amount.getByRole('textbox', { name: 'Currency' }))
    expect(fields()[2]).toEqual({ name: 'amount', type: 'money' })
  })

  it('sets and clears the max size and the accepted media types of a file field', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'receipt', type: 'file' }] }))
    const receipt = within(await openEntityField(0))

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
    const status = within(await openEntityField(0))
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
    const state = within(await openEntityField(0))

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
    const count = within(await openEntityField(0))
    await userEvent.type(count.getByRole('spinbutton', { name: 'Min' }), '0')
    await userEvent.type(count.getByRole('spinbutton', { name: 'Max' }), '10')
    expect(fields()[0]).toEqual({ name: 'count', type: 'integer', min: 0, max: 10 })
    await userEvent.clear(count.getByRole('spinbutton', { name: 'Min' }))
    expect(fields()[0]).toEqual({ name: 'count', type: 'integer', max: 10 })
    await userEvent.clear(count.getByRole('spinbutton', { name: 'Max' }))
    expect(fields()[0]).toEqual({ name: 'count', type: 'integer' })

    const total = within(await openEntityField(1))
    await userEvent.type(total.getByRole('spinbutton', { name: 'Min' }), '-1.5')
    expect(fields()[1]).toEqual({ name: 'total', type: 'decimal', min: -1.5 })

    const price = within(await openEntityField(2))
    await userEvent.type(price.getByRole('spinbutton', { name: 'Max' }), '99.25')
    expect(fields()[2]).toEqual({ name: 'price', type: 'money', currency: 'VND', max: 99.25 })
    await userEvent.clear(price.getByRole('spinbutton', { name: 'Max' }))
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
    const day = within(await openEntityField(0)).getByRole('textbox', { name: 'Min' })
    await userEvent.type(day, '2026-01-05{Enter}')
    expect(fields()[0]).toEqual({ name: 'day', type: 'date', min: '2026-01-05' })
    await clearPicker(within(await openEntityField(0)).getByTestId('studio-field-/fields/0/min'))
    expect(fields()[0]).toEqual({ name: 'day', type: 'date' })

    const at = within(await openEntityField(1)).getByRole('textbox', { name: 'Min' })
    await userEvent.type(at, '2026-01-05 10:30:00{Enter}')
    const written = fields()[1].min as string
    expect(written).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00Z$/)
    expect(new Date(written).getTime()).toBe(new Date(2026, 0, 5, 10, 30, 0).getTime())
  })

  it('shows the date and date-time limits of the file in their pickers', async () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'day', type: 'date', min: '2026-01-05', max: '2026-02-01' },
          { name: 'at', type: 'date-time', min: '2026-01-05T10:30:00Z' },
        ],
      }),
    )

    expect(
      within(within(await openEntityField(0)).getByTestId('studio-field-/fields/0/min')).getByRole('textbox', {
        name: 'Min',
      }),
    ).toHaveValue('2026-01-05')
    expect(
      within(within(await openEntityField(0)).getByTestId('studio-field-/fields/0/max')).getByRole('textbox', {
        name: 'Max',
      }),
    ).toHaveValue('2026-02-01')
    expect(
      within(within(await openEntityField(1)).getByTestId('studio-field-/fields/1/min')).getByRole('textbox', {
        name: 'Min',
      }),
    ).toHaveValue(dayjs('2026-01-05T10:30:00Z').format('YYYY-MM-DD HH:mm:ss'))
  })

  it('sets and clears minLength and searchable of a text field, and removes the key for a switch turned off', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'code', type: 'text' }] }))
    const code = within(await openEntityField(0))

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

    within(await openEntityField(0))
      .getByRole('switch', { name: 'Searchable' })
      .focus()
    await userEvent.keyboard(' ')
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', searchable: true })
  })

  it('hides default, min, max and minLength next to an expression, and keeps a key that is already there', async () => {
    renderDesigner(
      entity({
        fields: [{ name: 'code', type: 'text', sequence: 'InvoiceNumbers', minLength: 2, default: "'x'" }],
      }),
    )
    const code = within(await openEntityField(0))

    for (const name of ['Default', 'Min length']) {
      expect(code.queryByRole('textbox', { name })).toBeNull()
      expect(code.queryByRole('spinbutton', { name })).toBeNull()
    }
    expect(code.getByRole('switch', { name: 'Searchable' })).toBeInTheDocument()
  })

  describe('filter', () => {
    const fields = [
      { name: 'count', type: 'integer', required: true },
      { name: 'code', type: 'text', label: { textKey: 'field.total' } },
      { name: 'size', type: 'integer' },
      { name: 'total', type: 'decimal', unique: true },
    ]
    const rows = () => screen.getAllByTestId(/^studio-field-\/fields\/\d+$/)

    function renderFiltered(origins: (string | null)[]) {
      render(
        <Providers loaded={names}>
          <EntityDesigner value={entity({ fields })} onChange={vi.fn()} application="Shop" origins={origins} />
        </Providers>,
      )
    }

    it('offers the name, label, type, required and unique columns, and applying Type is integer leaves only the integer fields', async () => {
      renderFiltered(['count', 'code', 'size', 'total'])
      expect(rows()).toHaveLength(4)

      await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
      await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Column' }))
      expect(optionTexts()).toEqual(['Field name', 'Field label', 'Field type', 'Required', 'Unique'])
      await chooseOption('Field type')
      await pick('Value', 0, 'integer')
      await clickInDialog('Apply')

      expect(entityFieldNames()).toEqual(['count', 'size'])
      expect(screen.queryByRole('list')).not.toBeInTheDocument()
    })

    it('filters on Required as Yes and leaves only the fields that are required', async () => {
      renderFiltered(['count', 'code', 'size', 'total'])

      await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
      await pick('Column', 0, 'Required')
      await pick('Value', 0, 'Yes')
      await clickInDialog('Apply')

      expect(entityFieldNames()).toEqual(['count'])
    })

    it('keeps a field added since the last save shown, and hides a saved field that does not match', async () => {
      renderFiltered(['count', 'code', 'size', null])

      await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
      await pick('Column', 0, 'Field type')
      await pick('Value', 0, 'integer')
      await clickInDialog('Apply')

      // The decimal field `total` has no origin, so it is new and stays.
      expect(entityFieldNames()).toEqual(['count', 'size', 'total'])
    })
  })

  it('keeps the other keys of the entity when it sets a setting', async () => {
    const content = renderContent(entity({ fields: [{ name: 'count', type: 'integer' }] }))

    await userEvent.type(within(await openEntityField(0)).getByRole('spinbutton', { name: 'Min' }), '0')

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
    expect(indexes()).toEqual([{ name: 'index1', fields: [] }])

    await userEvent.click(row(0).getByRole('combobox', { name: 'Index fields' }))
    expect(optionTexts(await openDropdown())).toEqual(['stage', 'count'])
    await chooseOption('count')
    await chooseOption('stage')
    expect(indexes()).toEqual([{ name: 'index1', fields: ['count', 'stage'] }])
    await userEvent.keyboard('{Escape}')

    await userEvent.click(row(0).getByRole('checkbox', { name: 'Unique index' }))
    expect(indexes()).toEqual([{ name: 'index1', fields: ['count', 'stage'], unique: true }])

    await userEvent.click(screen.getByRole('button', { name: 'Add index' }))
    expect(indexes()).toEqual([
      { name: 'index1', fields: ['count', 'stage'], unique: true },
      { name: 'index2', fields: [] },
    ])
    expect(row(0).getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(row(1).getByRole('button', { name: 'Move down' })).toBeDisabled()
    await userEvent.click(row(1).getByRole('button', { name: 'Move up' }))
    expect(indexes()).toEqual([
      { name: 'index2', fields: [] },
      { name: 'index1', fields: ['count', 'stage'], unique: true },
    ])

    await userEvent.click(row(1).getByRole('checkbox', { name: 'Unique index' }))
    expect(indexes()).toEqual([
      { name: 'index2', fields: [] },
      { name: 'index1', fields: ['count', 'stage'] },
    ])
    expect(Object.hasOwn((indexes() as object[])[1], 'unique')).toBe(false)

    await userEvent.click(row(0).getByRole('button', { name: 'Remove index' }))
    expect(indexes()).toEqual([{ name: 'index1', fields: ['count', 'stage'] }])
    await userEvent.click(row(0).getByRole('button', { name: 'Remove index' }))
    expect(Object.hasOwn(content(), 'indexes')).toBe(false)
  })

  it('points to the JSON view for indexes that are not an array and hides the index buttons when it is read-only', () => {
    renderDesigner(entity({ indexes: 'x' }))
    expect(within(screen.getByTestId('studio-field-/indexes')).getByTestId('studio-json-only')).toBeInTheDocument()
  })

  it('offers no index button and turns the index inputs off when it is read-only', () => {
    renderDesigner(entity({ indexes: [{ name: 'code', fields: ['code'], unique: true }] }), vi.fn(), names, true)

    for (const name of ['Add index', 'Remove index']) {
      expect(screen.queryByRole('button', { name })).toBeNull()
    }
    expect(screen.getByRole('combobox', { name: 'Index fields' })).toBeDisabled()
    expect(screen.getByRole('checkbox', { name: 'Unique index' })).toBeDisabled()
  })

  it('checks a default at /fields/{i}/default, offers no field name and shows the unknown name error', async () => {
    // The scope of a default lists the functions but no aggregate.
    const scope = {
      type: null,
      fields: [],
      functions: [
        { name: 'length', parameters: ['text'], result: 'integer', group: 'Text', example: '', description: '' },
      ],
    }
    const fetcher = vi.fn(async (input: string, _init?: RequestInit) =>
      input.endsWith('/expressions/scope')
        ? new Response(JSON.stringify(scope), { status: 200 })
        : new Response(
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
          value={{ application: 'Shop', resourceId: 'b5a5f1cb', kind: 'entity', draft: () => draft, names }}
        >
          <EntityDesigner value={draft} onChange={vi.fn()} application="Shop" />
        </ExpressionCheckContext.Provider>
      </Providers>,
    )
    const row = within(within(await openEntityField(1)).getByTestId('studio-field-/fields/1/default'))

    // The editor loads lazily, and a plain read-only box stands in until it has.
    await waitFor(() =>
      expect(screen.getByTestId('studio-field-/fields/1/default').querySelector('.cm-editor')).not.toBeNull(),
    )
    const editor = row.getByRole('textbox', { name: 'Default' })
    // The completions are the rules and the functions of the scope: no field of the entity and no aggregate.
    await waitFor(() => expect(fetcher.mock.calls.some(([input]) => input.endsWith('/expressions/scope'))).toBe(true))
    await act(() => new Promise((resolve) => setTimeout(resolve, 20)))
    await userEvent.click(editor)
    await userEvent.keyboard('{Control>}[Space]{/Control}')
    const completions = (await screen.findAllByRole('option')).map((option) => option.textContent ?? '')
    expect(completions.some((text) => text.includes('length()'))).toBe(true)
    for (const name of ['total', 'tax', 'count()', 'sum()', 'now()']) {
      expect(
        completions.some((text) => text.startsWith(name)),
        name,
      ).toBe(false)
    }
    // Escape would close the field drawer too, so the editor loses focus to close its list instead.
    act(() => editor.blur())

    await userEvent.type(editor, 'total')

    await waitFor(() => expect(screen.getByTestId('studio-expression-error')).toHaveTextContent('AXC0046'))
    const bodies = fetcher.mock.calls
      .filter(([input]) => input.endsWith('/expressions/check'))
      .map(([, init]) => JSON.parse(init!.body as string))
    expect(bodies.at(-1)).toMatchObject({ path: '/fields/1/default', expression: 'total' })
    expect(bodies.every((body) => body.path === '/fields/1/default')).toBe(true)
  })

  it('moves, removes and adds a field by replacing the whole array', async () => {
    const value = entity()
    const onChange = vi.fn()
    const onOriginsChange = vi.fn()
    render(
      <Providers loaded={names}>
        <EntityDesigner
          value={value}
          onChange={onChange}
          application="Shop"
          origins={['total', 'code', 'note']}
          onOriginsChange={onOriginsChange}
        />
      </Providers>,
    )
    const fields = value.fields

    // Escape does not close a row's menu here, as focus stays on the row's button, so a click outside closes it.
    expect(await entityFieldMenuItem(0, 'Move up')).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(document.body)
    expect(await entityFieldMenuItem(2, 'Move down')).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(document.body)
    expect(await entityFieldMenuItem(1, 'Remove')).toHaveClass('ant-dropdown-menu-item-danger')
    await userEvent.click(document.body)

    await entityFieldAction(1, 'Move up')
    expect(onChange).toHaveBeenLastCalledWith('/fields', [fields[1], fields[0], fields[2]])
    expect(onOriginsChange).toHaveBeenLastCalledWith(['code', 'total', 'note'])
    await entityFieldAction(1, 'Move down')
    expect(onChange).toHaveBeenLastCalledWith('/fields', [fields[0], fields[2], fields[1]])
    expect(onOriginsChange).toHaveBeenLastCalledWith(['total', 'note', 'code'])
    await entityFieldAction(0, 'Remove')
    expect(onChange).toHaveBeenLastCalledWith('/fields', [fields[1], fields[2]])
    expect(onOriginsChange).toHaveBeenLastCalledWith(['code', 'note'], 'total')
    await addEntityField()
    expect(onChange).toHaveBeenLastCalledWith('/fields', [...fields, { name: '', type: 'text' }])
  })

  it('opens a new field in its drawer, and the drawer follows its field on a move and closes on its own removal', async () => {
    renderContent(entity())

    await addEntityField()
    const added = await screen.findByRole('dialog', { name: 'Unnamed field' })
    expect(within(within(added).getByTestId('studio-field-/fields/3/name')).getByRole('textbox')).toHaveValue('')

    await openEntityField(1)
    await entityFieldAction(1, 'Move up')
    let drawer = screen.getByRole('dialog', { name: 'code' })
    expect(within(within(drawer).getByTestId('studio-field-/fields/0/name')).getByRole('textbox')).toHaveValue('code')

    await entityFieldAction(0, 'Move down')
    drawer = screen.getByRole('dialog', { name: 'code' })
    expect(within(within(drawer).getByTestId('studio-field-/fields/1/name')).getByRole('textbox')).toHaveValue('code')
    expect(entityFieldNames()).toEqual(['total', 'code', 'note', 'Unnamed field'])

    // Removing another field before the drawer's field moves the drawer up an index.
    await entityFieldAction(0, 'Remove')
    drawer = screen.getByRole('dialog', { name: 'code' })
    expect(within(within(drawer).getByTestId('studio-field-/fields/0/name')).getByRole('textbox')).toHaveValue('code')
    expect(entityFieldNames()).toEqual(['code', 'note', 'Unnamed field'])

    // Removing the drawer's own field closes the drawer.
    await entityFieldAction(0, 'Remove')
    await waitFor(() => expect(screen.queryByTestId('studio-field-drawer')).toBeNull())
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(entityFieldNames()).toEqual(['note', 'Unnamed field'])
  })

  it('adds and removes a validation, with a message key and a field of the draft', async () => {
    const validation = {
      name: 'totalPositive',
      expression: 'total > 0',
      message: { textKey: 'validation.total' },
      field: 'total',
    }
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
      { name: 'validation1', expression: '', message: { textKey: '' }, field: '' },
    ])
  })

  it('names each added validation and index with the first name its list does not use', async () => {
    const content = renderContent(entity({ indexes: [{ name: 'Index1', fields: ['code'] }] }))

    await userEvent.click(screen.getByRole('button', { name: 'Add validation' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add validation' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add index' }))

    expect((content().validations as { name: string }[]).map((validation) => validation.name)).toEqual([
      'validation1',
      'validation2',
    ])
    expect((content().indexes as { name: string }[]).map((index) => index.name)).toEqual(['Index1', 'index2'])
    expect(screen.getByTestId('studio-field-/validations/1/name')).toHaveTextContent('validation2')
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
        validations: [
          { name: 'totalPositive', expression: 'total > 0', message: { textKey: 'validation.total' }, field: 'total' },
        ],
      }),
      vi.fn(),
      names,
      true,
    )

    const inputsOf = (scope: HTMLElement) => [
      ...within(scope).queryAllByRole('textbox'),
      ...within(scope).queryAllByRole('combobox'),
      ...within(scope).queryAllByRole('checkbox'),
      ...within(scope).queryAllByRole('spinbutton'),
      ...within(scope).queryAllByRole('radio'),
    ]
    // The table and the sections hold no field input: the settings are in the field drawer.
    for (const input of inputsOf(screen.getByTestId('studio-entity-designer'))) {
      expect(input).toBeDisabled()
    }
    expect(within(screen.getByTestId('studio-field-/label')).getByRole('textbox')).toHaveValue('Invoice')
    for (const name of ['Add field', 'Move up', 'Move down', 'Remove validation', 'Add validation']) {
      expect(screen.queryByRole('button', { name })).toBeNull()
    }
    // A read-only designer has no row menu, so no row of its fields has an Actions button.
    const fieldRows = screen.getAllByTestId(/^studio-field-\/fields\/\d+$/)
    expect(fieldRows.length).toBeGreaterThan(0)
    for (const fieldRow of fieldRows) {
      expect(within(fieldRow).queryByRole('button', { name: 'Actions' })).toBeNull()
    }

    // Each name still opens its field's drawer, with every input in it off.
    for (const [index, name] of ['code', 'status', 'total', 'price', 'receipt'].entries()) {
      const drawer = await openEntityField(index)
      expect(drawer).toBe(screen.getByRole('dialog', { name }))
      const inputs = inputsOf(drawer)
      expect(inputs.length).toBeGreaterThan(0)
      for (const input of inputs) {
        expect(input).toBeDisabled()
      }
      expect(within(drawer).getByRole('combobox', { name: 'Type' })).toBeDisabled()
      if (index === 0) {
        expect(within(drawer).getAllByRole('checkbox').length).toBeGreaterThan(0)
      }
      if (index === 3) {
        for (const radio of [/^Fixed code/, /^From a field/]) {
          expect(within(drawer).getByRole('radio', { name: radio })).toBeDisabled()
        }
      }
      if (index === 4) {
        expect(within(drawer).getByRole('spinbutton', { name: 'Max size (bytes)' })).toBeDisabled()
        expect(within(drawer).getByRole('combobox', { name: 'Accepted media types' })).toBeDisabled()
      }
      if (index === 1) {
        for (const label of ['Label of open', 'Label of closed']) {
          expect(within(drawer).getByRole('combobox', { name: label })).toBeDisabled()
        }
      }
      expect(within(screen.getAllByTestId(`studio-field-/fields/${index}`)[0]).getAllByRole('button')[0]).toBeEnabled()
    }

    await userEvent.type(
      within(within(await openEntityField(0)).getByTestId('studio-field-/fields/0/name')).getByRole('textbox'),
      'x',
    )
    expect(onChange).not.toHaveBeenCalled()
  })

  it('shows a disabled label box and plain boxes for the rest until the names have loaded', async () => {
    renderDesigner(
      entity({
        validations: [{ name: 'totalPositive', expression: 'total > 0', message: { textKey: 'v' }, field: 'total' }],
      }),
      vi.fn(),
      null,
    )

    expect(within(screen.getByTestId('studio-field-/label')).getByRole('textbox')).toBeDisabled()
    expect(screen.queryByRole('textbox', { name: 'Text key' })).toBeNull()
    expect(
      within(within(await openEntityField(0)).getByTestId('studio-field-/fields/0/label')).getByRole('textbox'),
    ).toHaveValue('field.total')
    expect(within(screen.getByTestId('studio-field-/validations/0/message/textKey')).getByRole('textbox')).toHaveValue(
      'v',
    )
  })
})

/** The titles of the groups in the drawer, in order, without those of the groups inside them. */
function groupTitles(drawer: HTMLElement): string[] {
  const body = within(drawer).getByTestId('studio-field-drawer')
  return Array.from(body.children)
    .filter((child) => child.tagName === 'FIELDSET')
    .map((group) => group.querySelector('legend')?.textContent ?? '')
}

describe('the groups of the field drawer', () => {
  it('shows a text field in the groups General, Text settings, Value, Validation, and Search and privacy, with Max length in Text settings', async () => {
    renderDesigner(entity())

    const drawer = await openEntityField(1)

    expect(groupTitles(drawer)).toEqual(['General', 'Text settings', 'Value', 'Validation', 'Search and privacy'])
    const settings = within(drawer).getByRole('group', { name: 'Text settings' })
    expect(within(settings).getByRole('spinbutton', { name: 'Max length' })).toHaveValue('20')
    expect(
      within(within(drawer).getByRole('group', { name: 'Validation' })).queryByRole('spinbutton', {
        name: 'Max length',
      }),
    ).toBeNull()
    expect(within(drawer).getByRole('group', { name: 'Validation' })).toContainElement(
      screen.getByTestId('studio-field-/fields/1/required'),
    )
  })

  it('shows a boolean field with no group of the type, and a reference field with a Value group for Matched', async () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'active', type: 'boolean' },
          { name: 'supplier', type: 'reference', target: 'Supplier' },
        ],
      }),
    )

    expect(groupTitles(await openEntityField(0))).toEqual(['General', 'Value', 'Validation', 'Search and privacy'])

    expect(groupTitles(await openEntityField(1))).toEqual(['General', 'Reference settings', 'Value', 'Validation'])
  })

  it('leaves out the Validation group of a child collection that has no validation to list and none to add', async () => {
    renderDesigner(entity({ fields: [{ type: 'child-collection', target: 'Supplier' }] }))
    expect(groupTitles(await openEntityField(0))).toEqual(['General', 'Child collection settings'])
  })

  it('shows precision and scale in one row, and min and max in another', async () => {
    renderDesigner(
      entity({
        fields: [
          { name: 'total', type: 'decimal', precision: 18, scale: 2, min: 0, max: 10 },
          { name: 'count', type: 'integer', min: 1, max: 5 },
        ],
      }),
    )

    const decimal = await openEntityField(0)
    const precision = within(decimal).getByRole('group', { name: 'Precision and scale' })
    expect(within(precision).getByRole('spinbutton', { name: 'Precision' })).toHaveValue('18')
    expect(within(precision).getByRole('spinbutton', { name: 'Scale' })).toHaveValue('2')
    expect(within(precision).queryByRole('spinbutton', { name: 'Min' })).toBeNull()
    const range = within(decimal).getByRole('group', { name: 'Range' })
    expect(within(range).getByRole('spinbutton', { name: 'Min' })).toHaveValue('0')
    expect(within(range).getByRole('spinbutton', { name: 'Max' })).toHaveValue('10')

    const integer = await openEntityField(1)
    expect(within(integer).queryByRole('group', { name: 'Precision and scale' })).toBeNull()
    expect(
      within(within(integer).getByRole('group', { name: 'Range' })).getByRole('spinbutton', { name: 'Min' }),
    ).toHaveValue('1')
  })

  describe('the validations at the field', () => {
    const validation = (name: string, field: string) => ({
      name,
      expression: 'total > 0',
      message: { textKey: 'validation.total' },
      field,
    })
    const validations = [validation('first', 'total'), validation('second', 'code'), validation('third', 'total')]

    it('lists only the validations whose field is this field, and the Validations section lists every one', async () => {
      renderDesigner(entity({ validations }))

      const drawer = await openEntityField(0)
      const box = (index: number) => within(drawer).queryByTestId(`studio-field-/validations/${index}`)

      expect(within(drawer).getByTestId('studio-field-/validations/0/name')).toHaveTextContent('first')
      expect(box(1)).toBeNull()
      expect(within(drawer).getByTestId('studio-field-/validations/2/name')).toHaveTextContent('third')
      // A validation at the field has no field picker, and its message is typed as texts.
      expect(within(drawer).queryByTestId('studio-field-/validations/0/field')).toBeNull()
      expect(within(box(0)!).getByRole('textbox', { name: 'English' })).toBeInTheDocument()
      expect(within(box(0)!).getByRole('group', { name: 'Message' })).toBeInTheDocument()
      const section = within(screen.getByTestId('studio-field-/validations'))
      for (const [index, name] of ['first', 'second', 'third'].entries()) {
        expect(section.getByTestId(`studio-field-/validations/${index}/name`)).toHaveTextContent(name)
      }
    })

    it('removes a validation from the drawer', async () => {
      const onChange = renderDesigner(entity({ validations }))

      const drawer = await openEntityField(0)
      await userEvent.click(
        within(within(drawer).getByTestId('studio-field-/validations/2')).getByRole('button', {
          name: 'Remove validation',
        }),
      )

      expect(onChange).toHaveBeenLastCalledWith('/validations/2', undefined)
    })

    it('adds a validation at the field, with its name in field', async () => {
      const content = renderContent(entity({ validations }))

      await userEvent.click(within(await openEntityField(0)).getByRole('button', { name: 'Add validation' }))

      expect(content().validations).toEqual([
        ...validations,
        { name: 'validation1', expression: '', message: { textKey: '' }, field: 'total' },
      ])
      expect(
        within(screen.getByTestId('studio-field-drawer')).getByTestId('studio-field-/validations/3/name'),
      ).toHaveTextContent('validation1')
    })

    it('adds the first validation of an entity that has none', async () => {
      const onChange = renderDesigner(entity())

      await userEvent.click(within(await openEntityField(1)).getByRole('button', { name: 'Add validation' }))

      expect(onChange).toHaveBeenLastCalledWith('/validations', [
        { name: 'validation1', expression: '', message: { textKey: '' }, field: 'code' },
      ])
    })

    it('has no Add validation for a field with no name, and lists no validation of a blank field', async () => {
      renderDesigner(entity({ fields: [{ type: 'text' }], validations: [validation('blank', '')] }))

      const drawer = await openEntityField(0)

      expect(within(drawer).queryByRole('button', { name: 'Add validation' })).toBeNull()
      expect(within(drawer).queryByTestId('studio-field-/validations/0')).toBeNull()
    })

    it('lists the validations in a read-only designer but has no Add validation or Remove validation', async () => {
      renderDesigner(entity({ validations }), vi.fn(), names, true)

      const drawer = await openEntityField(0)

      expect(within(drawer).getByTestId('studio-field-/validations/0/name')).toHaveTextContent('first')
      expect(within(drawer).queryByRole('button', { name: 'Add validation' })).toBeNull()
      expect(within(drawer).queryByRole('button', { name: 'Remove validation' })).toBeNull()
    })

    it('has no Add validation where the validations are not a list', async () => {
      renderDesigner(entity({ validations: 'y' }))

      expect(within(await openEntityField(0)).queryByRole('button', { name: 'Add validation' })).toBeNull()
    })
  })
})

/** The names with the fields of Supplier, which a matched reference is found by: its unique ones and the rest. */
const withSupplierFields = {
  ...names,
  entities: [
    names.entities[0],
    {
      id: 'e2',
      name: 'Supplier',
      module: null,
      fields: [
        { name: 'code', type: 'text', target: null, values: null, unique: true },
        { name: 'vatId', type: 'text', target: null, values: null, unique: true },
        { name: 'title', type: 'text', target: null, values: null, unique: false },
        { name: 'rank', type: 'integer', target: null, values: null, unique: true },
        { name: 'parent', type: 'reference', target: 'Supplier', values: null, unique: true },
      ],
    },
  ],
} as unknown as StudioNames

describe('where the value of a field comes from', () => {
  const sourceLabels = ['Entered by people', 'Computed', 'Sequence', 'Creator', 'Matched']
  const radio = (drawer: HTMLElement, label: string) =>
    within(drawer).getByRole('radio', { name: new RegExp(`^${label}`) })
  const radioLabels = (drawer: HTMLElement) =>
    within(within(drawer).getByRole('radiogroup', { name: labelOf('fields.valueSource') }))
      .getAllByRole('radio')
      .map((input) => sourceLabels.find((label) => within(input.closest('label')!).queryByText(label) !== null))

  it('offers a text field Entered by people, Computed, Sequence and Creator, each with its help', async () => {
    renderDesigner(entity({ fields: [{ name: 'code', type: 'text', required: true, default: "'a'" }] }))
    const drawer = await openEntityField(0)

    const group = within(drawer).getByRole('radiogroup', { name: labelOf('fields.valueSource') })
    expect(group).toHaveAccessibleDescription(helpOf('fields.valueSource'))
    expect(radioLabels(drawer)).toEqual(['Entered by people', 'Computed', 'Sequence', 'Creator'])
    for (const [way, label] of [
      ['entered', 'Entered by people'],
      ['computed', 'Computed'],
      ['sequence', 'Sequence'],
      ['creator', 'Creator'],
    ] as const) {
      expect(radio(drawer, label)).toHaveAccessibleDescription(helpOf(`fields.valueSource.${way}`))
    }
    expect(radio(drawer, 'Entered by people')).toBeChecked()
    expect(within(drawer).getByTestId('studio-field-/fields/0/required')).toBeVisible()
    expect(within(drawer).getByTestId('studio-field-/fields/0/default')).toBeVisible()
    expect(within(drawer).getByTestId('studio-field-/fields/0/userId')).toBeVisible()
  })

  it.each([
    ['integer', ['Entered by people', 'Computed']],
    ['decimal', ['Entered by people', 'Computed']],
    ['boolean', ['Entered by people', 'Computed']],
    ['date', ['Entered by people', 'Computed']],
    ['enum', ['Entered by people', 'Computed']],
    ['money', ['Entered by people', 'Computed']],
    ['reference', ['Entered by people', 'Matched']],
  ])('offers a %s field only the ways its type takes', async (type, expected) => {
    renderDesigner(entity({ fields: [{ name: 'f', type, target: 'Supplier' }] }))

    expect(radioLabels(await openEntityField(0))).toEqual(expected)
  })

  it.each(['references', 'file', 'child-collection'])(
    'offers a %s field no choice, which only has an entered value',
    async (type) => {
      renderDesigner(entity({ fields: [{ name: 'f', type, target: 'Supplier' }] }))

      expect(
        within(await openEntityField(0)).queryByRole('radiogroup', { name: labelOf('fields.valueSource') }),
      ).toBeNull()
    },
  )

  it('rewrites the field once when Computed is chosen, with no default and no Required', async () => {
    const onChange = renderDesigner(
      entity({
        fields: [
          { name: 'code', type: 'text', required: true, default: "'a'", minLength: 2, maxLength: 20, userId: true },
        ],
      }),
    )
    const drawer = await openEntityField(0)

    await userEvent.click(radio(drawer, 'Computed'))

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('/fields/0', { name: 'code', type: 'text', maxLength: 20, expression: '' })
  })

  it('shows only the settings of the chosen way, and Required only for an entered value', async () => {
    const fields = renderEditing(
      entity({ fields: [{ name: 'code', type: 'text', required: true, default: "'a'", maxLength: 20 }] }),
    )
    const drawer = await openEntityField(0)
    const shown = (setting: string) => within(drawer).queryByTestId(`studio-field-/fields/0/${setting}`) !== null

    await userEvent.click(radio(drawer, 'Computed'))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', maxLength: 20, expression: '' })
    expect(radio(drawer, 'Computed')).toBeChecked()
    expect(['expression', 'default', 'userId', 'sequence', 'required', 'minLength'].filter(shown)).toEqual([
      'expression',
    ])
    expect(
      within(drawer).getByTestId('studio-field-/fields/0/expression').querySelector('[aria-hidden="true"]'),
    ).toHaveTextContent('*')

    await userEvent.click(radio(drawer, 'Sequence'))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', maxLength: 20, sequence: '' })
    expect(['expression', 'default', 'userId', 'sequence', 'required', 'minLength'].filter(shown)).toEqual(['sequence'])

    await userEvent.click(radio(drawer, 'Creator'))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', maxLength: 20, creator: true })
    expect(['expression', 'default', 'userId', 'sequence', 'creator', 'required', 'minLength'].filter(shown)).toEqual(
      [],
    )
    expect(radio(drawer, 'Creator')).toBeChecked()

    await userEvent.click(radio(drawer, 'Entered by people'))
    expect(fields()[0]).toEqual({ name: 'code', type: 'text', maxLength: 20 })
    expect(['default', 'userId', 'required', 'minLength'].filter(shown)).toEqual([
      'default',
      'userId',
      'required',
      'minLength',
    ])
  })

  it('changes nothing when the way the field has is chosen again', async () => {
    const onChange = renderDesigner(
      entity({ fields: [{ name: 'code', type: 'text', expression: 'a', default: "'x'" }] }),
    )
    const drawer = await openEntityField(0)

    await userEvent.click(radio(drawer, 'Computed'))

    expect(onChange).not.toHaveBeenCalled()
  })

  it('leaves an emptied expression or sequence empty, so the field keeps its way', async () => {
    const onChange = renderDesigner(entity({ fields: [{ name: 'code', type: 'text', expression: 'a' }] }))
    const drawer = await openEntityField(0)

    await userEvent.clear(within(within(drawer).getByTestId('studio-field-/fields/0/expression')).getByRole('textbox'))

    expect(onChange).toHaveBeenLastCalledWith('/fields/0/expression', '')
  })

  it('shows the sequence picker, marked required, for a field with a sequence', async () => {
    renderDesigner(entity({ fields: [{ name: 'code', type: 'text', sequence: 'InvoiceNumbers' }] }))
    const drawer = await openEntityField(0)

    const picker = within(drawer).getByTestId('studio-field-/fields/0/sequence')
    expect(within(picker).getByText('InvoiceNumbers')).toBeVisible()
    expect(picker).toHaveTextContent('*')
  })

  it('writes userId for Holds a user and removes it when it is unticked', async () => {
    const fields = renderEditing(entity({ fields: [{ name: 'owner', type: 'text' }] }))
    const drawer = await openEntityField(0)
    const holds = () => within(drawer).getByRole('checkbox', { name: labelOf('fields.userId') })

    expect(holds()).toHaveAccessibleDescription(helpOf('fields.userId'))
    await userEvent.click(holds())
    expect(fields()[0]).toEqual({ name: 'owner', type: 'text', userId: true })
    await userEvent.click(holds())
    expect(fields()[0]).toEqual({ name: 'owner', type: 'text' })
  })

  it('shows Holds a user and Default only for a text field that is entered', async () => {
    renderDesigner(entity({ fields: [{ name: 'count', type: 'integer' }] }))
    const drawer = await openEntityField(0)

    expect(within(drawer).queryByTestId('studio-field-/fields/0/userId')).toBeNull()
    expect(within(drawer).getByTestId('studio-field-/fields/0/default')).toBeVisible()
  })

  describe('Matched', () => {
    function renderMatched(fields: unknown[], index = 0) {
      const onChange = vi.fn()
      render(
        <Providers loaded={withSupplierFields}>
          <EntityDesigner value={entity({ fields })} onChange={onChange} application="Shop" />
        </Providers>,
      )
      return { onChange, open: () => openEntityField(index) }
    }

    it('writes an empty match for Matched and drops Required', async () => {
      const { onChange, open } = renderMatched([
        { name: 'supplier', type: 'reference', target: 'Supplier', required: true },
      ])
      const drawer = await open()

      await userEvent.click(radio(drawer, 'Matched'))

      expect(onChange).toHaveBeenCalledTimes(1)
      expect(onChange).toHaveBeenCalledWith('/fields/0', {
        name: 'supplier',
        type: 'reference',
        target: 'Supplier',
        match: { field: '', from: '' },
      })
    })

    it("offers Find by only the target entity's unique fields", async () => {
      const { open } = renderMatched([
        { name: 'supplier', type: 'reference', target: 'supplier', match: { field: '', from: '' } },
      ])
      const drawer = await open()

      const findBy = within(drawer).getByTestId('studio-field-/fields/0/match/field')
      expect(findBy).toHaveTextContent('*')
      await userEvent.click(within(findBy).getByRole('combobox'))
      expect(optionTexts(await openDropdown())).toEqual(['code', 'vatId', 'rank', 'parent'])
    })

    it("offers From only this entity's fields of the Find by field's type, without sensitive, matched or computed ones", async () => {
      const { onChange, open } = renderMatched([
        { name: 'supplier', type: 'reference', target: 'Supplier', match: { field: 'code', from: '' } },
        { name: 'sku', type: 'text' },
        { name: 'barcode', type: 'text', maxLength: 5 },
        { name: 'quantity', type: 'integer' },
        { name: 'secret', type: 'text', sensitive: true },
        { name: 'derived', type: 'text', expression: "'a'" },
        { name: 'other', type: 'reference', target: 'Supplier', match: { field: 'code', from: 'sku' } },
        { name: '', type: 'text' },
      ])
      const drawer = await open()

      const from = within(drawer).getByTestId('studio-field-/fields/0/match/from')
      expect(from).toHaveTextContent('*')
      await userEvent.click(within(from).getByRole('combobox'))
      expect(optionTexts(await openDropdown())).toEqual(['sku', 'barcode'])

      await chooseOption('barcode')
      expect(onChange).toHaveBeenLastCalledWith('/fields/0/match/from', 'barcode')
    })

    it('offers From only a reference to the same target when Find by is a reference, and never the field itself', async () => {
      const { open } = renderMatched([
        { name: 'supplier', type: 'reference', target: 'Supplier', match: { field: 'parent', from: '' } },
        { name: 'partner', type: 'reference', target: 'supplier' },
        { name: 'invoice', type: 'reference', target: 'Invoice' },
        { name: 'sku', type: 'text' },
      ])
      const drawer = await open()

      await userEvent.click(
        within(within(drawer).getByTestId('studio-field-/fields/0/match/from')).getByRole('combobox'),
      )
      expect(optionTexts(await openDropdown())).toEqual(['partner'])
    })

    it('offers From nothing when Find by names no unique field of the target', async () => {
      const { open } = renderMatched([
        { name: 'supplier', type: 'reference', target: 'Supplier', match: { field: 'title', from: '' } },
        { name: 'sku', type: 'text' },
      ])
      const drawer = await open()

      await userEvent.click(
        within(within(drawer).getByTestId('studio-field-/fields/0/match/from')).getByRole('combobox'),
      )
      expect(optionTexts(await openDropdown())).toEqual([])
    })

    it('writes the picked Find by into the match', async () => {
      const { onChange, open } = renderMatched([
        { name: 'supplier', type: 'reference', target: 'Supplier', match: { field: 'code', from: 'sku' } },
        { name: 'sku', type: 'text' },
      ])
      const drawer = await open()
      const findBy = within(drawer).getByTestId('studio-field-/fields/0/match/field')

      await chooseOption('vatId', findBy)
      expect(onChange).toHaveBeenLastCalledWith('/fields/0/match/field', 'vatId')
    })

    it('draws Find by and From as plain boxes until the names have loaded', async () => {
      const onChange = vi.fn()
      render(
        <Providers loaded={null}>
          <EntityDesigner
            value={entity({
              fields: [
                { name: 'supplier', type: 'reference', target: 'Supplier', match: { field: 'code', from: 'sku' } },
              ],
            })}
            onChange={onChange}
            application="Shop"
          />
        </Providers>,
      )
      const drawer = await openEntityField(0)

      expect(within(within(drawer).getByTestId('studio-field-/fields/0/match/field')).getByRole('textbox')).toHaveValue(
        'code',
      )
      await userEvent.type(
        within(within(drawer).getByTestId('studio-field-/fields/0/match/from')).getByRole('textbox'),
        'x',
      )
      expect(onChange).toHaveBeenLastCalledWith('/fields/0/match/from', 'skux')
    })
  })

  describe('a hidden key already in the file', () => {
    it('stays hidden, and its diagnostic opens the drawer with focus on the drawer, with no change', async () => {
      const onChange = vi.fn()
      const value = entity({
        fields: [{ name: 'total', type: 'decimal', expression: 'a', default: '1', required: true }],
      })
      renderRouted(<EntityDesigner value={value} onChange={onChange} application="Shop" path="/fields/0/default" />, [
        '/studio?app=Shop',
      ])

      const drawer = await screen.findByTestId('studio-field-drawer')

      expect(drawer).toHaveFocus()
      expect(within(drawer).queryByTestId('studio-field-/fields/0/default')).toBeNull()
      expect(within(drawer).queryByTestId('studio-field-/fields/0/required')).toBeNull()
      expect(onChange).not.toHaveBeenCalled()
    })

    it('goes only when the author changes the way', async () => {
      const fields = renderEditing(
        entity({ fields: [{ name: 'total', type: 'decimal', expression: 'a', default: '1', required: true }] }),
      )
      const drawer = await openEntityField(0)

      await userEvent.click(radio(drawer, 'Entered by people'))

      expect(fields()[0]).toEqual({ name: 'total', type: 'decimal', default: '1', required: true })
    })
  })
})

describe('the texts of the entity designer', () => {
  it.each(typesWithOwnGroup)(
    'has a title and a help for the settings of a %s field in English and Vietnamese',
    (type) => {
      for (const catalogue of [platformEnglish, platformVietnamese] as Record<string, string>[]) {
        expect(catalogue[`shell.studio.field.entity.fields.group.type.${type}`]).toBeTruthy()
        expect(catalogue[`shell.studio.field.entity.fields.group.type.${type}.help`]).toBeTruthy()
      }
    },
  )

  it.each(drawnPaths)('has a label and a help for %s in English and Vietnamese', (path) => {
    for (const catalogue of [platformEnglish, platformVietnamese] as Record<string, string>[]) {
      expect(catalogue[`shell.studio.field.entity.${path}`]).toBeTruthy()
      expect(catalogue[`shell.studio.field.entity.${path}.help`]).toBeTruthy()
    }
  })

  it.each(['general', 'fields', 'validations', 'indexes'])(
    'has a title and a line for the %s section in English and Vietnamese',
    (name) => {
      for (const catalogue of [platformEnglish, platformVietnamese] as Record<string, string>[]) {
        expect(catalogue[`shell.studio.section.entity.${name}`]).toBeTruthy()
        expect(catalogue[`shell.studio.section.entity.${name}.line`]).toBeTruthy()
      }
    },
  )
})

describe('EntityDesigner with a customization layer', () => {
  function renderLayered(value: Record<string, unknown>, elements: LayerElement[], disabled = false) {
    const onReset = vi.fn()
    render(
      <Providers loaded={names}>
        <EntityDesigner value={value} onChange={vi.fn()} application="Shop" layer={{ elements, disabled, onReset }} />
      </Providers>,
    )
    return onReset
  }

  it('names each mark and each Reset to module after the element', async () => {
    const onReset = renderLayered(
      entity({
        validations: [
          { name: 'positive', expression: 'true', message: { textKey: 'validation.total' }, field: 'total' },
        ],
        indexes: [{ name: 'byCode', fields: ['code'] }],
      }),
      [
        { list: '/fields', key: 'note', mark: 'added' },
        { list: '/validations', key: 'positive', mark: 'changed' },
        { list: '/indexes', key: 'byCode', mark: 'added' },
      ],
    )

    expect(screen.getByRole('note', { name: 'note, added by this application' })).toHaveTextContent('Added')
    expect(screen.getByRole('note', { name: 'positive, changed by this application' })).toHaveTextContent('Changed')
    expect(screen.getByRole('note', { name: 'byCode, added by this application' })).toBeInTheDocument()
    expect(screen.getByTestId('studio-layer-mark-/fields/2')).toBeInTheDocument()
    expect(screen.queryByRole('note', { name: /total/ })).toBeNull()
    expect(screen.getAllByRole('button', { name: /^Reset to module/ })).toHaveLength(3)

    await userEvent.click(screen.getByRole('button', { name: 'Reset to module note' }))

    expect(onReset).toHaveBeenCalledWith('/fields', 'note')
  })

  it('shows a removed field as a muted row with its name and Reset to module', async () => {
    const onReset = renderLayered(entity({ fields: [{ name: 'total', type: 'decimal' }] }), [
      { list: '/fields', key: 'code', mark: 'removed' },
    ])

    const row = screen.getByTestId('studio-layer-removed-/fields/code')
    expect(row).toHaveTextContent('code')
    expect(row).toHaveAttribute('data-muted')
    expect(within(row).getByRole('note', { name: 'code, removed by this application' })).toBeInTheDocument()

    await userEvent.click(within(row).getByRole('button', { name: 'Reset to module code' }))

    expect(onReset).toHaveBeenCalledWith('/fields', 'code')
  })

  it('shows a removed index although the entity has none, and turns Reset to module off when disabled', () => {
    renderLayered(entity(), [{ list: '/indexes', key: 'byCode', mark: 'removed' }], true)

    expect(screen.getByTestId('studio-layer-removed-/indexes/byCode')).toHaveTextContent('byCode')
    expect(screen.getByRole('button', { name: 'Reset to module byCode' })).toBeDisabled()
  })
})

describe('typing in the entity designer', () => {
  const field = (name: string, type: string) => ({ name, type, default: "'1'" })
  const content = entity({
    fields: [field('a', 'text'), field('b', 'text'), field('c', 'decimal'), field('d', 'decimal')],
    validations: [0, 1, 2].map((index) => ({
      name: `v${index}`,
      expression: `a = '${index}'`,
      message: { textKey: 'validation.total' },
      field: 'a',
    })),
  })

  /** The designer over a draft that the edits change, with one expression check context that stays the same, as the editor gives it. */
  function renderStable() {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input.endsWith('/expressions/scope')
          ? new Response(JSON.stringify({ type: 'text', fields: [], functions: [] }), { status: 200 })
          : new Response(JSON.stringify({ type: null, diagnostics: [] }), { status: 200 }),
      ),
    )
    function Harness() {
      const [value, setValue] = useState<Record<string, unknown>>(content)
      const latest = useRef(value)
      useLayoutEffect(() => {
        latest.current = value
      })
      const check = useMemo(
        () => ({
          application: 'Shop',
          resourceId: 'b5a5f1cb',
          kind: 'entity',
          draft: () => latest.current,
          names: null,
        }),
        [],
      )
      const onChange = useCallback(
        (pointer: string, next: unknown) =>
          setValue((current) => setAt(current, pointer, next) as Record<string, unknown>),
        [],
      )
      return (
        <ExpressionCheckContext.Provider value={check}>
          <EntityDesigner value={value} onChange={onChange} application="Shop" />
          <output data-testid="entity-content">{JSON.stringify(value)}</output>
        </ExpressionCheckContext.Provider>
      )
    }
    render(
      <Providers loaded={null}>
        <Harness />
      </Providers>,
    )
  }

  /**
   * Waits until `editors` expression editors are drawn and have both answers, then forgets the draws so far. An editor
   * shows the type its place needs only once its scope has loaded, and the check answers with no type of its own. The
   * table draws none, so they are the three validations and the editors of the field whose drawer is open.
   */
  async function settled(editors: number) {
    await waitFor(() => {
      expect(document.querySelectorAll('.cm-editor')).toHaveLength(editors)
      expect(screen.getAllByTestId('studio-expression-type')).toHaveLength(editors)
      for (const editor of document.querySelectorAll('.cm-editor')) {
        expect(editor.closest('[aria-busy]')).toHaveAttribute('aria-busy', 'false')
      }
    })
    draws.length = 0
  }

  const saved = () => JSON.parse(screen.getByTestId('entity-content').textContent ?? '') as Record<string, any>

  beforeEach(() => {
    draws.length = 0
  })

  it('draws no other field row and no validation when a key goes into a field label in the drawer', async () => {
    renderStable()
    await settled(3)
    const drawer = await openEntityField(1)
    await settled(4)

    await userEvent.type(within(within(drawer).getByTestId('studio-field-/fields/1/label')).getByRole('textbox'), 'x')

    expect(saved().fields[1].label).toEqual({ textKey: 'x' })
    expect(draws.filter((pointer) => !pointer.startsWith('/fields/1/'))).toEqual([])
  })

  it('draws no other validation and no field row when a key goes into a validation expression', async () => {
    renderStable()
    await settled(3)

    const editor = screen.getByTestId('studio-field-/validations/0/expression').querySelector('.cm-editor')!
    const view = EditorView.findFromDOM(editor as HTMLElement)!
    act(() => view.dispatch({ changes: { from: view.state.doc.length, insert: 'x' } }))

    await waitFor(() => expect(saved().validations[0].expression).toBe("a = '0'x"))
    expect(draws.filter((pointer) => pointer !== '/validations/0/expression')).toEqual([])
  })
})
