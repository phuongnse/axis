import { createEvent, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { DataSourceParameter, EntityWidgetMetadata, FieldMetadata, FieldType, WidgetMetadata } from './site'
import { TableWidget } from './TableWidget'
import { TextProvider } from './TextProvider'

function field(name: string, type: FieldType, labelKey: string | null): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    computed: false,
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
    fields: null,
  }
}

const recordsPath = '/api/apps/E2eApp/entities/Note/records'

const noteWidget: EntityWidgetMetadata = {
  type: 'table',
  formPage: 'NoteForm',
  dataSource: null,
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
  formPage: 'NoteForm',
  entity: null,
  dataSource: {
    name: 'NoteList',
    rowsPath,
    entity: 'Note',
    parameters: [],
    pageSize: 10,
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
  'shell.table.create': 'New',
  'shell.table.open': 'Open',
  'shell.table.yes': 'Yes',
  'shell.table.no': 'No',
  'shell.table.empty': 'No records yet.',
  'shell.table.loadFailed': 'The records could not be loaded.',
  'note.title': 'Title',
  'note.done': 'Done',
  'note.dueAt': 'Due at',
  'note.category': 'Category',
  'note.lines': 'Lines',
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

function renderWidget(search = '', widget: WidgetMetadata = noteWidget) {
  return render(
    <MemoryRouter initialEntries={[`/e2e/notes${search}`]}>
      <TextProvider catalogs={catalogs} development={false}>
        <TableWidget sitePath="e2e" widget={widget} locale="en" />
      </TextProvider>
      <CurrentLocation />
    </MemoryRouter>,
  )
}

describe('TableWidget', () => {
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

  it('links the create button and each row to the form page', async () => {
    stubRecords()

    renderWidget()

    const row = await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.getByRole('link', { name: 'New' })).toHaveAttribute('href', '/e2e/noteform/new')
    expect(within(row).getByRole('link', { name: 'Open' })).toHaveAttribute('href', `/e2e/noteform/${noteId}`)
  })

  it('opens the form in place on a plain click of the create button', async () => {
    stubRecords()
    renderWidget()
    await screen.findByRole('row', { name: /Buy paper/ })

    await userEvent.click(screen.getByRole('link', { name: 'New' }))

    expect(screen.getByTestId('path')).toHaveTextContent('/e2e/noteform/new')
  })

  it('passes the table address with its paging and sorting to the form it opens', async () => {
    stubRecords()
    renderWidget('?pageSize=10&sort=-title')
    const row = await screen.findByRole('row', { name: /Buy paper/ })

    await userEvent.click(within(row).getByRole('link', { name: 'Open' }))

    expect(screen.getByTestId('path')).toHaveTextContent(`/e2e/noteform/${noteId}`)
    expect(screen.getByTestId('from')).toHaveTextContent('/e2e/notes?pageSize=10&sort=-title')
  })

  it('passes the table address to the create form', async () => {
    stubRecords()
    renderWidget('?page=2')
    await screen.findByRole('row', { name: /Buy paper/ })

    await userEvent.click(screen.getByRole('link', { name: 'New' }))

    expect(screen.getByTestId('path')).toHaveTextContent('/e2e/noteform/new')
    expect(screen.getByTestId('from')).toHaveTextContent('/e2e/notes?page=2')
  })

  it('keeps the link behaviour of the create button on a ctrl-click', async () => {
    stubRecords()
    renderWidget()
    await screen.findByRole('row', { name: /Buy paper/ })
    const link = screen.getByRole('link', { name: 'New' })
    const click = createEvent.click(link, { ctrlKey: true })
    // jsdom cannot open a new tab, so stop the default action once the widget has seen the click.
    const browserDefault = vi.fn((event: Event) => event.preventDefault())
    window.addEventListener('click', browserDefault)

    fireEvent(link, click)

    window.removeEventListener('click', browserDefault)
    expect(browserDefault).toHaveBeenCalled()
    expect(screen.getByTestId('path')).toHaveTextContent('/e2e/notes')
  })

  it('keeps a field named open next to the open column', async () => {
    stubRecords(
      `{"items":[{"id":"${noteId}","version":1,"values":{"open":"Monday"},"labels":{}}],"page":1,"pageSize":20,"totalCount":1}`,
    )

    renderWidget('', { ...noteWidget, entity: { ...noteWidget.entity, fields: [field('open', 'text', null)] } })

    const row = await screen.findByRole('row', { name: /Monday/ })
    expect(within(row).getByRole('link', { name: 'Open' })).toHaveAttribute('href', `/e2e/noteform/${noteId}`)
    await userEvent.click(screen.getByText('open'))
    await waitFor(() => expect(search()).toBe('?sort=open'))
    expect(screen.getByRole('columnheader', { name: 'open' })).toHaveAttribute('aria-sort', 'ascending')
  })

  it('has no create button and no open link without a form page', async () => {
    stubRecords()

    renderWidget('', { ...noteWidget, formPage: null })

    await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.queryByRole('link', { name: 'New' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Open' })).not.toBeInTheDocument()
  })

  it('sorts from page 1 and keeps parameters it does not own', async () => {
    const requests = stubRecords()
    renderWidget('?page=2&x=1')
    await screen.findByRole('row', { name: /Buy paper/ })
    expect(requests()).toEqual([`${recordsPath}?page=2`])

    await userEvent.click(screen.getByText('Title'))

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
    expect(screen.getByRole('columnheader', { name: 'Title' })).toHaveClass('ant-table-column-has-sorters')
    expect(screen.getByRole('columnheader', { name: 'Category' })).not.toHaveClass('ant-table-column-has-sorters')
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

  it('right-aligns numbers and shows them as the API wrote them', async () => {
    stubRecords(
      `{"items":[{"id":"${noteId}","version":3,"values":{"price":1250.50},"labels":{}}],"page":1,"pageSize":20,"totalCount":1}`,
    )

    renderWidget('', {
      type: 'table',
      formPage: null,
      entity: { ...noteWidget.entity, fields: [field('price', 'decimal', null)] },
      dataSource: null,
    })

    const cell = await screen.findByRole('cell', { name: '1250.50' })
    expect(cell).toHaveStyle({ textAlign: 'right' })
    expect(screen.getByRole('columnheader', { name: 'price' })).toBeInTheDocument()
  })

  it('heads a data source table with its column labels or names and requests its rows path', async () => {
    const requests = stubRecords(
      `{"items":[{"id":"${noteId}","values":{"title":"Buy paper","departmentName":"Finance"},"labels":{}}],"page":1,"pageSize":10,"totalCount":1}`,
    )

    renderWidget('?pageSize=10', noteListWidget)

    const row = await screen.findByRole('row', { name: /Buy paper/ })
    expect(screen.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Title',
      'departmentName',
      '',
    ])
    expect(within(row).getByText('Finance')).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: 'Open' })).toHaveAttribute('href', `/e2e/noteform/${noteId}`)
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
})
