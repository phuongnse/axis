import { Button, ConfigProvider, Flex, Form, Input, Table, type TableColumnsType } from 'antd'
import { useMemo, useRef } from 'react'
import { FieldInput } from './FieldInput'
import { formatValue } from './formatValue'
import type { RecordRow, RecordValue } from './records'
import type { FieldMetadata } from './site'
import { useText } from './texts'

interface ChildCollectionTableProps {
  /** A child collection field. Its `fields` are the child entity's fields. */
  field: FieldMetadata
  rows: RecordRow[]
  onChange: (rows: RecordRow[]) => void
  /** The messages of each cell, keyed `<index>/<field>`. */
  errors: Record<string, string[]>
  /** The UI locale, for showing computed values. */
  locale: string
  /** Shows every cell read-only, with no add or remove buttons. */
  readOnly?: boolean
}

// Field names start with a letter, so this key never names a field column.
const removeColumnKey = '$remove'

/**
 * Edits the rows of a child collection as a table: one column per child field, each cell the same
 * input the form uses for that field type, a remove button per row and an add button below. A
 * computed cell is read-only and shows the value the server returned. A read-only collection shows
 * every cell that way, and has no add or remove buttons.
 */
export function ChildCollectionTable({
  field,
  rows,
  onChange,
  errors,
  locale,
  readOnly = false,
}: ChildCollectionTableProps) {
  const t = useText()
  const childFields = useMemo(() => field.fields ?? [], [field.fields])
  // A client key per row object, carried over when a row is edited, so React keeps each row's inputs.
  const keys = useRef(new WeakMap<RecordRow, number>())
  const nextKey = useRef(0)
  const keyOf = (row: RecordRow) => {
    let key = keys.current.get(row)
    if (key === undefined) {
      key = nextKey.current++
      keys.current.set(row, key)
    }
    return key
  }

  const edit = (index: number, name: string, value: RecordValue) => {
    const row = { ...rows[index], [name]: value }
    keys.current.set(row, keyOf(rows[index]))
    onChange(rows.map((current, at) => (at === index ? row : current)))
  }
  const add = () => onChange([...rows, Object.fromEntries(childFields.map((child) => [child.name, null]))])
  const remove = (index: number) => onChange(rows.filter((_row, at) => at !== index))

  const columns: TableColumnsType<RecordRow> = [
    ...childFields.map((child) => {
      const label = child.labelKey ? t(child.labelKey) : child.name
      return {
        key: child.name,
        title: label,
        render: (_value: unknown, row: RecordRow, index: number) => {
          const cellErrors = errors[`${index}/${child.name}`]
          return (
            <Form.Item
              validateStatus={cellErrors ? 'error' : undefined}
              help={
                cellErrors && (
                  <span data-testid={`field-error-${field.name}-${index}-${child.name}`}>{cellErrors.join(' ')}</span>
                )
              }
            >
              {readOnly || child.computed ? (
                <Input
                  readOnly
                  aria-label={`${label} ${index + 1}`}
                  value={formatValue(child, row[child.name] ?? null, {}, { locale, text: t })}
                />
              ) : (
                <FieldInput
                  field={child}
                  value={row[child.name] ?? null}
                  onChange={(value) => edit(index, child.name, value)}
                  ariaLabel={`${label} ${index + 1}`}
                />
              )}
            </Form.Item>
          )
        },
      }
    }),
  ]
  if (!readOnly) {
    columns.push({
      key: removeColumnKey,
      render: (_value: unknown, _row: RecordRow, index: number) => (
        <Button onClick={() => remove(index)}>{t('shell.form.removeRow')}</Button>
      ),
    })
  }

  return (
    <Flex vertical gap="small">
      {/* A cell's item needs no space below it, as the table rows already space the inputs. */}
      <ConfigProvider theme={{ components: { Form: { itemMarginBottom: 0 } } }}>
        <Table<RecordRow> rowKey={keyOf} columns={columns} dataSource={rows} pagination={false} />
      </ConfigProvider>
      {!readOnly && (
        <Flex>
          <Button onClick={add}>{t('shell.form.addRow')}</Button>
        </Flex>
      )}
    </Flex>
  )
}
