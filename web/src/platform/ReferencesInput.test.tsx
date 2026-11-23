import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { ReferencesInput } from './ReferencesInput'
import type { RecordItem } from './records'
import type { FieldMetadata } from './site'
import { TextProvider } from './TextProvider'

// The lookup opens a list of records from the server. A button stands in for it and picks one record.
vi.mock('./ReferenceLookup', () => ({
  ReferenceLookup: ({ open, onPick }: { open: boolean; onPick: (record: RecordItem) => void }) =>
    open ? (
      <button type="button" onClick={() => onPick({ id: 'w1', version: 1, values: { title: 'Word' }, labels: {} })}>
        Pick Word
      </button>
    ) : null,
}))

const field: FieldMetadata = {
  name: 'software',
  type: 'references',
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
  target: { entity: 'Software', displayField: 'title', recordsPath: '/api/apps/E2eApp/entities/Software/records' },
  fields: null,
}

/** Keeps the ids and labels the input reports, as a form does. */
function Harness({ texts }: { texts: Record<string, string> }) {
  const [ids, setIds] = useState<string[]>([])
  const [labels, setLabels] = useState<(string | null)[]>([])
  return (
    <MemoryRouter>
      <TextProvider catalogs={[{ texts, fallbackTexts: platformEnglish }]} development={false}>
        <ReferencesInput
          field={field}
          title="Software"
          ids={ids}
          labels={labels}
          onChange={(nextIds, nextLabels) => {
            setIds(nextIds)
            setLabels(nextLabels)
          }}
        />
      </TextProvider>
    </MemoryRouter>
  )
}

describe.each([
  {
    locale: 'en',
    texts: platformEnglish,
    add: 'Add Software',
    added: 'Added Word',
    remove: 'Remove Word',
    removed: 'Removed Word',
  },
  {
    locale: 'vi',
    texts: platformVietnamese,
    add: 'Thêm Software',
    added: 'Đã thêm Word',
    remove: 'Gỡ Word',
    removed: 'Đã xóa Word',
  },
])('ReferencesInput in $locale', ({ texts, add, added, remove, removed }) => {
  it('names its Add and Remove buttons and announces an addition and a removal through one text each', async () => {
    render(<Harness texts={texts} />)

    await userEvent.click(screen.getByRole('button', { name: add }))
    await userEvent.click(screen.getByRole('button', { name: 'Pick Word' }))
    expect(screen.getByTestId('references-software-announcement')).toHaveTextContent(added)

    await userEvent.click(screen.getByRole('button', { name: remove }))
    expect(screen.getByTestId('references-software-announcement')).toHaveTextContent(removed)
  })
})
