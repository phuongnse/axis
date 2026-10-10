import { Alert, Button, Flex, Form, Input, Space } from 'antd'
import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { NotFoundPage } from '../pages/NotFoundPage'
import { ChildCollectionTable } from './ChildCollectionTable'
import { FieldInput } from './FieldInput'
import { formatValue } from './formatValue'
import { buildRecordBody } from './recordBody'
import { fetchRecord, saveRecord, type FieldValue, type RecordItem, type RecordRow, type RecordValue } from './records'
import { ReferenceLookup } from './ReferenceLookup'
import type { EntityWidgetMetadata, FieldMetadata } from './site'
import { useText } from './texts'

interface FormWidgetProps {
  widget: EntityWidgetMetadata
  /** The record to edit, or `null` to create one. */
  recordId: string | null
  /** Where save and cancel go: the table page the user came from. */
  returnTo: string
  /** The UI locale, for showing computed and sequence values. */
  locale: string
}

/** The values the form started from, which decide what changed, with the record's labels and version. */
interface Snapshot {
  initial: Record<string, FieldValue>
  labels: Record<string, string>
  version: number
}

type LoadState = 'loading' | 'ready' | 'missing' | 'failed'

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
 * Creates or edits one record of the widget's entity. The form sends only the fields that differ
 * from the values it started from, plus `version` on edit, and adds no rules of its own: the server
 * validates, and its errors appear on the fields their JSON Pointer names. A child collection that
 * changed is sent as its whole row list, and a row's errors appear on its cells. A computed field,
 * of the record or of a row, is shown read-only with the value the server returned, and never sent.
 * So is a sequence field, which is empty on a new record because the server numbers it on create.
 */
export function FormWidget({ widget, recordId, returnTo, locale }: FormWidgetProps) {
  const t = useText()
  const navigate = useNavigate()
  const { entity } = widget
  const [load, setLoad] = useState<LoadState>(recordId === null ? 'ready' : 'loading')
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
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)

  const start = useCallback((record: RecordItem) => {
    const next = snapshotOf(record)
    setSnapshot(next)
    setValues(next.initial)
    setLabels(next.labels)
    setFieldErrors({})
    setCellErrors({})
    setFormErrors([])
    setConflict(false)
    setLoad('ready')
  }, [])

  useEffect(() => {
    if (recordId === null) {
      return
    }
    const controller = new AbortController()
    fetchRecord(entity.recordsPath, recordId, controller.signal)
      .then((record) => (record ? start(record) : setLoad('missing')))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the record '${recordId}' of '${entity.name}' failed`, error)
          setLoad('failed')
        }
      })
    return () => controller.abort()
  }, [entity.recordsPath, entity.name, recordId, start])

  const reload = async () => {
    if (recordId === null) {
      return
    }
    try {
      const record = await fetchRecord(entity.recordsPath, recordId)
      if (record) {
        start(record)
      } else {
        setLoad('missing')
      }
    } catch (error) {
      console.warn(`Loading the record '${recordId}' of '${entity.name}' failed`, error)
      setFormErrors([t('shell.form.loadFailed')])
    }
  }

  // A pointer such as `lines/1/quantity` names a cell when the collection, its row and the child
  // field all exist.
  const cellOf = (name: string) => {
    const [collection, index, child, ...rest] = name.split('/')
    const field = entity.fields.find((candidate) => candidate.name === collection)
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

  const save = async () => {
    if (saving) {
      return
    }
    const changed = Object.fromEntries(
      entity.fields
        .filter(
          (field) =>
            !field.computed && !field.sequence && !sameValue(field, values[field.name], snapshot.initial[field.name]),
        )
        .map((field) => [field.name, values[field.name]]),
    )
    const body = buildRecordBody(entity.fields, changed, recordId === null ? undefined : snapshot.version)
    setSaving(true)
    setFieldErrors({})
    setCellErrors({})
    setFormErrors([])
    setConflict(false)
    try {
      const result = await saveRecord(entity.recordsPath, recordId, body)
      if (result.ok) {
        navigate(returnTo)
        return
      }
      const { problem } = result
      const pointers = Object.keys(problem.errors)
      if (pointers.length === 0) {
        // The record API answers a stale version with a 409 that names no field.
        if (problem.status === 409 && recordId !== null) {
          setConflict(true)
        } else {
          setFormErrors([t('shell.form.saveFailed')])
        }
        return
      }
      const onFields: Record<string, string[]> = {}
      const onCells: Record<string, Record<string, string[]>> = {}
      const onForm: string[] = []
      for (const pointer of pointers) {
        const name = pointer.startsWith(fieldPointer) ? pointer.slice(fieldPointer.length) : null
        const cell = name === null ? null : cellOf(name)
        // A failed validation's message is a text key. A fixed message is no key, so it shows as is.
        const messages = problem.errors[pointer].map((message) => t(message))
        if (cell) {
          onCells[cell.collection] = { ...onCells[cell.collection], [cell.key]: messages }
        } else if (name !== null && entity.fields.some((field) => field.name === name)) {
          onFields[name] = messages
        } else {
          onForm.push(...messages)
        }
      }
      setFieldErrors(onFields)
      setCellErrors(onCells)
      setFormErrors(onForm)
    } catch (error) {
      console.warn(`Saving a record of '${entity.name}' failed`, error)
      setFormErrors([t('shell.form.saveFailed')])
    } finally {
      setSaving(false)
    }
  }

  if (load === 'loading') {
    return null
  }
  if (load === 'missing') {
    return <NotFoundPage />
  }
  if (load === 'failed') {
    return <Alert data-testid="form-load-error" type="error" message={t('shell.form.loadFailed')} />
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

  const input = (field: FieldMetadata): ReactNode => {
    const value = values[field.name] ?? null
    const id = field.name
    if (field.computed || field.sequence) {
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

  return (
    <Flex vertical gap="middle">
      {conflict && (
        <Alert
          data-testid="form-conflict"
          type="warning"
          message={t('shell.form.conflict')}
          action={<Button onClick={reload}>{t('shell.form.reload')}</Button>}
        />
      )}
      {formErrors.length > 0 && <Alert data-testid="form-error" type="error" message={formErrors.join(' ')} />}
      <Form layout="vertical" onFinish={save}>
        {entity.fields.map((field) => {
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
              {input(field)}
            </Form.Item>
          )
        })}
        <Flex gap="small">
          <Button htmlType="submit" type="primary" loading={saving}>
            {t('shell.form.save')}
          </Button>
          <Button onClick={() => navigate(returnTo)}>{t('shell.form.cancel')}</Button>
        </Flex>
      </Form>
      {entity.fields
        .filter((field) => field.type === 'reference' && field.target)
        .map((field) => (
          <ReferenceLookup
            key={field.name}
            field={field}
            title={labelOf(field)}
            open={lookup === field.name}
            onPick={(record) => pick(field, record)}
            onClose={() => setLookup(null)}
          />
        ))}
    </Flex>
  )
}
