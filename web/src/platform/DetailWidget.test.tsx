import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { DetailWidget } from './DetailWidget'
import type { EntityWidgetMetadata, FieldMetadata, FieldType } from './site'
import { descriptionItem } from '../test/antd'
import { TextProvider } from './TextProvider'

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
const instancesPath = '/api/apps/E2eApp/processes/NoteClose/instances'
const noteId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'

// A detail over an entity with no form, so it shows every field.
const noteWidget: EntityWidgetMetadata = {
  type: 'detail',
  dataSource: null,
  form: null,
  tasksPath: null,
  process: null,
  instancesPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  access: null,
  detail: { record: 'note' },
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
      field('priority', 'integer', null),
      {
        ...field('category', 'reference', 'note.category'),
        target: { entity: 'Category', displayField: 'name', recordsPath: '/api/apps/E2eApp/entities/Category/records' },
      },
      {
        ...field('lines', 'child-collection', 'note.lines'),
        fields: [
          field('description', 'text', 'noteLine.description'),
          field('quantity', 'integer', 'noteLine.quantity'),
        ],
      },
    ],
  },
}

// The same note laid out by a form in two sections that leave out the priority.
const sectionsWidget: EntityWidgetMetadata = {
  ...noteWidget,
  form: {
    name: 'NoteSections',
    sections: [
      {
        name: 'main',
        titleKey: 'noteSections.main',
        fields: [
          { name: 'title', readOnly: false },
          { name: 'category', readOnly: false },
        ],
      },
      {
        name: 'details',
        titleKey: 'noteSections.details',
        fields: [
          { name: 'done', readOnly: false },
          { name: 'lines', readOnly: false },
        ],
      },
    ],
    steps: [],
  },
}

const closeAction = {
  type: 'startProcess' as const,
  process: 'NoteClose',
  labelKey: 'noteClose.action',
  instancesPath,
  visibleWhen: null,
}
const openAction = {
  type: 'navigate' as const,
  page: 'Notes',
  labelKey: 'note.open',
  parameters: { note: 'id', title: 'title' },
  record: null,
  opensForm: false,
  paths: {},
  visibleWhen: null,
}

function noteJson(title: string, version = 1) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"${title}","done":true,"priority":7,"category":"${categoryId}","lines":[{"description":"Pens","quantity":2}]},"labels":{"category":"Office"}}`
}

const instanceJson = `{"id":"01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7e","process":"NoteClose","subjectId":"${noteId}","releaseId":"01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7f","state":"running"}`

const texts = {
  'shell.table.yes': 'Yes',
  'shell.table.no': 'No',
  'shell.notFound.title': 'Page not found',
  'shell.form.loadFailed': 'The record could not be loaded.',
  'shell.form.processStarted': 'The process has started.',
  'shell.form.processRunning': 'This record is already in a running process.',
  'shell.form.startFailed': 'The process could not be started.',
  'shell.navigate.failed': 'The page could not be opened.',
  'note.title': 'Title',
  'note.done': 'Done',
  'note.category': 'Category',
  'note.lines': 'Lines',
  'noteLine.description': 'Description',
  'noteLine.quantity': 'Quantity',
  'noteSections.main': 'Main',
  'noteSections.details': 'Details',
  'noteClose.action': 'Close note',
  'noteClose.cannotClose': 'Only an open note can be closed.',
  'note.open': 'Open notes',
}
const catalogs = [{ texts, fallbackTexts: texts }]

type Answer = { status: number; body: string }

/** Stubs `fetch` with answers in order, and returns the requests made. */
function stubFetch(...answers: Answer[]) {
  const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => {
    const answer = answers.shift() ?? { status: 500, body: '{}' }
    return new Response(answer.body, { status: answer.status })
  })
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url, init]) => ({ url, method: init?.method ?? 'GET', body: init?.body }))
}

function CurrentLocation() {
  const location = useLocation()
  const state = location.state as { from?: string } | null
  return (
    <>
      <output data-testid="location">{`${location.pathname}${location.search}`}</output>
      <output data-testid="from">{state?.from}</output>
    </>
  )
}

function renderDetail(widget: EntityWidgetMetadata = noteWidget) {
  return render(
    <MemoryRouter initialEntries={[`/e2e/notedetail?note=${noteId}`]}>
      <TextProvider catalogs={catalogs} development={false}>
        <DetailWidget widget={widget} sitePath="e2e" recordId={noteId} locale="en" />
      </TextProvider>
      <CurrentLocation />
    </MemoryRouter>,
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('DetailWidget', () => {
  it('shows the fields of a form in its titled sections, read-only, and leaves out the others', async () => {
    const requests = stubFetch({ status: 200, body: noteJson('Buy paper') })

    renderDetail(sectionsWidget)

    expect(await screen.findByText('Main')).toBeInTheDocument()
    expect(screen.getByText('Details')).toBeInTheDocument()
    expect(descriptionItem('Title')).toHaveTextContent('Buy paper')
    expect(descriptionItem('Category')).toHaveTextContent('Office')
    expect(descriptionItem('Done')).toHaveTextContent('Yes')
    expect(screen.queryByText('priority')).toBeNull()
    // The rows of the child collection are the only inputs, and they are read-only.
    screen.getAllByRole('textbox').forEach((input) => expect(input).toHaveAttribute('readonly'))
    expect(requests()).toEqual([{ url: `${recordsPath}/${noteId}`, method: 'GET', body: undefined }])
  })

  it('shows every field of the entity without a form, labelled by its label key or its name', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper') })

    renderDetail()

    expect(await screen.findByText('Title')).toBeInTheDocument()
    expect(descriptionItem('priority')).toHaveTextContent('7')
    expect(descriptionItem('Category')).toHaveTextContent('Office')
    expect(screen.queryByText('Main')).toBeNull()
  })

  it('shows the rows of a child collection in a read-only table', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper') })

    renderDetail()

    expect(await screen.findByLabelText('Description 1')).toHaveValue('Pens')
    expect(screen.getByLabelText('Description 1')).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Quantity 1')).toHaveValue('2')
    expect(screen.queryByRole('button', { name: 'Add row' })).toBeNull()
  })

  it('shows the label of a reference in a child row instead of its id', async () => {
    const lines = noteWidget.entity.fields.find((candidate) => candidate.name === 'lines')!
    const widget: EntityWidgetMetadata = {
      ...noteWidget,
      entity: {
        ...noteWidget.entity,
        fields: [
          {
            ...lines,
            fields: [
              ...lines.fields!,
              {
                ...field('product', 'reference', null),
                target: {
                  entity: 'Product',
                  displayField: 'name',
                  recordsPath: '/api/apps/E2eApp/entities/Product/records',
                },
              },
            ],
          },
        ],
      },
    }
    const body = `{"id":"${noteId}","version":1,"values":{"lines":[{"description":"Pens","quantity":2,"product":"${categoryId}"},{"description":"Ink","quantity":1,"product":null}]},"labels":{"lines":[{"product":"Desk"},{}]}}`
    stubFetch({ status: 200, body })

    renderDetail(widget)

    expect(await screen.findByLabelText('product 1')).toHaveValue('Desk')
    expect(screen.getByLabelText('product 2')).toHaveValue('')
  })

  it('shows the not-found page when the record API answers 404', async () => {
    stubFetch({ status: 404, body: '{}' })

    renderDetail()

    expect(await screen.findByText('Page not found')).toBeInTheDocument()
    expect(screen.queryByTestId('detail')).toBeNull()
  })

  it('shows a load error when the record cannot be loaded', async () => {
    stubFetch({ status: 500, body: '{}' })

    renderDetail()

    expect(await screen.findByTestId('detail-load-error')).toHaveTextContent('The record could not be loaded.')
  })

  it('is busy while it loads', () => {
    stubFetch({ status: 200, body: noteJson('Buy paper') })

    renderDetail()

    expect(screen.getByTestId('detail')).toHaveAttribute('aria-busy', 'true')
  })

  it('shows no buttons when the widget lists no actions', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper') })

    renderDetail()

    await screen.findByText('Title')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('starts the process once without a save, shows the success message and loads the record again', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper') },
      { status: 201, body: instanceJson },
      { status: 200, body: noteJson('Closed by the process', 2) },
    )
    renderDetail({ ...noteWidget, actions: [closeAction] })

    await userEvent.click(await screen.findByRole('button', { name: 'Close note' }))

    expect(await screen.findByTestId('detail-started')).toHaveTextContent('The process has started.')
    expect(await screen.findByText('Closed by the process')).toBeInTheDocument()
    expect(requests()).toEqual([
      { url: `${recordsPath}/${noteId}`, method: 'GET', body: undefined },
      { url: instancesPath, method: 'POST', body: `{"subjectId":"${noteId}"}` },
      { url: `${recordsPath}/${noteId}`, method: 'GET', body: undefined },
    ])
  })

  it('shows a failed start condition as its text, and a running process and other failures as platform texts', async () => {
    stubFetch(
      { status: 200, body: noteJson('Buy paper') },
      { status: 422, body: '{"errors":{"":["noteClose.cannotClose"]}}' },
      { status: 409, body: '{}' },
      { status: 500, body: '{}' },
    )
    renderDetail({ ...noteWidget, actions: [closeAction] })
    const button = await screen.findByRole('button', { name: 'Close note' })

    await userEvent.click(button)
    expect(await screen.findByText('Only an open note can be closed.')).toBeInTheDocument()
    await userEvent.click(button)
    expect(await screen.findByText('This record is already in a running process.')).toBeInTheDocument()
    await userEvent.click(button)
    expect(await screen.findByText('The process could not be started.')).toBeInTheDocument()
    expect(screen.queryByTestId('detail-started')).toBeNull()
  })

  it('opens the target of a navigate button with the record values, and keeps the detail address for going back', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper') })
    renderDetail({ ...noteWidget, actions: [openAction] })

    await userEvent.click(await screen.findByRole('button', { name: 'Open notes' }))

    await waitFor(() =>
      expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/notes?note=${noteId}&title=Buy+paper`),
    )
    expect(screen.getByTestId('from')).toHaveTextContent(`/e2e/notedetail?note=${noteId}`)
  })

  it('hides a navigate button whose expression the SPA cannot work out', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper') })
    renderDetail({ ...noteWidget, actions: [{ ...openAction, parameters: { title: 'concat(title, done)' } }] })

    await screen.findByText('Title')
    expect(screen.queryByRole('button', { name: 'Open notes' })).toBeNull()
  })

  it('shows one message and stays on the detail when a read along a path is refused', async () => {
    const categoryPath = '/api/apps/E2eApp/entities/Category/records'
    const requests = stubFetch({ status: 200, body: noteJson('Buy paper') }, { status: 403, body: '{}' })
    renderDetail({
      ...noteWidget,
      actions: [{ ...openAction, parameters: { name: 'category.name' }, paths: { category: categoryPath } }],
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Open notes' }))

    expect(await screen.findByTestId('detail-error')).toHaveTextContent('The page could not be opened.')
    expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/notedetail?note=${noteId}`)
    expect(requests().map((request) => request.url)).toEqual([
      `${recordsPath}/${noteId}`,
      `${categoryPath}/${categoryId}`,
    ])
  })
})
