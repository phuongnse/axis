import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { type ReactNode, useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { setAt } from '../platform/jsonPatch'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, clearButton, optionTexts, selectRoot, shownDropdown } from '../test/antd'
import { LabelTextsContext } from './labelTextsContext'
import { PageEditor } from './PageEditor'

const names = {
  locales: ['en', 'vi'],
  entities: [
    { id: 'e1', name: 'PurchaseRequest', module: null, fields: [] },
    { id: 'e2', name: 'Supplier', module: null, fields: [] },
  ],
  dataSources: [
    { id: 'd1', name: 'PurchaseRequestList', module: null },
    { id: 'd2', name: 'SupplierList', module: null },
  ],
  processes: [
    { id: 'p1', name: 'PurchaseRequestApproval', steps: [] },
    { id: 'p2', name: 'Onboarding', steps: [] },
  ],
  rules: [],
  connectors: [],
  events: [],
  roles: [],
  sequences: [],
  forms: [
    { id: 'f1', name: 'RequestForm' },
    { id: 'f2', name: 'SupplierForm' },
  ],
  pages: [
    { id: 'g1', name: 'ReviewList' },
    { id: 'g2', name: 'ReviewForm' },
    { id: 'g3', name: 'EditPage' },
  ],
  texts: [{ key: 'pages.review.title', texts: { en: 'Review', vi: 'Xem xét' }, usedBy: 1 }],
} as unknown as StudioNames

const labelContext = {
  application: 'Shop',
  file: 'pages/review-list.json',
  name: 'ReviewList',
  saved: {},
  onTextsWritten: vi.fn(),
}

function page(widget: Record<string, unknown>, extra: Record<string, unknown> = {}) {
  return {
    id: 'b5a5f1cb-4f0f-4f3d-9a54-9d6e3a1b7f10',
    kind: 'page',
    name: 'ReviewList',
    formatVersion: 1,
    title: { textKey: 'pages.review.title' },
    widgets: [widget],
    ...extra,
  }
}

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderEditor(value: Record<string, unknown>, onChange = vi.fn(), loaded: StudioNames | null = names) {
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={loaded ?? undefined}>
            <LabelTextsContext.Provider value={labelContext}>
              <PageEditor value={value} onChange={onChange} />
            </LabelTextsContext.Provider>
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

/**
 * Presses Enter the way a browser does. The Select reads `which`, which user-event leaves at 0, so
 * `userEvent.keyboard('{Enter}')` would not pick the active option.
 */
async function pressEnter(target: HTMLElement) {
  await act(async () => {
    fireEvent.keyDown(target, { key: 'Enter', code: 'Enter', keyCode: 13, which: 13 })
    fireEvent.keyUp(target, { key: 'Enter', code: 'Enter', keyCode: 13, which: 13 })
  })
}

const table = { type: 'table', dataSource: 'PurchaseRequestList' }
const form = {
  type: 'form',
  entity: 'PurchaseRequest',
  actions: [{ type: 'startProcess', process: 'PurchaseRequestApproval', label: { textKey: 'pages.review.title' } }],
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('PageEditor in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with its fields visible', () => {
    renderEditor(page(form))

    const editor = screen.getByTestId('studio-page-editor')
    expect(editor.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(editor).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Name' })).toBeVisible()
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('ReviewList')
    expect(screen.getByTestId('studio-field-/title')).toBeVisible()
    expect(screen.getAllByRole('textbox', { name: 'Text key' })[0]).toBeVisible()
    expect(screen.getByTestId('studio-widget-/widgets/0')).toBeVisible()
    expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('form · PurchaseRequest')
    expect(screen.getByTestId('studio-field-/widgets/0/type')).toBeVisible()
    expect(screen.getByRole('combobox', { name: 'Widget type' })).toBeInTheDocument()
    expect(screen.getByTestId('studio-field-/widgets/0/entity')).toBeVisible()
    expect(screen.getByTestId('studio-action-0')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Add action' })).toBeVisible()
  })
})

describe('PageEditor page', () => {
  it('edits the name at its pointer', async () => {
    const onChange = renderEditor(page(table))

    await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'X')
    expect(onChange).toHaveBeenLastCalledWith('/name', 'ReviewListX')
  })

  it('shows the title with a box for each locale', () => {
    renderEditor(page(table))

    const title = within(screen.getByTestId('studio-field-/title'))
    expect(title.getByRole('textbox', { name: 'Text key' })).toHaveValue('pages.review.title')
    expect(title.getByRole('textbox', { name: 'en' })).toHaveValue('Review')
    expect(title.getByRole('textbox', { name: 'vi' })).toHaveValue('Xem xét')
  })

  it('falls back to a plain key box when the names have not loaded', async () => {
    const onChange = renderEditor(page(table), vi.fn(), null)

    const key = within(screen.getByTestId('studio-field-/title/textKey')).getByRole('textbox')
    await userEvent.type(key, 'X')
    expect(onChange).toHaveBeenLastCalledWith('/title/textKey', 'pages.review.titleX')
    await userEvent.type(within(screen.getByTestId('studio-field-/widgets/0/entity')).getByRole('textbox'), 'S')
    expect(onChange).toHaveBeenCalledWith('/widgets/0/entity', 'S')
  })

  it('points to the JSON view when widgets is not a list', () => {
    renderEditor(page(table, { widgets: { type: 'table' } }))

    expect(
      screen.getByTestId('studio-field-/widgets').querySelector(':scope > * > [data-testid="studio-json-only"]'),
    ).not.toBeNull()
    expect(screen.queryByTestId('studio-widget-/widgets/0')).not.toBeInTheDocument()
  })

  it('shows the type of an unknown widget and points to the JSON view', () => {
    renderEditor(page({ type: 'chart', series: [] }))

    expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('chart')
    const panel = screen.getByTestId('studio-field-/widgets/0')
    expect(panel.querySelector(':scope > [data-testid="studio-json-only"]')).not.toBeNull()
    expect(screen.queryByRole('combobox', { name: 'Widget type' })).not.toBeInTheDocument()
  })
})

describe('PageEditor table panel', () => {
  it('shows the entity and the data source', () => {
    renderEditor(page({ ...table, entity: undefined }))

    expect(screen.getByTestId('studio-field-/widgets/0/entity')).toBeVisible()
    expect(screen.getByTestId('studio-field-/widgets/0/dataSource')).toHaveTextContent('PurchaseRequestList')
    expect(screen.queryByTestId('studio-field-/widgets/0/form')).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-field-/widgets/0/process')).not.toBeInTheDocument()
  })

  it('edits the data source at its pointer', async () => {
    const onChange = renderEditor(page(table))

    await chooseOption('SupplierList', screen.getByTestId('studio-field-/widgets/0/dataSource'))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/dataSource', 'SupplierList')
  })

  it('picking an entity clears the data source, and picking a data source clears the entity', async () => {
    const onChange = renderEditor(page({ type: 'table', dataSource: 'PurchaseRequestList' }))

    await chooseOption('Supplier', screen.getByTestId('studio-field-/widgets/0/entity'))
    expect(onChange.mock.calls).toEqual([
      ['/widgets/0/entity', 'Supplier'],
      ['/widgets/0/dataSource', undefined],
    ])
  })

  it('clears the entity when a data source is picked', async () => {
    const onChange = renderEditor(page({ type: 'table', entity: 'PurchaseRequest' }))

    await chooseOption('SupplierList', screen.getByTestId('studio-field-/widgets/0/dataSource'))
    expect(onChange.mock.calls).toEqual([
      ['/widgets/0/dataSource', 'SupplierList'],
      ['/widgets/0/entity', undefined],
    ])
  })
})

describe('PageEditor form panel', () => {
  it('shows the entity, the form and the actions', () => {
    renderEditor(page(form))

    expect(screen.getByTestId('studio-field-/widgets/0/entity')).toHaveTextContent('PurchaseRequest')
    expect(screen.getByTestId('studio-field-/widgets/0/form')).toBeVisible()
    expect(screen.getByTestId('studio-field-/widgets/0/actions')).toBeVisible()
    expect(screen.queryByTestId('studio-field-/widgets/0/dataSource')).not.toBeInTheDocument()
    const action = within(screen.getByTestId('studio-action-0'))
    expect(action.getByText('startProcess')).toBeVisible()
    expect(screen.getByTestId('studio-field-/widgets/0/actions/0/process')).toHaveTextContent('PurchaseRequestApproval')
    expect(
      within(screen.getByTestId('studio-field-/widgets/0/actions/0/label')).getByRole('textbox', { name: 'en' }),
    ).toHaveValue('Review')
  })

  it('edits the entity and the form at their pointers, each clearing the other', async () => {
    const onChange = renderEditor(page({ type: 'form', entity: 'PurchaseRequest' }))

    await chooseOption('RequestForm', screen.getByTestId('studio-field-/widgets/0/form'))
    expect(onChange.mock.calls).toEqual([
      ['/widgets/0/form', 'RequestForm'],
      ['/widgets/0/entity', undefined],
    ])
  })

  it('picks an entity without touching the form when the form is not set', async () => {
    const onChange = renderEditor(page({ type: 'form', entity: 'PurchaseRequest' }))

    await chooseOption('Supplier', screen.getByTestId('studio-field-/widgets/0/entity'))
    expect(onChange.mock.calls).toEqual([['/widgets/0/entity', 'Supplier']])
  })

  it('edits the process of an action', async () => {
    const onChange = renderEditor(page(form))

    await chooseOption('Onboarding', screen.getByTestId('studio-field-/widgets/0/actions/0/process'))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/actions/0/process', 'Onboarding')
  })

  it('adds an action after the others and removes one by its index', async () => {
    const onChange = renderEditor(page(form))

    await userEvent.click(screen.getByRole('button', { name: 'Add action' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/actions', [
      ...form.actions,
      { type: 'startProcess', process: '', label: { textKey: '' } },
    ])

    await userEvent.click(within(screen.getByTestId('studio-action-0')).getByRole('button', { name: 'Remove action' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/actions/0', undefined)
  })

  it('adds the first action to a form that has none', async () => {
    const onChange = renderEditor(page({ type: 'form', entity: 'PurchaseRequest' }))

    await userEvent.click(screen.getByRole('button', { name: 'Add action' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/actions', [
      { type: 'startProcess', process: '', label: { textKey: '' } },
    ])
  })

  it('keeps the slot of an action that is not an object and points to the JSON view', () => {
    renderEditor(page({ type: 'form', entity: 'PurchaseRequest', actions: ['oops', form.actions[0]] }))

    expect(screen.getByTestId('studio-action-0').querySelector('[data-testid="studio-json-only"]')).not.toBeNull()
    expect(screen.getByTestId('studio-field-/widgets/0/actions/1/process')).toBeVisible()
  })
})

describe('PageEditor navigate actions', () => {
  /** Renders the editor over a draft that applies every change, so a test can read the JSON as it would be saved. */
  function renderStateful(initial: Record<string, unknown>) {
    let draft = initial
    function Harness() {
      const [value, setValue] = useState(initial)
      return (
        <PageEditor
          value={value}
          onChange={(pointer, next) => {
            draft = setAt(draft, pointer, next) as Record<string, unknown>
            setValue(draft)
          }}
        />
      )
    }
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioNamesContext.Provider value={names}>
            <LabelTextsContext.Provider value={labelContext}>
              <Harness />
            </LabelTextsContext.Provider>
          </StudioNamesContext.Provider>
        </TextProvider>
      </ThemeModeProvider>,
    )
    return () => draft
  }

  const rows = 'studio-field-/widgets/0/rowActions'
  const navigate = { type: 'navigate', page: 'ReviewForm', label: { textKey: 'pages.review.title' } }

  it('shows the row and toolbar actions of a table', () => {
    renderEditor(page(table))

    expect(screen.getByTestId(rows)).toBeVisible()
    expect(screen.getByTestId('studio-field-/widgets/0/toolbarActions')).toBeVisible()
    expect(screen.getAllByRole('button', { name: 'Add navigate action' })).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Add action' })).not.toBeInTheDocument()
  })

  it('saves a row navigate with its page and record', async () => {
    const saved = renderStateful(page(table))

    await userEvent.click(within(screen.getByTestId(rows)).getByRole('button', { name: 'Add navigate action' }))
    await chooseOption('ReviewForm', screen.getByTestId(`${rows}/0/page`))
    await userEvent.type(within(screen.getByTestId(`${rows}/0/record`)).getByRole('textbox'), 'id')

    expect((saved().widgets as unknown[])[0]).toEqual({
      ...table,
      rowActions: [{ type: 'navigate', page: 'ReviewForm', label: { textKey: '' }, record: 'id' }],
    })
  })

  it('adds a navigate action to a form and to a table toolbar', async () => {
    const onChange = renderEditor(page(form))
    await userEvent.click(screen.getByRole('button', { name: 'Add navigate action' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/actions', [
      ...form.actions,
      { type: 'navigate', page: '', label: { textKey: '' } },
    ])
  })

  it('adds a navigate action to the toolbar of a table', async () => {
    const onChange = renderEditor(page(table))

    await userEvent.click(
      within(screen.getByTestId('studio-field-/widgets/0/toolbarActions')).getByRole('button', {
        name: 'Add navigate action',
      }),
    )
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/toolbarActions', [
      { type: 'navigate', page: '', label: { textKey: '' } },
    ])
  })

  it('lists every page, including the current one, as a target', async () => {
    renderEditor(page({ ...table, rowActions: [navigate] }))

    await userEvent.click(within(screen.getByTestId(`${rows}/0/page`)).getByRole('combobox'))
    await waitFor(() => expect(optionTexts(shownDropdown()!)).toEqual(['ReviewList', 'ReviewForm', 'EditPage']))
  })

  it('removes the record key when the box is cleared', async () => {
    const saved = renderStateful(page({ ...table, rowActions: [{ ...navigate, record: 'id' }] }))

    expect(within(screen.getByTestId(`${rows}/0/record`)).getByRole('textbox')).toHaveValue('id')
    await userEvent.clear(within(screen.getByTestId(`${rows}/0/record`)).getByRole('textbox'))

    const action = ((saved().widgets as Record<string, unknown>[])[0].rowActions as Record<string, unknown>[])[0]
    expect(action).toEqual(navigate)
    expect('record' in action).toBe(false)
  })

  it('writes the expression of a parameter at its name', async () => {
    const saved = renderStateful(page({ ...table, rowActions: [navigate] }))

    await userEvent.click(
      within(screen.getByTestId(`${rows}/0/parameters`)).getByRole('button', { name: 'Add mapping' }),
    )
    await userEvent.type(
      within(screen.getByTestId(`${rows}/0/parameters/`)).getByRole('textbox', { name: 'Parameter' }),
      'item',
    )
    await userEvent.type(
      within(screen.getByTestId(`${rows}/0/parameters/item`)).getByRole('textbox', { name: 'Expression' }),
      'id',
    )

    const action = ((saved().widgets as Record<string, unknown>[])[0].rowActions as Record<string, unknown>[])[0]
    expect(action.parameters).toEqual({ item: 'id' })
  })

  it('removes an action by its index', async () => {
    const onChange = renderEditor(page({ ...table, rowActions: [navigate] }))

    await userEvent.click(within(screen.getByTestId('studio-action-0')).getByRole('button', { name: 'Remove action' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/rowActions/0', undefined)
  })
})

describe('PageEditor taskInbox panel', () => {
  it('has the type and no other property', () => {
    renderEditor(page({ type: 'taskInbox' }))

    expect(screen.getByRole('combobox', { name: 'Widget type' })).toBeInTheDocument()
    for (const key of ['entity', 'dataSource', 'form', 'process', 'actions']) {
      expect(screen.queryByTestId(`studio-field-/widgets/0/${key}`)).not.toBeInTheDocument()
    }
  })
})

describe('PageEditor processInspector panel', () => {
  it('shows the process and edits it at its pointer', async () => {
    const onChange = renderEditor(page({ type: 'processInspector', process: 'PurchaseRequestApproval' }))

    const field = screen.getByTestId('studio-field-/widgets/0/process')
    expect(field).toHaveTextContent('PurchaseRequestApproval')
    await chooseOption('Onboarding', field)
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/process', 'Onboarding')
  })

  it('does not allow clearing the process to nothing', async () => {
    renderEditor(page({ type: 'processInspector', process: 'Onboarding' }))

    await userEvent.hover(within(screen.getByTestId('studio-field-/widgets/0/process')).getByRole('combobox'))
    expect(clearButton()).toBeNull()
  })
})

describe('PageEditor widget type', () => {
  it('drops the known keys the new type does not take and keeps the others', async () => {
    const onChange = renderEditor(page({ ...form, form: 'RequestForm', 'x-note': 'keep' }))

    await chooseOption('table', screen.getByTestId('studio-field-/widgets/0/type'))
    expect(onChange.mock.calls).toEqual([
      ['/widgets/0/type', 'table'],
      ['/widgets/0/form', undefined],
      ['/widgets/0/actions', undefined],
    ])
  })

  it('adds an empty process for a process inspector', async () => {
    const onChange = renderEditor(page({ type: 'taskInbox' }))

    await chooseOption('processInspector', screen.getByTestId('studio-field-/widgets/0/type'))
    expect(onChange.mock.calls).toEqual([
      ['/widgets/0/type', 'processInspector'],
      ['/widgets/0/process', ''],
    ])
  })
})

describe('PageEditor widget tree', () => {
  const noted = { ...form, 'x-note': 'keep' }
  const columns = {
    type: 'columns',
    columns: [
      { width: 6, widgets: [table] },
      { width: 6, widgets: [noted] },
    ],
  }
  const inbox = { type: 'taskInbox' }
  const tree = () => page(columns, { widgets: [columns, inbox] })

  /** The buttons of one row, without those of the rows nested in it. */
  function row(pointer: string) {
    return within(screen.getByTestId(`studio-widget-row-${pointer}`))
  }

  it('shows a nested row for each widget in a container and opens its panel at its pointer', async () => {
    renderEditor(tree())

    expect(screen.getByTestId('studio-widget-/widgets/0')).toHaveTextContent('columns')
    expect(screen.getByTestId('studio-widget-/widgets/0/columns/0/widgets/0')).toHaveTextContent(
      'table · PurchaseRequestList',
    )
    expect(screen.getByTestId('studio-widget-/widgets/1')).toHaveTextContent('taskInbox')
    const nested = screen.getByTestId('studio-widget-/widgets/0/columns/1/widgets/0')
    expect(nested).toHaveTextContent('form · PurchaseRequest')
    expect(screen.queryByTestId('studio-field-/widgets/0/columns/1/widgets/0')).not.toBeInTheDocument()

    await userEvent.click(nested)
    expect(nested).toHaveAttribute('aria-current', 'true')
    expect(screen.getByTestId('studio-field-/widgets/0/columns/1/widgets/0')).toBeVisible()
    expect(screen.getByTestId('studio-field-/widgets/0/columns/1/widgets/0/entity')).toHaveTextContent(
      'PurchaseRequest',
    )
  })

  it('names the columns with their width and the tabs, and shows a section body', () => {
    renderEditor(
      page(columns, {
        widgets: [
          columns,
          { type: 'tabs', tabs: [{ label: { textKey: 'a' }, widgets: [inbox] }] },
          { type: 'section', title: { textKey: 't' }, widgets: [inbox] },
        ],
      }),
    )

    expect(screen.getByText('Column 2 · 6')).toBeVisible()
    expect(screen.getByText('Tab 1')).toBeVisible()
    expect(screen.getByTestId('studio-widget-/widgets/1/tabs/0/widgets/0')).toBeVisible()
    expect(screen.getByTestId('studio-widget-/widgets/2/widgets/0')).toBeVisible()
  })

  it('points to the JSON view when a container has parts the editor cannot show', () => {
    renderEditor(page(columns, { widgets: [{ type: 'columns', columns: 'oops' }, { type: 'section' }] }))

    expect(within(screen.getByTestId('studio-widget-list-/widgets')).getAllByTestId('studio-json-only')).toHaveLength(2)
  })

  it('moves up, moves down and removes in the list that holds the widget', async () => {
    const onChange = renderEditor(tree())

    expect(row('/widgets/0').getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(row('/widgets/1').getByRole('button', { name: 'Move down' })).toBeDisabled()
    expect(row('/widgets/0/columns/0/widgets/0').getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(row('/widgets/0/columns/0/widgets/0').getByRole('button', { name: 'Move down' })).toBeDisabled()

    await userEvent.click(row('/widgets/0').getByRole('button', { name: 'Move down' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [inbox, columns])
    await userEvent.click(row('/widgets/1').getByRole('button', { name: 'Move up' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [inbox, columns])
    await userEvent.click(row('/widgets/1').getByRole('button', { name: 'Remove widget' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [columns])
    await userEvent.click(row('/widgets/0/columns/1/widgets/0').getByRole('button', { name: 'Remove widget' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/columns/1/widgets', [])
  })

  it('reorders the widgets of a list inside a container', async () => {
    const onChange = renderEditor(
      page(columns, { widgets: [{ type: 'section', title: { textKey: '' }, widgets: [table, noted] }] }),
    )

    await userEvent.click(row('/widgets/0/widgets/0').getByRole('button', { name: 'Move down' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/widgets', [noted, table])
  })

  it('moves a widget to another list as one change and keeps the keys it does not know', async () => {
    const onChange = renderEditor(tree())

    const target = 'Page › columns 1 › Column 1'
    await chooseOption(target, selectRoot(row('/widgets/0/columns/1/widgets/0').getByRole('combobox')))
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [
      {
        type: 'columns',
        columns: [
          { width: 6, widgets: [table, noted] },
          { width: 6, widgets: [] },
        ],
      },
      inbox,
    ])
  })

  async function moveToOptions(pointer: string) {
    await userEvent.click(row(pointer).getByRole('combobox', { name: 'Move to' }))
    return waitFor(() => {
      expect(shownDropdown()).not.toBeNull()
      return optionTexts(shownDropdown()!)
    })
  }

  it('offers the other lists of the tree and not the list of the widget', async () => {
    renderEditor(tree())

    expect(await moveToOptions('/widgets/1')).toEqual(['Page › columns 1 › Column 1', 'Page › columns 1 › Column 2'])
  })

  it('leaves out the lists inside the widget it moves', async () => {
    renderEditor(tree())

    expect(await moveToOptions('/widgets/0/columns/0/widgets/0')).toEqual(['Page', 'Page › columns 1 › Column 2'])
  })

  it('offers a container no list when the other lists are inside it', async () => {
    renderEditor(page(columns, { widgets: [columns] }))

    expect(await moveToOptions('/widgets/0')).toEqual([])
  })

  it('adds the default shape of each container at the end of the list', async () => {
    const onChange = renderEditor(page(inbox, { widgets: [inbox] }))
    const adder = () => selectRoot(screen.getAllByRole('combobox', { name: 'Add widget' })[0])

    await chooseOption('columns', adder())
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [
      inbox,
      {
        type: 'columns',
        columns: [
          { width: 6, widgets: [] },
          { width: 6, widgets: [] },
        ],
      },
    ])
    await chooseOption('tabs', adder())
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [
      inbox,
      { type: 'tabs', tabs: [{ label: { textKey: '' }, widgets: [] }] },
    ])
    await chooseOption('section', adder())
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [
      inbox,
      { type: 'section', title: { textKey: '' }, widgets: [] },
    ])
    await chooseOption('processInspector', adder())
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [inbox, { type: 'processInspector', process: '' }])
  })

  it('adds a widget to the list inside a container', async () => {
    const onChange = renderEditor(tree())
    const list = screen.getByTestId('studio-widget-list-/widgets/0/columns/0/widgets')
    const adder = within(list.parentElement!).getByRole('combobox', { name: 'Add widget' })

    await chooseOption('table', selectRoot(adder))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/columns/0/widgets', [table, { type: 'table' }])
  })

  it('moves a top-level widget into a column with the keyboard alone', async () => {
    const onChange = renderEditor(tree())
    const picker = within(screen.getByTestId('studio-widget-row-/widgets/1')).getByRole('combobox', { name: 'Move to' })

    for (let presses = 0; presses < 60 && document.activeElement !== picker; presses++) {
      await userEvent.tab()
    }
    expect(picker).toHaveFocus()
    await userEvent.keyboard('Column 1')
    await pressEnter(picker)
    expect(onChange).toHaveBeenLastCalledWith('/widgets', [
      {
        type: 'columns',
        columns: [
          { width: 6, widgets: [table, inbox] },
          { width: 6, widgets: [noted] },
        ],
      },
    ])
  })

  it('shows the type of a container as fixed text and points to the JSON view', async () => {
    renderEditor(tree())

    await userEvent.click(screen.getByTestId('studio-widget-/widgets/0'))
    const panel = screen.getByTestId('studio-field-/widgets/0')
    expect(within(panel).getByTestId('studio-field-/widgets/0/type')).toHaveTextContent('columns')
    expect(within(panel).queryByRole('combobox', { name: 'Widget type' })).not.toBeInTheDocument()
    expect(panel.querySelector(':scope > [data-testid="studio-json-only"]')).not.toBeNull()
  })

  it('keeps the type picker of a leaf widget to the four types with a panel', async () => {
    renderEditor(tree())

    await userEvent.click(screen.getByTestId('studio-widget-/widgets/1'))
    await userEvent.click(screen.getByRole('combobox', { name: 'Widget type' }))
    await waitFor(() =>
      expect(optionTexts(shownDropdown()!)).toEqual(['table', 'form', 'taskInbox', 'processInspector']),
    )
  })
})
