import { Button, Form, Input, Space, Typography } from 'antd'
import { Fragment, useCallback, useMemo, useState, type ReactNode } from 'react'
import { ChildCollectionTable } from './ChildCollectionTable'
import { FieldInput } from './FieldInput'
import { formatValue } from './formatValue'
import type { FieldValue, RecordItem, RecordProblem, RecordRow, RecordValue } from './records'
import { ReferenceLookup } from './ReferenceLookup'
import type { EntityMetadata, FieldMetadata, FormMetadata } from './site'
import { useText } from './texts'

/** The values the form started from, which decide what changed, with the record's labels and version. */
interface Snapshot {
  initial: Record<string, FieldValue>
  labels: Record<string, string>
  version: number
}

/** A field the form shows, and whether it is shown without letting it be changed. */
interface ShownField {
  field: FieldMetadata
  readOnly: boolean
}

/** A titled group of shown fields. The entity shorthand has one section without a title. */
export interface Section {
  titleKey: string | null
  fields: ShownField[]
}

/**
 * The sections a form shows over its entity. With no form, every field is in one section without
 * a title. A computed or sequence field is set by the server, so it is read-only in any form.
 */
export function sectionsOf(entity: EntityMetadata, form: FormMetadata | null): Section[] {
  if (form === null) {
    return [
      { titleKey: null, fields: entity.fields.map((field) => ({ field, readOnly: field.computed || field.sequence })) },
    ]
  }
  return form.sections.map((section) => ({
    titleKey: section.titleKey,
    fields: section.fields.flatMap((entry) => {
      const field = entity.fields.find((candidate) => candidate.name === entry.name)
      return field ? [{ field, readOnly: entry.readOnly || field.computed || field.sequence }] : []
    }),
  }))
}

const fieldPointer = '/values/'

function emptySnapshot(fields: readonly FieldMetadata[]): Snapshot {
  return {
    initial: Object.fromEntries(fields.map((field) => [field.name, field.type === 'child-collection' ? [] : null])),
    labels: {},
    version: 0,
  }
}

function snapshotOf(record: RecordItem): Snapshot {
  return { initial: record.values, labels: record.labels, version: record.version }
}

function rowsOf(value: FieldValue | undefined): RecordRow[] {
  return Array.isArray(value) ? value : []
}

// Rows are compared by value, so an edit that is typed and then undone sends nothing.
function sameValue(field: FieldMetadata, a: FieldValue | undefined, b: FieldValue | undefined): boolean {
  if (field.type !== 'child-collection') {
    return a === b
  }
  const rowsA = rowsOf(a)
  const rowsB = rowsOf(b)
  return (
    rowsA.length === rowsB.length &&
    rowsA.every((row, index) =>
      (field.fields ?? []).every((child) => (row[child.name] ?? null) === (rowsB[index][child.name] ?? null)),
    )
  )
}

/**
 * The inputs of a record form, shared by the form widget and the task page. It holds the values
 * the form started from and the edited ones, renders the sections with their inputs and reference
 * lookups, tells which editable fields changed, and puts a problem's messages on the shown fields
 * their JSON Pointer names, on child collection cells, or above the form.
 */
export function useRecordForm(entity: EntityMetadata, sections: readonly Section[], locale: string) {
  const t = useText()
  const shown = useMemo(() => sections.flatMap((section) => section.fields), [sections])
  const [snapshot, setSnapshot] = useState<Snapshot>(() => emptySnapshot(entity.fields))
  const [values, setValues] = useState<Record<string, FieldValue>>(snapshot.initial)
  // The label of each reference, which follows the record picked in the lookup.
  const [labels, setLabels] = useState<Record<string, string>>(snapshot.labels)
  // The name of the reference field whose lookup is open.
  const [lookup, setLookup] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  // The errors of each child collection's cells, keyed by collection, then by `<index>/<field>`.
  const [cellErrors, setCellErrors] = useState<Record<string, Record<string, string[]>>>({})
  const [formErrors, setFormErrors] = useState<string[]>([])

  const clearErrors = useCallback(() => {
    setFieldErrors({})
    setCellErrors({})
    setFormErrors([])
  }, [])

  const start = useCallback(
    (record: RecordItem) => {
      const next = snapshotOf(record)
      setSnapshot(next)
      setValues(next.initial)
      setLabels(next.labels)
      clearErrors()
    },
    [clearErrors],
  )

  // A read-only field is shown but never sent, so a hand edit of its input changes nothing.
  const changed = (): Record<string, FieldValue> =>
    Object.fromEntries(
      shown
        .filter(({ readOnly }) => !readOnly)
        .map(({ field }) => field)
        .filter(
          (field) =>
            !field.computed && !field.sequence && !sameValue(field, values[field.name], snapshot.initial[field.name]),
        )
        .map((field) => [field.name, values[field.name]]),
    )

  // A pointer such as `lines/1/quantity` names a cell when the collection, its row and the child
  // field all exist.
  const cellOf = (name: string) => {
    const [collection, index, child, ...rest] = name.split('/')
    const field = shown.find((candidate) => candidate.field.name === collection)?.field
    if (
      rest.length > 0 ||
      field?.type !== 'child-collection' ||
      !/^(0|[1-9]\d*)$/.test(index ?? '') ||
      Number(index) >= rowsOf(values[collection]).length ||
      !field.fields?.some((candidate) => candidate.name === child)
    ) {
      return null
    }
    return { collection, key: `${index}/${child}` }
  }

  const showProblem = (problem: RecordProblem) => {
    const onFields: Record<string, string[]> = {}
    const onCells: Record<string, Record<string, string[]>> = {}
    const onForm: string[] = []
    for (const pointer of Object.keys(problem.errors)) {
      const name = pointer.startsWith(fieldPointer) ? pointer.slice(fieldPointer.length) : null
      const cell = name === null ? null : cellOf(name)
      // A failed validation's message is a text key. A fixed message is no key, so it shows as is.
      const messages = problem.errors[pointer].map((message) => t(message))
      if (cell) {
        onCells[cell.collection] = { ...onCells[cell.collection], [cell.key]: messages }
      } else if (name !== null && shown.some(({ field }) => field.name === name)) {
        onFields[name] = messages
      } else {
        onForm.push(...messages)
      }
    }
    setFieldErrors(onFields)
    setCellErrors(onCells)
    setFormErrors(onForm)
  }

  const set = (name: string, value: FieldValue) => setValues((previous) => ({ ...previous, [name]: value }))

  // Error keys use row indexes, so adding or removing a row clears the collection's cell errors.
  const setRows = (field: FieldMetadata, rows: RecordRow[]) => {
    if (rows.length !== rowsOf(values[field.name]).length) {
      setCellErrors((previous) => {
        const next = { ...previous }
        delete next[field.name]
        return next
      })
    }
    set(field.name, rows)
  }
  const labelOf = (field: FieldMetadata) => (field.labelKey ? t(field.labelKey) : field.name)

  const pick = (field: FieldMetadata, record: RecordItem) => {
    const display = field.target?.displayField
    set(field.name, record.id)
    setLabels((previous) => ({
      ...previous,
      [field.name]: display ? String(record.values[display] ?? '') : record.id,
    }))
    setLookup(null)
  }

  const clear = (field: FieldMetadata) => {
    set(field.name, null)
    setLabels((previous) => {
      const next = { ...previous }
      delete next[field.name]
      return next
    })
  }

  const input = (field: FieldMetadata, readOnly: boolean): ReactNode => {
    const value = values[field.name] ?? null
    const id = field.name
    if (field.type !== 'child-collection' && (readOnly || field.computed || field.sequence)) {
      // The server sets the value on save, so it is shown as a table cell shows it.
      return <Input id={id} readOnly value={formatValue(field, value as RecordValue, labels, { locale, text: t })} />
    }
    switch (field.type) {
      case 'reference':
        // The label cannot be typed. The record is chosen in the lookup, and its id is sent.
        return (
          <Space.Compact block>
            <Input id={id} readOnly value={labels[field.name] ?? ''} />
            <Button onClick={() => setLookup(field.name)}>{t('shell.form.choose')}</Button>
            {!field.required && value !== null && <Button onClick={() => clear(field)}>{t('shell.form.clear')}</Button>}
          </Space.Compact>
        )
      case 'child-collection':
        return (
          <ChildCollectionTable
            readOnly={readOnly}
            field={field}
            rows={rowsOf(value)}
            onChange={(rows) => setRows(field, rows)}
            errors={cellErrors[field.name] ?? {}}
            locale={locale}
          />
        )
      default:
        return (
          <FieldInput field={field} id={id} value={value as RecordValue} onChange={(next) => set(field.name, next)} />
        )
    }
  }

  const fields = (
    <>
      {sections.map((section, index) => (
        <Fragment key={index}>
          {section.titleKey !== null && <Typography.Title level={5}>{t(section.titleKey)}</Typography.Title>}
          {section.fields.map(({ field, readOnly }) => {
            const errors = fieldErrors[field.name]
            return (
              <Form.Item
                key={field.name}
                label={labelOf(field)}
                // A child collection is a table, with no single input for the label to point to.
                htmlFor={field.type === 'child-collection' ? undefined : field.name}
                required={field.required}
                validateStatus={errors ? 'error' : undefined}
                help={errors && <span data-testid={`field-error-${field.name}`}>{errors.join(' ')}</span>}
              >
                {input(field, readOnly)}
              </Form.Item>
            )
          })}
        </Fragment>
      ))}
      {shown
        .filter(({ field, readOnly }) => !readOnly && field.type === 'reference' && field.target)
        .map(({ field }) => (
          <ReferenceLookup
            key={field.name}
            field={field}
            title={labelOf(field)}
            open={lookup === field.name}
            onPick={(record) => pick(field, record)}
            onClose={() => setLookup(null)}
          />
        ))}
    </>
  )

  return {
    fields,
    start,
    changed,
    version: snapshot.version,
    showProblem,
    clearErrors,
    formErrors,
    setFormErrors,
  }
}
