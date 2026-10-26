import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import dayjs from 'dayjs'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { formItem, hasFieldError, isRequiredLabel } from '../test/antd'
import { FormWidget } from './FormWidget'
import type { EntityWidgetMetadata, FieldMetadata, FieldType } from './site'
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
const noteId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'

// Without access the form is not shaped by grants, so every field the form shows can be written.
const noteWidget: EntityWidgetMetadata = {
  type: 'form',
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
  detail: null,
  kpi: null,
  chart: null,
  entity: {
    name: 'Note',
    labelKey: 'note.label',
    displayField: null,
    recordsPath,
    fields: [
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200 },
      { ...field('code', 'text', 'note.code'), unique: true, maxLength: 50 },
      field('priority', 'integer', 'note.priority'),
      { ...field('amount', 'decimal', 'note.amount'), precision: 18, scale: 2 },
      field('done', 'boolean', 'note.done'),
      field('dueOn', 'date', 'note.dueOn'),
      field('dueAt', 'date-time', 'note.dueAt'),
      { ...field('status', 'enum', 'note.status'), values: ['open', 'closed'] },
      {
        ...field('category', 'reference', 'note.category'),
        target: { entity: 'Category', displayField: 'name', recordsPath: '/api/apps/E2eApp/entities/Category/records' },
      },
    ],
  },
}

// A note with only a title and its lines, a child collection.
const linesWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200 },
      {
        ...field('lines', 'child-collection', 'note.lines'),
        fields: [
          { ...field('description', 'text', 'noteLine.description'), required: true, maxLength: 100 },
          field('quantity', 'integer', 'noteLine.quantity'),
        ],
      },
    ],
  },
}

// A note whose total and each line's code the server computes.
const computedWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200 },
      field('priority', 'integer', 'note.priority'),
      { ...field('amount', 'decimal', 'note.amount'), precision: 18, scale: 2 },
      { ...field('total', 'decimal', 'note.total'), precision: 18, scale: 2, computed: true },
      {
        ...field('lines', 'child-collection', 'note.lines'),
        fields: [
          { ...field('description', 'text', 'noteLine.description'), maxLength: 100 },
          { ...field('code', 'text', 'noteLine.code'), computed: true },
        ],
      },
    ],
  },
}

// A note whose amount is money in the currency its currency field holds.
const moneyWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('currency', 'enum', 'note.currency'), values: ['VND', 'USD'] },
      { ...field('amount', 'money', 'note.amount'), precision: 18, scale: 2, currencyField: 'currency' },
    ],
  },
}

// A note whose number a sequence hands out on create.
const sequenceWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('number', 'text', 'note.number'), maxLength: 20, unique: true, sequence: true },
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200 },
      field('priority', 'integer', 'note.priority'),
    ],
  },
}

// A note whose creator the server sets on create.
const creatorWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200 },
      field('priority', 'integer', 'note.priority'),
      { ...field('createdBy', 'text', 'note.createdBy'), maxLength: 100, creator: true },
    ],
  },
}

// A note laid out by a form in two sections: code and lines are read-only, and done is left out.
const sectionsWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [...noteWidget.entity.fields, ...linesWidget.entity.fields.filter((field) => field.name === 'lines')],
  },
  form: {
    name: 'NoteSections',
    sections: [
      {
        name: 'main',
        titleKey: 'noteSections.main',
        fields: [
          { name: 'title', readOnly: false },
          { name: 'status', readOnly: false },
          { name: 'code', readOnly: true },
          { name: 'category', readOnly: true },
        ],
      },
      {
        name: 'details',
        titleKey: 'noteSections.details',
        fields: [
          { name: 'priority', readOnly: false },
          { name: 'lines', readOnly: true },
        ],
      },
    ],
    steps: [],
  },
}

// A note laid out as a wizard of two steps: the title and code, then the priority.
const stepsWidget: EntityWidgetMetadata = {
  ...noteWidget,
  form: {
    name: 'NoteSteps',
    sections: [
      {
        name: 'main',
        titleKey: 'noteSections.main',
        fields: [
          { name: 'title', readOnly: false },
          { name: 'code', readOnly: false },
        ],
      },
      { name: 'details', titleKey: 'noteSections.details', fields: [{ name: 'priority', readOnly: false }] },
    ],
    steps: [
      { titleKey: 'noteSteps.main', sections: ['main'] },
      { titleKey: 'noteSteps.details', sections: ['details'] },
    ],
  },
}

const instancesPath = '/api/apps/E2eApp/processes/NoteClose/instances'

// A note form with an action that starts the NoteClose process.
const actionWidget: EntityWidgetMetadata = {
  ...noteWidget,
  actions: [
    { type: 'startProcess', process: 'NoteClose', labelKey: 'noteClose.action', instancesPath, visibleWhen: null },
  ],
}

const instanceJson = `{"id":"01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7e","process":"NoteClose","subjectId":"${noteId}","releaseId":"01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7f","state":"running"}`

function sectionsJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"Buy paper","code":"N-1","priority":7,"done":true,"category":"${categoryId}","lines":[{"description":"Pens","quantity":2}]},"labels":{"category":"Office"}}`
}

function sequenceJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"number":"PR-2026-00042","title":"Buy paper","priority":2},"labels":{}}`
}

function creatorJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"Buy paper","priority":2,"createdBy":"anna"},"labels":{}}`
}

function computedJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"Buy paper","priority":2,"amount":1.50,"total":3.00,"lines":[{"description":"Pens","code":"PENS"}]},"labels":{}}`
}

function moneyJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"currency":"USD","amount":1.50},"labels":{}}`
}

function linesJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"Buy paper","lines":[{"description":"Pens","quantity":2},{"description":"Ink","quantity":1}]},"labels":{}}`
}

function noteJson(title: string, version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"${title}","code":null,"priority":7,"amount":12.50,"done":false,"dueOn":null,"dueAt":"2026-10-06T02:00:00.123456Z","status":null,"category":"${categoryId}"},"labels":{"category":"Office"}}`
}

const categoryPageJson = `{"items":[{"id":"${categoryId}","version":1,"values":{"name":"Office"},"labels":{}}],"page":1,"pageSize":20,"totalCount":1}`

const texts = {
  'shell.form.save': 'Save',
  'shell.form.cancel': 'Cancel',
  'shell.form.next': 'Next',
  'shell.form.back': 'Back',
  'shell.form.reload': 'Reload',
  'shell.form.conflict': 'This record was changed since you opened it.',
  'shell.form.saveFailed': 'The record could not be saved.',
  'shell.form.loadFailed': 'The record could not be loaded.',
  'shell.form.choose': 'Choose',
  'shell.form.clear': 'Clear',
  'shell.form.addRow': 'Add row',
  'shell.form.removeRow': 'Remove',
  'shell.lookup.search': 'Search',
  'shell.table.empty': 'No records yet.',
  'shell.table.loadFailed': 'The records could not be loaded.',
  'shell.notFound.title': 'Page not found',
  'shell.history.title': 'History',
  'shell.history.empty': 'No history yet.',
  'shell.history.fields': 'Changed',
  'shell.history.action.record.updated': 'Updated',
  'note.number': 'Number',
  'note.createdBy': 'Created by',
  'note.title': 'Title',
  'note.code': 'Code',
  'note.priority': 'Priority',
  'note.amount': 'Amount',
  'note.currency': 'Currency',
  'note.done': 'Done',
  'note.dueOn': 'Due on',
  'note.dueAt': 'Due at',
  'note.status': 'Status',
  'note.category': 'Category',
  'note.lines': 'Lines',
  'noteLine.description': 'Description',
  'noteLine.quantity': 'Quantity',
  'noteLine.code': 'Line code',
  'note.total': 'Total',
  'note.amountPositive': 'Amount must be positive.',
  'noteSections.main': 'Main',
  'noteSections.details': 'Details',
  'noteSteps.main': 'Step one',
  'noteSteps.details': 'Step two',
  'shell.form.processStarted': 'The process has started.',
  'shell.form.processRunning': 'This record is already in a running process.',
  'shell.form.startFailed': 'The process could not be started.',
  'shell.navigate.failed': 'The page could not be opened.',
  'noteClose.action': 'Close note',
  'noteClose.cannotClose': 'Only an open note can be closed.',
}
const catalogs = [{ texts, fallbackTexts: texts }]

/** An answer that never arrives: `fetch` rejects as on a lost connection. */
const networkError = 'networkError'

type Answer = { status: number; body: string } | typeof networkError

const historyPath = `${recordsPath}/${noteId}/history?page=1&pageSize=20`

const historyJson = `{"items":[{"id":"01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7d","occurredAt":"2026-10-06T02:05:00.123456Z","actor":"anna","actorName":"Anna Admin","action":"record.updated","processInstanceId":null,"details":{"version":2,"fields":["title"]}}],"page":1,"pageSize":20,"totalCount":1}`

const statePath = '/api/apps/E2eApp/forms/NoteSections/state'

const emptyStateJson = '{"sections":[],"fields":{},"errors":{}}'

/** The answers of the form state endpoint in order. A deferred answer is a promise the test resolves. */
let stateAnswers: (string | Promise<string>)[] = []

/**
 * Stubs `fetch` with answers in order, and returns the requests made. The history panel and the
 * form state calls load on their own, so a history or state request takes no answer from the queue
 * and is not listed: a history request gets one entry, and a state request the next of
 * `stateAnswers`, or an empty answer.
 */
function stubFetch(...answers: Answer[]) {
  const fetchMock = vi.fn(async (input: string, _init?: RequestInit) => {
    if (input.includes('/history?')) {
      return new Response(historyJson, { status: 200 })
    }
    if (input.endsWith('/state')) {
      return new Response(await (stateAnswers.shift() ?? emptyStateJson), { status: 200 })
    }
    const answer = answers.shift() ?? { status: 500, body: '{}' }
    if (answer === networkError) {
      throw new TypeError('Failed to fetch')
    }
    return new Response(answer.body, { status: answer.status })
  })
  vi.stubGlobal('fetch', fetchMock)
  return () =>
    fetchMock.mock.calls
      .filter(([url]) => !url.includes('/history?') && !url.endsWith('/state'))
      .map(([url, init]) => ({ url, method: init?.method ?? 'GET', body: init?.body }))
}

/** The `Idempotency-Key` of each start request made, in order. */
function startKeys() {
  return vi
    .mocked(fetch)
    .mock.calls.filter(([url]) => String(url) === instancesPath)
    .map(([, init]) => new Headers(init?.headers).get('Idempotency-Key'))
}

/** The form state requests made, with the parsed body of each. */
function stateRequests() {
  return vi
    .mocked(fetch)
    .mock.calls.filter(([url]) => String(url).endsWith('/state'))
    .map(([url, init]) => ({
      url: String(url),
      method: init?.method,
      csrf: new Headers(init?.headers).get('X-Axis-Csrf'),
      body: JSON.parse(String(init?.body)) as unknown,
    }))
}

/** The URLs of the history requests made. */
function historyRequests() {
  return vi
    .mocked(fetch)
    .mock.calls.map(([url]) => String(url))
    .filter((url) => url.includes('/history?'))
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

function renderForm(recordId: string | null = null, widget: EntityWidgetMetadata = noteWidget) {
  const path = `/e2e/noteform/${recordId ?? 'new'}`
  return render(
    <MemoryRouter initialEntries={[{ pathname: path, state: { from: '/e2e/notes?pageSize=10' } }]}>
      {/* Without motion the lookup dialog leaves the document at once, as jsdom runs no animations. */}
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <FormWidget
            widget={widget}
            sitePath="e2e"
            pages={['Notes', 'NoteForm']}
            recordId={recordId}
            returnTo="/e2e/notes?pageSize=10"
            locale="en"
          />
        </TextProvider>
      </ConfigProvider>
      <CurrentLocation />
    </MemoryRouter>,
  )
}

describe('FormWidget', () => {
  beforeEach(() => {
    stateAnswers = []
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows one input per field with its label, and the reference label read-only', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })

    renderForm(noteId)

    expect(await screen.findByLabelText('Title')).toHaveValue('Buy paper')
    expect(screen.getByLabelText('Title')).toHaveAttribute('maxlength', '200')
    expect(screen.getByLabelText('Priority')).toHaveValue('7')
    expect(screen.getByLabelText('Amount')).toHaveValue('12.50')
    expect(screen.getByLabelText('Amount')).toHaveAttribute('inputmode', 'decimal')
    expect(screen.getByLabelText('Done')).not.toBeChecked()
    // The picker shows the instant in the browser's time zone, to the second.
    expect(screen.getByLabelText('Due at')).toHaveValue(dayjs('2026-10-06T02:00:00Z').format('YYYY-MM-DD HH:mm:ss'))
    expect(screen.getByLabelText('Category')).toHaveValue('Office')
    expect(screen.getByLabelText('Category')).toHaveAttribute('readonly')
  })

  it('lists the choices of an enum as options, and marks the chosen one as selected', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId)
    const status = await screen.findByRole('combobox', { name: 'Status' })

    await userEvent.click(status)
    const listbox = await screen.findByRole('listbox')
    expect(within(listbox).getAllByRole('option')).toHaveLength(2)
    expect(within(listbox).getByRole('option', { name: 'open' })).toHaveAttribute('aria-selected', 'false')
    expect(within(listbox).getByRole('option', { name: 'closed' })).toHaveAttribute('aria-selected', 'false')

    await userEvent.click(within(listbox).getByRole('option', { name: 'closed' }))
    await userEvent.click(status)

    const reopened = await screen.findByRole('listbox')
    expect(within(reopened).getByRole('option', { name: 'closed' })).toHaveAttribute('aria-selected', 'true')
    expect(within(reopened).getByRole('option', { name: 'open' })).toHaveAttribute('aria-selected', 'false')
  })

  it('names the group of a reference input after its field, to find its Choose button', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })

    renderForm(noteId)

    const group = await screen.findByTestId('reference-category')
    expect(within(group).getByLabelText('Category')).toHaveValue('Office')
    expect(within(group).getByRole('button', { name: 'Choose' })).toBeInTheDocument()
  })

  it('shows the history of a saved record below the form', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })

    renderForm(noteId)

    const entry = await screen.findByTestId('history-entry')
    expect(screen.getByTestId('record-history')).toHaveTextContent('History')
    expect(entry).toHaveTextContent('Updated')
    expect(entry).toHaveTextContent('Anna Admin')
    expect(entry).toHaveTextContent('Changed: Title')
    expect(historyRequests()).toEqual([historyPath])
  })

  it('shows no history for a new record', async () => {
    stubFetch()
    renderForm()

    expect(await screen.findByLabelText('Title')).toHaveValue('')
    expect(screen.queryByTestId('record-history')).toBeNull()
    expect(historyRequests()).toEqual([])
  })

  it('marks required fields without blocking submit', async () => {
    stubFetch()
    renderForm()

    // Ant Design marks a required item by a class on its label; the server still decides.
    expect(isRequiredLabel(screen.getByText('Title', { selector: 'label' }))).toBe(true)
    expect(isRequiredLabel(screen.getByText('Code', { selector: 'label' }))).toBe(false)
  })

  it('confirms a picker value on Enter without sending the form', async () => {
    const requests = stubFetch()
    renderForm()

    await userEvent.type(screen.getByLabelText('Due on'), '2026-10-07{Enter}')

    expect(screen.getByLabelText('Due on')).toHaveValue('2026-10-07')
    expect(requests()).toEqual([])
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/noteform/new')
  })

  it('creates a record with numbers as typed and returns to the table', async () => {
    const requests = stubFetch({ status: 201, body: noteJson('Buy paper', 1) })
    renderForm()

    await userEvent.type(screen.getByLabelText('Title'), 'Buy paper')
    await userEvent.type(screen.getByLabelText('Priority'), '7')
    await userEvent.type(screen.getByLabelText('Amount'), '12.50')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/e2e/notes?pageSize=10'))
    expect(requests()).toEqual([
      { url: recordsPath, method: 'POST', body: '{"values":{"title":"Buy paper","priority":7,"amount":12.50}}' },
    ])
  })

  it('sends the version and only the changed fields on edit', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 200, body: noteJson('New', 2) },
    )
    renderForm(noteId)
    const title = await screen.findByLabelText('Title')

    await userEvent.clear(title)
    await userEvent.type(title, 'New')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"title":"New"}}',
    })
  })

  it('shows field errors under their field and other errors above the form', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"":["Must be a JSON object."],"/values/title":["Required."]}}',
    })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Required.')
    expect(screen.getByTestId('form-error')).toHaveTextContent('Must be a JSON object.')
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/noteform/new')
  })

  it('shows a validation text key as its text and a fixed message as is', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"/values/amount":["note.amountPositive"],"/values/title":["Required."]}}',
    })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('field-error-amount')).toHaveTextContent('Amount must be positive.')
    expect(screen.getByTestId('field-error-title')).toHaveTextContent('Required.')
  })

  it('shows a stale version as a conflict and reloads the current values', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 409, body: '{"title":"Conflict","status":409}' },
      { status: 200, body: noteJson('Changed elsewhere', 2) },
    )
    renderForm(noteId)
    const title = await screen.findByLabelText('Title')
    await userEvent.type(title, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('form-conflict')).toHaveTextContent('This record was changed since you opened it.')
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }))

    await waitFor(() => expect(screen.getByLabelText('Title')).toHaveValue('Changed elsewhere'))
    expect(screen.queryByTestId('form-conflict')).not.toBeInTheDocument()
    expect(requests()[2]).toEqual({ url: `${recordsPath}/${noteId}`, method: 'GET', body: undefined })
  })

  it('shows a duplicate unique value under its field', async () => {
    stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 409, body: '{"title":"Conflict","status":409,"errors":{"/values/code":["Must be unique."]}}' },
    )
    renderForm(noteId)
    await userEvent.type(await screen.findByLabelText('Code'), 'B-1')

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('field-error-code')).toHaveTextContent('Must be unique.')
    expect(screen.queryByTestId('form-conflict')).not.toBeInTheDocument()
  })

  it('picks a reference from the lookup and sends its id', async () => {
    const requests = stubFetch({ status: 200, body: categoryPageJson }, { status: 201, body: noteJson('Buy paper', 1) })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose' }))
    // The dialog renders into the document body, outside the form.
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(await within(dialog).findByText('Office'))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByLabelText('Category')).toHaveValue('Office')
    expect(requests()[0]).toEqual({
      url: '/api/apps/E2eApp/entities/Category/records?sort=name',
      method: 'GET',
      body: undefined,
    })

    await userEvent.type(screen.getByLabelText('Title'), 'Buy paper')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: recordsPath,
      method: 'POST',
      body: `{"values":{"title":"Buy paper","category":"${categoryId}"}}`,
    })
  })

  it('leaves a reference out when the record already set is picked again', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 200, body: categoryPageJson },
      { status: 200, body: noteJson('New', 2) },
    )
    renderForm(noteId)
    const title = await screen.findByLabelText('Title')

    await userEvent.click(screen.getByRole('button', { name: 'Choose' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Office'))
    await userEvent.type(title, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(3))
    expect(requests()[2].body).toBe('{"version":1,"values":{"title":"Buy paper!"}}')
  })

  it('reloads the lookup from page 1 when the search changes', async () => {
    const longCategoryPageJson = categoryPageJson.replace('"totalCount":1', '"totalCount":45')
    const requests = stubFetch(
      { status: 200, body: longCategoryPageJson },
      { status: 200, body: longCategoryPageJson },
      { status: 200, body: longCategoryPageJson },
    )
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose' }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText('Office')
    await userEvent.click(within(dialog).getByTitle('2'))
    await waitFor(() => expect(requests()).toHaveLength(2))
    await userEvent.type(within(dialog).getByLabelText('Search'), 'off')

    // The search applies once typing pauses, so the three key presses send one request.
    await waitFor(() => expect(requests()).toHaveLength(3))
    expect(requests()[1].url).toMatch(/\?page=2&sort=name$/)
    expect(requests()[2].url).toBe('/api/apps/E2eApp/entities/Category/records?search=off&sort=name')
    await new Promise((resolve) => setTimeout(resolve, 400))
    expect(requests()).toHaveLength(3)
  })

  it('shows the lookup error when the target records cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubFetch({ status: 500, body: '{}' })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose' }))

    expect(await screen.findByTestId('reference-lookup-error')).toHaveTextContent('The records could not be loaded.')
  })

  it('clears a reference and sends null', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 200, body: noteJson('Buy paper', 2) },
    )
    renderForm(noteId)
    expect(await screen.findByLabelText('Category')).toHaveValue('Office')

    await userEvent.click(screen.getByRole('button', { name: 'Clear' }))

    expect(screen.getByLabelText('Category')).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"category":null}}',
    })
  })

  it('shows no clear button for a required reference or an empty one', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    const requiredWidget: EntityWidgetMetadata = {
      ...noteWidget,
      entity: {
        ...noteWidget.entity,
        fields: noteWidget.entity.fields.map((f) => (f.name === 'category' ? { ...f, required: true } : f)),
      },
    }
    const { unmount } = renderForm(noteId, requiredWidget)
    expect(await screen.findByLabelText('Category')).toHaveValue('Office')
    expect(screen.getByRole('button', { name: 'Choose' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
    unmount()

    stubFetch()
    renderForm()
    expect(screen.getByLabelText('Category')).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
  })

  it('has the choose, clear and lookup search texts in every platform locale', () => {
    for (const catalog of [platformEnglish, platformVietnamese]) {
      expect(catalog).toHaveProperty(['shell.form.choose'])
      expect(catalog).toHaveProperty(['shell.form.clear'])
      expect(catalog).toHaveProperty(['shell.lookup.search'])
    }
  })

  it('sends added rows with numbers as typed and without their null fields', async () => {
    const requests = stubFetch({ status: 201, body: linesJson(1) })
    renderForm(null, linesWidget)

    await userEvent.type(screen.getByLabelText('Title'), 'Buy paper')
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.type(screen.getByLabelText('Description 1'), 'Pens')
    await userEvent.type(screen.getByLabelText('Quantity 1'), '12')
    await userEvent.type(screen.getByLabelText('Description 2'), 'Ink')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(1))
    expect(requests()[0]).toEqual({
      url: recordsPath,
      method: 'POST',
      body: '{"values":{"title":"Buy paper","lines":[{"description":"Pens","quantity":12},{"description":"Ink"}]}}',
    })
  })

  it('sends the version and the whole remaining row list when rows change', async () => {
    const requests = stubFetch({ status: 200, body: linesJson(1) }, { status: 200, body: linesJson(2) })
    renderForm(noteId, linesWidget)
    const quantity = await screen.findByLabelText('Quantity 1')
    expect(quantity).toHaveValue('2')
    expect(screen.getByLabelText('Description 2')).toHaveValue('Ink')

    await userEvent.clear(quantity)
    await userEvent.type(quantity, '3')
    await userEvent.click(screen.getAllByRole('button', { name: 'Remove' })[1])
    expect(screen.queryByLabelText('Description 2')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"lines":[{"description":"Pens","quantity":3}]}}',
    })
  })

  it('leaves the rows out when an edit to them is undone', async () => {
    const requests = stubFetch({ status: 200, body: linesJson(1) }, { status: 200, body: linesJson(2) })
    renderForm(noteId, linesWidget)
    const quantity = await screen.findByLabelText('Quantity 1')

    await userEvent.type(quantity, '5{Backspace}')
    await userEvent.type(screen.getByLabelText('Title'), '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1].body).toBe('{"version":1,"values":{"title":"Buy paper!"}}')
  })

  it('shows a row error on its cell, and clears cell errors when a row is added', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"/values/lines":["Too many rows."],"/values/lines/1/quantity":["Must be an integer."],"/values/lines/7/quantity":["Out of range."]}}',
    })
    renderForm(null, linesWidget)
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.type(screen.getByLabelText('Quantity 2'), 'x')

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('field-error-lines-1-quantity')).toHaveTextContent('Must be an integer.')
    expect(hasFieldError(screen.getByLabelText('Quantity 2'))).toBe(true)
    expect(hasFieldError(screen.getByLabelText('Quantity 1'))).toBe(false)
    expect(screen.getByTestId('field-error-lines')).toHaveTextContent('Too many rows.')
    // A row the form does not have cannot hold the error, so it shows above the form.
    expect(screen.getByTestId('form-error')).toHaveTextContent('Out of range.')

    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))

    expect(screen.queryByTestId('field-error-lines-1-quantity')).not.toBeInTheDocument()
  })

  it('shows a computed field and a computed cell read-only with the loaded value, and never sends them', async () => {
    const requests = stubFetch({ status: 200, body: computedJson(1) }, { status: 200, body: computedJson(2) })
    renderForm(noteId, computedWidget)

    const total = await screen.findByLabelText('Total')
    expect(total).toHaveValue('3.00')
    expect(total).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Line code 1')).toHaveValue('PENS')
    expect(screen.getByLabelText('Line code 1')).toHaveAttribute('readonly')

    await userEvent.type(total, '9')
    const priority = screen.getByLabelText('Priority')
    await userEvent.clear(priority)
    await userEvent.type(priority, '3')
    await userEvent.type(screen.getByLabelText('Description 1'), '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"priority":3,"lines":[{"description":"Pens!"}]}}',
    })
  })

  it('shows the currency beside a money input and sends the typed amount as a plain number', async () => {
    const requests = stubFetch({ status: 200, body: moneyJson(1) }, { status: 200, body: moneyJson(2) })
    renderForm(noteId, moneyWidget)

    const amount = await screen.findByLabelText('Amount')
    expect(amount).toHaveValue('1.50')
    expect(within(amount.closest('.ant-space-compact') as HTMLElement).getByText('USD')).toBeInTheDocument()

    await userEvent.clear(amount)
    await userEvent.type(amount, '1250.50')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"amount":1250.50}}',
    })
  })

  it('shows no currency beside a money input whose currency field is empty', async () => {
    stubFetch({ status: 200, body: moneyJson(1).replace('"USD"', 'null') })
    renderForm(noteId, moneyWidget)

    const amount = await screen.findByLabelText('Amount')
    expect(amount.closest('.ant-space-compact')).toBeNull()
  })

  it('leaves computed fields out of a create body', async () => {
    const requests = stubFetch({ status: 201, body: computedJson(1) })
    renderForm(null, computedWidget)

    expect(screen.getByLabelText('Total')).toHaveValue('')
    await userEvent.type(screen.getByLabelText('Title'), 'Buy paper')
    await userEvent.type(screen.getByLabelText('Amount'), '1.50')
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.type(screen.getByLabelText('Description 1'), 'Pens')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(1))
    expect(requests()[0]).toEqual({
      url: recordsPath,
      method: 'POST',
      body: '{"values":{"title":"Buy paper","amount":1.50,"lines":[{"description":"Pens"}]}}',
    })
  })

  it('shows a sequence field empty and read-only on create, and leaves it out of the body', async () => {
    const requests = stubFetch({ status: 201, body: sequenceJson(1) })
    renderForm(null, sequenceWidget)

    const number = screen.getByLabelText('Number')
    expect(number).toHaveValue('')
    expect(number).toHaveAttribute('readonly')
    await userEvent.type(screen.getByLabelText('Title'), 'Buy paper')
    await userEvent.type(number, 'PR-1')
    expect(number).toHaveValue('')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(1))
    expect(requests()[0]).toEqual({
      url: recordsPath,
      method: 'POST',
      body: '{"values":{"title":"Buy paper"}}',
    })
  })

  it('shows a sequence field read-only with the loaded value on edit, and leaves it out of the body', async () => {
    const requests = stubFetch({ status: 200, body: sequenceJson(1) }, { status: 200, body: sequenceJson(2) })
    renderForm(noteId, sequenceWidget)

    const number = await screen.findByLabelText('Number')
    await waitFor(() => expect(number).toHaveValue('PR-2026-00042'))
    expect(number).toHaveAttribute('readonly')
    await userEvent.type(number, '9')
    const priority = screen.getByLabelText('Priority')
    await userEvent.clear(priority)
    await userEvent.type(priority, '3')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"priority":3}}',
    })
  })

  it('shows a creator field read-only with the loaded value on edit, and leaves it out of the body', async () => {
    const requests = stubFetch({ status: 200, body: creatorJson(1) }, { status: 200, body: creatorJson(2) })
    renderForm(noteId, creatorWidget)

    const createdBy = await screen.findByLabelText('Created by')
    await waitFor(() => expect(createdBy).toHaveValue('anna'))
    expect(createdBy).toHaveAttribute('readonly')
    await userEvent.type(createdBy, 'x')
    expect(createdBy).toHaveValue('anna')
    const priority = screen.getByLabelText('Priority')
    await userEvent.clear(priority)
    await userEvent.type(priority, '3')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"priority":3}}',
    })
  })

  it('shows the sections of a form under their titles, with only the fields they list', async () => {
    stubFetch({ status: 200, body: sectionsJson(1) })
    renderForm(noteId, sectionsWidget)

    expect(await screen.findByLabelText('Title')).toHaveValue('Buy paper')
    expect(screen.getAllByRole('heading').map((heading) => heading.textContent)).toEqual(['Main', 'Details'])
    expect(screen.getByLabelText('Code')).toHaveValue('N-1')
    expect(screen.getByLabelText('Priority')).toHaveValue('7')
    expect(screen.queryByLabelText('Done')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Amount')).not.toBeInTheDocument()
  })

  it('shows read-only fields and rows without letting them change, and never sends them', async () => {
    const requests = stubFetch({ status: 200, body: sectionsJson(1) }, { status: 200, body: sectionsJson(2) })
    renderForm(noteId, sectionsWidget)

    const code = await screen.findByLabelText('Code')
    expect(code).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Category')).toHaveValue('Office')
    expect(screen.queryByRole('button', { name: 'Choose' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Description 1')).toHaveValue('Pens')
    expect(screen.getByLabelText('Description 1')).toHaveAttribute('readonly')
    expect(screen.queryByRole('button', { name: 'Add row' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument()

    await userEvent.type(code, '9')
    await userEvent.type(screen.getByLabelText('Description 1'), '!')
    const priority = screen.getByLabelText('Priority')
    await userEvent.clear(priority)
    await userEvent.type(priority, '3')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}?form=NoteSections`,
      method: 'PATCH',
      body: '{"version":1,"values":{"priority":3}}',
    })
  })

  it('shows a field the user may not write read-only, from the create fields on a new record and the update fields on a saved one', async () => {
    const fieldedWidget: EntityWidgetMetadata = {
      ...noteWidget,
      access: { actions: ['read', 'create', 'update'], createFields: ['title', 'priority'], updateFields: ['title'] },
    }
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 200, body: noteJson('New', 2) },
    )
    const { unmount } = renderForm(null, fieldedWidget)
    expect(screen.getByLabelText('Priority')).not.toHaveAttribute('readonly')
    expect(screen.getByLabelText('Code')).toHaveAttribute('readonly')
    unmount()

    renderForm(noteId, fieldedWidget)
    const title = await screen.findByLabelText('Title')
    const priority = screen.getByLabelText('Priority')
    expect(priority).toHaveAttribute('readonly')
    expect(screen.queryByRole('button', { name: 'Choose' })).not.toBeInTheDocument()

    await userEvent.type(priority, '3')
    await userEvent.clear(title)
    await userEvent.type(title, 'New')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"title":"New"}}',
    })
  })

  it('has no save button and shows every field read-only without the grant for the write', async () => {
    const readerWidget: EntityWidgetMetadata = {
      ...noteWidget,
      access: { actions: ['read', 'update'], createFields: [], updateFields: ['title'] },
    }
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    const { unmount } = renderForm(null, readerWidget)
    expect(screen.getByLabelText('Title')).toHaveAttribute('readonly')
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
    unmount()

    renderForm(noteId, readerWidget)
    expect(await screen.findByLabelText('Title')).not.toHaveAttribute('readonly')
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })

  it('shows an error on a field the form leaves out above the form', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"/values/done":["Required."],"/values/title":["Too long."]}}',
    })
    renderForm(null, sectionsWidget)

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Too long.')
    expect(screen.getByTestId('form-error')).toHaveTextContent('Required.')
  })

  describe('form state', () => {
    const hidePriority =
      '{"sections":[],"fields":{"priority":{"visible":false,"required":false,"readOnly":false}},"errors":{}}'

    function renderWithTimers(recordId: string | null = null) {
      vi.useFakeTimers({ shouldAdvanceTime: true })
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
      renderForm(recordId, sectionsWidget)
      return user
    }

    async function chooseStatus(user: ReturnType<typeof userEvent.setup>, option: string) {
      await user.click(screen.getByRole('combobox', { name: 'Status' }))
      await user.click(await screen.findByRole('option', { name: option }))
    }

    it('asks at once after a choice, and 300 ms after typing stops', async () => {
      stubFetch()
      const user = renderWithTimers()
      await waitFor(() => expect(stateRequests()).toHaveLength(1))
      expect(stateRequests()[0]).toEqual({
        url: statePath,
        method: 'POST',
        csrf: '1',
        body: { recordId: null, values: {}, touched: [] },
      })

      await chooseStatus(user, 'closed')
      await waitFor(() => expect(stateRequests()).toHaveLength(2))
      expect(stateRequests()[1].body).toEqual({ recordId: null, values: { status: 'closed' }, touched: ['status'] })

      await user.type(screen.getByLabelText('Priority'), '123')
      expect(stateRequests()).toHaveLength(2)
      // The clock also follows real time, so the checks keep a margin around 300 ms.
      await act(() => vi.advanceTimersByTimeAsync(200))
      expect(stateRequests()).toHaveLength(2)
      await act(() => vi.advanceTimersByTimeAsync(100))
      await waitFor(() => expect(stateRequests()).toHaveLength(3))
      expect(stateRequests()[2].body).toEqual({
        recordId: null,
        values: { status: 'closed', priority: 123 },
        touched: ['status', 'priority'],
      })
      await act(() => vi.advanceTimersByTimeAsync(1000))
      expect(stateRequests()).toHaveLength(3)
    })

    it('hides, requires and locks fields as the answer says, and drops an answer that a newer call overtook', async () => {
      let answerLate: (answer: string) => void = () => {}
      const late = new Promise<string>((resolve) => (answerLate = resolve))
      stateAnswers = [
        emptyStateJson,
        late,
        '{"sections":[],"fields":{"priority":{"visible":false,"required":false,"readOnly":false},"status":{"visible":true,"required":true,"readOnly":false},"title":{"visible":true,"required":true,"readOnly":true}},"errors":{}}',
      ]
      stubFetch()
      const user = renderWithTimers()
      await waitFor(() => expect(stateRequests()).toHaveLength(1))
      expect(screen.getByLabelText('Priority')).toBeInTheDocument()
      expect(isRequiredLabel(screen.getByText('Status'))).toBe(false)
      expect(screen.getByLabelText('Title')).not.toHaveAttribute('readonly')

      await chooseStatus(user, 'closed')
      await waitFor(() => expect(stateRequests()).toHaveLength(2))
      // The answer to the second call is pending, so the states stay.
      expect(screen.getByLabelText('Priority')).toBeInTheDocument()

      await chooseStatus(user, 'open')
      await waitFor(() => expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument())
      expect(isRequiredLabel(screen.getByText('Status'))).toBe(true)
      expect(screen.getByLabelText('Title')).toHaveAttribute('readonly')

      await act(async () => {
        answerLate('{"sections":[],"fields":{"status":{"visible":true,"required":false,"readOnly":false}},"errors":{}}')
        await vi.advanceTimersByTimeAsync(50)
      })
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      expect(isRequiredLabel(screen.getByText('Status'))).toBe(true)
      expect(screen.getByLabelText('Title')).toHaveAttribute('readonly')
    })

    it('never sends a hidden field, and names the form on save', async () => {
      stateAnswers = [emptyStateJson, hidePriority, hidePriority]
      const requests = stubFetch({ status: 201, body: noteJson('Pen', 1) })
      const user = renderWithTimers()
      await waitFor(() => expect(stateRequests()).toHaveLength(1))

      await user.type(screen.getByLabelText('Priority'), '5')
      await act(() => vi.advanceTimersByTimeAsync(300))
      await waitFor(() => expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument())
      expect(stateRequests()[1].body).toMatchObject({ values: { priority: 5 } })

      await user.type(screen.getByLabelText('Title'), 'Pen')
      await act(() => vi.advanceTimersByTimeAsync(300))
      await waitFor(() => expect(stateRequests()).toHaveLength(3))
      expect(stateRequests()[2].body).toEqual({
        recordId: null,
        values: { title: 'Pen' },
        touched: ['priority', 'title'],
      })

      await user.click(screen.getByRole('button', { name: 'Save' }))
      await waitFor(() => expect(requests()).toHaveLength(1))
      expect(requests()[0]).toEqual({
        url: `${recordsPath}?form=NoteSections`,
        method: 'POST',
        body: '{"values":{"title":"Pen"}}',
      })
    })

    describe('defaults', () => {
      const withDefaults = (defaults: string) => `{"sections":[],"fields":{},"errors":{},"defaults":${defaults}}`

      it('fills untouched empty fields of a new record, keeps a typed value and does not send a default', async () => {
        let answerFirst: (answer: string) => void = () => {}
        const first = new Promise<string>((resolve) => (answerFirst = resolve))
        stateAnswers = [first]
        const requests = stubFetch({ status: 201, body: noteJson('Pen', 1) })
        const user = renderWithTimers()
        await waitFor(() => expect(stateRequests()).toHaveLength(1))

        await user.type(screen.getByLabelText('Title'), 'Pen')
        await act(async () => {
          answerFirst(withDefaults('{"title":"Untitled","priority":3}'))
          await vi.advanceTimersByTimeAsync(50)
        })
        expect(screen.getByLabelText('Title')).toHaveValue('Pen')
        await waitFor(() => expect(screen.getByLabelText('Priority')).toHaveValue('3'))

        await act(() => vi.advanceTimersByTimeAsync(300))
        await waitFor(() => expect(stateRequests()).toHaveLength(2))
        expect(stateRequests()[1].body).toEqual({ recordId: null, values: { title: 'Pen' }, touched: ['title'] })

        await user.click(screen.getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(requests()).toHaveLength(1))
        expect(requests()[0].body).toBe('{"values":{"title":"Pen"}}')
      })

      it('sends null when the user clears a default', async () => {
        stateAnswers = [withDefaults('{"priority":3}')]
        const requests = stubFetch({ status: 201, body: noteJson('Pen', 1) })
        const user = renderWithTimers()
        await waitFor(() => expect(screen.getByLabelText('Priority')).toHaveValue('3'))

        await user.clear(screen.getByLabelText('Priority'))
        await user.type(screen.getByLabelText('Title'), 'Pen')
        await user.click(screen.getByRole('button', { name: 'Save' }))
        await waitFor(() => expect(requests()).toHaveLength(1))
        expect(requests()[0].body).toBe('{"values":{"title":"Pen","priority":null}}')
      })

      it('ignores the defaults of a saved record', async () => {
        stateAnswers = [withDefaults('{"priority":3,"status":"open"}')]
        stubFetch({ status: 200, body: sectionsJson(1) })
        const user = renderWithTimers(noteId)
        await waitFor(() => expect(stateRequests()).toHaveLength(1))

        await waitFor(() => expect(screen.getByLabelText('Priority')).toHaveValue('7'))
        await act(() => vi.advanceTimersByTimeAsync(50))
        expect(screen.getByLabelText('Priority')).toHaveValue('7')
        await user.click(screen.getByRole('combobox', { name: 'Status' }))
        const listbox = await screen.findByRole('listbox')
        expect(within(listbox).getByRole('option', { name: 'open' })).toHaveAttribute('aria-selected', 'false')
      })
    })

    it('shows a message under a touched field inside an alert, and none under an untouched field', async () => {
      const errors =
        '{"sections":[],"fields":{},"errors":{"/values/priority":["note.amountPositive"],"/values/title":["Required."]}}'
      stateAnswers = [emptyStateJson, errors, errors]
      stubFetch()
      const user = renderWithTimers()
      await waitFor(() => expect(stateRequests()).toHaveLength(1))

      await user.type(screen.getByLabelText('Priority'), '0')
      await act(() => vi.advanceTimersByTimeAsync(300))

      const alert = await screen.findByRole('alert')
      expect(alert).toHaveTextContent('Amount must be positive.')
      expect(formItem(screen.getByLabelText('Priority'))).toContainElement(alert)
      expect(hasFieldError(screen.getByLabelText('Priority'))).toBe(true)
      expect(screen.queryByText('Required.')).not.toBeInTheDocument()
      expect(screen.getAllByRole('alert')).toHaveLength(1)

      // Leaving a field touches it, and asks at once.
      await user.click(screen.getByLabelText('Title'))
      await user.tab()
      await waitFor(() => expect(stateRequests()).toHaveLength(3))
      expect(stateRequests()[2].body).toMatchObject({ touched: ['priority', 'title'] })
      expect(await screen.findByText('Required.')).toBeInTheDocument()
      expect(formItem(screen.getByLabelText('Title'))).toContainElement(screen.getByText('Required.'))
    })

    it('asks when a saved record is loaded', async () => {
      stubFetch({ status: 200, body: sectionsJson(1) })
      renderForm(noteId, sectionsWidget)
      await screen.findByLabelText('Title')
      await waitFor(() => expect(stateRequests()).toHaveLength(1))
      expect(stateRequests()[0].body).toEqual({ recordId: noteId, values: {}, touched: [] })
    })

    it('makes no state call for a widget that names an entity, or when the user cannot save', async () => {
      stubFetch()
      renderForm(null, noteWidget)
      await userEvent.type(screen.getByLabelText('Title'), 'Pen')
      await act(() => new Promise((resolve) => setTimeout(resolve, 400)))
      expect(stateRequests()).toEqual([])
      cleanup()

      renderForm(null, {
        ...sectionsWidget,
        access: { actions: ['read', 'update'], createFields: [], updateFields: ['title'] },
      })
      await userEvent.click(screen.getByLabelText('Title'))
      await userEvent.tab()
      await act(() => new Promise((resolve) => setTimeout(resolve, 400)))
      expect(stateRequests()).toEqual([])
    })

    it('keeps the last states when a state call fails', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
      stateAnswers = [emptyStateJson]
      stubFetch()
      vi.mocked(fetch).mockImplementation(async (input) => {
        if (String(input).endsWith('/state')) {
          return new Response('{}', { status: 500 })
        }
        return new Response(historyJson, { status: 200 })
      })
      renderForm(null, sectionsWidget)

      await waitFor(() => expect(warn).toHaveBeenCalled())
      expect(screen.getByLabelText('Priority')).toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      warn.mockRestore()
    })
  })

  it('has the add-row and remove-row texts in every platform locale', () => {
    for (const catalog of [platformEnglish, platformVietnamese]) {
      expect(catalog).toHaveProperty(['shell.form.addRow'])
      expect(catalog).toHaveProperty(['shell.form.removeRow'])
    }
  })

  it('returns to the table on cancel without a request', async () => {
    const requests = stubFetch()
    renderForm()

    await userEvent.type(screen.getByLabelText('Title'), 'Draft')
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/notes?pageSize=10')
    expect(requests()).toEqual([])
  })

  it('shows the not-found page for an unknown record', async () => {
    stubFetch({ status: 404, body: '{}' })

    renderForm(noteId)

    expect(await screen.findByTestId('not-found')).toHaveTextContent('Page not found')
  })

  it('shows an error when the record cannot be loaded or saved', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubFetch({ status: 500, body: '{}' })
    const { unmount } = renderForm(noteId)
    expect(await screen.findByTestId('form-load-error')).toHaveTextContent('The record could not be loaded.')
    unmount()

    stubFetch({ status: 500, body: '{}' })
    renderForm()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('form-error')).toHaveTextContent('The record could not be saved.')
  })

  describe('steps', () => {
    const titleRequired = '{"sections":[],"fields":{},"errors":{"/values/title":["Required."]}}'

    async function renderSteps() {
      const user = userEvent.setup()
      renderForm(null, stepsWidget)
      await waitFor(() => expect(stateRequests()).toHaveLength(1))
      return user
    }

    it('shows the first step with its progress, and no Save until the last step', async () => {
      stubFetch()
      await renderSteps()

      expect(screen.getByLabelText('Title')).toBeInTheDocument()
      expect(screen.getByLabelText('Code')).toBeInTheDocument()
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      const progress = screen.getByTestId('form-steps')
      expect(within(progress).getByText('Step one')).toBeInTheDocument()
      expect(within(progress).getByText('Step two')).toBeInTheDocument()
      expect(progress.querySelector('.ant-steps-item-active')).toHaveTextContent('Step one')
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Next' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
    })

    it('moves on with Next and shows Save and Back on the last step', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      stubFetch()
      const user = await renderSteps()

      await user.click(screen.getByRole('button', { name: 'Next' }))

      expect(await screen.findByLabelText('Priority')).toBeInTheDocument()
      expect(screen.queryByLabelText('Title')).not.toBeInTheDocument()
      expect(screen.getByTestId('form-steps').querySelector('.ant-steps-item-active')).toHaveTextContent('Step two')
      expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument()
      // The check sends the step's fields as touched, at once.
      expect(stateRequests()[1].body).toMatchObject({ touched: ['title', 'code'] })
    })

    it('stays on the step when the answer has an error for one of its fields, and Back keeps the values', async () => {
      stateAnswers = [emptyStateJson, titleRequired, emptyStateJson]
      stubFetch()
      const user = await renderSteps()

      await user.click(screen.getByRole('button', { name: 'Next' }))
      expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Required.')
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()

      await user.type(screen.getByLabelText('Title'), 'Pen')
      await user.click(screen.getByRole('button', { name: 'Next' }))
      await user.type(await screen.findByLabelText('Priority'), '5')
      await user.click(screen.getByRole('button', { name: 'Back' }))

      expect(await screen.findByLabelText('Title')).toHaveValue('Pen')
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Next' }))
      expect(await screen.findByLabelText('Priority')).toHaveValue('5')
    })

    it('acts as Next when Enter is pressed on a step before the last', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      stubFetch()
      const user = await renderSteps()

      await user.type(screen.getByLabelText('Title'), 'Pen{Enter}')

      expect(await screen.findByLabelText('Priority')).toBeInTheDocument()
    })

    it('moves to the next step when the check fails', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
      stateAnswers = [emptyStateJson]
      stubFetch()
      const user = await renderSteps()
      vi.mocked(fetch).mockImplementation(async (input) => {
        if (String(input).endsWith('/state')) {
          return new Response('{}', { status: 500 })
        }
        return new Response(historyJson, { status: 200 })
      })

      await user.click(screen.getByRole('button', { name: 'Next' }))

      expect(await screen.findByLabelText('Priority')).toBeInTheDocument()
      expect(warn).toHaveBeenCalled()
      warn.mockRestore()
    })

    it('saves the changes of every step', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      const requests = stubFetch({ status: 201, body: noteJson('Pen', 1) })
      const user = await renderSteps()

      await user.type(screen.getByLabelText('Title'), 'Pen')
      await user.click(screen.getByRole('button', { name: 'Next' }))
      await user.type(await screen.findByLabelText('Priority'), '5')
      await user.click(screen.getByRole('button', { name: 'Save' }))

      await waitFor(() => expect(requests()).toHaveLength(1))
      expect(requests()[0]).toEqual({
        url: `${recordsPath}?form=NoteSteps`,
        method: 'POST',
        body: '{"values":{"title":"Pen","priority":5}}',
      })
    })

    it('opens the first step with an error after a failed save', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      stubFetch({
        status: 400,
        body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Too long."]}}',
      })
      const user = await renderSteps()
      await user.click(screen.getByRole('button', { name: 'Next' }))
      await user.click(await screen.findByRole('button', { name: 'Save' }))

      expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Too long.')
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      expect(screen.getByTestId('form-steps').querySelector('.ant-steps-item-active')).toHaveTextContent('Step one')
    })

    it('skips a step whose fields the answer hides', async () => {
      stateAnswers = [
        '{"sections":[],"fields":{"priority":{"visible":false,"required":false,"readOnly":false}},"errors":{}}',
      ]
      stubFetch()
      await renderSteps()

      expect(await screen.findByRole('button', { name: 'Save' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument()
      expect(screen.queryByText('Step two')).not.toBeInTheDocument()
    })
  })

  describe('start process action', () => {
    it('shows the action on a saved record and none on a new one', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      const { unmount } = renderForm(noteId, actionWidget)
      expect(await screen.findByRole('button', { name: 'Close note' })).toBeEnabled()
      unmount()

      stubFetch()
      renderForm(null, actionWidget)
      expect(await screen.findByLabelText('Title')).toHaveValue('')
      expect(screen.queryByRole('button', { name: 'Close note' })).toBeNull()
    })

    it('shows a navigate button on a saved record that opens the target with the record values', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })

      renderForm(noteId, {
        ...actionWidget,
        actions: [
          {
            type: 'navigate',
            page: 'Notes',
            labelKey: 'note.open',
            parameters: { note: 'id', title: 'Title', category: 'category' },
            record: null,
            opensForm: false,
            paths: {},
            visibleWhen: null,
          },
          ...actionWidget.actions,
        ],
      })

      await userEvent.click(await screen.findByRole('button', { name: 'note.open' }))

      await waitFor(() =>
        expect(screen.getByTestId('location')).toHaveTextContent(
          `/e2e/notes?note=${noteId}&title=Buy+paper&category=${categoryId}`,
        ),
      )
      expect(screen.getByTestId('from')).toHaveTextContent(`/e2e/noteform/${noteId}`)
    })

    it('shows one message and opens no page when a field along a path is missing', async () => {
      const categoryPath = '/api/apps/E2eApp/entities/Category/records'
      const requests = stubFetch(
        { status: 200, body: noteJson('Buy paper', 1) },
        { status: 200, body: `{"id":"${categoryId}","version":1,"values":{"code":"OFF"},"labels":{}}` },
      )
      renderForm(noteId, {
        ...actionWidget,
        actions: [
          {
            type: 'navigate',
            page: 'Notes',
            labelKey: 'note.open',
            parameters: { name: 'category.name' },
            record: null,
            opensForm: false,
            paths: { category: categoryPath },
            visibleWhen: null,
          },
        ],
      })

      await userEvent.click(await screen.findByRole('button', { name: 'note.open' }))

      expect(await screen.findByTestId('form-error')).toHaveTextContent('The page could not be opened.')
      expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/noteform/${noteId}`)
      expect(requests().map((request) => request.url)).toEqual([
        `${recordsPath}/${noteId}`,
        `${categoryPath}/${categoryId}`,
      ])
    })

    it('shows no navigate button on a new record, and hides one it cannot work out', async () => {
      const navigateAction = {
        type: 'navigate' as const,
        page: 'Notes',
        labelKey: 'note.open',
        parameters: {},
        record: null,
        opensForm: false,
        paths: {},
        visibleWhen: null,
      }
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      const { unmount } = renderForm(null, { ...actionWidget, actions: [navigateAction] })
      expect(await screen.findByLabelText('Title')).toHaveValue('')
      expect(screen.queryByRole('button', { name: 'note.open' })).toBeNull()
      unmount()

      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, {
        ...actionWidget,
        actions: [{ ...navigateAction, parameters: { title: 'concat(title, code)' } }, ...actionWidget.actions],
      })
      expect(await screen.findByRole('button', { name: 'Close note' })).toBeEnabled()
      expect(screen.queryByRole('button', { name: 'note.open' })).toBeNull()
    })

    it('keeps the action on a read-only form without a save button', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })

      renderForm(noteId, { ...actionWidget, access: { actions: ['read'], createFields: [], updateFields: [] } })

      expect(await screen.findByRole('button', { name: 'Close note' })).toBeEnabled()
      expect(screen.getByLabelText('Title')).toHaveAttribute('readonly')
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    })

    it('starts the process without a save when nothing changed, then shows a message and reloads', async () => {
      const requests = stubFetch(
        { status: 200, body: noteJson('Buy paper', 1) },
        { status: 201, body: instanceJson },
        { status: 200, body: noteJson('Closed by the process', 2) },
      )
      renderForm(noteId, actionWidget)

      await userEvent.click(await screen.findByRole('button', { name: 'Close note' }))

      expect(await screen.findByTestId('form-started')).toHaveTextContent('The process has started.')
      await waitFor(() => expect(screen.getByLabelText('Title')).toHaveValue('Closed by the process'))
      expect(requests()).toEqual([
        { url: `${recordsPath}/${noteId}`, method: 'GET', body: undefined },
        { url: instancesPath, method: 'POST', body: `{"subjectId":"${noteId}"}` },
        { url: `${recordsPath}/${noteId}`, method: 'GET', body: undefined },
      ])
      expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/noteform/${noteId}`)
    })

    it('saves the changed fields first, then starts the process', async () => {
      const requests = stubFetch(
        { status: 200, body: noteJson('Buy paper', 1) },
        { status: 200, body: noteJson('New', 2) },
        { status: 201, body: instanceJson },
        { status: 200, body: noteJson('New', 3) },
      )
      renderForm(noteId, actionWidget)
      const title = await screen.findByLabelText('Title')
      await userEvent.clear(title)
      await userEvent.type(title, 'New')

      await userEvent.click(screen.getByRole('button', { name: 'Close note' }))

      expect(await screen.findByTestId('form-started')).toBeInTheDocument()
      await waitFor(() => expect(requests()).toHaveLength(4))
      expect(requests().slice(1, 3)).toEqual([
        { url: `${recordsPath}/${noteId}`, method: 'PATCH', body: '{"version":1,"values":{"title":"New"}}' },
        { url: instancesPath, method: 'POST', body: `{"subjectId":"${noteId}"}` },
      ])
    })

    it('starts nothing when the save fails', async () => {
      const requests = stubFetch(
        { status: 200, body: noteJson('Buy paper', 1) },
        { status: 400, body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Required."]}}' },
      )
      renderForm(noteId, actionWidget)
      await userEvent.clear(await screen.findByLabelText('Title'))

      await userEvent.click(screen.getByRole('button', { name: 'Close note' }))

      expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Required.')
      expect(requests().map(({ url, method }) => `${method} ${url}`)).toEqual([
        `GET ${recordsPath}/${noteId}`,
        `PATCH ${recordsPath}/${noteId}`,
      ])
      expect(screen.queryByTestId('form-started')).toBeNull()
    })

    it('sends the same Idempotency-Key after a start got no answer, and a new one after a 201', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      stubFetch(
        { status: 200, body: noteJson('Buy paper', 1) },
        networkError,
        { status: 201, body: instanceJson },
        { status: 200, body: noteJson('Buy paper', 2) },
        { status: 409, body: '{"title":"Conflict","status":409}' },
      )
      renderForm(noteId, actionWidget)
      const action = await screen.findByRole('button', { name: 'Close note' })

      await userEvent.click(action)
      expect(await screen.findByTestId('form-error')).toHaveTextContent('The process could not be started.')
      await waitFor(() => expect(action).toBeEnabled())
      await userEvent.click(action)
      expect(await screen.findByTestId('form-started')).toBeInTheDocument()
      await waitFor(() => expect(action).toBeEnabled())
      await userEvent.click(action)
      expect(await screen.findByTestId('form-error')).toHaveTextContent('This record is already in a running process.')

      const [first, second, third] = startKeys()
      expect(first).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
      expect(second).toBe(first)
      expect(third).not.toBe(first)
    })

    it('shows a failed start condition as its text, looked up in the site texts', async () => {
      stubFetch(
        { status: 200, body: noteJson('Buy paper', 1) },
        { status: 400, body: '{"title":"Invalid","status":400,"errors":{"/subjectId":["noteClose.cannotClose"]}}' },
      )
      renderForm(noteId, actionWidget)

      await userEvent.click(await screen.findByRole('button', { name: 'Close note' }))

      expect(await screen.findByTestId('form-error')).toHaveTextContent('Only an open note can be closed.')
      expect(screen.queryByTestId('form-started')).toBeNull()
    })

    it('has the start texts in every platform locale', () => {
      for (const catalog of [platformEnglish, platformVietnamese]) {
        expect(catalog).toHaveProperty(['shell.form.processStarted'])
        expect(catalog).toHaveProperty(['shell.form.processRunning'])
        expect(catalog).toHaveProperty(['shell.form.startFailed'])
        expect(catalog).toHaveProperty(['shell.navigate.failed'])
      }
    })
  })
})
