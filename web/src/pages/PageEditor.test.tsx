import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, clearButton, clearSelect, optionTexts, shownDropdown } from '../test/antd'
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

const table = { type: 'table', dataSource: 'PurchaseRequestList', formPage: 'ReviewForm' }
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
    expect(screen.getByTestId('studio-widget-0')).toBeVisible()
    expect(screen.getByTestId('studio-widget-0')).toHaveTextContent('form · PurchaseRequest')
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
    expect(screen.queryByTestId('studio-widget-0')).not.toBeInTheDocument()
  })

  it('shows the type of an unknown widget and points to the JSON view', () => {
    renderEditor(page({ type: 'chart', series: [] }))

    expect(screen.getByTestId('studio-widget-0')).toHaveTextContent('chart')
    const panel = screen.getByTestId('studio-field-/widgets/0')
    expect(panel.querySelector(':scope > [data-testid="studio-json-only"]')).not.toBeNull()
    expect(screen.queryByRole('combobox', { name: 'Widget type' })).not.toBeInTheDocument()
  })
})

describe('PageEditor table panel', () => {
  it('shows the entity, data source and form page', () => {
    renderEditor(page({ ...table, entity: undefined }))

    expect(screen.getByTestId('studio-field-/widgets/0/entity')).toBeVisible()
    expect(screen.getByTestId('studio-field-/widgets/0/dataSource')).toHaveTextContent('PurchaseRequestList')
    expect(screen.getByTestId('studio-field-/widgets/0/formPage')).toHaveTextContent('ReviewForm')
    expect(screen.queryByTestId('studio-field-/widgets/0/form')).not.toBeInTheDocument()
    expect(screen.queryByTestId('studio-field-/widgets/0/process')).not.toBeInTheDocument()
  })

  it('edits the data source and the form page at their pointers', async () => {
    const onChange = renderEditor(page(table))

    await chooseOption('SupplierList', screen.getByTestId('studio-field-/widgets/0/dataSource'))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/dataSource', 'SupplierList')
    await chooseOption('EditPage', screen.getByTestId('studio-field-/widgets/0/formPage'))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/formPage', 'EditPage')
  })

  it('clears an optional picker by removing its key', async () => {
    const onChange = renderEditor(page(table))

    await clearSelect(screen.getByTestId('studio-field-/widgets/0/formPage'))
    expect(onChange).toHaveBeenLastCalledWith('/widgets/0/formPage', undefined)
  })

  it('leaves the page itself out of the form pages', async () => {
    renderEditor(page(table))

    await userEvent.click(within(screen.getByTestId('studio-field-/widgets/0/formPage')).getByRole('combobox'))
    await waitFor(() => expect(optionTexts(shownDropdown()!)).toEqual(['ReviewForm', 'EditPage']))
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

describe('PageEditor taskInbox panel', () => {
  it('has the type and no other property', () => {
    renderEditor(page({ type: 'taskInbox' }))

    expect(screen.getByRole('combobox', { name: 'Widget type' })).toBeInTheDocument()
    for (const key of ['entity', 'dataSource', 'form', 'formPage', 'process', 'actions']) {
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
