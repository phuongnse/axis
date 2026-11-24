import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import dayjs from 'dayjs'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { followLink, renderGuarded } from '../test/leaveGuard'
import { chooseOption, formItem, formMessages, hasFieldError, isRequiredLabel, tabTo } from '../test/antd'
import { filterTexts } from '../test/tableFilter'
import { CanvasActionsContext } from './canvasActions'
import { designTokensFor } from './designTokens'
import { FormWidget } from './FormWidget'
import { PageLayout } from './PageLayout'
import { RecordList, type RecordListValue } from './recordList'
import { RecordPagesContext } from './recordPages'
import type { EntityWidgetMetadata, FieldMetadata, FieldType, WidgetAccess } from './site'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { NarrowWidthOverride } from './useNarrowWidth'
import type { ThemeMode } from './themeMode'
import { hostileMarkup } from '../test/hostile'
import { withAntdStyles } from '../test/antdStyles'

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
  deadLettersPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  priorities: {},
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

// The lines of a note, required and with help, so the toolbar shows the label, the required mark and the help.
const helpedLinesWidget: EntityWidgetMetadata = {
  ...linesWidget,
  entity: {
    ...linesWidget.entity,
    fields: linesWidget.entity.fields.map((entry) =>
      entry.name === 'lines' ? { ...entry, required: true, helpKey: 'note.linesHelp' } : entry,
    ),
  },
}

// A note whose lines each name a category, a reference cell in a child collection.
const lineCategoryWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200 },
      {
        ...field('lines', 'child-collection', 'note.lines'),
        fields: [
          { ...field('description', 'text', 'noteLine.description'), required: true, maxLength: 100 },
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

// A note with only a title and the software it uses, a set of references.
const softwareWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200 },
      {
        ...field('software', 'references', 'note.software'),
        target: { entity: 'Software', displayField: 'name', recordsPath: '/api/apps/E2eApp/entities/Software/records' },
      },
    ],
  },
}

// A note whose title, total, software and attachment have help text, and whose priority has none.
const helpWidget: EntityWidgetMetadata = {
  ...noteWidget,
  entity: {
    ...noteWidget.entity,
    fields: [
      { ...field('title', 'text', 'note.title'), required: true, maxLength: 200, helpKey: 'note.titleHelp' },
      field('priority', 'integer', 'note.priority'),
      {
        ...field('total', 'decimal', 'note.total'),
        precision: 18,
        scale: 2,
        computed: true,
        helpKey: 'note.totalHelp',
      },
      {
        ...field('software', 'references', 'note.software'),
        helpKey: 'note.softwareHelp',
        target: { entity: 'Software', displayField: 'name', recordsPath: '/api/apps/E2eApp/entities/Software/records' },
      },
      {
        ...field('attachment', 'file', 'note.attachment'),
        helpKey: 'note.attachmentHelp',
        uploadPath: '/api/apps/E2eApp/entities/Note/files',
      },
    ],
  },
}

const wordId = '11111111-1111-4111-8111-111111111111'
const excelId = '22222222-2222-4222-8222-222222222222'
const visioId = '33333333-3333-4333-8333-333333333333'

const softwarePageJson = `{"items":[{"id":"${wordId}","version":1,"values":{"name":"Word"},"labels":{}},{"id":"${excelId}","version":1,"values":{"name":"Excel"},"labels":{}},{"id":"${visioId}","version":1,"values":{"name":"Visio"},"labels":{}}],"page":1,"pageSize":20,"totalCount":3}`

function softwareJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"Laptop","software":["${wordId}","${excelId}"]},"labels":{"software":["Word","Excel"]}}`
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
  return `{"id":"${noteId}","version":${version},"values":{"title":"Buy paper","priority":2,"createdBy":"requester"},"labels":{}}`
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
  'shell.studio.save': 'Save',
  'shell.studio.discard': 'Discard',
  'shell.studio.discardConfirm': 'Discard all unsaved changes?',
  'shell.studio.cancel': 'Cancel',
  'shell.studio.saveBar.unsaved': 'Unsaved changes',
  'shell.studio.saveBar.noUnsaved': 'No unsaved changes',
  'shell.form.next': 'Next',
  'shell.form.back': 'Back',
  'shell.form.reload': 'Reload',
  'shell.form.conflict': 'This record has changed since you opened it. Reload it to see the latest values.',
  'shell.form.saveFailed': 'The record could not be saved.',
  'shell.form.loadFailed': 'The record could not be loaded.',
  'shell.form.choose': 'Choose…',
  'shell.form.clear': 'Clear',
  'shell.form.openRecord': 'Open',
  'shell.form.addRow': 'Add row',
  'shell.form.removeRow': 'Delete row',
  'shell.form.removeReference': 'Remove',
  'shell.form.removeReferenceNamed': 'Remove {name}',
  'shell.form.add': 'Add',
  'shell.form.addNamed': 'Add {field}',
  'shell.form.added': 'Added {name}',
  'shell.form.removed': 'Removed {name}',
  'shell.table.search': 'Search',
  ...filterTexts,
  'shell.table.empty': 'No records yet.',
  'shell.value.notSet': 'Not set',
  'shell.table.yes': 'Yes',
  'shell.table.no': 'No',
  'shell.table.loadFailed': 'The records could not be loaded.',
  'shell.notFound.title': 'Page not found',
  'shell.record.notFound': 'This record does not exist, or you cannot open it.',
  'shell.record.goToList': 'Go to {page}',
  'notes.nav': 'Notes',
  'shell.history.title': 'History',
  'shell.history.empty': 'No history yet.',
  'shell.history.fields': 'Changed: {fields}',
  'shell.history.action.record.updated': 'Updated',
  'note.label': 'Note',
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
  'note.software': 'Software',
  'note.lines': 'Lines',
  'note.linesHelp': 'One line per item.',
  'noteLine.description': 'Description',
  'noteLine.quantity': 'Quantity',
  'noteLine.code': 'Line code',
  'note.total': 'Total',
  'note.amountPositive': 'Amount must be positive.',
  'note.attachment': 'Attachment',
  'note.titleHelp': 'Shown on the list.',
  'note.totalHelp': 'Worked out by the server.',
  'note.softwareHelp': 'Choose each tool the note uses.',
  'note.attachmentHelp': 'Attach the signed form.',
  'noteSections.main': 'Main',
  'noteSections.details': 'Details',
  'noteSteps.main': 'Step one',
  'noteSteps.details': 'Step two',
  'shell.form.processStarted': 'The process has started.',
  'shell.form.processRunning': 'This record is already in a running process.',
  'shell.form.startFailed': 'The process could not be started.',
  'shell.form.newRecord': platformEnglish['shell.form.newRecord'],
  'shell.navigate.failed': 'The page could not be opened.',
  'noteClose.action': 'Close note',
  'noteClose.cannotClose': 'Only an open note can be closed.',
}
const catalogs = [{ texts, fallbackTexts: texts }]
const vietnameseCatalogs = [
  { texts: { ...texts, 'shell.form.newRecord': platformVietnamese['shell.form.newRecord'] }, fallbackTexts: texts },
]

/** An answer that never arrives: `fetch` rejects as on a lost connection. */
const networkError = 'networkError'

type Answer = { status: number; body: string } | typeof networkError

const historyPath = `${recordsPath}/${noteId}/history?page=1&pageSize=20`

const historyJson = `{"items":[{"id":"01926b3e-5f40-7c8a-9b1d-2e3f4a5b6c7d","occurredAt":"2026-10-06T02:05:00.123456Z","actor":"requester","actorName":"Requester User","action":"record.updated","processInstanceId":null,"details":{"version":2,"fields":["title"]}}],"page":1,"pageSize":20,"totalCount":1}`

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
    // The history names its entries' people through `GET /api/me`, which is not one of the answers, with nobody signed in.
    if (input === '/api/me') {
      return new Response('{}', { status: 401 })
    }
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
      .filter(([url]) => !url.includes('/history?') && !url.endsWith('/state') && url !== '/api/me')
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

function renderForm(
  recordId: string | null = null,
  widget: EntityWidgetMetadata = noteWidget,
  mode?: ThemeMode,
  narrow?: boolean,
  list: RecordListValue | null = null,
  recordPages?: Record<string, string>,
  onCanvas?: boolean,
  titled?: boolean,
  textCatalogs: typeof catalogs = catalogs,
) {
  const path = `/e2e/noteform/${recordId ?? 'new'}`
  const formWidget = (
    <FormWidget
      widget={widget}
      sitePath="e2e"
      pages={['Notes', 'NoteForm']}
      recordId={recordId}
      returnTo="/e2e/notes?pageSize=10"
      locale="en"
    />
  )
  const form = (
    <NarrowWidthOverride value={narrow}>
      <MemoryRouter initialEntries={[{ pathname: path, state: { from: '/e2e/notes?pageSize=10' } }]}>
        {/* Without motion the lookup dialog leaves the document at once, as jsdom runs no animations. */}
        <ConfigProvider theme={{ token: { motion: false } }}>
          <TextProvider catalogs={textCatalogs} development={false}>
            <RecordPagesContext value={recordPages ? { sitePath: 'e2e', recordPages } : null}>
              <RecordList value={list}>
                <CanvasActionsContext.Provider value={onCanvas ? { selected: undefined } : null}>
                  {titled ? <PageLayout title="Note">{formWidget}</PageLayout> : formWidget}
                </CanvasActionsContext.Provider>
              </RecordList>
            </RecordPagesContext>
          </TextProvider>
        </ConfigProvider>
        <CurrentLocation />
      </MemoryRouter>
    </NarrowWidthOverride>
  )
  return render(mode ? <ThemeModeProvider initialMode={mode}>{form}</ThemeModeProvider> : form)
}

// A new form with no change shows no save bar, so a test that needs the server's answer to an empty form submits it, as
// Enter does.
function submitForm() {
  act(() => document.querySelector('form')!.requestSubmit())
}

describe('FormWidget', () => {
  beforeEach(() => {
    stateAnswers = []
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows a value that holds markup as text', async () => {
    const record = {
      id: noteId,
      version: 1,
      values: {
        title: hostileMarkup,
        code: null,
        priority: 7,
        amount: 12.5,
        done: false,
        dueOn: null,
        dueAt: null,
        status: null,
        category: categoryId,
      },
      labels: { category: 'Office' },
    }
    stubFetch({ status: 200, body: JSON.stringify(record) })

    renderForm(noteId)

    expect(await screen.findByDisplayValue(hostileMarkup)).toBeInTheDocument()
    expect(document.querySelector('img, [onerror]')).toBeNull()
  })

  it('shows one input per field with its label, and the reference label read-only', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })

    renderForm(noteId)

    expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('Buy paper')
    expect(screen.getByRole('textbox', { name: 'Title' })).toHaveAttribute('maxlength', '200')
    expect(screen.getByLabelText('Priority')).toHaveValue('7')
    expect(screen.getByLabelText('Amount')).toHaveValue('12.50')
    expect(screen.getByLabelText('Amount')).toHaveAttribute('inputmode', 'decimal')
    expect(screen.getByLabelText('Done')).not.toBeChecked()
    // The picker shows the instant in the browser's time zone, to the minute, in the pattern of the locale.
    expect(screen.getByLabelText('Due at')).toHaveValue(dayjs('2026-10-06T02:00:00Z').format('MM/DD/YYYY h:mm A'))
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
    expect(within(group).getByRole('button', { name: 'Choose…' })).toBeInTheDocument()
  })

  it('shows Choose and Clear as icon buttons with no visible text', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId)
    await screen.findByLabelText('Category')

    const choose = screen.getByRole('button', { name: 'Choose…' })
    const clear = screen.getByRole('button', { name: 'Clear' })
    expect(choose.textContent).toBe('')
    expect(clear.textContent).toBe('')
    expect(choose.querySelector('.anticon-search')).not.toBeNull()
    expect(clear.querySelector('.anticon-close')).not.toBeNull()
  })

  it('draws Choose and Clear as bordered buttons joined to the input', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId)
    await screen.findByLabelText('Category')

    const group = screen.getByTestId('reference-category')
    const choose = within(group).getByRole('button', { name: 'Choose…' })
    const clear = within(group).getByRole('button', { name: 'Clear' })
    expect(choose).toHaveClass('ant-btn-variant-outlined', 'ant-btn-compact-item')
    expect(clear).toHaveClass('ant-btn-variant-outlined', 'ant-btn-compact-item', 'ant-btn-compact-last-item')
  })

  it('names Choose on keyboard focus', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId)
    await screen.findByLabelText('Category')

    await tabTo(screen.getByRole('button', { name: 'Choose…' }))

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Choose…')
  })

  describe('with antd styles', () => {
    withAntdStyles()

    it('names Clear on keyboard focus', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId)
      await screen.findByLabelText('Category')

      await tabTo(screen.getByRole('button', { name: 'Clear' }))

      expect(await screen.findByRole('tooltip')).toHaveTextContent('Clear')
    })
  })

  it('shows the history of a saved record below the form', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })

    renderForm(noteId)

    const entry = await screen.findByTestId('history-entry')
    expect(screen.getByTestId('record-history')).toHaveTextContent('History')
    expect(entry).toHaveTextContent('Updated')
    expect(entry).toHaveTextContent('Requester User')
    expect(entry).toHaveTextContent('Changed: Title')
    expect(historyRequests()).toEqual([historyPath])
  })

  it('shows no history for a new record', async () => {
    stubFetch()
    renderForm()

    expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('')
    expect(screen.queryByTestId('record-history')).toBeNull()
    expect(historyRequests()).toEqual([])
  })

  it('marks required fields without blocking submit', async () => {
    stubFetch()
    renderForm()

    // Ant Design marks a required item by a class on its label; the server still decides.
    expect(isRequiredLabel(screen.getByText('Title'))).toBe(true)
    expect(isRequiredLabel(screen.getByText('Code'))).toBe(false)
  })

  it('shows a hidden star after a required label, and aria-required on its input only', () => {
    stubFetch()
    renderForm()

    const mark = document.querySelector('label[for="title"]')?.lastElementChild
    expect(mark).toHaveTextContent('*')
    expect(mark).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByRole('textbox', { name: 'Title' })).toHaveAttribute('aria-required', 'true')
    expect(document.querySelector('label[for="code"] [aria-hidden="true"]')).toBeNull()
    expect(screen.getByRole('textbox', { name: 'Code' })).not.toHaveAttribute('aria-required')
  })

  it('confirms a picker value on Enter without sending the form', async () => {
    const requests = stubFetch()
    renderForm()

    await userEvent.type(screen.getByLabelText('Due on'), '10/07/2026{Enter}')

    expect(screen.getByLabelText('Due on')).toHaveValue('10/07/2026')
    expect(requests()).toEqual([])
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/noteform/new')
  })

  it('creates a record with numbers as typed and returns to the table', async () => {
    const requests = stubFetch({ status: 201, body: noteJson('Buy paper', 1) })
    renderForm()

    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Buy paper')
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
    const title = await screen.findByRole('textbox', { name: 'Title' })

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

  describe('with antd styles', () => {
    withAntdStyles()

    it('sets a field error 4px under its input in 14/20 text', async () => {
      stubFetch({ status: 400, body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Required."]}}' })
      renderForm(null, noteWidget, 'light')

      submitForm()

      const message = await screen.findByTestId('field-error-title')
      expect(formMessages(message)).toHaveStyle({
        marginTop: '4px',
        fontSize: '14px',
        lineHeight: '20px',
      })
    })

    it('keeps no margin below any form item, and the layout gives the 24px gap between rows', async () => {
      stubFetch({ status: 400, body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Required."]}}' })
      const { container } = renderForm(null, noteWidget, 'light')

      submitForm()
      await screen.findByTestId('field-error-title')

      const items = Array.from(container.querySelectorAll<HTMLElement>('form .ant-form-item'))
      expect(items.length).toBeGreaterThan(1)
      for (const item of items) {
        expect(getComputedStyle(item).getPropertyValue('--ant-form-item-margin-bottom')).toBe('0px')
        expect(item.closest<HTMLElement>('[data-testid^="field-column-"]')?.parentElement?.style.rowGap).toBe(
          `${designTokensFor('light').spacing.lg}px`,
        )
      }
    })

    it("draws a form's titled groups as parts a borderSubtle rule apart, with none above the first", async () => {
      stubFetch({ status: 200, body: sectionsJson(1) })
      renderForm(noteId, sectionsWidget, 'light')

      const main = await screen.findByRole('region', { name: 'Main' })
      const details = screen.getByRole('region', { name: 'Details' })
      expect(within(main).getByRole('heading', { level: 2 })).toHaveTextContent('Main')
      expect(within(details).getByRole('heading', { level: 2 })).toHaveTextContent('Details')
      expect(main.previousElementSibling).toBeNull()
      const rule = details.previousElementSibling
      expect(rule).toHaveAttribute('role', 'separator')
      expect(rule).toHaveStyle({ borderTop: `1px solid ${designTokensFor('light').colour.borderSubtle}` })
    })
  })

  it('draws no rule on a form with one untitled group', async () => {
    renderForm()

    expect(await screen.findByRole('textbox', { name: 'Title' })).toBeInTheDocument()
    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('draws the history of a saved record as the last part, headed History, with a rule above it and no card', async () => {
    stubFetch({ status: 200, body: sectionsJson(1) })
    renderForm(noteId, sectionsWidget)

    const history = await screen.findByRole('region', { name: 'History' })
    expect(history).toBe(screen.getByTestId('record-history'))
    expect(within(history).getByRole('heading', { level: 2 })).toHaveTextContent('History')
    expect(history.previousElementSibling).toHaveAttribute('role', 'separator')
    expect(history.nextElementSibling).toBeNull()
    expect(document.querySelector('.ant-card')).toBeNull()
  })

  it('shows field errors under their field and other errors above the form', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"":["Must be a JSON object."],"/values/title":["Required."]}}',
    })
    renderForm()

    submitForm()

    expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Required.')
    expect(screen.getByTestId('form-error')).toHaveTextContent('Must be a JSON object.')
    expect(screen.getByTestId('form-error')).toHaveFocus()
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/noteform/new')
  })

  it('marks each field in error, focuses the first and takes the mark off when the person types', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Required."],"/values/code":["Already used."]}}',
    })
    renderForm()

    submitForm()

    expect(await screen.findByTestId('field-error-code')).toHaveTextContent('Already used.')
    const title = screen.getByRole('textbox', { name: 'Title' })
    expect(title).toHaveFocus()
    expect(title).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('textbox', { name: 'Code' })).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('Priority')).not.toHaveAttribute('aria-invalid')

    await userEvent.setup().type(title, 'Pen')

    expect(screen.queryByTestId('field-error-title')).not.toBeInTheDocument()
    expect(title).not.toHaveAttribute('aria-invalid')
    expect(screen.getByRole('textbox', { name: 'Code' })).toHaveAttribute('aria-invalid', 'true')
  })

  it('marks a reference, references and file field in error on the element that takes the focus', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"/values/software":["Required."],"/values/attachment":["Too large."],"/values/category":["Required."]}}',
    })
    renderForm(null, {
      ...helpWidget,
      entity: {
        ...helpWidget.entity,
        fields: [
          ...helpWidget.entity.fields,
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
    })

    submitForm()

    expect(await screen.findByTestId('field-error-category')).toHaveTextContent('Required.')
    // The Add button takes the mark and the focus for references, and the Upload button for a file.
    expect(document.getElementById('software')).toHaveAttribute('aria-invalid', 'true')
    expect(document.getElementById('software')).toHaveFocus()
    expect(document.getElementById('attachment')).toHaveAttribute('aria-invalid', 'true')
    expect(document.getElementById('category')).toHaveAttribute('aria-invalid', 'true')
    expect(document.getElementById('priority')).not.toHaveAttribute('aria-invalid')
  })

  it('scrolls an error above the form into view and focuses it', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"":["Must be a JSON object."]}}',
    })
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      renderForm()

      submitForm()

      const error = await screen.findByTestId('form-error')
      await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' }))
      expect(scrollIntoView.mock.contexts).toEqual([error])
      expect(error).toHaveFocus()
      expect(within(error).getByRole('alert')).toHaveTextContent('Must be a JSON object.')
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('shows a validation text key as its text and a fixed message as is', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"/values/amount":["note.amountPositive"],"/values/title":["Required."]}}',
    })
    renderForm()

    submitForm()

    expect(await screen.findByTestId('field-error-amount')).toHaveTextContent('Amount must be positive.')
    expect(screen.getByTestId('field-error-title')).toHaveTextContent('Required.')
  })

  it('shows help text that names its input with aria-describedby, and no help element for a field without help', async () => {
    stubFetch()
    const { container } = renderForm(null, helpWidget)

    expect(await screen.findByText('Shown on the list.')).toHaveAttribute('id', 'title-help')
    expect(screen.getByText('Shown on the list.')).toHaveStyle({ marginTop: '-4px', marginBottom: '8px' })
    expect(screen.getByRole('textbox', { name: 'Title' })).toHaveAttribute('aria-describedby', 'title-help')
    expect(container.querySelector('#priority-help')).toBeNull()
    expect(screen.getByLabelText('Priority')).not.toHaveAttribute('aria-describedby')
  })

  it('names the references Add button with its help text, and a read-only value with no help link', async () => {
    stubFetch()
    renderForm(null, helpWidget)

    expect(await screen.findByText('Worked out by the server.')).toHaveAttribute('id', 'total-help')
    expect(screen.getByTestId('field-value-total')).not.toHaveAttribute('aria-describedby')
    expect(screen.getByRole('button', { name: 'Add Software' })).toHaveAttribute('aria-describedby', 'software-help')
  })

  it('shows a file field help text without tying the upload button to it', async () => {
    stubFetch()
    renderForm(null, helpWidget)

    expect(await screen.findByText('Attach the signed form.')).toHaveAttribute('id', 'attachment-help')
    expect(document.getElementById('attachment')).not.toHaveAttribute('aria-describedby')
  })

  it('keeps the help text of a field while it shows a validation message', async () => {
    stubFetch({ status: 400, body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Required."]}}' })
    renderForm(null, helpWidget)

    submitForm()

    expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Required.')
    expect(screen.getByText('Shown on the list.')).toHaveAttribute('id', 'title-help')
  })

  it('shows a stale version as a conflict and reloads the current values', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 409, body: '{"title":"Conflict","status":409}' },
      { status: 200, body: noteJson('Changed elsewhere', 2) },
    )
    renderForm(noteId)
    const title = await screen.findByRole('textbox', { name: 'Title' })
    await userEvent.type(title, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('form-conflict')).toHaveTextContent('This record has changed since you opened it.')
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }))

    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Title' })).toHaveValue('Changed elsewhere'))
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

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
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

    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Buy paper')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1]).toEqual({
      url: recordsPath,
      method: 'POST',
      body: `{"values":{"title":"Buy paper","category":"${categoryId}"}}`,
    })
  })

  it('picks a lookup row with Enter', async () => {
    stubFetch({ status: 200, body: categoryPageJson })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
    const dialog = await screen.findByRole('dialog')
    const row = await within(dialog).findByRole('row', { name: /Office/ })
    expect(row).toHaveAttribute('tabindex', '0')
    // The Enter goes to the row itself, since the opening dialog may still take focus.
    expect(fireEvent.keyDown(row, { key: 'Enter' })).toBe(false)

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByLabelText('Category')).toHaveValue('Office')
  })

  it('leaves a reference out when the record already set is picked again', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 200, body: categoryPageJson },
      { status: 200, body: noteJson('New', 2) },
    )
    renderForm(noteId)
    const title = await screen.findByRole('textbox', { name: 'Title' })

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Office'))
    await userEvent.type(title, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(3))
    expect(requests()[2].body).toBe('{"version":1,"values":{"title":"Buy paper!"}}')
  })

  it('searches the lookup with the trimmed text on Enter and keeps the address', async () => {
    const requests = stubFetch({ status: 200, body: categoryPageJson }, { status: 200, body: categoryPageJson })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText('Office')
    await userEvent.type(within(dialog).getByRole('searchbox', { name: 'Search' }), '  off  {Enter}')

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1].url).toBe('/api/apps/E2eApp/entities/Category/records?search=off&sort=name')
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/noteform/new')
  })

  it('keeps the lookup search and sort when it goes to page 2', async () => {
    const longCategoryPageJson = categoryPageJson.replace('"totalCount":1', '"totalCount":45')
    const requests = stubFetch(
      { status: 200, body: longCategoryPageJson },
      { status: 200, body: longCategoryPageJson },
      { status: 200, body: longCategoryPageJson },
    )
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText('Office')
    await userEvent.type(within(dialog).getByRole('searchbox', { name: 'Search' }), 'off{Enter}')
    await waitFor(() => expect(requests()).toHaveLength(2))
    await userEvent.click(within(dialog).getByTitle('2'))

    await waitFor(() => expect(requests()).toHaveLength(3))
    expect(requests()[2].url).toBe('/api/apps/E2eApp/entities/Category/records?search=off&page=2&sort=name')
  })

  it('filters the lookup by its display column and sends it with the search, keeping the address', async () => {
    const requests = stubFetch({ status: 200, body: categoryPageJson }, { status: 200, body: categoryPageJson })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
    const lookup = await screen.findByRole('dialog')
    await within(lookup).findByText('Office')
    await userEvent.click(within(lookup).getByRole('button', { name: 'Filter' }))
    // The builder opens over the lookup, after it in the document, so its steps use the newest dialog.
    const builder = screen.getAllByRole('dialog').at(-1)!
    await userEvent.click(within(builder).getAllByRole('combobox', { name: 'Operator' })[0])
    await chooseOption('contains')
    await userEvent.type(within(builder).getByRole('textbox', { name: 'Value' }), 'off')
    await userEvent.click(within(builder).getByRole('button', { name: 'Apply' }))

    const filter = JSON.stringify({ all: [{ field: 'name', op: 'contains', value: 'off' }] })
    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1].url).toBe(
      `/api/apps/E2eApp/entities/Category/records?${new URLSearchParams({ filter, sort: 'name' })}`,
    )
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/e2e\/noteform\/new$/)
  })

  it('gives a lookup whose target has no display field no Filter', async () => {
    const widget: EntityWidgetMetadata = {
      ...noteWidget,
      entity: {
        ...noteWidget.entity,
        fields: noteWidget.entity.fields.map((f) =>
          f.name === 'category' ? { ...f, target: { ...f.target!, displayField: null } } : f,
        ),
      },
    }
    stubFetch({ status: 200, body: categoryPageJson })
    renderForm(null, widget)

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
    const lookup = await screen.findByRole('dialog')
    // With no display field the column shows the record id, so the row is there once the id shows.
    await within(lookup).findByText(categoryId)

    expect(within(lookup).queryByRole('button', { name: 'Filter' })).not.toBeInTheDocument()
  })

  it('does not search the lookup until Enter is pressed', async () => {
    const requests = stubFetch({ status: 200, body: categoryPageJson })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText('Office')
    await userEvent.type(within(dialog).getByRole('searchbox', { name: 'Search' }), 'off')

    await new Promise((resolve) => setTimeout(resolve, 400))
    expect(requests()).toHaveLength(1)
    expect(requests().some(({ url }) => url.includes('search='))).toBe(false)
  })

  it('shows the lookup error when the target records cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubFetch({ status: 500, body: '{}' })
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Choose…' }))

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
    expect(await screen.findByRole('textbox', { name: 'Category' })).toHaveValue('Office')
    expect(screen.getByRole('button', { name: 'Choose…' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
    unmount()

    stubFetch()
    renderForm()
    expect(screen.getByLabelText('Category')).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
  })

  it('adds two records to a references field, removes the first, adds a third and sends the ids in pick order', async () => {
    const requests = stubFetch(
      { status: 200, body: softwarePageJson },
      { status: 200, body: softwarePageJson },
      { status: 200, body: softwarePageJson },
      { status: 201, body: softwareJson(1) },
    )
    renderForm(null, softwareWidget)
    const announcement = screen.getByTestId('references-software-announcement')
    expect(announcement).toHaveAttribute('aria-live', 'polite')

    await userEvent.click(screen.getByRole('button', { name: 'Add Software' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Word'))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(announcement).toHaveTextContent('Added Word')

    await userEvent.click(screen.getByRole('button', { name: 'Add Software' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Excel'))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(announcement).toHaveTextContent('Added Excel')
    const list = screen.getByRole('list', { name: 'Software' })
    expect(
      within(list)
        .getAllByRole('listitem')
        .map((item) => item.firstChild?.textContent),
    ).toEqual(['WordRemove', 'ExcelRemove'])

    await userEvent.click(screen.getByRole('button', { name: 'Remove Word' }))
    expect(announcement).toHaveTextContent('Removed Word')
    expect(screen.getByRole('button', { name: 'Add Software' })).toHaveFocus()
    expect(screen.queryByRole('button', { name: 'Remove Word' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Add Software' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Visio'))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Laptop')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(4))
    expect(requests()[3]).toEqual({
      url: recordsPath,
      method: 'POST',
      body: `{"values":{"title":"Laptop","software":["${excelId}","${visioId}"]}}`,
    })
  })

  it('leaves the set unchanged when a record that is in it is picked again', async () => {
    stubFetch({ status: 200, body: softwareJson(1) }, { status: 200, body: softwarePageJson })
    renderForm(noteId, softwareWidget)
    await screen.findByText('Excel')

    await userEvent.click(screen.getByRole('button', { name: 'Add Software' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Word'))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByTestId('references-software-announcement')).toBeEmptyDOMElement()
  })

  it('sends an unchanged set of references only when it changes', async () => {
    const requests = stubFetch({ status: 200, body: softwareJson(1) }, { status: 200, body: softwareJson(2) })
    renderForm(noteId, softwareWidget)
    const title = await screen.findByRole('textbox', { name: 'Title' })

    await userEvent.type(title, '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1].body).toBe('{"version":1,"values":{"title":"Laptop!"}}')
  })

  it('sends a set that was reordered by removing and adding again', async () => {
    const requests = stubFetch(
      { status: 200, body: softwareJson(1) },
      { status: 200, body: softwarePageJson },
      { status: 200, body: softwareJson(2) },
    )
    renderForm(noteId, softwareWidget)
    await screen.findByText('Excel')

    await userEvent.click(screen.getByRole('button', { name: 'Remove Word' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add Software' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Word'))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(3))
    expect(requests()[2].body).toBe(`{"version":1,"values":{"software":["${excelId}","${wordId}"]}}`)
  })

  it('shows the id of a record whose label the user may not read', async () => {
    stubFetch({
      status: 200,
      body: softwareJson(1).replace('"software":["Word","Excel"]', '"software":[null,"Excel"]'),
    })
    renderForm(noteId, softwareWidget)

    expect(await screen.findByText(wordId)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: `Remove ${wordId}` })).toBeInTheDocument()
  })

  describe('record pages', () => {
    const recordPages = { Category: '/e2e/records/category', Software: '/e2e/software' }
    const readOnlyAccess: WidgetAccess = { actions: ['read', 'update'], createFields: [], updateFields: [] }

    it('opens a chosen reference between Choose and Clear, as a link that passes the page it leaves', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, noteWidget, undefined, undefined, null, recordPages)

      const group = await screen.findByTestId('reference-category')
      const names = Array.from(group.querySelectorAll('button, a')).map((control) => control.getAttribute('aria-label'))
      expect(names).toEqual(['Choose…', 'Open Office', 'Clear'])
      const open = within(group).getByRole('link', { name: 'Open Office' })
      expect(open).toHaveAttribute('href', `/e2e/records/category/${categoryId}`)
      expect(open.querySelector('.anticon-export')).not.toBeNull()
      expect(open).toHaveClass('ant-btn-variant-outlined', 'ant-btn-compact-item')

      await userEvent.click(open)

      expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/records/category/${categoryId}`)
      expect(screen.getByTestId('from')).toHaveTextContent(`/e2e/noteform/${noteId}`)
    })

    it('draws the reference as plain text with no Open button when its entity has no record page', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, noteWidget, undefined, undefined, null, { Software: '/e2e/software' })

      await screen.findByLabelText('Category')

      expect(screen.queryByRole('link')).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Choose…' })).toBeInTheDocument()
    })

    it('draws no Open button for a reference whose label the person may not read', async () => {
      stubFetch({
        status: 200,
        body: noteJson('Buy paper', 1).replace('"labels":{"category":"Office"}', '"labels":{}'),
      })
      renderForm(noteId, noteWidget, undefined, undefined, null, recordPages)

      await screen.findByLabelText('Category')

      expect(screen.queryByRole('link')).not.toBeInTheDocument()
    })

    it('draws no Open button outside a site', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId)

      await screen.findByLabelText('Category')

      expect(screen.queryByRole('link')).not.toBeInTheDocument()
    })

    it('shows a read-only reference as a link to its record page, and as text without one', async () => {
      const readOnly: EntityWidgetMetadata = { ...noteWidget, access: readOnlyAccess }
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, readOnly, undefined, undefined, null, recordPages)

      const link = await within(await screen.findByTestId('field-value-category')).findByRole('link', {
        name: 'Office',
      })
      expect(link).toHaveAttribute('href', `/e2e/records/category/${categoryId}`)
      cleanup()

      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, readOnly)

      expect(await screen.findByTestId('field-value-category')).toHaveTextContent('Office')
      expect(screen.queryByRole('link')).not.toBeInTheDocument()
    })

    it('shows each entry of a set of references as a link, read-only or editable', async () => {
      stubFetch({ status: 200, body: softwareJson(1) })
      renderForm(noteId, { ...softwareWidget, access: readOnlyAccess }, undefined, undefined, null, recordPages)

      const value = await screen.findByTestId('field-value-software')
      expect(within(value).getByRole('link', { name: 'Word' })).toHaveAttribute('href', `/e2e/software/${wordId}`)
      expect(within(value).getByRole('link', { name: 'Excel' })).toHaveAttribute('href', `/e2e/software/${excelId}`)
      expect(value).toHaveTextContent('Word, Excel')
      cleanup()

      stubFetch({ status: 200, body: softwareJson(1) })
      renderForm(noteId, softwareWidget, undefined, undefined, null, recordPages)

      const list = await screen.findByRole('list', { name: 'Software' })
      expect(within(list).getByRole('link', { name: 'Word' })).toHaveAttribute('href', `/e2e/software/${wordId}`)
      expect(within(list).getByRole('button', { name: 'Remove Word' })).toBeInTheDocument()
    })

    it('shows an entry of a set of references as text when its label is missing', async () => {
      stubFetch({
        status: 200,
        body: softwareJson(1).replace('"labels":{"software":["Word","Excel"]}', '"labels":{"software":["Word",null]}'),
      })
      renderForm(noteId, { ...softwareWidget, access: readOnlyAccess }, undefined, undefined, null, recordPages)

      const value = await screen.findByTestId('field-value-software')

      expect(within(value).getAllByRole('link')).toHaveLength(1)
      expect(value).toHaveTextContent(`Word, ${excelId}`)
    })
  })

  it('shows a read-only references field as its labels without Add or Remove buttons', async () => {
    const readOnlyWidget: EntityWidgetMetadata = {
      ...softwareWidget,
      access: { actions: ['read', 'create', 'update'], createFields: ['title'], updateFields: ['title'] },
    }
    stubFetch({ status: 200, body: softwareJson(1) })
    renderForm(noteId, readOnlyWidget)

    expect(await screen.findByTestId('field-value-software')).toHaveTextContent('Word, Excel')
    expect(screen.queryByRole('textbox', { name: 'Software' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add Software' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Remove / })).not.toBeInTheDocument()
  })

  describe('read-only values as plain text', () => {
    const fileId = '44444444-4444-4444-8444-444444444444'
    const downloadPath = '/api/apps/E2eApp/entities/Note/files'
    const readOnlyWidget: EntityWidgetMetadata = {
      ...softwareWidget,
      entity: {
        ...softwareWidget.entity,
        fields: [
          ...softwareWidget.entity.fields,
          field('done', 'boolean', 'note.done'),
          field('priority', 'integer', 'note.priority'),
          { ...field('attachment', 'file', 'note.attachment'), downloadPath },
        ],
      },
      access: { actions: ['read', 'update'], createFields: [], updateFields: [] },
    }

    it('shows each read-only value as its text, with Yes or No, joined labels and a file link', async () => {
      const values = `{"title":"Laptop","software":["${wordId}","${excelId}"],"done":true,"priority":7,"attachment":{"id":"${fileId}","name":"brief.pdf","mediaType":"application/pdf","size":"1024"}}`
      stubFetch({
        status: 200,
        body: `{"id":"${noteId}","version":1,"values":${values},"labels":{"software":["Word","Excel"]}}`,
      })
      renderForm(noteId, readOnlyWidget)

      expect(await screen.findByTestId('field-value-title')).toHaveTextContent('Laptop')
      expect(screen.getByTestId('field-value-software')).toHaveTextContent('Word, Excel')
      expect(screen.getByTestId('field-value-done')).toHaveTextContent('Yes')
      expect(screen.getByTestId('field-value-priority')).toHaveTextContent('7')
      expect(screen.getByRole('link', { name: 'brief.pdf' })).toHaveAttribute('href', `${downloadPath}/${fileId}`)
      expect(screen.getByTestId('field-value-title')).toHaveStyle({ minHeight: '32px' })
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    })

    it('shows an empty read-only value, set of references and file as Not set in italics', async () => {
      const values = `{"title":"Laptop","software":[],"done":false,"priority":null,"attachment":null}`
      stubFetch({ status: 200, body: `{"id":"${noteId}","version":1,"values":${values},"labels":{}}` })
      renderForm(noteId, readOnlyWidget)

      expect(await screen.findByTestId('field-value-done')).toHaveTextContent('No')
      for (const name of ['software', 'priority', 'attachment']) {
        const value = screen.getByTestId(`field-value-${name}`)
        expect(within(value).getByText('Not set')).toHaveStyle({ fontStyle: 'italic' })
      }
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    })
  })

  describe('page heading', () => {
    // A note named by its title, so a saved record's page is headed by it.
    const namedWidget: EntityWidgetMetadata = {
      ...noteWidget,
      entity: { ...noteWidget.entity, displayField: 'title' },
    }

    it('heads a new record with New and its entity, and no line under it', () => {
      renderForm(null, noteWidget, undefined, undefined, null, undefined, false, true)

      const heading = screen.getByTestId('page-heading')
      expect(within(heading).getByRole('heading', { level: 1, name: 'New Note' })).toBeInTheDocument()
      expect(heading).toHaveTextContent(/^New Note$/)
    })

    it('heads a new record in Vietnamese with the Vietnamese New text', () => {
      renderForm(null, noteWidget, undefined, undefined, null, undefined, false, true, vietnameseCatalogs)

      const heading = screen.getByTestId('page-heading')
      expect(within(heading).getByRole('heading', { level: 1, name: 'Note mới' })).toBeInTheDocument()
      expect(heading).toHaveTextContent(/^Note mới$/)
    })

    it("heads a saved record with its name, and its entity's label under it in textMuted", async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, namedWidget, 'light', undefined, null, undefined, false, true)

      const heading = screen.getByTestId('page-heading')
      expect(await within(heading).findByRole('heading', { level: 1, name: 'Buy paper' })).toBeInTheDocument()
      expect(within(heading).getByText('Note')).toHaveStyle({ color: designTokensFor('light').colour.textMuted })
    })

    it("keeps the page's title while the saved record loads", () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, namedWidget, undefined, undefined, null, undefined, false, true)

      expect(
        within(screen.getByTestId('page-heading')).getByRole('heading', { level: 1, name: 'Note' }),
      ).toBeInTheDocument()
    })

    it("keeps the page's title for a record that is missing", async () => {
      stubFetch({ status: 404, body: '{}' })
      renderForm(noteId, namedWidget, undefined, undefined, null, undefined, false, true)

      expect(await screen.findByTestId('record-not-found')).toBeInTheDocument()
      expect(screen.getByTestId('page-heading')).toHaveTextContent(/^Note$/)
    })

    it.each([
      ['an empty', '""'],
      ['a null', 'null'],
    ])("keeps the page's title for a record with %s display value", async (_, title) => {
      stubFetch({ status: 200, body: `{"id":"${noteId}","version":1,"values":{"title":${title}},"labels":{}}` })
      renderForm(noteId, namedWidget, undefined, undefined, null, undefined, false, true)

      await screen.findByRole('textbox', { name: 'Title' })
      expect(screen.getByTestId('page-heading')).toHaveTextContent(/^Note$/)
    })

    it("keeps the page's title on a designer's canvas, for a new record and a saved one", async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(null, noteWidget, undefined, undefined, null, undefined, true, true)
      expect(screen.getByTestId('page-heading')).toHaveTextContent(/^Note$/)

      cleanup()
      renderForm(noteId, namedWidget, undefined, undefined, null, undefined, true, true)
      await screen.findByRole('textbox', { name: 'Title' })
      expect(screen.getByTestId('page-heading')).toHaveTextContent(/^Note$/)
    })
  })

  it('has the choose, clear, add, added and removed texts in every platform locale', () => {
    for (const catalog of [platformEnglish, platformVietnamese]) {
      expect(catalog).toHaveProperty(['shell.form.choose'])
      expect(catalog).toHaveProperty(['shell.form.clear'])
      expect(catalog).toHaveProperty(['shell.form.add'])
      expect(catalog).toHaveProperty(['shell.form.added'])
      expect(catalog).toHaveProperty(['shell.form.removed'])
    }
  })

  it('sends added rows with numbers as typed and without their null fields', async () => {
    const requests = stubFetch({ status: 201, body: linesJson(1) })
    renderForm(null, linesWidget)

    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Buy paper')
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.type(screen.getByRole('textbox', { name: 'Description 1' }), 'Pens')
    await userEvent.type(screen.getByLabelText('Quantity 1'), '12')
    await userEvent.type(screen.getByRole('textbox', { name: 'Description 2' }), 'Ink')
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
    expect(screen.getByRole('textbox', { name: 'Description 2' })).toHaveValue('Ink')

    await userEvent.clear(quantity)
    await userEvent.type(quantity, '3')
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete row' })[1])
    expect(screen.queryByRole('textbox', { name: 'Description 2' })).not.toBeInTheDocument()
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
    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), '!')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(requests()).toHaveLength(2))
    expect(requests()[1].body).toBe('{"version":1,"values":{"title":"Buy paper!"}}')
  })

  it('shows an editable child collection label, required mark and help on its toolbar, not above its table', () => {
    stubFetch()
    renderForm(null, helpedLinesWidget)

    const title = within(screen.getByTestId('axis-table-title'))
    expect(title.getByText('Lines')).toBeInTheDocument()
    expect(title.getByText('*')).toHaveAttribute('aria-hidden', 'true')
    expect(title.getByText('One line per item.')).toHaveAttribute('id', 'lines-help')
    expect(screen.getAllByText('Lines')).toHaveLength(1)
    expect(screen.getByTestId('field-column-lines').querySelector('.ant-form-item-label')).toBeNull()
    // The title and Add row sit in the same toolbar, so they share one row.
    const toolbar = screen.getByTestId('axis-table-title').closest('.ant-flex')?.parentElement
    expect(toolbar).toContainElement(screen.getByRole('button', { name: 'Add row' }))
  })

  it('shows a read-only child collection label above its table, with no toolbar', async () => {
    stubFetch({ status: 200, body: sectionsJson(1) })
    renderForm(noteId, sectionsWidget)

    await screen.findByTestId('cell-lines-0-description')
    expect(screen.getByTestId('field-column-lines').querySelector('.ant-form-item-label')).toHaveTextContent('Lines')
    expect(screen.queryByTestId('axis-table-title')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Add row' })).not.toBeInTheDocument()
  })

  it('shows a child collection reference cell as a search icon button with no visible text', async () => {
    stubFetch()
    renderForm(null, lineCategoryWidget)
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))

    const choose = screen.getByRole('button', { name: 'Choose… Category 1' })
    expect(choose.textContent).toBe('')
    expect(choose.querySelector('.anticon-search')).not.toBeNull()
    expect(choose).toHaveClass('ant-btn-variant-outlined', 'ant-btn-compact-item')
  })

  it('draws the Clear of a child collection reference cell as a bordered button joined to its input', async () => {
    stubFetch({ status: 200, body: categoryPageJson })
    renderForm(null, lineCategoryWidget)
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.click(screen.getByRole('button', { name: 'Choose… Category 1' }))
    await userEvent.click(await within(await screen.findByRole('dialog')).findByText('Office'))

    const clear = await screen.findByRole('button', { name: 'Clear Category 1' })
    expect(clear.textContent).toBe('')
    expect(clear).toHaveClass('ant-btn-variant-outlined', 'ant-btn-compact-item', 'ant-btn-compact-last-item')
  })

  it('shows each child collection row remove button as a danger button with a trash icon', async () => {
    stubFetch()
    renderForm(null, linesWidget)
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))

    const remove = screen.getByRole('button', { name: 'Delete row' })
    expect(remove).toHaveStyle({ color: designTokensFor('light').colour.dangerText })
    expect(remove.querySelector('.anticon-delete')).not.toBeNull()
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

    expect(await screen.findByTestId('field-value-total')).toHaveTextContent('3.00')
    expect(screen.queryByRole('textbox', { name: 'Total' })).not.toBeInTheDocument()
    expect(screen.getByTestId('cell-lines-0-code')).toHaveTextContent('PENS')
    expect(screen.queryByRole('textbox', { name: 'Line code 1' })).toBeNull()

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

  it('finds a computed field and a computed cell by their test ids, with the loaded value', async () => {
    stubFetch({ status: 200, body: computedJson(1) })
    renderForm(noteId, computedWidget)

    await screen.findByTestId('field-value-total')
    expect(screen.getByTestId('field-value-total')).toHaveTextContent('3.00')
    expect(screen.getByTestId('cell-lines-0-code')).toHaveTextContent('PENS')
  })

  it('shows an empty computed cell as a muted dash in plain text, with no input', async () => {
    stubFetch({
      status: 200,
      body: `{"id":"${noteId}","version":1,"values":{"title":"Buy paper","priority":2,"amount":1.50,"total":3.00,"lines":[{"description":"Pens","code":"PENS"},{"description":"Ink"}]},"labels":{}}`,
    })
    renderForm(noteId, computedWidget)

    const empty = await screen.findByTestId('cell-lines-1-code')
    expect(empty).toHaveTextContent('—')
    expect(within(empty).getByText('—')).toHaveStyle({ fontStyle: 'italic' })
    expect(within(empty).queryByRole('textbox')).toBeNull()
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

    expect(screen.getByTestId('field-value-total')).toHaveTextContent('Not set')
    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Buy paper')
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

    const number = screen.getByTestId('field-value-number')
    expect(number).toHaveTextContent('Not set')
    expect(screen.queryByRole('textbox', { name: 'Number' })).not.toBeInTheDocument()
    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Buy paper')
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

    const number = await screen.findByTestId('field-value-number')
    await waitFor(() => expect(number).toHaveTextContent('PR-2026-00042'))
    expect(screen.queryByRole('textbox', { name: 'Number' })).not.toBeInTheDocument()
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

    const createdBy = await screen.findByTestId('field-value-createdBy')
    await waitFor(() => expect(createdBy).toHaveTextContent('requester'))
    expect(screen.queryByRole('textbox', { name: 'Created by' })).not.toBeInTheDocument()
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

    expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('Buy paper')
    expect(screen.getAllByRole('heading').map((heading) => heading.textContent)).toEqual(['Main', 'Details', 'History'])
    expect(screen.getByTestId('field-value-code')).toHaveTextContent('N-1')
    expect(screen.getByLabelText('Priority')).toHaveValue('7')
    expect(screen.queryByLabelText('Done')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Amount')).not.toBeInTheDocument()
  })

  it('draws no heading for a section with no field to show, so it adds no gap', async () => {
    stubFetch({ status: 200, body: sectionsJson(1) })
    const form = sectionsWidget.form!
    renderForm(noteId, {
      ...sectionsWidget,
      form: { ...form, sections: [...form.sections, { name: 'empty', titleKey: 'noteSections.main', fields: [] }] },
    })

    expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('Buy paper')
    expect(screen.getAllByRole('heading').map((heading) => heading.textContent)).toEqual(['Main', 'Details', 'History'])
  })

  it('puts each field in a column of its width, and a field without a width on the whole row', async () => {
    stubFetch({ status: 200, body: sectionsJson(1) })
    const form = sectionsWidget.form!
    renderForm(noteId, {
      ...sectionsWidget,
      form: {
        ...form,
        sections: [
          {
            ...form.sections[0],
            fields: [
              { name: 'title', readOnly: false, width: 6 },
              { name: 'status', readOnly: false, width: 6 },
              { name: 'code', readOnly: true },
            ],
          },
        ],
      },
    })

    const column = (field: HTMLElement) => field.closest('.ant-form-item')?.parentElement
    expect(column(await screen.findByRole('textbox', { name: 'Title' }))).toHaveStyle({ gridColumn: 'span 6' })
    expect(column(await screen.findByLabelText('Status'))).toHaveStyle({ gridColumn: 'span 6' })
    expect(column(await screen.findByTestId('field-value-code'))).toHaveStyle({ gridColumn: 'span 12' })
  })

  it('puts every field on the whole row at narrow width', async () => {
    stubFetch({ status: 200, body: sectionsJson(1) })
    const form = sectionsWidget.form!
    renderForm(
      noteId,
      {
        ...sectionsWidget,
        form: {
          ...form,
          sections: [
            {
              ...form.sections[0],
              fields: [
                { name: 'title', readOnly: false, width: 6 },
                { name: 'status', readOnly: false, width: 6 },
              ],
            },
          ],
        },
      },
      undefined,
      true,
    )

    const column = (field: HTMLElement) => field.closest('.ant-form-item')?.parentElement
    expect(column(await screen.findByRole('textbox', { name: 'Title' }))).toHaveStyle({ gridColumn: 'span 12' })
    expect(column(await screen.findByLabelText('Status'))).toHaveStyle({ gridColumn: 'span 12' })
  })

  it('shows read-only fields and rows without letting them change, and never sends them', async () => {
    const requests = stubFetch({ status: 200, body: sectionsJson(1) }, { status: 200, body: sectionsJson(2) })
    renderForm(noteId, sectionsWidget)

    await screen.findByTestId('field-value-code')
    expect(screen.queryByRole('textbox', { name: 'Code' })).not.toBeInTheDocument()
    expect(screen.getByTestId('field-value-category')).toHaveTextContent('Office')
    expect(screen.queryByRole('button', { name: 'Choose…' })).not.toBeInTheDocument()
    expect(screen.getByTestId('cell-lines-0-description')).toHaveTextContent('Pens')
    expect(screen.queryByRole('textbox', { name: 'Description 1' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Add row' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete row' })).not.toBeInTheDocument()

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
    expect(screen.queryByRole('textbox', { name: 'Code' })).not.toBeInTheDocument()
    unmount()

    renderForm(noteId, fieldedWidget)
    const title = await screen.findByRole('textbox', { name: 'Title' })
    expect(screen.getByTestId('field-value-priority')).toHaveTextContent('7')
    expect(screen.queryByRole('textbox', { name: 'Priority' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Choose…' })).not.toBeInTheDocument()

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

  it('has no save bar and shows every field read-only without the grant for the write', async () => {
    const readerWidget: EntityWidgetMetadata = {
      ...noteWidget,
      access: { actions: ['read', 'update'], createFields: [], updateFields: ['title'] },
    }
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    const { unmount } = renderForm(null, readerWidget)
    expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
    expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    unmount()

    renderForm(noteId, readerWidget)
    const title = await screen.findByRole('textbox', { name: 'Title' })
    expect(title).not.toHaveAttribute('readonly')
    expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
    await userEvent.type(title, '!')
    expect(await screen.findByTestId('form-save-bar')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })

  it('shows no save bar and no Save or Discard to a person without the update grant', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId, { ...noteWidget, access: { actions: ['read'], createFields: [], updateFields: [] } })

    expect(await screen.findByText('Buy paper')).toBeInTheDocument()
    expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Discard' })).not.toBeInTheDocument()
  })

  it('shows an error on a field the form leaves out above the form', async () => {
    stubFetch({
      status: 400,
      body: '{"title":"Invalid","status":400,"errors":{"/values/done":["Required."],"/values/title":["Too long."]}}',
    })
    renderForm(null, sectionsWidget)

    submitForm()

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
      expect(screen.getByRole('textbox', { name: 'Title' })).not.toHaveAttribute('readonly')

      await chooseStatus(user, 'closed')
      await waitFor(() => expect(stateRequests()).toHaveLength(2))
      // The answer to the second call is pending, so the states stay.
      expect(screen.getByLabelText('Priority')).toBeInTheDocument()

      await chooseStatus(user, 'open')
      await waitFor(() => expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument())
      expect(isRequiredLabel(screen.getByText('Status'))).toBe(true)
      expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()

      await act(async () => {
        answerLate('{"sections":[],"fields":{"status":{"visible":true,"required":false,"readOnly":false}},"errors":{}}')
        await vi.advanceTimersByTimeAsync(50)
      })
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      expect(isRequiredLabel(screen.getByText('Status'))).toBe(true)
      expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
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

      await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen')
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

        await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen')
        await act(async () => {
          answerFirst(withDefaults('{"title":"Untitled","priority":3}'))
          await vi.advanceTimersByTimeAsync(50)
        })
        expect(screen.getByRole('textbox', { name: 'Title' })).toHaveValue('Pen')
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
        await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen')
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
      await user.click(screen.getByRole('textbox', { name: 'Title' }))
      await user.tab()
      await waitFor(() => expect(stateRequests()).toHaveLength(3))
      expect(stateRequests()[2].body).toMatchObject({ touched: ['priority', 'title'] })
      expect(await screen.findByText('Required.')).toBeInTheDocument()
      expect(formItem(screen.getByRole('textbox', { name: 'Title' }))).toContainElement(screen.getByText('Required.'))
    })

    it('asks when a saved record is loaded', async () => {
      stubFetch({ status: 200, body: sectionsJson(1) })
      renderForm(noteId, sectionsWidget)
      await screen.findByRole('textbox', { name: 'Title' })
      await waitFor(() => expect(stateRequests()).toHaveLength(1))
      expect(stateRequests()[0].body).toEqual({ recordId: noteId, values: {}, touched: [] })
    })

    it('makes no state call for a widget that names an entity, or when the user cannot save', async () => {
      stubFetch()
      renderForm(null, noteWidget)
      await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen')
      await act(() => new Promise((resolve) => setTimeout(resolve, 400)))
      expect(stateRequests()).toEqual([])
      cleanup()

      renderForm(null, {
        ...sectionsWidget,
        access: { actions: ['read', 'update'], createFields: [], updateFields: ['title'] },
      })
      expect(screen.getByTestId('field-value-title')).toHaveTextContent('Not set')
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
      expect(catalog).toHaveProperty(['shell.form.removeReference'])
    }
  })

  it('draws no Save or Cancel row, and no save bar before a change', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId)

    expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('Buy paper')
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
  })

  it('shows the save bar after a change, and Save sends the change and returns to the table', async () => {
    const requests = stubFetch(
      { status: 200, body: noteJson('Buy paper', 1) },
      { status: 200, body: noteJson('Buy ink', 2) },
    )
    renderForm(noteId)
    const title = await screen.findByRole('textbox', { name: 'Title' })

    await userEvent.clear(title)
    await userEvent.type(title, 'Buy ink')
    const bar = await screen.findByTestId('form-save-bar')
    expect(within(bar).getByText('Unsaved changes')).toBeInTheDocument()
    expect(within(bar).getByTestId('form-save')).toHaveClass('ant-btn-variant-solid')
    await userEvent.click(within(bar).getByTestId('form-save'))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/e2e/notes?pageSize=10'))
    expect(requests()[1]).toEqual({
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"title":"Buy ink"}}',
    })
  })

  function renderGuardedForm() {
    return renderGuarded(
      <TextProvider catalogs={catalogs} development={false}>
        <FormWidget
          widget={noteWidget}
          sitePath="e2e"
          pages={['Notes', 'NoteForm']}
          recordId={noteId}
          returnTo="/e2e/notes?pageSize=10"
          locale="en"
        />
      </TextProvider>,
    )
  }

  it('asks "Leave without saving?" before a link while the form has a change, and not without one', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    const router = renderGuardedForm()
    const title = await screen.findByRole('textbox', { name: 'Title' })
    await userEvent.type(title, '!')

    await followLink()

    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/studio')
  })

  it('returns to the table after a save without asking "Leave without saving?"', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) }, { status: 200, body: noteJson('Buy paper!', 2) })
    const router = renderGuardedForm()
    await userEvent.type(await screen.findByRole('textbox', { name: 'Title' }), '!')

    await userEvent.click(await screen.findByTestId('form-save'))

    await waitFor(() => expect(router.state.location.pathname).toBe('/e2e/notes'))
    expect(router.state.location.search).toBe('?pageSize=10')
    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
  })

  it('asks before Discard, then puts the typed value back and hides the save bar', async () => {
    const requests = stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId)
    const title = await screen.findByRole('textbox', { name: 'Title' })

    await userEvent.type(title, '!')
    expect(title).toHaveValue('Buy paper!')
    await userEvent.click(within(await screen.findByTestId('form-save-bar')).getByRole('button', { name: 'Discard' }))
    expect(await screen.findByText('Discard all unsaved changes?')).toBeInTheDocument()
    expect(title).toHaveValue('Buy paper!')
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Discard' }))

    await waitFor(() => expect(title).toHaveValue('Buy paper'))
    expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
    expect(requests()).toHaveLength(1)
  })

  it('puts a new record back to its empty form on Discard', async () => {
    stubFetch()
    renderForm()
    const title = screen.getByRole('textbox', { name: 'Title' })

    await userEvent.type(title, 'Draft')
    await userEvent.click(within(await screen.findByTestId('form-save-bar')).getByRole('button', { name: 'Discard' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Discard' }))

    await waitFor(() => expect(title).toHaveValue(''))
    expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
  })

  it('draws no save bar inside a designer canvas, whichever change is made', async () => {
    stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
    renderForm(noteId, noteWidget, undefined, undefined, null, undefined, true)
    await userEvent.type(await screen.findByRole('textbox', { name: 'Title' }), '!')

    expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
  })

  it('shows the missing-record line with a link to its list for a record the API answers 404 for', async () => {
    stubFetch({ status: 404, body: '{}' })

    renderForm(noteId, noteWidget, undefined, undefined, { labelKey: 'notes.nav', to: '/e2e/notes?pageSize=10' })

    expect(await screen.findByTestId('record-not-found')).toHaveTextContent(
      'This record does not exist, or you cannot open it. Go to Notes',
    )
    expect(screen.getByRole('link', { name: 'Go to Notes' })).toHaveAttribute('href', '/e2e/notes?pageSize=10')
    expect(screen.queryByTestId('not-found')).toBeNull()
  })

  it('shows no save, cancel or record action for a record the API answers 404 for', async () => {
    stubFetch({ status: 404, body: '{}' })

    renderForm(
      noteId,
      {
        ...actionWidget,
        actions: [
          {
            type: 'navigate',
            page: 'Notes',
            labelKey: 'note.open',
            parameters: {},
            record: null,
            opensForm: false,
            paths: {},
            visibleWhen: null,
          },
          ...actionWidget.actions,
        ],
      },
      undefined,
      undefined,
      { labelKey: 'notes.nav', to: '/e2e/notes' },
    )

    await screen.findByTestId('record-not-found')
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.queryByRole('textbox')).toBeNull()
  })

  it('shows the missing-record line without a link when the page gives no list', async () => {
    stubFetch({ status: 404, body: '{}' })

    renderForm(noteId)

    expect(await screen.findByTestId('record-not-found')).toHaveTextContent(
      'This record does not exist, or you cannot open it.',
    )
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('shows an error when the record cannot be loaded or saved', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubFetch({ status: 500, body: '{}' })
    const { unmount } = renderForm(noteId)
    expect(await screen.findByTestId('form-load-error')).toHaveTextContent('The record could not be loaded.')
    unmount()

    stubFetch({ status: 500, body: '{}' })
    renderForm()
    submitForm()

    expect(await screen.findByTestId('form-error')).toHaveTextContent('The record could not be saved.')
  })

  it('shows the quota title when a save is refused past the storage quota', async () => {
    stubFetch({
      status: 403,
      body: JSON.stringify({
        title: 'This tenant has reached its storage quota.',
        status: 403,
        reason: 'quota-exceeded',
        quota: 'storage',
      }),
    })
    renderForm()
    submitForm()

    expect(await screen.findByTestId('form-error')).toHaveTextContent('This tenant has reached its storage quota.')
  })

  it('keeps the generic message for a 403 without a reason', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubFetch({ status: 403, body: '{"title":"You may not save this record.","status":403}' })
    renderForm()
    submitForm()

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

    it('shows the first step with its progress, and no save bar until the last step', async () => {
      stubFetch()
      const user = await renderSteps()

      expect(screen.getByRole('textbox', { name: 'Title' })).toBeInTheDocument()
      expect(screen.getByLabelText('Code')).toBeInTheDocument()
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      const progress = screen.getByTestId('steps-bar')
      expect(within(progress).getByText('Step one')).toBeInTheDocument()
      expect(within(progress).getByText('Step two')).toBeInTheDocument()
      expect(progress.querySelector('[aria-current="step"]')).toHaveTextContent('Step one')
      expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Next' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
      await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen')
      expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    })

    it('moves on with Next and shows Back, and the save bar after a change, on the last step', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      stubFetch()
      const user = await renderSteps()

      await user.click(screen.getByRole('button', { name: 'Next' }))

      expect(await screen.findByLabelText('Priority')).toBeInTheDocument()
      expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
      expect(screen.getByTestId('steps-bar').querySelector('[aria-current="step"]')).toHaveTextContent('Step two')
      expect(screen.getByRole('button', { name: 'Back' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument()
      expect(screen.queryByTestId('form-save-bar')).not.toBeInTheDocument()
      await user.type(screen.getByLabelText('Priority'), '5')
      expect(await screen.findByTestId('form-save-bar')).toBeInTheDocument()
      // The check sends the step's fields as touched, at once.
      expect(stateRequests()[1].body).toMatchObject({ touched: ['title', 'code'] })
    })

    it('stays on the step when the answer has an error for one of its fields, and Back keeps the values', async () => {
      stateAnswers = [emptyStateJson, titleRequired, emptyStateJson]
      stubFetch()
      const user = await renderSteps()

      await user.click(screen.getByRole('button', { name: 'Next' }))
      expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Required.')
      expect(screen.getByRole('textbox', { name: 'Title' })).toHaveFocus()
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()

      await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen')
      await user.click(screen.getByRole('button', { name: 'Next' }))
      await user.type(await screen.findByLabelText('Priority'), '5')
      await user.click(screen.getByRole('button', { name: 'Back' }))

      expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('Pen')
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Next' }))
      expect(await screen.findByLabelText('Priority')).toHaveValue('5')
    })

    it('acts as Next when Enter is pressed on a step before the last', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      stubFetch()
      const user = await renderSteps()

      await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen{Enter}')

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

      await user.type(screen.getByRole('textbox', { name: 'Title' }), 'Pen')
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
      await user.type(await screen.findByLabelText('Priority'), '5')
      await user.click(await screen.findByRole('button', { name: 'Save' }))

      expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Too long.')
      expect(screen.queryByLabelText('Priority')).not.toBeInTheDocument()
      expect(screen.getByTestId('steps-bar').querySelector('[aria-current="step"]')).toHaveTextContent('Step one')
      const title = screen.getByRole('textbox', { name: 'Title' })
      expect(title).toHaveFocus()
      expect(title).toHaveAttribute('aria-invalid', 'true')
    })

    it('keeps the focus on an error above the form when a failed save also opens the step of a field error', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      stubFetch({
        status: 400,
        body: '{"title":"Invalid","status":400,"errors":{"":["Save refused."],"/values/title":["Too long."]}}',
      })
      const user = await renderSteps()
      await user.click(screen.getByRole('button', { name: 'Next' }))
      await user.type(await screen.findByLabelText('Priority'), '5')
      await user.click(await screen.findByRole('button', { name: 'Save' }))

      expect(await screen.findByTestId('form-error')).toHaveFocus()
      expect(within(screen.getByTestId('form-error')).getByRole('alert')).toHaveTextContent('Save refused.')
      expect(screen.getByTestId('steps-bar').querySelector('[aria-current="step"]')).toHaveTextContent('Step one')
      const title = screen.getByRole('textbox', { name: 'Title' })
      expect(title).toHaveAttribute('aria-invalid', 'true')
      expect(title).not.toHaveFocus()
    })

    it('focuses the first input of the step after a refused save with an error above the form, on Back and on Next', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson]
      stubFetch({
        status: 400,
        body: '{"title":"Invalid","status":400,"errors":{"":["Save refused."]}}',
      })
      const user = await renderSteps()
      await user.click(screen.getByRole('button', { name: 'Next' }))
      await user.type(await screen.findByLabelText('Priority'), '5')
      await user.click(await screen.findByRole('button', { name: 'Save' }))
      expect(await screen.findByTestId('form-error')).toHaveFocus()

      await user.click(screen.getByRole('button', { name: 'Back' }))
      expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveFocus()

      await user.click(screen.getByRole('button', { name: 'Next' }))
      expect(await screen.findByLabelText('Priority')).toHaveFocus()
    })

    it('replaces the message of a refused save with the answer message while the typed value is still wrong', async () => {
      stateAnswers = [emptyStateJson, emptyStateJson, titleRequired]
      stubFetch({
        status: 400,
        body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Too long."]}}',
      })
      const user = await renderSteps()
      await user.click(screen.getByRole('button', { name: 'Next' }))
      await user.type(await screen.findByLabelText('Priority'), '5')
      await user.click(await screen.findByRole('button', { name: 'Save' }))
      expect(await screen.findByTestId('field-error-title')).toHaveTextContent('Too long.')

      await user.type(screen.getByRole('textbox', { name: 'Title' }), 'x')

      await waitFor(() => expect(screen.getByTestId('field-error-title')).toHaveTextContent('Required.'))
      expect(screen.queryByText('Too long.')).not.toBeInTheDocument()
    })

    it('skips a step whose fields the answer hides', async () => {
      stateAnswers = [
        '{"sections":[],"fields":{"priority":{"visible":false,"required":false,"readOnly":false}},"errors":{}}',
      ]
      stubFetch()
      const user = await renderSteps()

      await user.type(await screen.findByRole('textbox', { name: 'Title' }), 'Pen')
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
      expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('')
      expect(screen.queryByRole('button', { name: 'Close note' })).toBeNull()
    })

    const openAction = {
      type: 'navigate' as const,
      page: 'Notes',
      labelKey: 'note.open',
      parameters: { note: 'id' },
      record: null,
      opensForm: false,
      paths: {},
      visibleWhen: null,
    }

    it('draws the start and navigate actions as outlined buttons on the page heading', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(
        noteId,
        { ...actionWidget, actions: [openAction, ...actionWidget.actions] },
        undefined,
        undefined,
        null,
        undefined,
        false,
        true,
      )

      const heading = await screen.findByTestId('page-actions')
      const action = await within(heading).findByRole('button', { name: 'Close note' })
      const open = within(heading).getByRole('button', { name: 'note.open' })
      expect(action).toHaveClass('ant-btn-variant-outlined')
      expect(open).toHaveClass('ant-btn-variant-outlined')
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    })

    it('draws the actions at the top of the form on the right when there is no page heading', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      const { container } = renderForm(noteId, actionWidget)

      const action = await screen.findByRole('button', { name: 'Close note' })
      expect(action).toHaveClass('ant-btn-variant-outlined')
      expect(screen.queryByTestId('page-actions')).not.toBeInTheDocument()
      const row = action.closest<HTMLElement>('.ant-flex')!.parentElement!
      expect(row).toHaveClass('ant-flex-justify-end')
      expect(
        container.querySelector('form')!.compareDocumentPosition(row) & Node.DOCUMENT_POSITION_PRECEDING,
      ).toBeTruthy()
    })

    it('keeps the actions inside the form on a designer canvas, and draws no save bar there', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, actionWidget, undefined, undefined, null, undefined, true, true)

      const action = await screen.findByRole('button', { name: 'Close note' })
      expect(within(screen.getByTestId('page-actions')).queryByRole('button')).not.toBeInTheDocument()
      expect(action).toBeInTheDocument()
    })

    it('keeps the action outlined when the user cannot save the record', async () => {
      stubFetch({ status: 200, body: noteJson('Buy paper', 1) })
      renderForm(noteId, { ...actionWidget, access: { actions: ['read'], createFields: [], updateFields: [] } })

      expect(await screen.findByRole('button', { name: 'Close note' })).toHaveClass('ant-btn-variant-outlined')
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
      expect(await screen.findByRole('textbox', { name: 'Title' })).toHaveValue('')
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
      expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
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
      await waitFor(() => expect(screen.getByRole('textbox', { name: 'Title' })).toHaveValue('Closed by the process'))
      expect(requests()).toEqual([
        { url: `${recordsPath}/${noteId}`, method: 'GET', body: undefined },
        { url: instancesPath, method: 'POST', body: `{"subjectId":"${noteId}","via":"noteClose.action"}` },
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
      const title = await screen.findByRole('textbox', { name: 'Title' })
      await userEvent.clear(title)
      await userEvent.type(title, 'New')

      await userEvent.click(screen.getByRole('button', { name: 'Close note' }))

      expect(await screen.findByTestId('form-started')).toBeInTheDocument()
      await waitFor(() => expect(requests()).toHaveLength(4))
      expect(requests().slice(1, 3)).toEqual([
        { url: `${recordsPath}/${noteId}`, method: 'PATCH', body: '{"version":1,"values":{"title":"New"}}' },
        { url: instancesPath, method: 'POST', body: `{"subjectId":"${noteId}","via":"noteClose.action"}` },
      ])
    })

    it('starts nothing when the save fails', async () => {
      const requests = stubFetch(
        { status: 200, body: noteJson('Buy paper', 1) },
        { status: 400, body: '{"title":"Invalid","status":400,"errors":{"/values/title":["Required."]}}' },
      )
      renderForm(noteId, actionWidget)
      await userEvent.clear(await screen.findByRole('textbox', { name: 'Title' }))

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
