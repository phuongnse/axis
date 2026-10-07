import { createEvent, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { FieldMetadata, FieldType, WidgetMetadata } from './site'
import { TableWidget } from './TableWidget'
import { TextProvider } from './TextProvider'

function field(name: string, type: FieldType, labelKey: string | null): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
  }
}

const recordsPath = '/api/apps/E2eApp/entities/Note/records'

const noteWidget: WidgetMetadata = {
  type: 'table',
  formPage: 'NoteForm',
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
}
const catalogs = [{ texts, fallbackTexts: texts }]

/** Stubs `fetch` with the record API and returns the requested URLs. */
function stubRecords(body = notePage, status = 200) {
  const fetchMock = vi.fn(async (_input: string) => new Response(body, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

function CurrentLocation() {
  const location = useLocation()
  const state = location.state as { from?: string } | null
  return (
    <>
      <output data-testid="path">{location.pathname}</output>
      <output data-testid="search">{location.search}</output>
      <output data-testid="from">{state?.from}</output>
    </>
  )
}

function search() {
  return screen.getByTestId('search').textContent
}

function renderWidget(search = '', widget = noteWidget) {
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

  it('right-aligns numbers and shows them as the API wrote them', async () => {
    stubRecords(
      `{"items":[{"id":"${noteId}","version":3,"values":{"price":1250.50},"labels":{}}],"page":1,"pageSize":20,"totalCount":1}`,
    )

    renderWidget('', {
      type: 'table',
      formPage: null,
      entity: { ...noteWidget.entity, fields: [field('price', 'decimal', null)] },
    })

    const cell = await screen.findByRole('cell', { name: '1250.50' })
    expect(cell).toHaveStyle({ textAlign: 'right' })
    expect(screen.getByRole('columnheader', { name: 'price' })).toBeInTheDocument()
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
})
