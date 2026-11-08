import { DeleteOutlined } from '@ant-design/icons'
import { Modal } from 'antd'
import { useState, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn, type ColumnKind } from '../platform/AxisTable'
import { setAt } from '../platform/jsonPatch'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import type { ChangeHandler } from './SchemaForm'

/** A column of the list: one property of the item, named by its field text. */
export interface ObjectListColumn {
  /** The item's property. */
  key: string
  /** Its field text. */
  title: string
  kind: ColumnKind
  /** What the cell shows, such as a label or a nested list in words. Without it the cell shows the raw value. */
  render?: (item: Record<string, unknown>) => ReactNode
}

type Item = Record<string, unknown>

interface Row {
  key: string
  index: number
  item: Item
}

/** The item being added (`index` null) or edited, as a copy that nothing reads until Done. */
interface Editing {
  index: number | null
  copy: Item
}

function isEmpty(value: unknown): boolean {
  return (
    value === undefined ||
    value === null ||
    value === '' ||
    (Array.isArray(value) && value.length === 0) ||
    (typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0)
  )
}

// JSON Schema's `required` asks only that the property is present, so an empty list or object is a value.
function hasValue(value: unknown): boolean {
  return value !== undefined && value !== null && !(typeof value === 'string' && value.trim() === '')
}

/**
 * The table and dialog of a list of objects, see docs/reference/studio-resource-form.md#lists-of-objects. The table
 * leaves out a column that is empty on every row, except the first, which opens the row. Add… and the first cell open
 * a dialog that edits a copy of the item, so nothing changes until Done, which writes the copy with one change. Remove
 * writes `undefined` at the item's index. The caller draws the dialog's fields, so the component knows no JSON Schema.
 */
export function ObjectListTable({
  pointer,
  items,
  columns,
  itemName,
  required,
  large,
  newItem,
  fields,
  onChange,
  testId,
}: {
  /** The JSON Pointer of the list. */
  pointer: string
  items: Item[]
  columns: ObjectListColumn[]
  /** The item's name in the dialog's title and in Add…, such as "module". */
  itemName: string
  /** The property names the item must have a value for before Done is on. */
  required: string[]
  /** Whether the item holds a list or a map, which takes the large dialog. */
  large: boolean
  newItem: () => Item
  /** Draws the dialog's fields on the copy. Its `change` takes a pointer relative to the item. */
  fields: (copy: Item, change: ChangeHandler) => ReactNode
  onChange: ChangeHandler
  testId?: string
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [editing, setEditing] = useState<Editing | null>(null)
  const named = (text: string) => text.replace('{name}', itemName)

  const rows: Row[] = items.map((item, index) => ({ key: String(index), index, item }))
  const cellOf = (column: ObjectListColumn, item: Item) => (column.render ? column.render(item) : item[column.key])
  const present = (column: ObjectListColumn, item: Item) => {
    const value = cellOf(column, item)
    return column.render ? value !== null && value !== undefined && value !== '' : !isEmpty(value)
  }
  const shown = columns.filter(
    (column, at) => at === 0 || rows.length === 0 || rows.some((row) => present(column, row.item)),
  )

  const openAdd = () => setEditing({ index: null, copy: newItem() })
  const openEdit = (index: number) => setEditing({ index, copy: structuredClone(items[index]) })
  const complete = editing !== null && required.every((name) => hasValue(editing.copy[name]))
  const done = () => {
    if (editing === null) return
    if (editing.index === null) {
      onChange(pointer, [...items, editing.copy])
    } else {
      onChange(`${pointer}/${editing.index}`, editing.copy)
    }
    setEditing(null)
  }

  const tableColumns: AxisColumn<Row>[] = shown.map((column, at) => ({
    key: column.key,
    title: column.title,
    kind: column.kind,
    render: (_value, row) => {
      const content = cellOf(column, row.item)
      const text = content === null || content === undefined || content === '' ? null : content
      const shownText = typeof text === 'object' ? (text as ReactNode) : text === null ? null : String(text)
      if (at > 0) return shownText
      return (
        <AxisButton role="link" size="small" onClick={() => openEdit(row.index)}>
          {shownText ?? named(t('shell.studio.list.editTitle'))}
        </AxisButton>
      )
    },
  }))

  return (
    <>
      <AxisTable<Row>
        testId={testId}
        rowKey={(row) => row.key}
        columns={tableColumns}
        dataSource={rows}
        emptyText={t('shell.table.empty')}
        toolbar={{
          main: (
            <AxisButton role="main" onClick={openAdd}>
              {named(t('shell.studio.list.add'))}
            </AxisButton>
          ),
        }}
        rowActions={{
          configured: 1,
          actions: (row) => [
            {
              key: 'remove',
              labelKey: 'shell.studio.list.remove',
              icon: <DeleteOutlined />,
              destructive: true,
              nameSuffix: String(row.index + 1),
              onClick: () => onChange(`${pointer}/${row.index}`, undefined),
            },
          ],
        }}
      />
      <Modal
        open={editing !== null}
        width={large ? size.dialog.large : size.dialog.medium}
        title={
          editing && named(t(editing.index === null ? 'shell.studio.list.addTitle' : 'shell.studio.list.editTitle'))
        }
        okText={t('shell.studio.list.done')}
        cancelText={t('shell.studio.cancel')}
        okButtonProps={{ disabled: !complete }}
        onOk={done}
        onCancel={() => setEditing(null)}
        destroyOnHidden
      >
        {editing &&
          fields(editing.copy, (relative, next) =>
            setEditing((current) => current && { ...current, copy: setAt(current.copy, relative, next) as Item }),
          )}
      </Modal>
    </>
  )
}
