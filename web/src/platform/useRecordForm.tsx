import { Button, Form, Input, Space, Typography } from 'antd'
import { Fragment, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { ChildCollectionTable } from './ChildCollectionTable'
import { FieldInput } from './FieldInput'
import { fetchFormState, type FormStateAnswer } from './formState'
import { formatValue } from './formatValue'
import { buildFormStateBody } from './recordBody'
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
  /** The name a wizard step lists, or `null` when the section has none, as the entity shorthand's. */
  name: string | null
  titleKey: string | null
  fields: ShownField[]
}

/**
 * The sections a form shows over its entity. With no form, every field is in one section without
 * a title. A computed, sequence or creator field is set by the server, so it is read-only in any
 * form. With `writable`, a field it does not hold is read-only too, because the user may not write
 * it.
 */
export function sectionsOf(
  entity: EntityMetadata,
  form: FormMetadata | null,
  writable?: ReadonlySet<string>,
): Section[] {
  const fixed = (field: FieldMetadata) =>
    field.computed || field.sequence || field.creator || (writable !== undefined && !writable.has(field.name))
  if (form === null) {
    return [{ name: null, titleKey: null, fields: entity.fields.map((field) => ({ field, readOnly: fixed(field) })) }]
  }
  return form.sections.map((section) => ({
    name: section.name,
    titleKey: section.titleKey,
    fields: section.fields.flatMap((entry) => {
      const field = entity.fields.find((candidate) => candidate.name === entry.name)
      return field ? [{ field, readOnly: entry.readOnly || fixed(field) }] : []
    }),
  }))
}

/**
 * Where a form asks the server for its states: the form state endpoint, and the id of the record
 * being edited, or `null` for a new one. It is left out for an endpoint that knows its record, such
 * as a task's, which rejects the key.
 */
export interface FormConditions {
  statePath: string
  recordId?: string | null
}

/** A call to the form state endpoint waits `delay` ms. A new `n` replaces the call that waits. */
interface StateRequest {
  delay: 0 | 300
  n: number
}

const fieldPointer = '/values/'

// Typing sends many changes, so a text or number input and a child collection wait for a pause. A
// choice is one change, so it asks at once.
function delayOf(type: FieldMetadata['type'] | undefined): StateRequest['delay'] {
  return type === 'text' || type === 'integer' || type === 'decimal' || type === 'child-collection' ? 300 : 0
}

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
 *
 * With `conditions`, it also asks the server's form state endpoint which fields to show, require
 * and lock, and which messages to show. It asks once when a record is started, at once after a
 * choice or the first blur of a field, and 300 ms after typing stops. It keeps the last answered
 * states while a call is pending and drops an answer that a newer call has overtaken. It never
 * sends a field the answer hides or locks, and shows the answer's messages under touched fields.
 * A task's conditions have no `recordId`, so the hook asks when the task's subject record starts.
 */
export function useRecordForm(
  entity: EntityMetadata,
  sections: readonly Section[],
  locale: string,
  conditions?: FormConditions,
) {
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
  const asking = conditions !== undefined
  // The fields the user has changed or left.
  const [touched, setTouched] = useState<ReadonlySet<string>>(() => new Set())
  const [answer, setAnswer] = useState<FormStateAnswer | null>(null)
  // A new record has nothing to wait for, so it asks when it opens. A saved one asks when it starts.
  const [request, setRequest] = useState<StateRequest>({ delay: 0, n: asking && conditions.recordId === null ? 1 : 0 })
  // The number of the newest call sent. An answer that is not for it is dropped.
  const sequence = useRef(0)
  // The call the timer sends, read when it fires so it uses the newest values.
  const draft = useRef<(() => { statePath: string; body: string }) | null>(null)

  const ask = useCallback(
    (delay: StateRequest['delay']) => setRequest((previous) => ({ delay, n: previous.n + 1 })),
    [],
  )

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
      if (asking) {
        // An answer for the record that was open before is no answer for this one.
        sequence.current++
        setTouched(new Set())
        setAnswer(null)
        ask(0)
      }
    },
    [clearErrors, asking, ask],
  )

  const stateOf = (name: string) => answer?.fields[name]
  // A field the form or the answer locks is shown but never sent.
  const lockedOf = ({ field, readOnly }: ShownField) => readOnly || stateOf(field.name)?.readOnly === true
  const hiddenOf = (field: FieldMetadata) => stateOf(field.name)?.visible === false

  // A read-only field is shown but never sent, so a hand edit of its input changes nothing. A field
  // the answer hides is not sent either.
  const changed = (): Record<string, FieldValue> =>
    Object.fromEntries(
      shown
        .filter((entry) => !lockedOf(entry) && !hiddenOf(entry.field))
        .map(({ field }) => field)
        .filter(
          (field) =>
            !field.computed &&
            !field.sequence &&
            !field.creator &&
            !sameValue(field, values[field.name], snapshot.initial[field.name]),
        )
        .map((field) => [field.name, values[field.name]]),
    )

  // Whether the form or the answer locks the shown field of that name.
  const locked = (name: string) => {
    const entry = shown.find(({ field }) => field.name === name)
    return entry !== undefined && lockedOf(entry)
  }

  // Whether the answer hides the shown field of that name.
  const hidden = (name: string) => {
    const entry = shown.find(({ field }) => field.name === name)
    return entry !== undefined && hiddenOf(entry.field)
  }

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

  // Returns the names of the fields and collections that got a message, so a wizard can open the
  // step that has them.
  const showProblem = (problem: RecordProblem): string[] => {
    const onFields: Record<string, string[]> = {}
    const onCells: Record<string, Record<string, string[]>> = {}
    const onForm: string[] = []
    const named = new Set<string>()
    for (const pointer of Object.keys(problem.errors)) {
      const name = pointer.startsWith(fieldPointer) ? pointer.slice(fieldPointer.length) : null
      const cell = name === null ? null : cellOf(name)
      // A failed validation's message is a text key. A fixed message is no key, so it shows as is.
      const messages = problem.errors[pointer].map((message) => t(message))
      if (cell) {
        onCells[cell.collection] = { ...onCells[cell.collection], [cell.key]: messages }
        named.add(cell.collection)
      } else if (name !== null && shown.some(({ field }) => field.name === name)) {
        onFields[name] = messages
        named.add(name)
      } else {
        onForm.push(...messages)
      }
    }
    setFieldErrors(onFields)
    setCellErrors(onCells)
    setFormErrors(onForm)
    return [...named]
  }

  // The field becomes touched, and the form asks for its states: at once, or after typing pauses.
  const touch = (name: string, delay: StateRequest['delay']) => {
    if (asking) {
      setTouched((previous) => (previous.has(name) ? previous : new Set(previous).add(name)))
      ask(delay)
    }
  }

  const set = (name: string, value: FieldValue) => {
    setValues((previous) => ({ ...previous, [name]: value }))
    touch(name, delayOf(entity.fields.find((field) => field.name === name)?.type))
  }

  // Leaving a field touches it, so its messages show. A field that is touched already asked.
  const leave = (name: string) => {
    if (!touched.has(name)) {
      touch(name, 0)
    }
  }

  /**
   * Touches the fields and asks the state endpoint at once, without the timer. Resolves `false`
   * when the newest answer has an error for one of them. Without `conditions`, or when the call
   * fails, it resolves `true`, because the server checks the save again.
   */
  const check = async (names: readonly string[]): Promise<boolean> => {
    if (!conditions) {
      return true
    }
    const all = new Set([...touched, ...names])
    setTouched(all)
    const mine = ++sequence.current
    try {
      const next = await fetchFormState(
        conditions.statePath,
        buildFormStateBody(entity.fields, changed(), conditions.recordId, [...all]),
      )
      if (mine === sequence.current) {
        setAnswer(next)
      }
      return !names.some((name) => `${fieldPointer}${name}` in next.errors)
    } catch (error) {
      console.warn(`Loading the form state from '${conditions.statePath}' failed`, error)
      return true
    }
  }

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
    if (field.type !== 'child-collection' && (readOnly || field.computed || field.sequence || field.creator)) {
      // The server sets the value on save, so it is shown as a table cell shows it.
      return <Input id={id} readOnly value={formatValue(field, value as RecordValue, labels, { locale, text: t })} />
    }
    switch (field.type) {
      case 'reference':
        // The label cannot be typed. The record is chosen in the lookup, and its id is sent.
        return (
          <Space.Compact block data-testid={`reference-${field.name}`}>
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

  // A save error wins. Otherwise the answer's messages show under a touched field.
  const errorsOf = (name: string) =>
    fieldErrors[name] ??
    (touched.has(name) ? answer?.errors[`${fieldPointer}${name}`]?.map((message) => t(message)) : undefined)

  // The inputs of the sections a wizard step lists, or of every section with `null`.
  const fieldsFor = (names: readonly string[] | null): ReactNode => (
    <>
      {sections.map((section, index) =>
        names !== null && !(section.name !== null && names.includes(section.name)) ? null : (
          <Fragment key={index}>
            {section.titleKey !== null &&
              !(section.fields.length > 0 && section.fields.every(({ field }) => hiddenOf(field))) && (
                <Typography.Title level={5}>{t(section.titleKey)}</Typography.Title>
              )}
            {section.fields
              .filter(({ field }) => !hiddenOf(field))
              .map((entry) => {
                const { field } = entry
                const errors = errorsOf(field.name)
                const control = input(field, lockedOf(entry))
                return (
                  <Form.Item
                    key={field.name}
                    label={labelOf(field)}
                    // A child collection is a table, with no single input for the label to point to.
                    htmlFor={field.type === 'child-collection' ? undefined : field.name}
                    required={stateOf(field.name)?.required ?? field.required}
                    validateStatus={errors ? 'error' : undefined}
                    help={
                      errors && (
                        <span role="alert" data-testid={`field-error-${field.name}`}>
                          {errors.join(' ')}
                        </span>
                      )
                    }
                  >
                    {asking ? <div onBlur={() => leave(field.name)}>{control}</div> : control}
                  </Form.Item>
                )
              })}
          </Fragment>
        ),
      )}
      {shown
        .filter(
          (entry) =>
            !lockedOf(entry) && !hiddenOf(entry.field) && entry.field.type === 'reference' && entry.field.target,
        )
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

  const fields = fieldsFor(null)

  // The newest body is read when the timer fires. A write in an effect keeps the render pure.
  useEffect(() => {
    draft.current = conditions
      ? () => ({
          statePath: conditions.statePath,
          body: buildFormStateBody(entity.fields, changed(), conditions.recordId, [...touched]),
        })
      : null
  })

  // One timer for the request that waits: a newer request clears it, which debounces typing.
  useEffect(() => {
    if (request.n === 0) {
      return
    }
    const timer = setTimeout(() => {
      const call = draft.current?.()
      if (!call) {
        return
      }
      const mine = ++sequence.current
      fetchFormState(call.statePath, call.body)
        .then((next) => {
          if (mine === sequence.current) {
            setAnswer(next)
          }
        })
        .catch((error: unknown) => {
          // The server checks a save again, so a lost answer keeps the last states and tells no one.
          console.warn(`Loading the form state from '${call.statePath}' failed`, error)
        })
    }, request.delay)
    return () => clearTimeout(timer)
  }, [request])

  // An answer that arrives after the form is gone is dropped.
  useEffect(
    () => () => {
      sequence.current++
    },
    [],
  )

  return {
    fields,
    fieldsFor,
    hidden,
    locked,
    check,
    start,
    changed,
    version: snapshot.version,
    showProblem,
    clearErrors,
    formErrors,
    setFormErrors,
  }
}
