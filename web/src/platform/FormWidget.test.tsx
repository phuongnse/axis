import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { FormWidget } from './FormWidget'
import type { FieldMetadata, FieldType, WidgetMetadata } from './site'
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
const noteId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'

const noteWidget: WidgetMetadata = {
  type: 'form',
  formPage: null,
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

function noteJson(title: string, version: number) {
  return `{"id":"${noteId}","version":${version},"values":{"title":"${title}","code":null,"priority":7,"amount":12.50,"done":false,"dueOn":null,"dueAt":"2026-10-06T02:00:00.123456Z","status":null,"category":"${categoryId}"},"labels":{"category":"Office"}}`
}

const texts = {
  'shell.form.save': 'Save',
  'shell.form.cancel': 'Cancel',
  'shell.form.reload': 'Reload',
  'shell.form.conflict': 'This record was changed since you opened it.',
  'shell.form.saveFailed': 'The record could not be saved.',
  'shell.form.loadFailed': 'The record could not be loaded.',
  'shell.notFound.title': 'Page not found',
  'note.title': 'Title',
  'note.code': 'Code',
  'note.priority': 'Priority',
  'note.amount': 'Amount',
  'note.done': 'Done',
  'note.dueOn': 'Due on',
  'note.dueAt': 'Due at',
  'note.status': 'Status',
  'note.category': 'Category',
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

function renderForm(recordId: string | null = null) {
  const path = `/e2e/noteform/${recordId ?? 'new'}`
  return render(
    <MemoryRouter initialEntries={[{ pathname: path, state: { from: '/e2e/notes?pageSize=10' } }]}>
      <TextProvider catalogs={catalogs} development={false}>
        <FormWidget widget={noteWidget} recordId={recordId} returnTo="/e2e/notes?pageSize=10" />
      </TextProvider>
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
