import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FileInput } from './FileInput'
import type { FileValue } from './records'
import type { FieldMetadata } from './site'
import { TextProvider } from './TextProvider'

const fileId = '3c1d5e7f-9a2b-4c4d-8e6f-0a1b2c3d4e5f'
const uploadPath = '/api/apps/E2eApp/entities/Asset/fields/receipt/files'
const stored: FileValue = { id: fileId, name: 'receipt.pdf', mediaType: 'application/pdf', size: '14' }

function fileField(overrides: Partial<FieldMetadata> = {}): FieldMetadata {
  return {
    name: 'receipt',
    type: 'file',
    labelKey: null,
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
    maxSize: 1024,
    accept: ['application/pdf'],
    uploadPath,
    downloadPath: '/api/apps/E2eApp/files',
    ...overrides,
  }
}

const texts = {
  'shell.form.upload': 'Upload',
  'shell.form.uploaded': 'Uploaded',
  'shell.form.removed': 'Removed',
  'shell.form.removeFile': 'Remove',
  'shell.form.fileTooLarge': 'The file is too large.',
  'shell.form.fileTypeRefused': 'This type of file is not accepted.',
  'shell.form.uploadFailed': 'The file could not be uploaded.',
}
const catalogs = [{ texts, fallbackTexts: texts }]

/** Keeps the value the input reports, as a form does, and records every change. */
function Harness({
  field,
  initial,
  changes,
}: {
  field: FieldMetadata
  initial: FileValue | null
  changes: (FileValue | null)[]
}) {
  const [value, setValue] = useState(initial)
  return (
    <TextProvider catalogs={catalogs} development={false}>
      <FileInput
        field={field}
        value={value}
        label="Receipt"
        id="receipt"
        onChange={(next) => {
          changes.push(next)
          setValue(next)
        }}
      />
    </TextProvider>
  )
}

function renderInput(field: FieldMetadata, initial: FileValue | null = null) {
  const changes: (FileValue | null)[] = []
  render(<Harness field={field} initial={initial} changes={changes} />)
  return changes
}

function stubUpload(status: number, body = '') {
  const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => new Response(body, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const pdf = () => new File(['%PDF-1.4\n%test\n'], 'receipt.pdf', { type: 'application/pdf' })
const answered =
  '{"id":"3c1d5e7f-9a2b-4c4d-8e6f-0a1b2c3d4e5f","name":"receipt.pdf","mediaType":"application/pdf","size":14}'

afterEach(() => vi.unstubAllGlobals())

describe('FileInput', () => {
  it('uploads a picked file, shows its name as a download link and reports the stored file', async () => {
    const fetchMock = stubUpload(201, answered)
    const changes = renderInput(fileField())

    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())

    const link = await screen.findByRole('link', { name: 'receipt.pdf' })
    expect(link).toHaveAttribute('href', `/api/apps/E2eApp/files/${fileId}`)
    expect(changes).toEqual([stored])
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(uploadPath)
    expect(init!.method).toBe('POST')
    expect((init!.body as FormData).get('file')).toBeInstanceOf(File)
    expect(new Headers(init!.headers).get('X-Axis-Csrf')).toBe('1')
    expect(screen.getByTestId('file-receipt-announcement')).toHaveTextContent('Uploaded receipt.pdf')
  })

  it('offers only the accepted types in the file picker', () => {
    renderInput(fileField({ accept: ['application/pdf', 'image/*'] }))

    expect(screen.getByTestId('file-picker-receipt')).toHaveAttribute('accept', 'application/pdf,image/*')
  })

  it('removes the file of a field that is not required and moves focus to the upload button', async () => {
    const changes = renderInput(fileField(), stored)

    await userEvent.click(screen.getByRole('button', { name: 'Remove Receipt' }))

    expect(changes).toEqual([null])
    expect(screen.queryByRole('link', { name: 'receipt.pdf' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Upload Receipt' })).toHaveFocus()
    expect(screen.getByTestId('file-receipt-announcement')).toHaveTextContent('Removed receipt.pdf')
  })

  it('has no remove button on a required field', () => {
    renderInput(fileField({ required: true }), stored)

    expect(screen.getByRole('link', { name: 'receipt.pdf' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove Receipt' })).toBeNull()
  })

  it('shows the too large message when the server answers 413', async () => {
    stubUpload(413)
    const changes = renderInput(fileField())

    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())

    expect(await screen.findByRole('alert')).toHaveTextContent('The file is too large.')
    expect(changes).toEqual([])
  })

  it('shows the type message when the server answers 415', async () => {
    stubUpload(415)
    renderInput(fileField({ accept: null }))

    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())

    expect(await screen.findByRole('alert')).toHaveTextContent('This type of file is not accepted.')
  })

  it('shows a generic message for any other failure, also when the request throws', async () => {
    stubUpload(500)
    renderInput(fileField())
    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())
    expect(await screen.findByRole('alert')).toHaveTextContent('The file could not be uploaded.')

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('The file could not be uploaded.'))
  })

  it('refuses a file above the limit in the browser without sending it', async () => {
    const fetchMock = stubUpload(201, answered)
    const changes = renderInput(fileField({ maxSize: 4 }))

    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())

    expect(await screen.findByRole('alert')).toHaveTextContent('The file is too large.')
    expect(fetchMock).not.toHaveBeenCalled()
    expect(changes).toEqual([])
  })

  it('clears the message after the next successful upload', async () => {
    stubUpload(413)
    renderInput(fileField())
    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())
    await screen.findByRole('alert')

    stubUpload(201, answered)
    await userEvent.upload(screen.getByTestId('file-picker-receipt'), pdf())

    await screen.findByRole('link', { name: 'receipt.pdf' })
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('shows no upload button without an upload path, and none when read-only', () => {
    const { unmount } = render(
      <TextProvider catalogs={catalogs} development={false}>
        <FileInput field={fileField({ uploadPath: null })} value={stored} label="Receipt" onChange={() => undefined} />
      </TextProvider>,
    )
    expect(screen.queryByRole('button', { name: 'Upload Receipt' })).toBeNull()
    expect(screen.getByRole('link', { name: 'receipt.pdf' })).toBeInTheDocument()
    unmount()

    render(
      <TextProvider catalogs={catalogs} development={false}>
        <FileInput field={fileField()} value={stored} label="Receipt" readOnly onChange={() => undefined} />
      </TextProvider>,
    )
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('shows the name as plain text when the field has no download path', () => {
    renderInput(fileField({ downloadPath: null }), stored)

    expect(screen.getByText('receipt.pdf')).toBeInTheDocument()
    expect(screen.queryByRole('link')).toBeNull()
  })
})
