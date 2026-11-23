import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { ImportDialog } from './ImportDialog'
import type { FieldMetadata } from './site'
import { TextProvider } from './TextProvider'

const importsPath = '/api/apps/E2eApp/entities/Note/imports'
const importId = '6f0c2a54-3b7e-4d1a-9c58-2e4f7a1b9d03'

function field(name: string): FieldMetadata {
  return {
    name,
    type: 'text',
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
  }
}

function answer(state: string, errors: Record<string, string[]> = {}, status = 200) {
  return new Response(JSON.stringify({ id: importId, state, key: null, errors, createdAt: '2026-01-01T00:00:00Z' }), {
    status,
  })
}

/** Accepts the upload as pending and answers every read of the import as failed with four kinds of error. */
function stubFailedImport() {
  const fetchMock = vi.fn(async (input: string, _init?: RequestInit) => {
    if (input === importsPath) {
      return answer('pending', {}, 202)
    }
    return answer('failed', {
      '/rows/2/values/title': ['Required.'],
      '/rows/3': ['Wrong number of cells.'],
      '/columns/3': ['Unknown field.'],
      '/columns': ['No header.'],
    })
  })
  vi.stubGlobal('fetch', fetchMock)
}

afterEach(() => vi.unstubAllGlobals())

describe.each([
  {
    locale: 'en',
    texts: platformEnglish,
    check: 'Check',
    lines: ['Row 2, title: Required.', 'Row 3: Wrong number of cells.', 'Column 3: Unknown field.', 'File: No header.'],
  },
  {
    locale: 'vi',
    texts: platformVietnamese,
    check: 'Kiểm tra',
    lines: ['Dòng 2, title: Required.', 'Dòng 3: Wrong number of cells.', 'Cột 3: Unknown field.', 'Tệp: No header.'],
  },
])('ImportDialog in $locale', ({ texts, check, lines }) => {
  it('lists each error as a row and field, row, column or file line', async () => {
    stubFailedImport()
    render(
      <TextProvider catalogs={[{ texts, fallbackTexts: platformEnglish }]} development={false}>
        <ImportDialog
          importsPath={importsPath}
          fields={[field('title')]}
          onClose={() => undefined}
          onCommitted={() => undefined}
        />
      </TextProvider>,
    )
    const dialog = screen.getByRole('dialog')
    const input = dialog.querySelector('input[type=file]') as HTMLInputElement
    await userEvent.upload(input, new File(['title\nBuy\n'], 'notes.csv', { type: 'application/vnd.ms-excel' }))
    await userEvent.click(within(dialog).getByRole('button', { name: check }))

    const status = await screen.findByTestId('import-status')
    await waitFor(() => expect(status).toHaveTextContent(lines[0]))
    for (const line of lines) {
      expect(status).toHaveTextContent(line)
    }
  })
})
