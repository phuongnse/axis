import { CloseOutlined, DeleteOutlined, SearchOutlined } from '@ant-design/icons'
import { ConfigProvider, Flex, Form, Input, Space, Table, type TableColumnsType } from 'antd'
import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { AxisButton } from './AxisButton'
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
// The height of an input, with the text centred in it, so a plain cell lines up with the inputs in its row.
const plainCellStyle: CSSProperties = { minHeight: 32, display: 'flex', alignItems: 'center' }
const plainNumberStyle: CSSProperties = { ...plainCellStyle, justifyContent: 'flex-end', ...numberStyle }
// A cell's message sits under its input and stays left-aligned, even in a number column.
const cellMessageStyle: CSSProperties = { display: 'block', textAlign: 'left' }

/** The reference cell whose lookup is open. */
interface OpenLookup {
  index: number
  field: FieldMetadata
}

/**
 * Edits the rows of a child collection as a table: one column per child field, each editable cell the
 * same input the form uses for that field type, a remove button per row and an add button above the
 * table on the right. The first input of an added row takes focus. A reference cell shows the label of
 * the record it points to, with a search icon button that opens the reference lookup. A computed cell
 * and every cell of a read-only collection show their value as plain text, with no input. A read-only
 * collection has no add or remove buttons.
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
  const childFields = useMemo(() => field.fields ?? [], [field.fields])
  // A client key per row object, carried over when a row is edited, so React keeps each row's inputs.
  const keys = useRef(new WeakMap<RecordRow, number>())
  const nextKey = useRef(0)
  const [lookup, setLookup] = useState<OpenLookup | null>(null)
  // The client key of an added row, until its first input has taken focus.
  const pendingFocus = useRef<number | null>(null)
  const container = useRef<HTMLDivElement>(null)
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
  const add = () => {
    const row: RecordRow = Object.fromEntries(childFields.map((child) => [child.name, null]))
    const key = nextKey.current++
    keys.current.set(row, key)
    pendingFocus.current = key
    onChange([...rows, row], [...aligned, {}])
  }
  // Runs after every render, because the parent may pass the added row on a later render than the click.
  useEffect(() => {
    const key = pendingFocus.current
    if (key === null) return
    const row = container.current?.querySelector(`tr[data-row-key="${key}"]`)
    if (!row) return
    pendingFocus.current = null
    row.querySelector<HTMLElement>('input:not([tabindex="-1"]), textarea, button')?.focus()
  })
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
        // The inputs keep a usable width when the table scrolls sideways. A reference cell has two buttons.
        width: child.type === 'reference' ? 240 : 160,
        // Numbers sit on the right, in the header and the cells.
        align: isNumeric(child.type) ? ('right' as const) : undefined,
        // Cells line up at the top, so a message under one cell never moves the other inputs of its row.
        onCell: () => ({ style: { verticalAlign: 'top' as const } }),
        render: (_value: unknown, row: RecordRow, index: number) => {
          const cellErrors = errors[`${index}/${child.name}`]
          const shown = formatValue(child, row[child.name] ?? null, aligned[index] ?? {}, { locale, text: t }, row)
          return (
            <Form.Item
              validateStatus={cellErrors ? 'error' : undefined}
              help={
                cellErrors && (
                  <span data-testid={`field-error-${field.name}-${index}-${child.name}`} style={cellMessageStyle}>
                    {cellErrors.join(' ')}
                  </span>
                )
              }
            >
              {readOnly || child.computed ? (
                <div
                  data-testid={`cell-${field.name}-${index}-${child.name}`}
                  style={isNumeric(child.type) ? plainNumberStyle : plainCellStyle}
                >
                  {child.type === 'file' ? (
                    isFileValue(row[child.name]) ? (
                      <FileLink field={child} value={row[child.name] as FileValue} />
                    ) : (
                      <EmptyDash />
                    )
                  ) : shown ? (
                    shown
                  ) : (
                    <EmptyDash />
                  )}
                </div>
              ) : child.type === 'reference' ? (
                // The label cannot be typed. The record is chosen in the lookup, and its id is sent.
                <Space.Compact block data-testid={`reference-${field.name}-${index}-${child.name}`}>
                  <Input readOnly aria-label={`${label} ${index + 1}`} value={aligned[index]?.[child.name] ?? ''} />
                  <AxisButton
                    role="icon"
                    joined
                    nameKey="shell.form.choose"
                    nameSuffix={`${label} ${index + 1}`}
                    icon={<SearchOutlined />}
                    onClick={() => setLookup({ index, field: child })}
                  />
                  {!child.required && row[child.name] != null && (
                    <AxisButton
                      role="icon"
                      joined
                      nameKey="shell.form.clear"
                      nameSuffix={`${label} ${index + 1}`}
                      icon={<CloseOutlined />}
                      onClick={() => edit(index, child.name, null)}
                    />
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
      onCell: () => ({ style: { verticalAlign: 'top' as const } }),
      render: (_value: unknown, _row: RecordRow, index: number) => (
        <AxisButton
          role="icon"
          destructive
          nameKey="shell.form.removeRow"
          icon={<DeleteOutlined />}
          onClick={() => remove(index)}
        />
      ),
    })
  }

  return (
    <Flex vertical gap="small" ref={container}>
      {!readOnly && (
        <Flex justify="end">
          <AxisButton role="main" onClick={add}>
            {t('shell.form.addRow')}
          </AxisButton>
        </Flex>
      )}
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
