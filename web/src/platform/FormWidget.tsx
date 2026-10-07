import { Alert, Button, Checkbox, DatePicker, Flex, Form, Input, Select } from 'antd'
import dayjs from 'dayjs'
import { useCallback, useEffect, useState, type KeyboardEvent, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { NotFoundPage } from '../pages/NotFoundPage'
import { buildRecordBody } from './recordBody'
import { fetchRecord, saveRecord, type RecordItem, type RecordValue } from './records'
import type { FieldMetadata, WidgetMetadata } from './site'
import { useText } from './texts'

interface FormWidgetProps {
  widget: WidgetMetadata
  /** The record to edit, or `null` to create one. */
  recordId: string | null
  /** Where save and cancel go: the table page the user came from. */
  returnTo: string
}

/** The values the form started from, which decide what changed, with the record's labels and version. */
interface Snapshot {
  initial: Record<string, RecordValue>
  labels: Record<string, string>
  version: number
}

type LoadState = 'loading' | 'ready' | 'missing' | 'failed'

const fieldPointer = '/values/'

function emptySnapshot(fields: readonly FieldMetadata[]): Snapshot {
  return { initial: Object.fromEntries(fields.map((field) => [field.name, null])), labels: {}, version: 0 }
}

function snapshotOf(record: RecordItem): Snapshot {
  return { initial: record.values, labels: record.labels, version: record.version }
}

// Enter in a picker only confirms the picked value. The picker handles it first, so stopping the
// default here only stops the browser's implicit form submission.
function confirmOnly(event: KeyboardEvent) {
  if (event.key === 'Enter') {
    event.preventDefault()
  }
}

// The record API writes six fraction digits, and the picker works to the second.
function dateTimeValue(value: RecordValue) {
  return value === null ? null : dayjs(String(value).replace(/\.\d+(?=Z$)/, ''))
}

/**
 * Creates or edits one record of the widget's entity. The form sends only the fields that differ
 * from the values it started from, plus `version` on edit, and adds no rules of its own: the server
 * validates, and its errors appear on the fields their JSON Pointer names.
 */
export function FormWidget({ widget, recordId, returnTo }: FormWidgetProps) {
  const t = useText()
  const navigate = useNavigate()
  const { entity } = widget
  const [load, setLoad] = useState<LoadState>(recordId === null ? 'ready' : 'loading')
  const [snapshot, setSnapshot] = useState<Snapshot>(() => emptySnapshot(entity.fields))
  const [values, setValues] = useState<Record<string, RecordValue>>(snapshot.initial)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [formErrors, setFormErrors] = useState<string[]>([])
  const [conflict, setConflict] = useState(false)
  const [saving, setSaving] = useState(false)

  const start = useCallback((record: RecordItem) => {
    const next = snapshotOf(record)
    setSnapshot(next)
    setValues(next.initial)
    setFieldErrors({})
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

  const save = async () => {
    if (saving) {
      return
    }
    const changed = Object.fromEntries(
      entity.fields
        .filter((field) => field.type !== 'reference' && values[field.name] !== snapshot.initial[field.name])
        .map((field) => [field.name, values[field.name]]),
    )
    const body = buildRecordBody(entity.fields, changed, recordId === null ? undefined : snapshot.version)
    setSaving(true)
    setFieldErrors({})
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
      const onForm: string[] = []
      for (const pointer of pointers) {
        const name = pointer.startsWith(fieldPointer) ? pointer.slice(fieldPointer.length) : null
        if (name !== null && entity.fields.some((field) => field.name === name)) {
          onFields[name] = problem.errors[pointer]
        } else {
          onForm.push(...problem.errors[pointer])
        }
      }
      setFieldErrors(onFields)
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

  const set = (name: string, value: RecordValue) => setValues((previous) => ({ ...previous, [name]: value }))

  const input = (field: FieldMetadata): ReactNode => {
    const value = values[field.name] ?? null
    const id = field.name
    const text = (event: { target: { value: string } }) => set(field.name, event.target.value || null)
    switch (field.type) {
      case 'text':
        return (
          <Input
            id={id}
            value={value === null ? '' : String(value)}
            maxLength={field.maxLength ?? undefined}
            onChange={text}
          />
        )
      case 'integer':
      case 'decimal':
        // Plain text inputs keep the typed text unchanged, so no digit is lost.
        return (
          <Input
            id={id}
            inputMode={field.type === 'integer' ? 'numeric' : 'decimal'}
            value={value === null ? '' : String(value)}
            onChange={text}
          />
        )
      case 'boolean':
        return <Checkbox id={id} checked={value === true} onChange={(event) => set(field.name, event.target.checked)} />
      case 'date':
        return (
          <DatePicker
            id={id}
            value={value === null ? null : dayjs(String(value))}
            onChange={(date) => set(field.name, date ? date.format('YYYY-MM-DD') : null)}
            onKeyDown={confirmOnly}
          />
        )
      case 'date-time':
        return (
          <DatePicker
            id={id}
            showTime
            format="YYYY-MM-DD HH:mm:ss"
            value={dateTimeValue(value)}
            // UTC to the second, as an RFC 3339 string the record API accepts.
            onChange={(date) =>
              set(field.name, date ? new Date(date.valueOf()).toISOString().replace(/\.\d{3}Z$/, 'Z') : null)
            }
            onKeyDown={confirmOnly}
          />
        )
      case 'enum':
        return (
          <Select
            id={id}
            allowClear
            value={value === null ? null : String(value)}
            options={(field.values ?? []).map((option) => ({ value: option, label: option }))}
            onChange={(option: string | undefined) => set(field.name, option ?? null)}
          />
        )
      case 'reference':
        // The lookup to choose a record comes later. Until then the label is shown and never sent.
        return <Input id={id} readOnly value={snapshot.labels[field.name] ?? ''} />
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
              label={field.labelKey ? t(field.labelKey) : field.name}
              htmlFor={field.name}
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
    </Flex>
  )
}
