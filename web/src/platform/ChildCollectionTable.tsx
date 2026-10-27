import { CloseOutlined, DeleteOutlined, SearchOutlined } from '@ant-design/icons'
import { Button, ConfigProvider, Flex, Form, Input, Space, Table, Tooltip, type TableColumnsType } from 'antd'
import { useMemo, useRef, useState, type CSSProperties } from 'react'
import { compactFormTheme } from './antTheme'
import { EmptyDash } from './EmptyValue'
import { FieldInput } from './FieldInput'
import { FileLink } from './FileInput'
import { formatValue, isNumeric, moneyCurrency } from './formatValue'
import {
  isFileValue,
  type FileValue,
  type RecordItem,
  type RecordRow,
  type RecordValue,
  type RowLabels,
} from './records'
import { ReferenceLookup } from './ReferenceLookup'
import type { FieldMetadata } from './site'
import { useText } from './texts'
import { useEmptyStyle } from './useEmptyStyle'

interface ChildCollectionTableProps {
  /** A child collection field. Its `fields` are the child entity's fields. */
  field: FieldMetadata
  rows: RecordRow[]
  /** The labels of each row's references, by row index. A row that has none, or a missing entry, has no label. */
  labels: RowLabels[]
  /** Called with the new rows and the labels aligned with them. */
  onChange: (rows: RecordRow[], labels: RowLabels[]) => void
  /** The messages of each cell, keyed `<index>/<field>`. */
  errors: Record<string, string[]>
  /** The UI locale, for showing computed values. */
  locale: string
  /** Shows every cell read-only, with no add or remove buttons. */
  readOnly?: boolean
}

// Field names start with a letter, so this key never names a field column.
const removeColumnKey = '$remove'

// Digits of equal width, on the right.
const numberStyle: CSSProperties = { textAlign: 'right', fontVariantNumeric: 'tabular-nums' }

/** The reference cell whose lookup is open. */
interface OpenLookup {
  index: number
  field: FieldMetadata
}

/**
 * Edits the rows of a child collection as a table: one column per child field, each cell the same
 * input the form uses for that field type, a remove button per row and an add button below. A
 * reference cell shows the label of the record it points to, with a search icon button that opens the
 * reference lookup. A computed cell is read-only and shows the value the server returned. A read-only
 * collection shows every cell that way, and has no add or remove buttons.
 */
export function ChildCollectionTable({
  field,
  rows,
  labels,
  onChange,
  errors,
  locale,
  readOnly = false,
}: ChildCollectionTableProps) {
  const t = useText()
  const emptyStyle = useEmptyStyle()
  const childFields = useMemo(() => field.fields ?? [], [field.fields])
  // A client key per row object, carried over when a row is edited, so React keeps each row's inputs.
  const keys = useRef(new WeakMap<RecordRow, number>())
  const nextKey = useRef(0)
  const [lookup, setLookup] = useState<OpenLookup | null>(null)
  const keyOf = (row: RecordRow) => {
    let key = keys.current.get(row)
    if (key === undefined) {
      key = nextKey.current++
      keys.current.set(row, key)
    }
    return key
  }

  // The labels are kept aligned with the rows by index, so a row without an entry gets an empty one.
  const aligned = rows.map((_row, at): RowLabels => labels[at] ?? {})
  // Sets a cell's value and its label, or clears the label when there is none.
  const edit = (index: number, name: string, value: RecordValue, label?: string) => {
    const row = { ...rows[index], [name]: value }
    keys.current.set(row, keyOf(rows[index]))
    const { [name]: _previous, ...kept } = aligned[index]
    onChange(
      rows.map((current, at) => (at === index ? row : current)),
      aligned.map((current, at) =>
        at === index ? (label === undefined ? kept : { ...kept, [name]: label }) : current,
      ),
    )
  }
  const add = () =>
    onChange([...rows, Object.fromEntries(childFields.map((child) => [child.name, null]))], [...aligned, {}])
  const remove = (index: number) =>
    onChange(
      rows.filter((_row, at) => at !== index),
      aligned.filter((_label, at) => at !== index),
    )
  const pick = (open: OpenLookup, record: RecordItem) => {
    const display = open.field.target?.displayField
    edit(open.index, open.field.name, record.id, display ? String(record.values[display] ?? '') : record.id)
    setLookup(null)
  }

  const columns: TableColumnsType<RecordRow> = [
    ...childFields.map((child) => {
      const label = child.labelKey ? t(child.labelKey) : child.name
      return {
        key: child.name,
        title: label,
        // The inputs keep a usable width when the table scrolls sideways.
        width: 160,
        // Numbers sit on the right, in the header and the cells.
        align: isNumeric(child.type) ? ('right' as const) : undefined,
        render: (_value: unknown, row: RecordRow, index: number) => {
          const cellErrors = errors[`${index}/${child.name}`]
          const shown = formatValue(child, row[child.name] ?? null, aligned[index] ?? {}, { locale, text: t }, row)
          return (
            <Form.Item
              validateStatus={cellErrors ? 'error' : undefined}
              help={
                cellErrors && (
                  <span data-testid={`field-error-${field.name}-${index}-${child.name}`}>{cellErrors.join(' ')}</span>
                )
              }
            >
              {(readOnly || child.computed) && child.type === 'file' ? (
                isFileValue(row[child.name]) ? (
                  <FileLink field={child} value={row[child.name] as FileValue} />
                ) : (
                  <EmptyDash />
                )
              ) : readOnly || child.computed ? (
                <Input
                  readOnly
                  aria-label={`${label} ${index + 1}`}
                  value={shown || '—'}
                  style={{ ...(shown ? undefined : emptyStyle), ...(isNumeric(child.type) ? numberStyle : undefined) }}
                />
              ) : child.type === 'reference' ? (
                // The label cannot be typed. The record is chosen in the lookup, and its id is sent.
                <Space.Compact block data-testid={`reference-${field.name}-${index}-${child.name}`}>
                  <Input readOnly aria-label={`${label} ${index + 1}`} value={aligned[index]?.[child.name] ?? ''} />
                  <Tooltip title={t('shell.form.choose')}>
                    <Button
                      aria-label={`${t('shell.form.choose')} ${label} ${index + 1}`}
                      icon={<SearchOutlined />}
                      onClick={() => setLookup({ index, field: child })}
                    />
                  </Tooltip>
                  {!child.required && row[child.name] != null && (
                    <Tooltip title={t('shell.form.clear')}>
                      <Button
                        aria-label={`${t('shell.form.clear')} ${label} ${index + 1}`}
                        icon={<CloseOutlined />}
                        onClick={() => edit(index, child.name, null)}
                      />
                    </Tooltip>
                  )}
                </Space.Compact>
              ) : (
                <FieldInput
                  field={child}
                  value={row[child.name] ?? null}
                  currency={moneyCurrency(child, row)}
                  onChange={(value) => edit(index, child.name, value)}
                  ariaLabel={`${label} ${index + 1}`}
                  locale={locale}
                  alignEnd={isNumeric(child.type)}
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
        <Tooltip title={t('shell.form.removeRow')}>
          <Button
            danger
            aria-label={t('shell.form.removeRow')}
            icon={<DeleteOutlined />}
            onClick={() => remove(index)}
          />
        </Tooltip>
      ),
    })
  }

  return (
    <Flex vertical gap="small">
      {/* A cell's item needs no space below it, as the table rows already space the inputs. */}
      <ConfigProvider theme={compactFormTheme}>
        <Table<RecordRow>
          rowKey={keyOf}
          columns={columns}
          scroll={{ x: 'max-content' }}
          dataSource={rows}
          pagination={false}
        />
      </ConfigProvider>
      {!readOnly && (
        <Flex>
          <Button onClick={add}>{t('shell.form.addRow')}</Button>
        </Flex>
      )}
      {lookup?.field.target && (
        <ReferenceLookup
          field={lookup.field}
          title={lookup.field.labelKey ? t(lookup.field.labelKey) : lookup.field.name}
          open
          onPick={(record) => pick(lookup, record)}
          onClose={() => setLookup(null)}
        />
      )}
    </Flex>
  )
}
