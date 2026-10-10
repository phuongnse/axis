import { Button, Form, Input, Select, Space } from 'antd'
import { useEffect, useState } from 'react'
import { FieldInput } from './FieldInput'
import { fetchRecord, type RecordItem, type RecordValue } from './records'
import { ReferenceLookup } from './ReferenceLookup'
import type { DataSourceParameter, FieldMetadata } from './site'
import { useText } from './texts'

interface FilterBarProps {
  /** The data source parameters, in declaration order. */
  parameters: readonly DataSourceParameter[]
  /** The value of each parameter that is given, from the URL. */
  values: Record<string, string>
  /** The server's messages for each rejected parameter. */
  errors: Record<string, string[]>
  /** Sets a parameter's value, or clears it with `null`. */
  onChange: (name: string, value: string | null) => void
}

interface FilterInputProps {
  parameter: DataSourceParameter
  id: string
  value: string | null
  onChange: (value: string | null) => void
}

// A record id in the hyphenated 8-4-4-4-12 hex form, as the record API accepts it.
const recordIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const inputWidth = 200

// The display field's value, as a picked reference shows in the form, or the id without one.
function recordLabel(record: RecordItem, displayField: string | null): string {
  return displayField ? String(record.values[displayField] ?? '') : record.id
}

// A parameter has the properties of a field that an input reads. The rest do not apply to it.
function asField(parameter: DataSourceParameter): FieldMetadata {
  return {
    ...parameter,
    unique: false,
    computed: false,
    sequence: false,
    maxLength: null,
    precision: null,
    scale: null,
    fields: null,
  }
}

/**
 * The inputs of a table bound to a data source, one per parameter, labelled with the parameter's
 * label or its name. The bar adds no rules of its own: the server checks every value, and its
 * messages show under the input they name.
 */
export function FilterBar({ parameters, values, errors, onChange }: FilterBarProps) {
  const t = useText()
  return (
    <Form layout="inline" data-testid="filter-bar">
      {parameters.map((parameter) => {
        const { name } = parameter
        const id = `filter-${name}`
        const messages = errors[name]
        return (
          <Form.Item
            key={name}
            label={parameter.labelKey ? t(parameter.labelKey) : name}
            htmlFor={id}
            validateStatus={messages ? 'error' : undefined}
            help={
              messages && (
                <span data-testid={`filter-error-${name}`}>{messages.map((message) => t(message)).join(' ')}</span>
              )
            }
          >
            <FilterInput
              parameter={parameter}
              id={id}
              value={values[name] ?? null}
              onChange={(value) => onChange(name, value)}
            />
          </Form.Item>
        )
      })}
    </Form>
  )
}

function FilterInput(props: FilterInputProps) {
  const t = useText()
  const { parameter, id, value, onChange } = props
  switch (parameter.type) {
    case 'text':
    case 'integer':
    case 'decimal':
      return <TypedFilter {...props} />
    case 'boolean':
      // An optional parameter has three states, true, false and not given, which a checkbox cannot show.
      return (
        <Select
          id={id}
          allowClear
          style={{ width: inputWidth }}
          value={value}
          options={[
            { value: 'true', label: t('shell.table.yes') },
            { value: 'false', label: t('shell.table.no') },
          ]}
          onChange={(option: string | undefined) => onChange(option ?? null)}
        />
      )
    case 'reference':
      return <ReferenceFilter {...props} />
    default:
      // A picked date or option applies at once.
      return (
        <FieldInput
          field={asField(parameter)}
          id={id}
          style={{ width: inputWidth }}
          value={value}
          onChange={(next: RecordValue) => onChange(next === null ? null : String(next))}
        />
      )
  }
}

/**
 * A text or number input. The value applies on Enter or when the input loses focus, so typing does
 * not request rows and add a history entry for every key.
 */
function TypedFilter({ parameter, id, value, onChange }: FilterInputProps) {
  const [draft, setDraft] = useState<RecordValue>(value)
  const [shown, setShown] = useState(value)
  // A value from the URL, such as after the back button, replaces the draft.
  if (shown !== value) {
    setShown(value)
    setDraft(value)
  }
  const commit = () => {
    const next = draft === null ? null : String(draft)
    if (next !== value) {
      onChange(next)
    }
  }
  return (
    <div
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') {
          commit()
        }
      }}
    >
      <FieldInput field={asField(parameter)} id={id} style={{ width: inputWidth }} value={draft} onChange={setDraft} />
    </div>
  )
}

/**
 * The label of the chosen record, read-only, with a choose button that opens the lookup and a clear
 * button. The URL holds only the record id, so after a reload the record is loaded for its label.
 */
function ReferenceFilter({ parameter, id, value, onChange }: FilterInputProps) {
  const t = useText()
  const [open, setOpen] = useState(false)
  // The label of the record the value names, once known.
  const [known, setKnown] = useState<{ id: string; label: string } | null>(null)
  const { name, labelKey, target } = parameter
  const { displayField, recordsPath } = target!

  // A value that is no record id names no record, so it shows as it is. The server reports it.
  const label = value === null ? '' : known?.id === value ? known.label : recordIdPattern.test(value) ? '' : value

  useEffect(() => {
    if (value === null || known?.id === value || !recordIdPattern.test(value)) {
      return
    }
    const controller = new AbortController()
    fetchRecord(recordsPath, value, controller.signal)
      .then((record) => setKnown({ id: value, label: record ? recordLabel(record, displayField) : value }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the record '${value}' for the filter '${name}' failed`, error)
          setKnown({ id: value, label: value })
        }
      })
    return () => controller.abort()
  }, [value, known, recordsPath, displayField, name])

  return (
    <>
      <Space.Compact style={{ width: inputWidth + 150 }}>
        <Input id={id} readOnly value={label} />
        <Button onClick={() => setOpen(true)}>{t('shell.form.choose')}</Button>
        {value !== null && <Button onClick={() => onChange(null)}>{t('shell.form.clear')}</Button>}
      </Space.Compact>
      <ReferenceLookup
        field={asField(parameter)}
        title={labelKey ? t(labelKey) : name}
        open={open}
        onPick={(record) => {
          setKnown({ id: record.id, label: recordLabel(record, displayField) })
          setOpen(false)
          onChange(record.id)
        }}
        onClose={() => setOpen(false)}
      />
    </>
  )
}
