import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type {
  DataSourceParameter,
  DeleteAction,
  EntityWidgetMetadata,
  FieldMetadata,
  FieldType,
  NavigateAction,
  StartProcessAction,
  WidgetAccess,
  WidgetMetadata,
} from './site'
import { isSortableHeader } from '../test/antd'
import { hostileMarkup } from '../test/hostile'
import { designTokensFor } from './designTokens'
import { TableWidget } from './TableWidget'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { narrowWidthQuery } from './useNarrowWidth'

function field(name: string, type: FieldType, labelKey: string | null): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    min: null,
    max: null,
    minLength: null,
    values: null,
    target: null,
    fields: null,
  }
}

const recordsPath = '/api/apps/E2eApp/entities/Note/records'

// The table only reads the actions, so the field lists stay empty.
const everyAction: WidgetAccess = {
  actions: ['read', 'create', 'update', 'delete'],
  createFields: [],
  updateFields: [],
}

const noteWidget: EntityWidgetMetadata = {
  type: 'table',
  dataSource: null,
  form: null,
  tasksPath: null,
  process: null,
  instancesPath: null,
  deadLettersPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  access: everyAction,
  detail: null,
  kpi: null,
  chart: null,
  entity: {
    name: 'Note',
    labelKey: 'note.label',
    displayField: null,
    recordsPath,
    fields: [
      field('title', 'text', 'note.title'),
      field('done', 'boolean', 'note.done'),
      field('dueAt', 'date-time', 'note.dueAt'),
      {
        ...field('category', 'reference', 'note.category'),
        target: {
          entity: 'Category',
          displayField: 'name',
          recordsPath: '/api/apps/E2eApp/entities/Category/records',
        },
      },
    ],
  },
}

const rowsPath = '/api/apps/E2eApp/data-sources/NoteList/rows'

// A table over a data source whose default page size is 10, which is also offered by default.
const noteListWidget: WidgetMetadata = {
  type: 'table',
  entity: null,
  form: null,
  tasksPath: null,
  process: null,
  instancesPath: null,
  deadLettersPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  access: everyAction,
  detail: null,
  kpi: null,
  chart: null,
  dataSource: {
    name: 'NoteList',
    rowsPath,
    entity: 'Note',
    parameters: [],
    pageSize: 10,
    search: false,
    columns: [
      { name: 'title', type: 'text', labelKey: 'note.title', values: null, target: null },
      { name: 'departmentName', type: 'text', labelKey: null, values: null, target: null },
    ],
  },
}

const categoryTarget = {
  entity: 'Category',
  displayField: 'name',
  recordsPath: '/api/apps/E2eApp/entities/Category/records',
}

function parameter(name: string, type: FieldType, labelKey: string | null): DataSourceParameter {
  return { name, type, required: false, labelKey, values: null, target: null }
}

// One parameter of each type, labelled, but for the date-time one, which shows its name.
const filterParameters: DataSourceParameter[] = [
  parameter('titleFilter', 'text', 'filter.title'),
  parameter('minCount', 'integer', 'filter.minCount'),
  parameter('maxPrice', 'decimal', 'filter.maxPrice'),
  parameter('doneFilter', 'boolean', 'filter.done'),
  parameter('dueFrom', 'date', 'filter.dueFrom'),
  parameter('createdAfter', 'date-time', null),
  { ...parameter('statusFilter', 'enum', 'filter.status'), values: ['open', 'approved'] },
  { ...parameter('categoryFilter', 'reference', 'filter.category'), target: categoryTarget },
]

const filteredWidget: WidgetMetadata = {
  ...noteListWidget,
  dataSource: { ...noteListWidget.dataSource!, parameters: filterParameters },
}

const noteId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'

// Written by hand, as the server writes it, so numbers keep their source text.
const notePage = `{"items":[{"id":"${noteId}","version":1,"values":{"title":"Buy paper","done":false,"dueAt":null,"category":"${categoryId}"},"labels":{"category":"Office"}}],"page":1,"pageSize":20,"totalCount":45}`

const texts = {
  'shell.table.yes': 'Yes',
  'shell.table.no': 'No',
  'shell.table.empty': 'No records yet.',
  'shell.table.loadFailed': 'The records could not be loaded.',
  'shell.table.search': 'Search',
  'shell.table.refresh': 'Refresh',
  'shell.table.rowActions': 'Actions',
  'shell.table.delete': 'Delete',
  'shell.table.deleteConfirm': 'Delete this record? This cannot be undone.',
  'shell.table.deleted': 'The record was deleted.',
  'shell.table.deleteDenied': 'You may not delete this record.',
  'shell.table.deleteMissing': 'This record no longer exists.',
  'shell.table.deleteInUse': 'This record is in use and cannot be deleted.',
  'shell.table.deleteFailed': 'The record could not be deleted.',
  'shell.table.import': 'Import',
  'shell.table.importTitle': 'Import records',
  'shell.table.importChoose': 'Choose a CSV or Excel file',
  'shell.table.importCheck': 'Check',
  'shell.table.importChecking': 'Checking the file…',
  'shell.table.importChecked': 'The file is clean.',
  'shell.table.importFailed': 'The import failed.',
  'shell.table.importRow': 'Row',
  'shell.table.importColumn': 'Column',
  'shell.table.importFile': 'File',
  'shell.table.importCommit': 'Commit',
  'shell.table.importCommitting': 'Creating the records…',
  'shell.table.imported': 'The records were imported.',
  'shell.table.importDenied': 'You may not import records here.',
  'shell.table.importTooLarge': 'The file is too large.',
  'shell.table.importWrongType': 'Only a CSV or Excel file can be imported.',
  'shell.table.importUploadFailed': 'The file could not be imported.',
  'shell.table.importClose': 'Close',
  'shell.table.export': 'Export',
  'shell.table.exportCsv': 'CSV',
  'shell.table.exportXlsx': 'Excel',
  'shell.table.exportTruncated': 'The file stops at the row limit.',
  'shell.table.exportDenied': 'You may not export these records.',
  'shell.table.exportPending': 'A field is being recomputed.',
  'shell.table.exportTimeout': 'The export took too long.',
  'shell.table.exportFailed': 'The records could not be exported.',
  'shell.navigate.failed': 'The page could not be opened.',
  'shell.form.cancel': 'Cancel',
  'note.delete': 'Delete note',
  'note.title': 'Title',
  'note.done': 'Done',
  'note.dueAt': 'Due at',
  'note.category': 'Category',
  'note.receipt': 'Receipt',
  'note.lines': 'Lines',
  'note.software': 'Software',
  'note.details': 'Details',
  'note.edit': 'Edit',
  'note.add': 'Add note',
  'note.close': 'Close note',
  'shell.form.processRunning': 'This record is already in a running process.',
  'shell.form.startFailed': 'The process could not be started.',
  'shell.form.choose': 'Choose',
  'shell.form.clear': 'Clear',
  'filter.title': 'Title contains',
  'filter.minCount': 'Minimum count',
  'filter.maxPrice': 'Maximum price',
  'filter.done': 'Is done',
  'filter.dueFrom': 'Due from',
  'filter.status': 'State',
  'filter.category': 'In category',
}
const catalogs = [{ texts, fallbackTexts: texts }]

/** Stubs `fetch` with the record API and returns the requested URLs. */
function stubRecords(body = notePage, status = 200) {
  const fetchMock = vi.fn(async (_input: string) => new Response(body, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

/** Stubs `fetch` with a handler per URL and returns the requested URLs. */
function stubFetch(handler: (url: string) => Response) {
  const fetchMock = vi.fn(async (input: string) => handler(input))
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

const emptyRows = '{"items":[],"page":1,"pageSize":10,"totalCount":0}'

function CurrentLocation() {
  const location = useLocation()
  const navigate = useNavigate()
  const state = location.state as { from?: string } | null
  return (
    <>
      <output data-testid="path">{location.pathname}</output>
      <output data-testid="search">{location.search}</output>
      <output data-testid="from">{state?.from}</output>
      <button type="button" onClick={() => navigate(-1)}>
        Back
      </button>
    </>
  )
}

function search() {
  return screen.getByTestId('search').textContent
}

function renderWidget(search = '', widget: WidgetMetadata = noteWidget, pageValues?: Record<string, string>) {
  return render(
    <MemoryRouter initialEntries={[`/e2e/notes${search}`]}>
      <TextProvider catalogs={catalogs} development={false}>
        <TableWidget sitePath="e2e" widget={widget} locale="en" pageValues={pageValues} />
      </TextProvider>
      <CurrentLocation />
    </MemoryRouter>,
  )
}

function renderWidgetInDark(widget: WidgetMetadata) {
  return render(
    <ThemeModeProvider initialMode="dark">
      <MemoryRouter initialEntries={['/e2e/notes']}>
        <TextProvider catalogs={catalogs} development={false}>
          <TableWidget sitePath="e2e" widget={widget} locale="en" />
        </TextProvider>
      </MemoryRouter>
    </ThemeModeProvider>,
  )
}

const receipt = {
  ...field('receipt', 'file', 'note.receipt'),
  downloadPath: '/api/apps/E2eApp/files',
  uploadPath: '/api/apps/E2eApp/entities/Note/fields/receipt/files',
}
const receiptWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: { ...noteWidget.entity!, fields: [...noteWidget.entity!.fields, receipt] },
}
const fileId = '3c1d5e7f-9a2b-4c4d-8e6f-0a1b2c3d4e5f'
const receiptPage = `{"items":[{"id":"${noteId}","version":1,"values":{"title":"Buy paper","done":false,"dueAt":null,"category":"${categoryId}","receipt":{"id":"${fileId}","name":"paper.pdf","mediaType":"application/pdf","size":2048}},"labels":{"category":"Office"}}],"page":1,"pageSize":20,"totalCount":1}`

describe('TableWidget', () => {
  it('shows a value that holds markup as text', async () => {
    const page = {
      items: [
        {
          id: noteId,
          version: 1,
          values: { title: hostileMarkup, done: false, dueAt: null, category: categoryId },
          labels: { category: 'Office' },
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    }
    stubRecords(JSON.stringify(page))

    renderWidget()

    expect(await screen.findByText(hostileMarkup)).toBeInTheDocument()
    expect(document.querySelector('img, [onerror]')).toBeNull()
  })

  it('shows a file as a download link and does not sort by it', async () => {
    stubRecords(receiptPage)

    renderWidget('', receiptWidget)

    const row = await screen.findByRole('row', { name: /Buy paper/ })
    const link = within(row).getByRole('link', { name: 'paper.pdf' })
    expect(link).toHaveAttribute('href', `/api/apps/E2eApp/files/${fileId}`)
    expect(isSortableHeader(screen.getByRole('columnheader', { name: 'Receipt' }))).toBe(false)
    expect(isSortableHeader(screen.getByRole('columnheader', { name: 'Title' }))).toBe(true)
  })

  it('heads the columns with the field labels and shows formatted values', async () => {
    stubRecords()

    renderWidget()

    const row = await screen.findByRole('row', { name: /Buy paper/ })
    for (const title of ['Title', 'Done', 'Due at', 'Category']) {
      expect(screen.getByRole('columnheader', { name: title })).toBeInTheDocument()
    }
    expect(within(row).getByText('Office')).toBeInTheDocument()
    expect(within(row).getByText('No')).toBeInTheDocument()
  })

  it('has no open link and no create button without navigate actions', async () => {
    stubRecords()

    renderWidget()

    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByRole('link', { name: 'New' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Open' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument()
  })

  it('shows the groups of a grouped data source with measure names as headers and no row action buttons', async () => {
    const departmentTarget = {
      entity: 'Department',
      displayField: 'name',
      recordsPath: '/api/apps/E2eApp/entities/Department/records',
    }
    const groupedWidget: WidgetMetadata = {
      type: 'table',
      entity: null,
      form: null,
      tasksPath: null,
      process: null,
      instancesPath: null,
      deadLettersPath: null,
      actions: [],
      rowActions: [],
      toolbarActions: [],
      parameters: {},
      access: everyAction,
      detail: null,
      kpi: null,
      chart: null,
      dataSource: {
        name: 'NotesByDepartment',
        rowsPath,
        entity: 'Note',
        parameters: [],
        pageSize: 10,
        search: false,
        columns: [
          { name: 'department', type: 'reference', labelKey: 'note.category', values: null, target: departmentTarget },
          { name: 'requests', type: 'integer', labelKey: null, values: null, target: null },
          { name: 'total', type: 'decimal', labelKey: null, values: null, target: null },
        ],
      },
    }
    stubRecords(
      `{"items":[{"id":null,"values":{"department":"${categoryId}","requests":2,"total":10.50},"labels":{"department":"Finance"}},{"id":null,"values":{"department":"${noteId}","requests":1,"total":3.00},"labels":{"department":"Sales"}}],"page":1,"pageSize":10,"totalCount":2}`,
    )
    const errors = vi.spyOn(console, 'error')

    renderWidget('', groupedWidget)

    const finance = await screen.findByRole('row', { name: /Finance/ })
    const sales = screen.getByRole('row', { name: /Sales/ })
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Category',
      'requests',
      'total',
    ])
    expect(within(finance).getByText('10.50')).toBeInTheDocument()
    expect(within(sales).getByText('3.00')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'New' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Open' })).not.toBeInTheDocument()
    // Group rows have no id, so each needs a key of its own.
    expect(errors.mock.calls.flat().join(' ')).not.toMatch(/same key/)
    errors.mockRestore()
  })

  describe('navigate actions', () => {
    const detail: NavigateAction = {
      type: 'navigate',
      page: 'TicketDetail',
      labelKey: 'note.details',
      parameters: { ticket: 'id' },
      record: null,
      opensForm: false,
      paths: {},
      visibleWhen: null,
    }
    const createNote: NavigateAction = {
      type: 'navigate',
      page: 'NoteForm',
      labelKey: 'note.add',
      parameters: {},
      record: null,
      opensForm: true,
      paths: {},
      visibleWhen: null,
    }

    it('opens the target page with the row id in the query, from the table address', async () => {
      stubRecords()
      renderWidget('?page=2', { ...noteWidget, rowActions: [detail] })
      const row = await screen.findByRole('row', { name: /Buy paper/ })

      const link = within(row).getByRole('link', { name: 'Buy paper' })
      expect(link).toHaveAttribute('href', `/e2e/ticketdetail?ticket=${noteId}`)
      await userEvent.click(link)

      await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent('/e2e/ticketdetail'))
      expect(search()).toBe(`?ticket=${noteId}`)
      expect(screen.getByTestId('from')).toHaveTextContent('/e2e/notes?page=2')
      expect(within(row).queryByRole('button', { name: 'Details' })).not.toBeInTheDocument()
    })

    it('opens the record of a form page with record', async () => {
      stubRecords()
      renderWidget('', { ...noteWidget, rowActions: [{ ...createNote, labelKey: 'note.edit', record: 'id' }] })
      const row = await screen.findByRole('row', { name: /Buy paper/ })

      const link = within(row).getByRole('link', { name: 'Buy paper' })
      expect(link).toHaveAttribute('href', `/e2e/noteform/${noteId}`)
      await userEvent.click(link)

      await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent(`/e2e/noteform/${noteId}`))
      expect(search()).toBe('')
      expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    })

    describe('with a reference path', () => {
      const categoryPath = '/api/apps/E2eApp/entities/Category/records'
      const named: NavigateAction = {
        ...detail,
        parameters: { category: 'category.name' },
        paths: { category: categoryPath },
      }
      const categoryAnswer = (url: string, answer: Response) =>
        url.startsWith(categoryPath) ? answer : new Response(notePage)

      it('reads the record along the path on click and puts the value in the query', async () => {
        const requested = stubFetch((url) =>
          categoryAnswer(
            url,
            new Response(`{"id":"${categoryId}","version":1,"values":{"name":"Office"},"labels":{}}`),
          ),
        )
        renderWidget('', { ...noteWidget, rowActions: [named] })
        const row = await screen.findByRole('row', { name: /Buy paper/ })

        // A reference path is read on click, so the link has no address.
        const link = within(row).getByRole('link', { name: 'Buy paper' })
        expect(link).not.toHaveAttribute('href')
        await userEvent.click(link)

        await waitFor(() => expect(search()).toBe('?category=Office'))
        expect(screen.getByTestId('path')).toHaveTextContent('/e2e/ticketdetail')
        expect(requested()).toContain(`${categoryPath}/${categoryId}`)
      })

      it('shows one message and opens no page when the record is missing', async () => {
        stubFetch((url) => categoryAnswer(url, new Response('{}', { status: 404 })))
        renderWidget('', { ...noteWidget, rowActions: [named] })
        const row = await screen.findByRole('row', { name: /Buy paper/ })

        await userEvent.click(within(row).getByRole('link', { name: 'Buy paper' }))

        expect(await screen.findByTestId('table-action-error')).toHaveTextContent('The page could not be opened.')
        expect(screen.getByTestId('path')).toHaveTextContent('/e2e/notes')
      })

      it('shows plain text when paths has no entry for a hop', async () => {
        stubRecords()
        renderWidget('', { ...noteWidget, rowActions: [{ ...named, paths: {} }] })
        const row = await screen.findByRole('row', { name: /Buy paper/ })

        expect(within(row).queryByRole('link')).not.toBeInTheDocument()
        expect(within(row).getByText('Buy paper')).toBeInTheDocument()
      })
    })

    describe('at narrow width', () => {
      beforeEach(() => {
        vi.stubGlobal('matchMedia', (query: string) => ({
          matches: query === narrowWidthQuery,
          media: query,
          onchange: null,
          addListener() {},
          removeListener() {},
          addEventListener() {},
          removeEventListener() {},
          dispatchEvent: () => false,
        }))
      })

      it('folds the row actions into one Actions button', async () => {
        stubRecords()
        renderWidget('', {
          ...noteWidget,
          rowActions: [detail, { ...createNote, labelKey: 'note.edit', record: 'id' }],
        })
        const row = await screen.findByRole('row', { name: /Buy paper/ })

        const buttons = within(row).getAllByRole('button')
        expect(buttons).toHaveLength(1)
        expect(buttons[0]).toHaveAccessibleName('Actions')
        expect(within(row).getByRole('link', { name: 'Buy paper' })).toBeInTheDocument()
        expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
      })

      it('opens the target page from a menu entry, from the table address', async () => {
        stubRecords()
        renderWidget('?page=2', {
          ...noteWidget,
          rowActions: [detail, { ...createNote, labelKey: 'note.edit', record: 'id' }],
        })
        const row = await screen.findByRole('row', { name: /Buy paper/ })

        await userEvent.click(within(row).getByRole('button', { name: 'Actions' }))
        await userEvent.click(await screen.findByRole('menuitem', { name: 'Edit' }))

        await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent(`/e2e/noteform/${noteId}`))
        expect(search()).toBe('')
        expect(screen.getByTestId('from')).toHaveTextContent('/e2e/notes?page=2')
      })

      it('shows no menu button on a row whose actions are all hidden', async () => {
        stubRecords()
        renderWidget('', { ...noteWidget, rowActions: [{ ...detail, parameters: { ticket: 'concat(title, id)' } }] })
        const row = await screen.findByRole('row', { name: /Buy paper/ })

        expect(within(row).queryByRole('button')).not.toBeInTheDocument()
      })
    })

    it('shows plain text when the expression of the link cannot be worked out by the SPA', async () => {
      stubRecords()
      renderWidget('', { ...noteWidget, rowActions: [{ ...detail, parameters: { ticket: 'concat(title, id)' } }] })
      const row = await screen.findByRole('row', { name: /Buy paper/ })

      expect(within(row).queryByRole('link')).not.toBeInTheDocument()
      expect(within(row).getByText('Buy paper')).toBeInTheDocument()
    })

    it('shows the identifying cell as a link only on the rows where visibleWhen is true', async () => {
      const otherId = '01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c80'
      stubRecords(
        `{"items":[{"id":"${noteId}","version":1,"values":{"title":"Buy paper","status":"open"},"labels":{}},{"id":"${otherId}","version":1,"values":{"title":"Sell paper","status":"closed"},"labels":{}}],"page":1,"pageSize":20,"totalCount":2}`,
      )
      const statusField = { ...field('status', 'enum', 'note.status'), values: ['open', 'closed'] }
      renderWidget('', {
        ...noteWidget,
        entity: { ...noteWidget.entity, fields: [field('title', 'text', 'note.title'), statusField] },
        rowActions: [{ ...detail, visibleWhen: "status == 'open'" }],
      })
      const open = await screen.findByRole('row', { name: /Buy paper/ })
      const closed = await screen.findByRole('row', { name: /Sell paper/ })

      expect(within(open).getByRole('link', { name: 'Buy paper' })).toBeInTheDocument()
      expect(within(closed).queryByRole('link')).not.toBeInTheDocument()
      expect(within(closed).getByText('Sell paper')).toBeInTheDocument()
    })

    it('shows a toolbar button to a form page only with the create grant', async () => {
      stubRecords()
      const { unmount } = renderWidget('', {
        ...noteWidget,
        access: { actions: ['read'], createFields: [], updateFields: [] },
        toolbarActions: [createNote],
      })
      await screen.findByRole('row', { name: /Buy paper/ })
      expect(screen.queryByRole('button', { name: 'Add note' })).not.toBeInTheDocument()
      unmount()

      stubRecords()
      renderWidget('', { ...noteWidget, toolbarActions: [createNote] })
      await screen.findByRole('row', { name: /Buy paper/ })

      await userEvent.click(screen.getByRole('button', { name: 'Add note' }))

      expect(screen.getByTestId('path')).toHaveTextContent('/e2e/noteform/new')
    })

    it('shows a toolbar button to a page that is not a form page without the create grant', async () => {
      stubRecords()
      renderWidget('', {
        ...noteWidget,
        access: { actions: ['read'], createFields: [], updateFields: [] },
        toolbarActions: [{ ...detail, parameters: { kind: "'all'" } }],
      })
      await screen.findByRole('row', { name: /Buy paper/ })

      await userEvent.click(screen.getByRole('button', { name: 'Details' }))

      expect(screen.getByTestId('path')).toHaveTextContent('/e2e/ticketdetail')
      expect(search()).toBe('?kind=all')
    })

    it('shows no button on a group row, which has no id', async () => {
      const groupedWidget: WidgetMetadata = {
        ...noteListWidget,
        rowActions: [detail],
        dataSource: {
          ...noteListWidget.dataSource!,
          columns: [{ name: 'departmentName', type: 'text', labelKey: null, values: null, target: null }],
        },
      }
      stubRecords(
        '{"items":[{"id":null,"values":{"departmentName":"Finance"},"labels":{}}],"page":1,"pageSize":10,"totalCount":1}',
      )

      renderWidget('', groupedWidget)

      const row = await screen.findByRole('row', { name: /Finance/ })
      expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    })
  })

  it('sorts from page 1 and keeps parameters it does not own', async () => {
    const requests = stubRecords()
    renderWidget('?page=2&x=1')
    await screen.findByRole('row', { name: /Buy paper/ })
    expect(requests()).toEqual([`${recordsPath}?page=2`])

    // Ant Design draws the header twice when the table scrolls sideways, so ask for the accessible one.
    await userEvent.click(screen.getByRole('columnheader', { name: 'Title' }))

    await waitFor(() => expect(search()).toBe('?x=1&sort=title'))
    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toBe(`${recordsPath}?sort=title`)
  })

  it('moves between pages and keeps the sort', async () => {
    const requests = stubRecords()
    renderWidget('?sort=-title')
    await screen.findByRole('row', { name: /Buy paper/ })

    await userEvent.click(screen.getByTitle('2'))

    await waitFor(() => expect(search()).toBe('?page=2&sort=-title'))
    await waitFor(() => expect(requests()).toContain(`${recordsPath}?page=2&sort=-title`))
  })

  it('is busy while the records load, and marks the current page', async () => {
    stubRecords()
    const { container } = renderWidget('?page=2')
    const root = container.firstElementChild as HTMLElement
    expect(root).toHaveAttribute('aria-busy', 'true')

    await screen.findByRole('row', { name: /Buy paper/ })

    expect(root).toHaveAttribute('aria-busy', 'false')
    expect(screen.getByTitle('2').querySelector('[aria-current="page"]')).toHaveTextContent('2')
    expect(container.querySelectorAll('[aria-current="page"]')).toHaveLength(1)
  })

  it('changes the page size from page 1', async () => {
    const requests = stubRecords()
    renderWidget('?page=2&pageSize=10&sort=title')
    await screen.findByRole('row', { name: /Buy paper/ })

    // Page 2 still exists at 20 a page, so only the widget brings it back to page 1.
    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('20 / page'))

    await waitFor(() => expect(search()).toBe('?sort=title'))
    await waitFor(() => expect(requests()).toContain(`${recordsPath}?sort=title`))
  })

  it('moves a page past the end to the last page', async () => {
    const requests = stubRecords()

    renderWidget('?page=9&x=1')

    await waitFor(() => expect(search()).toBe('?x=1&page=3'))
    await waitFor(() => expect(requests()).toEqual([`${recordsPath}?page=9`, `${recordsPath}?page=3`]))
  })

  it('removes the page when there are no records', async () => {
    stubRecords('{"items":[],"page":4,"pageSize":20,"totalCount":0}')

    renderWidget('?page=4')

    await waitFor(() => expect(search()).toBe(''))
  })

  it('drops invalid parameters and requests the default page', async () => {
    const requests = stubRecords()

    renderWidget('?page=0&pageSize=7&sort=nope')

    await screen.findByRole('row', { name: /Buy paper/ })
    await waitFor(() => expect(search()).toBe(''))
    expect(requests()).toEqual([recordsPath])
  })

  it('does not offer sorting by a reference', async () => {
    stubRecords()

    renderWidget()

    await screen.findByRole('row', { name: /Buy paper/ })
    expect(isSortableHeader(screen.getByRole('columnheader', { name: 'Title' }))).toBe(true)
    expect(isSortableHeader(screen.getByRole('columnheader', { name: 'Category' }))).toBe(false)
  })

  it('shows no column for a child collection and ignores it as a sort', async () => {
    const requests = stubRecords()
    const lines: FieldMetadata = {
      ...field('lines', 'child-collection', 'note.lines'),
      fields: [field('description', 'text', null)],
    }

    renderWidget('?sort=lines', {
      ...noteWidget,
      entity: { ...noteWidget.entity, fields: [...noteWidget.entity.fields, lines] },
    })

    await screen.findByRole('row', { name: /Buy paper/ })
    await waitFor(() => expect(search()).toBe(''))
    expect(requests()).toEqual([recordsPath])
    expect(screen.queryByRole('columnheader', { name: 'Lines' })).not.toBeInTheDocument()
  })

  it('shows no column for a references field and ignores it as a sort', async () => {
    const requests = stubRecords()
    const software: FieldMetadata = {
      ...field('software', 'references', 'note.software'),
      target: { entity: 'Software', displayField: 'name', recordsPath: '/api/apps/E2eApp/entities/Software/records' },
    }

    renderWidget('?sort=software', {
      ...noteWidget,
      entity: { ...noteWidget.entity, fields: [...noteWidget.entity.fields, software] },
    })

    await screen.findByRole('row', { name: /Buy paper/ })
    await waitFor(() => expect(search()).toBe(''))
    expect(requests()).toEqual([recordsPath])
    expect(screen.queryByRole('columnheader', { name: 'Software' })).not.toBeInTheDocument()
  })

  it('right-aligns numbers in tabular figures and groups them without losing a digit', async () => {
    stubRecords(
      `{"items":[{"id":"${noteId}","version":3,"values":{"price":1250.50},"labels":{}}],"page":1,"pageSize":20,"totalCount":1}`,
    )

    renderWidget('', {
      type: 'table',
      entity: { ...noteWidget.entity, fields: [field('price', 'decimal', null)] },
      dataSource: null,
      form: null,
      tasksPath: null,
      process: null,
      instancesPath: null,
      deadLettersPath: null,
      actions: [],
      rowActions: [],
      toolbarActions: [],
      parameters: {},
      access: everyAction,
      detail: null,
      kpi: null,
      chart: null,
    })

    const cell = await screen.findByRole('cell', { name: '1,250.50' })
    expect(cell).toHaveStyle({ textAlign: 'right', fontVariantNumeric: 'tabular-nums' })
    expect(screen.getByRole('columnheader', { name: 'price' })).toHaveStyle({ textAlign: 'right' })
  })

  it('shows a null cell as a muted italic dash', async () => {
    stubRecords(
      `{"items":[{"id":"${noteId}","version":3,"values":{"title":null},"labels":{}}],"page":1,"pageSize":20,"totalCount":1}`,
    )

    renderWidget('', {
      ...noteWidget,
      entity: { ...noteWidget.entity!, fields: [field('title', 'text', null)] },
    })

    const dash = await screen.findByText('—')
    expect(dash).toHaveStyle({ fontStyle: 'italic' })
    // The colour is the muted text token, not the plain text colour.
    expect(dash).toHaveStyle({ color: designTokensFor('light').colour.textMuted })
  })

  it('leaves a null cell of a group row blank', async () => {
    stubRecords(
      `{"items":[{"id":null,"values":{"department":null,"requests":2},"labels":{}}],"page":1,"pageSize":10,"totalCount":1}`,
    )

    renderWidget('', {
      ...noteListWidget,
      dataSource: {
        ...noteListWidget.dataSource!,
        columns: [
          { name: 'department', type: 'text', labelKey: null, values: null, target: null },
          { name: 'requests', type: 'integer', labelKey: null, values: null, target: null },
        ],
      },
    })

    const row = (await screen.findByText('2')).closest('tr')!
    expect(within(row).queryByText('—')).not.toBeInTheDocument()
    expect(within(row).getAllByRole('cell')[0]).toHaveTextContent(/^$/)
  })

  it('heads a data source table with its column labels or names and requests its rows path', async () => {
    const requests = stubRecords(
      `{"items":[{"id":"${noteId}","values":{"title":"Buy paper","departmentName":"Finance"},"labels":{}}],"page":1,"pageSize":10,"totalCount":1}`,
    )

    renderWidget('?pageSize=10', noteListWidget)

    const row = await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Title', 'departmentName'])
    expect(within(row).getByText('Finance')).toBeInTheDocument()
    // The data source's own page size is the default, so the URL and the request leave it out.
    await waitFor(() => expect(search()).toBe(''))
    expect(requests()).toEqual([rowsPath])
  })

  it('keeps a page size other than the data source default in the URL and the rows request', async () => {
    const requests = stubRecords('{"items":[],"page":2,"pageSize":20,"totalCount":45}')

    renderWidget('?page=2&pageSize=20&sort=-title', noteListWidget)

    await screen.findByText('No records yet.')
    expect(search()).toBe('?page=2&pageSize=20&sort=-title')
    expect(requests()).toEqual([`${rowsPath}?page=2&pageSize=20&sort=-title`])
  })

  it('shows the empty state when there are no records', async () => {
    stubRecords('{"items":[],"page":1,"pageSize":20,"totalCount":0}')

    renderWidget()

    expect(await screen.findByText('No records yet.')).toBeInTheDocument()
  })

  it('shows an error when the records cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubRecords('{}', 500)

    renderWidget()

    expect(await screen.findByTestId('records-error')).toHaveTextContent('The records could not be loaded.')
  })

  it('shows one filter input per data source parameter, by type, labelled with its label or name', async () => {
    stubRecords(emptyRows)

    renderWidget('', filteredWidget)

    await screen.findByText('No records yet.')
    for (const label of [
      'Title contains',
      'Minimum count',
      'Maximum price',
      'Due from',
      'createdAfter',
      'In category',
    ]) {
      expect(screen.getByLabelText(label)).toHaveRole('textbox')
    }
    for (const label of ['Is done', 'State']) {
      expect(screen.getByLabelText(label)).toHaveRole('combobox')
    }
    expect(screen.getByLabelText('Minimum count')).toHaveAttribute('inputmode', 'numeric')
    expect(screen.getByLabelText('Maximum price')).toHaveAttribute('inputmode', 'decimal')
    expect(screen.getByLabelText('In category')).toHaveAttribute('readonly')
    const bar = screen.getByTestId('filter-bar')
    expect(within(bar).getByRole('button', { name: 'Choose' })).toBeInTheDocument()
    expect(within(bar).queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
  })

  it('requests the rows with the page value of a bound parameter, which has no filter input', async () => {
    const requests = stubRecords(emptyRows)
    const boundWidget: WidgetMetadata = {
      ...noteListWidget,
      parameters: { inCategory: 'category' },
      dataSource: {
        ...noteListWidget.dataSource!,
        parameters: [
          { ...parameter('inCategory', 'reference', null), required: true, target: categoryTarget },
          parameter('titleFilter', 'text', 'filter.title'),
        ],
      },
    }

    renderWidget('?category=' + categoryId + '&titleFilter=paper', boundWidget, { category: categoryId })
    await screen.findByText('No records yet.')

    expect(requests()).toContain(`${rowsPath}?inCategory=${categoryId}&titleFilter=paper`)
    expect(document.getElementById('filter-inCategory')).toBeNull()
    expect(document.getElementById('filter-titleFilter')).not.toBeNull()
    // The page parameter stays in the URL as it is.
    expect(search()).toBe(`?category=${categoryId}&titleFilter=paper`)
  })

  it('has no filter bar for an entity or a data source without parameters', async () => {
    stubRecords()

    renderWidget('', noteWidget)

    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByTestId('filter-bar')).not.toBeInTheDocument()
  })

  it('writes a picked option before page, pageSize and sort, from page 1, and requests the filtered rows', async () => {
    const requests = stubRecords('{"items":[],"page":2,"pageSize":20,"totalCount":45}')
    renderWidget('?page=2&pageSize=20&sort=-title', filteredWidget)
    await screen.findByText('No records yet.')

    await userEvent.click(screen.getByLabelText('State'))
    await userEvent.click(await screen.findByTitle('approved'))

    await waitFor(() => expect(search()).toBe('?statusFilter=approved&pageSize=20&sort=-title'))
    await waitFor(() => expect(requests()).toContain(`${rowsPath}?statusFilter=approved&pageSize=20&sort=-title`))
  })

  it('applies typed text on Enter or blur, not on every key, and the back button restores it', async () => {
    const requests = stubRecords(emptyRows)
    renderWidget('', filteredWidget)
    await screen.findByText('No records yet.')
    const input = screen.getByLabelText('Title contains')

    await userEvent.type(input, 'paper')
    expect(search()).toBe('')
    await userEvent.keyboard('{Enter}')
    await waitFor(() => expect(search()).toBe('?titleFilter=paper'))
    await waitFor(() => expect(requests()).toContain(`${rowsPath}?titleFilter=paper`))

    await userEvent.clear(input)
    await userEvent.type(input, 'desk')
    await userEvent.click(screen.getByLabelText('Minimum count'))
    await waitFor(() => expect(search()).toBe('?titleFilter=desk'))

    await userEvent.click(screen.getByRole('button', { name: 'Back' }))
    await waitFor(() => expect(search()).toBe('?titleFilter=paper'))
    expect(input).toHaveValue('paper')
  })

  it('writes a yes or no filter and removes a cleared value from the URL', async () => {
    stubRecords(emptyRows)
    renderWidget('?titleFilter=paper&doneFilter=true', filteredWidget)
    await screen.findByText('No records yet.')
    expect(screen.getByTestId('filter-bar')).toHaveTextContent('Yes')

    await userEvent.click(screen.getByLabelText('Is done'))
    await userEvent.click(await screen.findByTitle('No'))
    await waitFor(() => expect(search()).toBe('?titleFilter=paper&doneFilter=false'))

    await userEvent.clear(screen.getByLabelText('Title contains'))
    await userEvent.keyboard('{Enter}')
    await waitFor(() => expect(search()).toBe('?doneFilter=false'))
  })

  it('drops empty and repeated values from the URL without requesting them', async () => {
    const requests = stubRecords(emptyRows)

    renderWidget('?titleFilter=&statusFilter=open&statusFilter=approved', filteredWidget)

    await waitFor(() => expect(search()).toBe('?statusFilter=open'))
    expect(requests()).toEqual([`${rowsPath}?statusFilter=open`])
  })

  it('picks a reference filter in the lookup and shows its label again after a reload', async () => {
    const requests = stubFetch((url) => {
      if (url.startsWith(`${categoryTarget.recordsPath}/${categoryId}`)) {
        return new Response(`{"id":"${categoryId}","version":1,"values":{"name":"Office"},"labels":{}}`)
      }
      if (url.startsWith(categoryTarget.recordsPath)) {
        return new Response(
          `{"items":[{"id":"${categoryId}","version":1,"values":{"name":"Office"},"labels":{}}],"page":1,"pageSize":20,"totalCount":1}`,
        )
      }
      return new Response(emptyRows)
    })
    const view = renderWidget('', filteredWidget)
    await screen.findByText('No records yet.')

    await userEvent.click(within(screen.getByTestId('filter-bar')).getByRole('button', { name: 'Choose' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByRole('row', { name: /Office/ }))

    await waitFor(() => expect(search()).toBe(`?categoryFilter=${categoryId}`))
    expect(screen.getByRole('textbox', { name: 'In category' })).toHaveValue('Office')
    expect(requests()).not.toContain(`${categoryTarget.recordsPath}/${categoryId}`)

    view.unmount()
    renderWidget(`?categoryFilter=${categoryId}`, filteredWidget)
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'In category' })).toHaveValue('Office'))
    expect(requests()).toContain(`${categoryTarget.recordsPath}/${categoryId}`)

    await userEvent.click(within(screen.getByTestId('filter-bar')).getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(search()).toBe(''))
    expect(screen.getByRole('textbox', { name: 'In category' })).toHaveValue('')
  })

  it('shows a rejected parameter value under its input, with no rows and no load error', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubRecords(
      '{"title":"One or more validation errors occurred.","errors":{"statusFilter":["Must be one of the declared values."]}}',
      400,
    )

    renderWidget('?statusFilter=bogus', filteredWidget)

    expect(await screen.findByTestId('filter-error-statusFilter')).toHaveTextContent(
      'Must be one of the declared values.',
    )
    expect(screen.queryByTestId('records-error')).not.toBeInTheDocument()
    expect(screen.getByText('No records yet.')).toBeInTheDocument()
    expect(search()).toBe('?statusFilter=bogus')
  })

  it('shows the load error for a 400 that names no parameter', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubRecords('{"title":"The data source filter could not be evaluated for these rows."}', 400)

    renderWidget('', filteredWidget)

    expect(await screen.findByTestId('records-error')).toBeInTheDocument()
    expect(screen.queryByTestId('filter-error-statusFilter')).not.toBeInTheDocument()
  })

  describe('identifying column, row action menu and Refresh', () => {
    const open: NavigateAction = {
      type: 'navigate',
      page: 'NoteForm',
      labelKey: 'note.details',
      parameters: {},
      record: 'id',
      opensForm: true,
      paths: {},
      visibleWhen: null,
    }
    const close: StartProcessAction = {
      type: 'startProcess',
      process: 'NoteClose',
      labelKey: 'note.close',
      instancesPath: '/api/apps/E2eApp/processes/NoteClose/instances',
      visibleWhen: null,
    }
    const remove: DeleteAction = { type: 'delete', labelKey: 'note.delete', recordsPath, visibleWhen: null }
    const edit: NavigateAction = { ...open, labelKey: 'note.edit' }

    it('puts the display field first, also when it is declared later', async () => {
      stubRecords()
      const widget = {
        ...noteWidget,
        entity: {
          ...noteWidget.entity,
          displayField: 'title',
          fields: [field('done', 'boolean', 'note.done'), field('title', 'text', 'note.title')],
        },
        rowActions: [open],
      }

      renderWidget('', widget)

      const row = await screen.findByRole('row', { name: /Buy paper/ })
      expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual(['Title', 'Done'])
      expect(within(row).getByRole('link', { name: 'Buy paper' })).toHaveAttribute('href', `/e2e/noteform/${noteId}`)
    })

    it('shows the label as the link text when the identifying cell is empty', async () => {
      stubRecords(notePage.replace('"title":"Buy paper"', '"title":null'))

      renderWidget('', { ...noteWidget, rowActions: [open] })

      const link = await screen.findByRole('link', { name: 'Details' })
      expect(link).toHaveAttribute('href', `/e2e/noteform/${noteId}`)
    })

    it('shows one delete as a trash button named by its label, with no Actions button', async () => {
      stubRecords()

      renderWidget('', { ...noteWidget, rowActions: [remove] })

      const row = await screen.findByRole('row', { name: /Buy paper/ })
      expect(within(row).getByRole('button', { name: 'Delete note' })).toBeInTheDocument()
      expect(within(row).queryByRole('button', { name: 'Actions' })).not.toBeInTheDocument()
      expect(within(row).queryByRole('link')).not.toBeInTheDocument()
    })

    it('draws the delete icon in dangerText and names it on keyboard focus in dark mode', async () => {
      stubRecords()
      renderWidgetInDark({ ...noteWidget, rowActions: [remove] })

      const row = await screen.findByRole('row', { name: /Buy paper/ })
      const button = within(row).getByRole('button', { name: 'Delete note' })
      expect(button).toHaveStyle({ color: designTokensFor('dark').colour.dangerText })
      act(() => button.focus())

      expect(await screen.findByRole('tooltip')).toHaveTextContent('Delete note')
    })

    it('names the Actions menu on keyboard focus', async () => {
      stubRecords()
      renderWidget('', { ...noteWidget, rowActions: [open, close, edit, remove] })

      const row = await screen.findByRole('row', { name: /Buy paper/ })
      act(() => within(row).getByRole('button', { name: 'Actions' }).focus())

      expect(await screen.findByRole('tooltip')).toHaveTextContent('Actions')
    })

    it('folds three or more other actions into one Actions menu with the delete marked danger', async () => {
      stubRecords()

      renderWidget('', { ...noteWidget, rowActions: [open, close, edit, remove] })

      const row = await screen.findByRole('row', { name: /Buy paper/ })
      const buttons = within(row).getAllByRole('button')
      expect(buttons).toHaveLength(1)
      expect(buttons[0]).toHaveAccessibleName('Actions')
      expect(within(row).getByRole('link', { name: 'Buy paper' })).toBeInTheDocument()
      await userEvent.click(buttons[0])
      const items = await screen.findAllByRole('menuitem')
      expect(items.map((item) => item.textContent)).toEqual(['Close note', 'Edit', 'Delete note'])
      expect(items[2]).toHaveClass('ant-dropdown-menu-item-danger')
      expect(items[0]).not.toHaveClass('ant-dropdown-menu-item-danger')
    })

    it('shows one or two other actions in the row', async () => {
      stubRecords()

      renderWidget('', { ...noteWidget, rowActions: [open, close, remove] })

      const row = await screen.findByRole('row', { name: /Buy paper/ })
      expect(within(row).getByRole('button', { name: 'Close note' })).toBeInTheDocument()
      expect(within(row).getByRole('button', { name: 'Delete note' })).toBeInTheDocument()
      expect(within(row).queryByRole('button', { name: 'Actions' })).not.toBeInTheDocument()
    })

    it('keeps a download link in a file column, so the first navigate action stays a row action', async () => {
      stubRecords(receiptPage)
      const widget = {
        ...receiptWidget,
        entity: {
          ...receiptWidget.entity,
          fields: [receiptWidget.entity.fields.find((candidate) => candidate.name === 'receipt')!],
        },
        rowActions: [open],
      }

      renderWidget('', widget)

      const row = await screen.findByRole('row', { name: /paper.pdf/ })
      expect(within(row).getByRole('link', { name: 'paper.pdf' })).toHaveAttribute(
        'href',
        `/api/apps/E2eApp/files/${fileId}`,
      )
      expect(within(row).getByRole('button', { name: 'Details' })).toBeInTheDocument()
    })

    it('loads the same page of rows again when Refresh is clicked', async () => {
      const requests = stubRecords()
      renderWidget('?page=2')
      await screen.findByRole('row', { name: /Buy paper/ })

      await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))

      await waitFor(() => expect(requests()).toHaveLength(2))
      expect(requests()).toEqual([`${recordsPath}?page=2`, `${recordsPath}?page=2`])
      expect(search()).toBe('?page=2')
    })

    it('shows the toolbar with Refresh on a table with no search or toolbar actions', async () => {
      stubRecords()

      renderWidget()

      expect(await screen.findByRole('button', { name: 'Refresh' })).toBeInTheDocument()
    })

    it('names Refresh on keyboard focus', async () => {
      stubRecords()
      renderWidget()

      const refresh = await screen.findByRole('button', { name: 'Refresh' })
      act(() => refresh.focus())

      expect(await screen.findByRole('tooltip')).toHaveTextContent('Refresh')
    })

    it('draws the first toolbar action as the main action and the others as secondary in dark mode', async () => {
      stubRecords()
      const main: NavigateAction = { ...open, record: null, labelKey: 'note.add' }
      const other: NavigateAction = { ...main, labelKey: 'note.close' }

      renderWidgetInDark({ ...noteWidget, toolbarActions: [main, other] })

      await screen.findByRole('row', { name: /Buy paper/ })
      const { colour } = designTokensFor('dark')
      expect(screen.getByRole('button', { name: 'Add note' })).toHaveStyle({
        color: colour.primaryText,
        borderColor: colour.primaryText,
      })
      expect(screen.getByRole('button', { name: 'Close note' }).style.color).toBe('')
    })

    it('makes the first shown toolbar action the main one when an earlier action is hidden', async () => {
      stubRecords()
      const hidden: NavigateAction = { ...open, record: null, labelKey: 'note.add' }
      const shown: NavigateAction = {
        ...open,
        page: 'TicketDetail',
        labelKey: 'note.details',
        record: null,
        opensForm: false,
        parameters: { kind: "'all'" },
      }

      renderWidgetInDark({
        ...noteWidget,
        access: { actions: ['read'], createFields: [], updateFields: [] },
        toolbarActions: [hidden, shown],
      })

      await screen.findByRole('row', { name: /Buy paper/ })
      expect(screen.queryByRole('button', { name: 'Add note' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Details' })).toHaveStyle({
        color: designTokensFor('dark').colour.primaryText,
      })
    })

    it('puts the first toolbar action last, after Refresh, Import and Export', async () => {
      stubRecords()
      const main: NavigateAction = { ...open, record: null, labelKey: 'note.add' }
      const other: NavigateAction = { ...main, labelKey: 'note.close' }

      renderWidget('', {
        ...noteWidget,
        access: { ...everyAction, actions: [...everyAction.actions, 'import', 'export'] },
        toolbarActions: [main, other],
      })

      await screen.findByRole('row', { name: /Buy paper/ })
      const bar = screen.getByRole('button', { name: 'Refresh' }).parentElement!
      expect(
        within(bar)
          .getAllByRole('button')
          .map((button) => button.textContent || button.getAttribute('aria-label')),
      ).toEqual(['Refresh', 'Import', 'Export', 'Close note', 'Add note'])
    })
  })

  describe('startProcess row action', () => {
    const closeNote: StartProcessAction = {
      type: 'startProcess',
      process: 'NoteClose',
      labelKey: 'note.close',
      instancesPath: '/api/apps/E2eApp/processes/NoteClose/instances',
      visibleWhen: null,
    }
    const closableWidget = { ...noteWidget, rowActions: [closeNote] }

    /** Stubs `fetch`: GET answers the notes, POST answers `startStatus`. Returns the calls made. */
    function stubStart(startStatus: number, startBody = '') {
      const fetchMock = vi.fn(
        async (_input: string, init?: RequestInit) =>
          new Response(init?.method === 'POST' ? startBody : notePage, {
            status: init?.method === 'POST' ? startStatus : 200,
          }),
      )
      vi.stubGlobal('fetch', fetchMock)
      return fetchMock.mock.calls
    }

    it('posts the row id with an idempotency key, then requests the rows again', async () => {
      const calls = stubStart(201)
      renderWidget('', closableWidget)
      const row = await screen.findByRole('row', { name: /Buy paper/ })

      await userEvent.click(within(row).getByRole('button', { name: 'Close note' }))

      await waitFor(() => expect(calls.filter(([, init]) => init?.method !== 'POST')).toHaveLength(2))
      const [first, second] = calls.filter(([, init]) => init?.method !== 'POST')
      expect(second[0]).toBe(first[0])
      const posts = calls.filter(([, init]) => init?.method === 'POST')
      expect(posts).toHaveLength(1)
      const [url, init] = posts[0]
      expect(url).toBe(closeNote.instancesPath)
      expect(init?.body).toBe(JSON.stringify({ subjectId: noteId }))
      expect(new Headers(init?.headers).get('Idempotency-Key')).toBeTruthy()
      expect(screen.queryByTestId('table-action-error')).not.toBeInTheDocument()
    })

    it('shows the running process text for a 409', async () => {
      const calls = stubStart(409, '{"title":"Conflict"}')
      renderWidget('', closableWidget)
      const row = await screen.findByRole('row', { name: /Buy paper/ })

      await userEvent.click(within(row).getByRole('button', { name: 'Close note' }))

      expect(await screen.findByTestId('table-action-error')).toHaveTextContent(
        'This record is already in a running process.',
      )
      expect(calls.filter(([, init]) => init?.method !== 'POST')).toHaveLength(1)
    })

    it('shows a start condition message as text', async () => {
      stubStart(422, '{"title":"Invalid","errors":{"start":["Only open notes can be closed."]}}')
      renderWidget('', closableWidget)
      const row = await screen.findByRole('row', { name: /Buy paper/ })

      await userEvent.click(within(row).getByRole('button', { name: 'Close note' }))

      expect(await screen.findByTestId('table-action-error')).toHaveTextContent('Only open notes can be closed.')
    })

    it('shows the start failed text for a server error and keeps the key for the retry', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      const calls = stubStart(500)
      renderWidget('', closableWidget)
      const row = await screen.findByRole('row', { name: /Buy paper/ })

      await userEvent.click(within(row).getByRole('button', { name: 'Close note' }))
      expect(await screen.findByTestId('table-action-error')).toHaveTextContent('The process could not be started.')
      await userEvent.click(within(row).getByRole('button', { name: 'Close note' }))

      await waitFor(() => expect(calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(2))
      const keys = calls
        .filter(([, init]) => init?.method === 'POST')
        .map(([, init]) => new Headers(init?.headers).get('Idempotency-Key'))
      expect(keys[0]).toBe(keys[1])
    })
  })

  describe('delete row action', () => {
    const deleteNote: DeleteAction = {
      type: 'delete',
      labelKey: 'note.delete',
      recordsPath,
      visibleWhen: null,
    }
    const deletableWidget = { ...noteWidget, rowActions: [deleteNote] }

    /** Stubs `fetch`: GET answers the notes, DELETE answers `deleteStatus`. Returns the calls made. */
    function stubDelete(deleteStatus: number) {
      const fetchMock = vi.fn(
        async (_input: string, init?: RequestInit) =>
          new Response(init?.method === 'DELETE' ? null : notePage, {
            status: init?.method === 'DELETE' ? deleteStatus : 200,
          }),
      )
      vi.stubGlobal('fetch', fetchMock)
      return {
        gets: () => fetchMock.mock.calls.filter(([, init]) => init?.method !== 'DELETE'),
        deletes: () => fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE'),
      }
    }

    async function clickDelete() {
      const row = await screen.findByRole('row', { name: /Buy paper/ })
      await userEvent.click(within(row).getByRole('button', { name: 'Delete note' }))
      return screen.findByRole('dialog')
    }

    it('asks for confirmation, sends the delete and then requests the rows again', async () => {
      const { gets, deletes } = stubDelete(204)
      renderWidget('', deletableWidget)

      const dialog = await clickDelete()
      expect(dialog).toHaveTextContent('Delete this record? This cannot be undone.')
      expect(deletes()).toHaveLength(0)
      await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

      expect(await screen.findByTestId('table-action-done')).toHaveTextContent('The record was deleted.')
      await waitFor(() => expect(gets()).toHaveLength(2))
      expect(gets()[1][0]).toBe(gets()[0][0])
      expect(deletes()).toHaveLength(1)
      const [url, init] = deletes()[0]
      expect(url).toBe(`${recordsPath}/${noteId}`)
      expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
      expect(screen.queryByTestId('table-action-error')).not.toBeInTheDocument()
    })

    it('sends nothing when the user cancels the dialog', async () => {
      const { gets, deletes } = stubDelete(204)
      renderWidget('', deletableWidget)

      const dialog = await clickDelete()
      await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(deletes()).toHaveLength(0)
      expect(gets()).toHaveLength(1)
      expect(screen.queryByTestId('table-action-done')).not.toBeInTheDocument()
    })

    it.each([
      [403, 'You may not delete this record.'],
      [404, 'This record no longer exists.'],
      [409, 'This record is in use and cannot be deleted.'],
    ])('shows its own message for a %i and keeps the rows', async (status, message) => {
      const { gets, deletes } = stubDelete(status)
      renderWidget('', deletableWidget)

      const dialog = await clickDelete()
      await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

      expect(await screen.findByTestId('table-action-error')).toHaveTextContent(message)
      expect(deletes()).toHaveLength(1)
      expect(gets()).toHaveLength(1)
      expect(screen.getByRole('row', { name: /Buy paper/ })).toBeInTheDocument()
      expect(screen.queryByTestId('table-action-done')).not.toBeInTheDocument()
    })

    it('shows the failed text for any other answer', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      const { gets } = stubDelete(500)
      renderWidget('', deletableWidget)

      const dialog = await clickDelete()
      await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

      expect(await screen.findByTestId('table-action-error')).toHaveTextContent('The record could not be deleted.')
      expect(gets()).toHaveLength(1)
    })

    it('shows the failed text when the request throws', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
      vi.stubGlobal(
        'fetch',
        vi.fn(async (_input: string, init?: RequestInit) => {
          if (init?.method === 'DELETE') {
            throw new TypeError('Failed to fetch')
          }
          return new Response(notePage)
        }),
      )
      renderWidget('', deletableWidget)

      const dialog = await clickDelete()
      await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

      expect(await screen.findByTestId('table-action-error')).toHaveTextContent('The record could not be deleted.')
      expect(warn).toHaveBeenCalled()
    })

    it('hides the button on a row where its visibleWhen is false', async () => {
      stubDelete(204)
      renderWidget('', { ...noteWidget, rowActions: [{ ...deleteNote, visibleWhen: 'done == true' }] })

      const row = await screen.findByRole('row', { name: /Buy paper/ })

      expect(within(row).queryByRole('button', { name: 'Delete note' })).not.toBeInTheDocument()
    })
  })
})

describe('TableWidget search', () => {
  const searchableWidget: WidgetMetadata = {
    ...noteWidget,
    entity: { ...noteWidget.entity, displayField: 'title' },
  }

  it('applies a search on Enter, keeps it in the URL, goes back to page 1 and sends it', async () => {
    const urls = stubRecords()
    const user = userEvent.setup()
    renderWidget('?page=3', searchableWidget)
    await screen.findByRole('row', { name: /Buy paper/ })

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'paper{Enter}')

    await waitFor(() => expect(search()).toBe('?search=paper'))
    await waitFor(() => expect(urls().at(-1)).toContain('search=paper'))
    expect(urls().at(-1)).not.toContain('page=')
  })

  it('does not apply on every key and sends the text as typed', async () => {
    const urls = stubRecords()
    const user = userEvent.setup()
    renderWidget('', searchableWidget)
    await screen.findByRole('row', { name: /Buy paper/ })
    const count = urls().length

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), ' a&b ')

    expect(search()).toBe('')
    expect(urls()).toHaveLength(count)
    await user.keyboard('{Enter}')
    await waitFor(() => expect(search()).toBe('?search=+a%26b+'))
    await waitFor(() => expect(urls().at(-1)).toContain('search=+a%26b+'))
  })

  it('shows the search from the URL and removes it when cleared', async () => {
    stubRecords()
    const user = userEvent.setup()
    renderWidget('?search=paper&page=2', searchableWidget)
    const box = screen.getByRole('searchbox', { name: 'Search' })
    expect(box).toHaveValue('paper')

    await user.clear(box)
    await user.keyboard('{Enter}')

    await waitFor(() => expect(search()).toBe(''))
  })

  it('keeps the search when paging and brings it back with the back button', async () => {
    stubRecords()
    const user = userEvent.setup()
    renderWidget('?search=paper', searchableWidget)
    await screen.findByRole('row', { name: /Buy paper/ })

    await user.click(await screen.findByRole('listitem', { name: '2' }))

    await waitFor(() => expect(search()).toBe('?search=paper&page=2'))
    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'x{Enter}')
    await waitFor(() => expect(search()).toBe('?search=paperx'))
    await user.click(screen.getByRole('button', { name: 'Back' }))
    await waitFor(() => expect(search()).toBe('?search=paper&page=2'))
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('paper')
  })

  it('shows no search box without a display field the user may read, and drops a search from the URL', async () => {
    const urls = stubRecords()
    const hidden: WidgetMetadata = {
      ...noteWidget,
      entity: { ...noteWidget.entity, displayField: 'name' },
    }

    renderWidget('?search=paper', hidden)

    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
    await waitFor(() => expect(search()).toBe(''))
    expect(urls().every((url) => !url.includes('search='))).toBe(true)
  })

  it('shows no search box on an entity without a display field', async () => {
    stubRecords()

    renderWidget('', noteWidget)

    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
  })

  it('shows a search box on a data source table only when the data source declares search', async () => {
    stubFetch(() => new Response(emptyRows))
    const searchable: WidgetMetadata = {
      ...noteListWidget,
      dataSource: { ...noteListWidget.dataSource!, search: true },
    }

    const without = renderWidget('', noteListWidget)
    await screen.findByText('No records yet.')
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
    without.unmount()

    renderWidget('', searchable)
    expect(await screen.findByRole('searchbox', { name: 'Search' })).toBeInTheDocument()
  })
})

describe('TableWidget import', () => {
  const importsPath = '/api/apps/E2eApp/entities/Note/imports'
  const importId = '6f0c2a54-3b7e-4d1a-9c58-2e4f7a1b9d03'
  const withImport: WidgetAccess = { ...everyAction, actions: [...everyAction.actions, 'import'] }
  const importWidget: WidgetMetadata = { ...noteWidget, access: withImport }

  function answer(state: string, errors: Record<string, string[]> = {}, status = 200) {
    return new Response(JSON.stringify({ id: importId, state, key: null, errors, createdAt: '2026-01-01T00:00:00Z' }), {
      status,
    })
  }

  /** Stubs `fetch`: the records, the upload, then each read of the import in turn, then the commit. */
  function stubImport(reads: Response[], uploadStatus = 202, commitStatus = 202) {
    const remaining = [...reads]
    const fetchMock = vi.fn(async (input: string, _init?: RequestInit) => {
      if (input === importsPath) {
        return uploadStatus === 202 ? answer('pending', {}, 202) : new Response(null, { status: uploadStatus })
      }
      if (input === `${importsPath}/${importId}/commit`) {
        return commitStatus === 202 ? answer('checked', {}, 202) : new Response(null, { status: commitStatus })
      }
      if (input === `${importsPath}/${importId}`) {
        return remaining.shift()!
      }
      return new Response(notePage, { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    return {
      records: () => fetchMock.mock.calls.filter(([url]) => url.startsWith(recordsPath)),
      uploads: () => fetchMock.mock.calls.filter(([url]) => url === importsPath),
      commits: () => fetchMock.mock.calls.filter(([url]) => url.endsWith('/commit')),
    }
  }

  async function chooseAndCheck(name = 'notes.csv') {
    await userEvent.click(await screen.findByRole('button', { name: 'Import' }))
    const dialog = await screen.findByRole('dialog')
    const input = dialog.querySelector('input[type=file]') as HTMLInputElement
    await userEvent.upload(input, new File(['title\nBuy\n'], name, { type: 'application/vnd.ms-excel' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Check' }))
    return dialog
  }

  it('shows the Import button only when the access lists import on an entity table', async () => {
    stubRecords()
    renderWidget('', noteWidget)
    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByRole('button', { name: 'Import' })).not.toBeInTheDocument()
  })

  it('shows the Import button when the access lists import', async () => {
    stubRecords()
    renderWidget('', importWidget)
    expect(await screen.findByRole('button', { name: 'Import' })).toBeInTheDocument()
  })

  it('has no Import button on a data source table', async () => {
    stubRecords()
    renderWidget('', { ...noteListWidget, access: withImport })
    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByRole('button', { name: 'Import' })).not.toBeInTheDocument()
  })

  it('lists the dry run errors by row and field label and offers no Commit', async () => {
    const { uploads, commits } = stubImport([
      answer('failed', {
        '/rows/2/values/title': ['Required.'],
        '/rows/3': ['Wrong number of cells.'],
        '/columns/3': ['Unknown field.'],
        '/columns': ['No header.'],
      }),
    ])
    renderWidget('', importWidget)

    await chooseAndCheck()

    const status = await screen.findByTestId('import-status')
    await waitFor(() => expect(status).toHaveTextContent('Row 2 · Title: Required.'))
    expect(status).toHaveTextContent('Row 3: Wrong number of cells.')
    expect(status).toHaveTextContent('Column 3: Unknown field.')
    expect(status).toHaveTextContent('File: No header.')
    expect(status).toHaveAttribute('aria-live', 'polite')
    expect(screen.queryByRole('button', { name: 'Commit' })).not.toBeInTheDocument()
    const [url, init] = uploads()[0]
    expect(url).toBe(importsPath)
    expect(new Headers(init?.headers).get('Content-Type')).toBe('text/csv')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
    expect(commits()).toHaveLength(0)
  })

  it('commits a checked import and requests the rows again once it is committed', async () => {
    const { records, commits } = stubImport([answer('checked'), answer('committed')])
    renderWidget('', importWidget)

    const dialog = await chooseAndCheck()
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Commit' }))

    expect(await screen.findByTestId('table-action-done')).toHaveTextContent('The records were imported.')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await waitFor(() => expect(records()).toHaveLength(2))
    expect(commits()).toHaveLength(1)
    expect(new Headers(commits()[0][1]?.headers).get('X-Axis-Csrf')).toBe('1')
  })

  it('refuses a file that is neither CSV nor xlsx and sends nothing', async () => {
    const { uploads } = stubImport([])
    renderWidget('', importWidget)

    await userEvent.click(await screen.findByRole('button', { name: 'Import' }))
    const dialog = await screen.findByRole('dialog')
    const input = dialog.querySelector('input[type=file]') as HTMLInputElement
    await userEvent.upload(input, new File(['x'], 'notes.txt', { type: 'text/plain' }), { applyAccept: false })

    expect(await screen.findByTestId('import-problem')).toHaveTextContent('Only a CSV or Excel file can be imported.')
    expect(within(dialog).getByRole('button', { name: 'Check' })).toBeDisabled()
    expect(uploads()).toHaveLength(0)
  })

  it.each([
    [403, 'You may not import records here.'],
    [413, 'The file is too large.'],
    [415, 'Only a CSV or Excel file can be imported.'],
    [500, 'The file could not be imported.'],
  ])('shows its own message for an upload answered with %i', async (status, message) => {
    stubImport([], status)
    renderWidget('', importWidget)

    await chooseAndCheck()

    expect(await screen.findByTestId('import-problem')).toHaveTextContent(message)
  })
})

describe('TableWidget export', () => {
  const exportsPath = `${recordsPath}/export`
  const withExport: WidgetAccess = { ...everyAction, actions: [...everyAction.actions, 'export'] }
  // The record API searches the display field, so this table has a search box.
  const searchableNotes: EntityWidgetMetadata = {
    ...noteWidget,
    entity: { ...noteWidget.entity, displayField: 'title' },
  }
  const exportWidget: WidgetMetadata = { ...searchableNotes, access: withExport }
  const createObjectURL = vi.fn((_blob: Blob) => 'blob:notes')
  const revokeObjectURL = vi.fn()

  /** Stubs `fetch`: the records, and the export with `exported` as its answer. */
  function stubExport(exported: () => Response) {
    const fetchMock = vi.fn(async (input: string) =>
      input.startsWith(exportsPath) || input.endsWith('/export') || input.includes('/export?')
        ? exported()
        : new Response(notePage, { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)
    return () => fetchMock.mock.calls.map(([url]) => url).filter((url) => url.includes('/export'))
  }

  function file(headers: Record<string, string> = {}) {
    return new Response('title\nBuy paper\n', {
      status: 200,
      headers: { 'Content-Disposition': "attachment; filename=Note.csv; filename*=UTF-8''Note.csv", ...headers },
    })
  }

  /** Opens the dropdown with the keyboard, then picks `item` with the keyboard. */
  async function chooseExport(item: string) {
    const button = await screen.findByRole('button', { name: 'Export' })
    button.focus()
    await userEvent.keyboard('{Enter}')
    await userEvent.click(await screen.findByRole('menuitem', { name: item }))
  }

  beforeEach(() => {
    createObjectURL.mockClear()
    revokeObjectURL.mockClear()
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }))
  })

  it('shows the Export button when the access lists export and hides it when it does not', async () => {
    stubRecords()
    const { unmount } = renderWidget('', exportWidget)
    expect(await screen.findByRole('button', { name: 'Export' })).toBeInTheDocument()
    unmount()

    renderWidget('', searchableNotes)
    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByRole('button', { name: 'Export' })).not.toBeInTheDocument()
  })

  it('shows the Export button on a data source table too', async () => {
    stubRecords()
    renderWidget('', { ...noteListWidget, access: withExport })
    expect(await screen.findByRole('button', { name: 'Export' })).toBeInTheDocument()
  })

  it('fetches the file with the search, the sort and the format, and no paging, and saves it under the server name', async () => {
    const exported = stubExport(() => file())
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    renderWidget('?search=pap&sort=-title&page=2&pageSize=10', exportWidget)

    await chooseExport('CSV')

    await waitFor(() => expect(exported()).toHaveLength(1))
    const address = exported()[0]
    expect(address.startsWith(`${exportsPath}?`)).toBe(true)
    expect([...new URLSearchParams(address.split('?')[1])]).toEqual([
      ['search', 'pap'],
      ['sort', '-title'],
      ['format', 'csv'],
    ])
    await waitFor(() => expect(click).toHaveBeenCalledTimes(1))
    expect((click.mock.contexts[0] as HTMLAnchorElement).download).toBe('Note.csv')
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:notes')
    expect(screen.queryByTestId('table-export-truncated')).not.toBeInTheDocument()
    expect(screen.queryByTestId('table-action-error')).not.toBeInTheDocument()
    click.mockRestore()
  })

  it('asks for xlsx from the Excel item', async () => {
    const exported = stubExport(() => file())
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    renderWidget('', exportWidget)

    await chooseExport('Excel')

    await waitFor(() => expect(exported()).toEqual([`${exportsPath}?format=xlsx`]))
    click.mockRestore()
  })

  it('shows a notice when the server cut the file at the row limit', async () => {
    stubExport(() => file({ 'Axis-Export-Truncated': 'true' }))
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    renderWidget('', exportWidget)

    await chooseExport('CSV')

    expect(await screen.findByTestId('table-export-truncated')).toHaveTextContent('The file stops at the row limit.')
    expect(click).toHaveBeenCalledTimes(1)
    click.mockRestore()
  })

  it.each([
    [403, 'You may not export these records.'],
    [409, 'A field is being recomputed.'],
    [503, 'The export took too long.'],
    [500, 'The records could not be exported.'],
  ])('shows its own message for an export answered with %i and saves nothing', async (status, message) => {
    stubExport(() => new Response(null, { status }))
    renderWidget('', exportWidget)

    await chooseExport('CSV')

    expect(await screen.findByTestId('table-action-error')).toHaveTextContent(message)
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('shows the generic message when the request fails', async () => {
    const fetchMock = vi.fn(async (input: string) => {
      if (input.includes('/export')) {
        throw new TypeError('network')
      }
      return new Response(notePage, { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderWidget('', exportWidget)

    await chooseExport('CSV')

    expect(await screen.findByTestId('table-action-error')).toHaveTextContent('The records could not be exported.')
  })
})
