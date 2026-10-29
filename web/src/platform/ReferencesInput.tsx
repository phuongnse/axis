import { Flex } from 'antd'
import { useRef, useState } from 'react'
import { AxisButton } from './AxisButton'
import { ReferenceLookup } from './ReferenceLookup'
import type { RecordItem } from './records'
import type { FieldMetadata } from './site'
import { useText } from './texts'
import { visuallyHidden } from './visuallyHidden'

interface ReferencesInputProps {
  /** The references field to choose records for. Its `target` is not `null`. */
  field: FieldMetadata
  /** The field's label, as the list's name and the lookup's title. */
  title: string
  /** The ids of the chosen records, in order. */
  ids: readonly string[]
  /** The label of each id at the same index, or `null` where the user may not read it. */
  labels: readonly (string | null)[]
  onChange: (ids: string[], labels: (string | null)[]) => void
  /** The id of the element that describes the Add button, such as the field's help text. */
  ariaDescribedBy?: string
}

/**
 * The input of a references field: the chosen records as a list of labels in their order, each with
 * a Remove button, and an Add button that opens the reference lookup to pick one more record. A pick
 * appends the record and closes the lookup, and a record that is in the set already is left out. A
 * polite live region announces each addition and removal. A label that is missing shows the id.
 */
export function ReferencesInput({ field, title, ids, labels, onChange, ariaDescribedBy }: ReferencesInputProps) {
  const t = useText()
  const [open, setOpen] = useState(false)
  const [announcement, setAnnouncement] = useState('')
  const add = useRef<HTMLButtonElement>(null)
  const labelAt = (index: number) => labels[index] ?? ids[index]

  const pick = (record: RecordItem) => {
    const display = field.target?.displayField
    setOpen(false)
    if (ids.includes(record.id)) {
      return
    }
    const label = display ? String(record.values[display] ?? '') : record.id
    onChange([...ids, record.id], [...ids.map((_, index) => labels[index] ?? null), label])
    setAnnouncement(`${t('shell.form.added')} ${label}`)
  }

  const remove = (index: number) => {
    const label = labelAt(index)
    onChange(
      ids.filter((_, position) => position !== index),
      ids.map((_, position) => labels[position] ?? null).filter((_, position) => position !== index),
    )
    setAnnouncement(`${t('shell.form.removed')} ${label}`)
    add.current?.focus()
  }

  return (
    <div data-testid={`references-${field.name}`}>
      {ids.length > 0 && (
        <ul aria-label={title} style={{ listStyle: 'none', margin: '0 0 8px', padding: 0 }}>
          {ids.map((id, index) => (
            <li key={id} style={{ marginBottom: 4 }}>
              <Flex align="center" gap="small">
                <span>{labelAt(index)}</span>
                <AxisButton
                  role="destructive"
                  size="small"
                  aria-label={`${t('shell.form.removeRow')} ${labelAt(index)}`}
                  onClick={() => remove(index)}
                >
                  {t('shell.form.removeRow')}
                </AxisButton>
              </Flex>
            </li>
          ))}
        </ul>
      )}
      {/* The field's label points at this button, so its own name says what it adds to. */}
      <AxisButton
        role="secondary"
        id={field.name}
        ref={add}
        aria-label={`${t('shell.form.add')} ${title}`}
        aria-describedby={ariaDescribedBy}
        onClick={() => setOpen(true)}
      >
        {t('shell.form.add')}
      </AxisButton>
      <ReferenceLookup field={field} title={title} open={open} onPick={pick} onClose={() => setOpen(false)} />
      <div
        role="status"
        aria-live="polite"
        style={visuallyHidden}
        data-testid={`references-${field.name}-announcement`}
      >
        {announcement}
      </div>
    </div>
  )
}
