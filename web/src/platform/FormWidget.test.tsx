import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import dayjs from 'dayjs'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
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
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
    fields: null,
  }
}

const recordsPath = '/api/apps/E2eApp/entities/Note/records'
const noteId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'

const noteWidget: EntityWidgetMetadata = {
  type: 'form',
  formPage: null,
  dataSource: null,
  form: null,
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
        titleKey: 'noteSections.main',
        fields: [
          { name: 'title', readOnly: false },
          { name: 'code', readOnly: true },
          { name: 'category', readOnly: true },
        ],
      },
      {
        titleKey: 'noteSections.details',
        fields: [
          { name: 'priority', readOnly: false },
          { name: 'lines', readOnly: true },
        ],
      },
    ],
  },
}

function sectionsJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"Buy paper","code":"N-1","priority":7,"done":true,"category":"${categoryId}","lines":[{"description":"Pens","quantity":2}]},"labels":{"category":"Office"}}`
}

function sequenceJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"number":"PR-2026-00042","title":"Buy paper","priority":2},"labels":{}}`
}

function computedJson(version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"Buy paper","priority":2,"amount":1.50,"total":3.00,"lines":[{"description":"Pens","code":"PENS"}]},"labels":{}}`
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
  'note.number': 'Number',
  'note.title': 'Title',
  'note.code': 'Code',
  'note.priority': 'Priority',
  'note.amount': 'Amount',
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
  return <output data-testid="location">{`${location.pathname}${location.search}`}</output>
}

function renderForm(recordId: string | null = null, widget: EntityWidgetMetadata = noteWidget) {
  const path = `/e2e/noteform/${recordId ?? 'new'}`
  return render(
    <MemoryRouter initialEntries={[{ pathname: path, state: { from: '/e2e/notes?pageSize=10' } }]}>
      {/* Without motion the lookup dialog leaves the document at once, as jsdom runs no animations. */}
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <FormWidget widget={widget} recordId={recordId} returnTo="/e2e/notes?pageSize=10" locale="en" />
        </TextProvider>
      </ConfigProvider>
      <CurrentLocation />
    </MemoryRouter>,
  )
}

describe('FormWidget', () => {
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

  it('marks required fields without blocking submit', async () => {
    stubFetch()
    renderForm()

    // Ant Design marks a required item by a class on its label; the server still decides.
    expect(screen.getByText('Title', { selector: 'label' })).toHaveClass('ant-form-item-required')
    expect(screen.getByText('Code', { selector: 'label' })).not.toHaveClass('ant-form-item-required')
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
    expect(screen.getByLabelText('Quantity 2').closest('.ant-form-item')).toHaveClass('ant-form-item-has-error')
    expect(screen.getByLabelText('Quantity 1').closest('.ant-form-item')).not.toHaveClass('ant-form-item-has-error')
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
      url: `${recordsPath}/${noteId}`,
      method: 'PATCH',
      body: '{"version":1,"values":{"priority":3}}',
    })
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
})
