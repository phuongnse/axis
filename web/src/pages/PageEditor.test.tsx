import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
    { id: 'g1', name: 'ReviewList', parameters: [{ name: 'request', type: 'reference', required: true }] },
    { id: 'g2', name: 'ReviewForm', parameters: [] },
    { id: 'g3', name: 'EditPage', parameters: [] },
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

describe('PageEditor delete actions', () => {
  const deleteAction = { type: 'delete', label: { textKey: 'notes.delete' } }

  it('shows a delete row action as JSON only and still removes it', async () => {
    const onChange = renderEditor(page({ ...table, rowActions: [deleteAction] }))

    const action = within(screen.getByTestId('studio-action-0'))
    expect(action.getByText('delete')).toBeVisible()
    expect(
      screen.getByTestId('studio-field-/widgets/0/rowActions/0').querySelector('[data-testid="studio-json-only"]'),
    ).not.toBeNull()
    expect(screen.queryByTestId('studio-field-/widgets/0/rowActions/0/process')).not.toBeInTheDocument()

    await userEvent.click(action.getByRole('button', { name: 'Remove action' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/rowActions/0', undefined)
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

  it('types the parameter name and writes its expression when the page is not in the names', async () => {
    const saved = renderStateful(page({ ...table, rowActions: [{ ...navigate, page: 'Unsaved' }] }))

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

  const toReviewList = { ...navigate, page: 'ReviewList' }

  function firstAction(saved: () => Record<string, unknown>) {
    return ((saved().widgets as Record<string, unknown>[])[0].rowActions as Record<string, unknown>[])[0]
  }

  it('shows one expression for each parameter of the chosen page', () => {
    renderEditor(page({ ...table, rowActions: [toReviewList] }))

    const row = screen.getByTestId(`${rows}/0/parameters/request`)
    expect(row).toHaveTextContent('request · reference · required')
    expect(within(row).getByRole('textbox', { name: 'Expression' })).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Add mapping' })).not.toBeInTheDocument()
  })

  it('shows no parameter row for a page that declares none', () => {
    renderEditor(page({ ...table, rowActions: [navigate] }))

    expect(within(screen.getByTestId(`${rows}/0/parameters`)).queryAllByRole('textbox')).toHaveLength(0)
  })

  it('writes the expression of a declared parameter and removes the key when it is cleared', async () => {
    const saved = renderStateful(page({ ...table, rowActions: [toReviewList] }))
    const box = () => within(screen.getByTestId(`${rows}/0/parameters/request`)).getByRole('textbox')

    await userEvent.type(box(), 'id')
    expect(firstAction(saved).parameters).toEqual({ request: 'id' })

    await userEvent.clear(box())
    expect(firstAction(saved).parameters).toEqual({})
  })

  it('removes the parameter values the new page does not declare when the page changes', async () => {
    const saved = renderStateful(
      page({ ...table, rowActions: [{ ...toReviewList, parameters: { request: 'id', Gone: 'id' } }] }),
    )

    await chooseOption('ReviewForm', screen.getByTestId(`${rows}/0/page`))

    const action = firstAction(saved)
    expect(action.page).toBe('ReviewForm')
    expect('parameters' in action).toBe(false)
  })

  it('keeps the parameter values the new page declares, ignoring letter case', async () => {
    const saved = renderStateful(
      page({
        ...table,
        rowActions: [{ ...navigate, parameters: { Request: 'id', other: 'id' } }],
      }),
    )

    await chooseOption('ReviewList', screen.getByTestId(`${rows}/0/page`))

    expect(firstAction(saved).parameters).toEqual({ Request: 'id' })
  })

  it('shows a value the page does not declare with a note and removes it on request', async () => {
    const saved = renderStateful(
      page({ ...table, rowActions: [{ ...toReviewList, parameters: { request: 'id', extra: 'id' } }] }),
    )

    const row = screen.getByTestId(`${rows}/0/parameters/extra`)
    expect(row).toHaveTextContent('Not a parameter of this page')
    expect(within(row).getByRole('textbox', { name: 'Expression' })).toHaveValue('id')
    await userEvent.click(within(row).getByRole('button', { name: 'Remove' }))

    expect(firstAction(saved).parameters).toEqual({ request: 'id' })
  })

  it('removes an action by its index', async () => {
    const onChange = renderEditor(page({ ...table, rowActions: [navigate] }))

    await userEvent.click(within(screen.getByTestId('studio-action-0')).getByRole('button', { name: 'Remove action' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/rowActions/0', undefined)
  })
})

describe('PageEditor parameters', () => {
  const parameters = 'studio-field-/parameters'

  it('lists the parameters the page declares', () => {
    renderEditor(
      page(table, { parameters: [{ name: 'request', type: 'reference', target: 'Supplier', required: true }] }),
    )

    const item = screen.getByTestId(`${parameters}/0`)
    expect(within(item).getByRole('textbox', { name: 'Parameter name' })).toHaveValue('request')
    expect(within(item).getByRole('combobox', { name: 'Type' })).toBeInTheDocument()
    expect(within(item).getByRole('switch', { name: 'Required' })).toBeChecked()
    expect(screen.getByTestId(`${parameters}/0/target`)).toHaveTextContent('Supplier')
  })

  it('adds a parameter with a name and the text type', async () => {
    const onChange = renderEditor(page(table))

    await userEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
    expect(onChange).toHaveBeenLastCalledWith('/parameters', [{ name: '', type: 'text' }])
  })

  it('writes a required reference parameter with its target', async () => {
    let draft: Record<string, unknown> = page(table)
    function Harness() {
      const [value, setValue] = useState(draft)
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

    await userEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Parameter name' }), 'request')
    await chooseOption('reference', screen.getByTestId(`${parameters}/0/type`))
    await chooseOption('Supplier', screen.getByTestId(`${parameters}/0/target`))
    await userEvent.click(screen.getByRole('switch', { name: 'Required' }))

    expect(draft.parameters).toEqual([{ name: 'request', type: 'reference', target: 'Supplier', required: true }])

    await userEvent.click(screen.getByRole('switch', { name: 'Required' }))
    expect(draft.parameters).toEqual([{ name: 'request', type: 'reference', target: 'Supplier' }])

    await chooseOption('text', screen.getByTestId(`${parameters}/0/type`))
    expect(draft.parameters).toEqual([{ name: 'request', type: 'text' }])
  })

  it('removes the target when the type changes from reference', async () => {
    const onChange = renderEditor(
      page(table, { parameters: [{ name: 'request', type: 'reference', target: 'Supplier' }] }),
    )

    await chooseOption('text', screen.getByTestId(`${parameters}/0/type`))
    expect(onChange).toHaveBeenCalledWith('/parameters/0/type', 'text')
    expect(onChange).toHaveBeenLastCalledWith('/parameters/0/target', undefined)
  })

  it('removes the values when the type changes from enum', async () => {
    const onChange = renderEditor(
      page(table, { parameters: [{ name: 'state', type: 'enum', values: ['open', 'closed'] }] }),
    )

    expect(screen.getByTestId(`${parameters}/0/values`)).toHaveTextContent('open')
    await chooseOption('integer', screen.getByTestId(`${parameters}/0/type`))
    expect(onChange).toHaveBeenLastCalledWith('/parameters/0/values', undefined)
  })

  it('removes a parameter', async () => {
    const onChange = renderEditor(page(table, { parameters: [{ name: 'request', type: 'text' }] }))

    await userEvent.click(screen.getByRole('button', { name: 'Remove parameter' }))
    expect(onChange).toHaveBeenLastCalledWith('/parameters/0', undefined)
  })

  it('points to the JSON view when the parameters are not a list', () => {
    renderEditor(page(table, { parameters: 'broken' }))

    expect(within(screen.getByTestId(parameters)).getByTestId('studio-json-only')).toBeVisible()
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

  it('shows the type of a container as fixed text and gives it a panel', async () => {
    renderEditor(tree())

    await userEvent.click(screen.getByTestId('studio-widget-/widgets/0'))
    const panel = screen.getByTestId('studio-field-/widgets/0')
    expect(within(panel).getByTestId('studio-field-/widgets/0/type')).toHaveTextContent('columns')
    expect(within(panel).queryByRole('combobox', { name: 'Widget type' })).not.toBeInTheDocument()
    expect(within(panel).queryByTestId('studio-json-only')).not.toBeInTheDocument()
    expect(within(panel).getByTestId('studio-field-/widgets/0/columns/0/width')).toBeVisible()
  })

  it('points to the JSON view when the parts of a container are malformed', async () => {
    renderEditor(page(columns, { widgets: [{ type: 'columns', columns: 'oops' }] }))

    await userEvent.click(screen.getByTestId('studio-widget-/widgets/0'))
    const panel = screen.getByTestId('studio-field-/widgets/0')
    expect(within(panel).getByTestId('studio-field-/widgets/0/type')).toHaveTextContent('columns')
    expect(within(panel).getByTestId('studio-json-only')).toBeVisible()
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

describe('PageEditor container panels', () => {
  const inbox = { type: 'taskInbox' }
  const columns = {
    type: 'columns',
    columns: [
      { width: 6, widgets: [inbox] },
      { width: 6, widgets: [inbox], 'x-note': 'keep' },
    ],
  }
  const tabs = {
    type: 'tabs',
    tabs: [
      { label: { textKey: 'tabs.one' }, widgets: [inbox] },
      { label: { textKey: 'tabs.two' }, widgets: [inbox], 'x-note': 'keep' },
    ],
  }
  const section = { type: 'section', title: { textKey: 'pages.review.title' }, widgets: [inbox], 'x-note': 'keep' }

  async function open(widget: Record<string, unknown>, onChange = vi.fn(), loaded: StudioNames | null = names) {
    renderEditor(page(widget), onChange, loaded)
    await userEvent.click(screen.getByTestId('studio-widget-/widgets/0'))
    return onChange
  }

  afterEach(() => vi.unstubAllGlobals())

  it('edits the width of a column', async () => {
    const onChange = await open(columns)

    const width = within(screen.getByTestId('studio-field-/widgets/0/columns/1/width')).getByRole('spinbutton')
    expect(width).toHaveValue('6')
    await userEvent.clear(width)
    await userEvent.type(width, '4')

    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/columns/1/width', 4)
  })

  it('adds a column with the rest of 12 and removes a column, keeping the other columns as they are', async () => {
    const onChange = await open({
      ...columns,
      columns: [columns.columns[0], columns.columns[1]].map((c) => ({ ...c, width: 4 })),
    })

    await userEvent.click(screen.getByRole('button', { name: 'Add column' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/columns', [
      { width: 4, widgets: [inbox] },
      { width: 4, widgets: [inbox], 'x-note': 'keep' },
      { width: 4, widgets: [] },
    ])

    await userEvent.click(within(screen.getByTestId('studio-column-0')).getByRole('button', { name: 'Remove column' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/columns', [{ width: 4, widgets: [inbox], 'x-note': 'keep' }])
  })

  it('adds a column of width 1 when the widths use all of 12', async () => {
    const onChange = await open(columns)

    await userEvent.click(screen.getByRole('button', { name: 'Add column' }))

    expect(onChange.mock.lastCall![1]).toEqual([columns.columns[0], columns.columns[1], { width: 1, widgets: [] }])
  })

  it('disables Remove column on the last column', async () => {
    await open({ type: 'columns', columns: [{ width: 12, widgets: [inbox] }] })

    expect(screen.getByRole('button', { name: 'Remove column' })).toBeDisabled()
  })

  it('writes the texts of a tab label in every locale and puts its key in the draft', async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify({ resources: [], diagnostics: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    )
    vi.stubGlobal('fetch', fetchMock)
    const onChange = await open(tabs)

    const label = within(screen.getByTestId('studio-field-/widgets/0/tabs/1/label'))
    await userEvent.type(label.getByRole('textbox', { name: 'en' }), 'Second')
    await userEvent.type(label.getByRole('textbox', { name: 'vi' }), 'Thứ hai')
    await userEvent.click(label.getByRole('button', { name: 'Write texts' }))

    await waitFor(() => expect(onChange).toHaveBeenCalled())
    expect(onChange).toHaveBeenCalledWith('/widgets/0/tabs/1/label', { textKey: 'tabs.two' })
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(decodeURIComponent(url)).toContain('/texts/tabs.two')
    expect(JSON.parse(init.body as string)).toEqual({ texts: { en: 'Second', vi: 'Thứ hai' } })
  })

  it('writes the key of a tab label when the names are not loaded', async () => {
    const onChange = await open(tabs, vi.fn(), null)

    const key = within(screen.getByTestId('studio-field-/widgets/0/tabs/1/label')).getByRole('textbox')
    await userEvent.type(key, 'x')

    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/tabs/1/label/textKey', 'tabs.twox')
  })

  it('adds a tab with an empty label and removes a tab, and disables Remove tab on the last one', async () => {
    const onChange = await open(tabs)

    await userEvent.click(screen.getByRole('button', { name: 'Add tab' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/tabs', [
      tabs.tabs[0],
      tabs.tabs[1],
      { label: { textKey: '' }, widgets: [] },
    ])

    await userEvent.click(within(screen.getByTestId('studio-tab-0')).getByRole('button', { name: 'Remove tab' }))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/tabs', [tabs.tabs[1]])
  })

  it('disables Remove tab on the last tab', async () => {
    await open({ type: 'tabs', tabs: [tabs.tabs[0]] })

    expect(screen.getByRole('button', { name: 'Remove tab' })).toBeDisabled()
  })

  it('edits the title of a section', async () => {
    const onChange = await open(section, vi.fn(), null)

    const key = within(screen.getByTestId('studio-field-/widgets/0/title')).getByRole('textbox')
    await userEvent.type(key, 'x')

    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/title/textKey', 'pages.review.titlex')
  })

  it('shows the title of a section with a text box per locale', async () => {
    await open(section)

    const title = within(screen.getByTestId('studio-field-/widgets/0/title'))
    expect(title.getByRole('textbox', { name: 'en' })).toHaveValue('Review')
    expect(title.getByRole('textbox', { name: 'vi' })).toHaveValue('Xem xét')
  })

  it('switches Collapsed on by writing true', async () => {
    const onChange = await open(section)

    const collapsed = within(screen.getByTestId('studio-field-/widgets/0/collapsed')).getByRole('switch')
    expect(collapsed).not.toBeChecked()
    await userEvent.click(collapsed)
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/collapsed', true)
  })

  it('switches Collapsed off by removing the key', async () => {
    const onChange = await open({ ...section, collapsed: true })

    const collapsed = within(screen.getByTestId('studio-field-/widgets/0/collapsed')).getByRole('switch')
    expect(collapsed).toBeChecked()
    await userEvent.click(collapsed)
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/collapsed', undefined)
  })

  it('never writes a whole part, so a key the panels do not show stays', async () => {
    const onChange = await open(columns)
    const width = within(screen.getByTestId('studio-field-/widgets/0/columns/1/width')).getByRole('spinbutton')
    await userEvent.clear(width)
    await userEvent.type(width, '4')
    await userEvent.click(screen.getByRole('button', { name: 'Add column' }))
    await userEvent.click(within(screen.getByTestId('studio-column-0')).getByRole('button', { name: 'Remove column' }))

    const pointers = onChange.mock.calls.map(([pointer]) => pointer)
    expect(pointers.every((pointer) => pointer.startsWith('/widgets/0/columns'))).toBe(true)
    const lists = onChange.mock.calls.filter(([pointer]) => pointer === '/widgets/0/columns')
    expect(lists).toHaveLength(2)
    for (const [, list] of lists) {
      expect(list).toContainEqual(columns.columns[1])
    }
  })

  it('keeps a key it does not show on a tab and on a section through an edit', async () => {
    const tabChange = await open(tabs)
    await userEvent.click(screen.getByRole('button', { name: 'Add tab' }))
    expect(tabChange.mock.lastCall![1]).toContainEqual(tabs.tabs[1])
    await userEvent.click(within(screen.getByTestId('studio-tab-0')).getByRole('button', { name: 'Remove tab' }))
    expect(tabChange.mock.lastCall![1]).toContainEqual(tabs.tabs[1])
    cleanup()

    // An edit of a section is a write at one key of it, so the other keys stay in the saved file.
    const sectionChange = await open(section)
    const key = within(screen.getByTestId('studio-field-/widgets/0/title')).getByRole('textbox', { name: 'en' })
    await userEvent.type(key, '!')
    await userEvent.click(within(screen.getByTestId('studio-field-/widgets/0/collapsed')).getByRole('switch'))
    const pointers = sectionChange.mock.calls.map(([pointer]) => pointer)
    expect(pointers.every((pointer) => pointer.startsWith('/widgets/0/') && pointer !== '/widgets/0')).toBe(true)
    expect(pointers).toContain('/widgets/0/collapsed')
  })

  it('writes the texts of a section title and puts its key in the draft', async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify({ resources: [], diagnostics: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    )
    vi.stubGlobal('fetch', fetchMock)
    const onChange = await open(section)

    const title = within(screen.getByTestId('studio-field-/widgets/0/title'))
    await userEvent.type(title.getByRole('textbox', { name: 'en' }), '!')
    await userEvent.click(title.getByRole('button', { name: 'Write texts' }))

    await waitFor(() => expect(onChange).toHaveBeenCalledWith('/widgets/0/title', { textKey: 'pages.review.title' }))
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(decodeURIComponent(url)).toContain('/texts/pages.review.title')
    expect(JSON.parse(init.body as string)).toEqual({ texts: { en: 'Review!', vi: 'Xem xét' } })
  })

  it('writes only the collapsed key on a section with a key it does not show', async () => {
    const onChange = await open(section)

    await userEvent.click(within(screen.getByTestId('studio-field-/widgets/0/collapsed')).getByRole('switch'))

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('/widgets/0/collapsed', true)
  })

  it('shows a malformed part as JSON only and keeps its Remove button', async () => {
    const onChange = await open({ type: 'columns', columns: [{ width: 6, widgets: [inbox] }, 'oops'] })

    const part = within(screen.getByTestId('studio-column-1'))
    expect(part.getByTestId('studio-json-only')).toBeVisible()
    const remove = part.getByRole('button', { name: 'Remove column' })
    expect(remove).toBeEnabled()
    await userEvent.click(remove)
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/columns', [{ width: 6, widgets: [inbox] }])
  })
})
