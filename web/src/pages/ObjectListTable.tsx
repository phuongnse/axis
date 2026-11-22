import { DeleteOutlined } from '@ant-design/icons'
import { Flex, Modal } from 'antd'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { AxisButton, TitleRowActions } from '../platform/AxisButton'
import { AxisTable, type AxisColumn, type ColumnKind } from '../platform/AxisTable'
import { focusFirstError } from '../platform/focusFirstError'
import { setAt } from '../platform/jsonPatch'
import { PartBoundary } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import type { ChangeHandler } from './SchemaForm'
import { DialogFooter } from '../platform/DialogFooter'

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
  /** Whether a click on Done has shown the messages of the empty required fields. */
  checked: boolean
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
 * Done stays on. A click on it with a required field empty shows the field's message, focuses the first such field and
 * keeps the dialog open, see docs/reference/actions-and-controls.md#fields. The focus goes to the first box marked in
 * error, so a box the dialog draws after Done's render is focused once it is marked.
 * With `addIn`, Add… is drawn into that element and the table has no toolbar row.
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
  onDone,
  onCancel,
  addIn,
  firstCellMark,
  testId,
}: {
  /** The JSON Pointer of the list. */
  pointer: string
  items: Item[]
  columns: ObjectListColumn[]
  /** The item's name in the dialog's title and in Add…, such as "module". */
  itemName: string
  /** The property names the item must have a value for, which a click on Done checks. */
  required: string[]
  /** Whether the item holds a list or a map, which takes the large dialog. */
  large: boolean
  newItem: () => Item
  /**
   * Draws the dialog's fields on the copy, for the item at `index`, or `items.length` for Add. Its `change` takes a pointer
   * relative to the item. `problem(name)` is the message of a required property that is empty after a click on Done, or
   * `null`. The caller passes it as the `error` of the property's field, which marks the input in error.
   */
  fields: (copy: Item, change: ChangeHandler, index: number, problem: (name: string) => string | null) => ReactNode
  onChange: ChangeHandler
  /** Called right after Done writes the item, in the same event, so the caller can write what it held for the copy. */
  onDone?: () => void
  /** Called when the dialog closes without Done, so the caller can drop what it held for the copy. */
  onCancel?: () => void
  /** The element that Add… is drawn into once it exists, in place of the toolbar. `null` is an element not yet drawn. */
  addIn?: Element | null
  /** What the first cell of row `index` shows after its link, such as a layer's tag. */
  firstCellMark?: (index: number) => ReactNode
  testId?: string
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [editing, setEditing] = useState<Editing | null>(null)
  // Counts the clicks on Done that found a required field empty, so each click asks for the focus once more.
  const [focusRequest, setFocusRequest] = useState(0)
  const bodyRef = useRef<HTMLDivElement>(null)
  const named = (text: string) => text.replace('{name}', itemName)
  const emptyRequired = (copy: Item) => required.filter((name) => !hasValue(copy[name]))
  const missing = editing?.checked ? emptyRequired(editing.copy) : []

  // The first box in error takes the focus. A box the dialog draws a render later is not there yet, so the observer
  // waits for it, and it stops once a box has the focus.
  useEffect(() => {
    const body = bodyRef.current
    if (focusRequest === 0 || body === null || focusFirstError(body)) return
    const observer = new MutationObserver(() => {
      if (focusFirstError(body)) observer.disconnect()
    })
    observer.observe(body, { subtree: true, childList: true, attributes: true, attributeFilter: ['aria-invalid'] })
    return () => observer.disconnect()
  }, [focusRequest])

  const rows: Row[] = items.map((item, index) => ({ key: String(index), index, item }))
  const cellOf = (column: ObjectListColumn, item: Item) => (column.render ? column.render(item) : item[column.key])
  const present = (column: ObjectListColumn, item: Item) => {
    const value = cellOf(column, item)
    return column.render ? value !== null && value !== undefined && value !== '' : !isEmpty(value)
  }
  const shown = columns.filter(
    (column, at) => at === 0 || rows.length === 0 || rows.some((row) => present(column, row.item)),
  )

  const openAdd = () => {
    setFocusRequest(0)
    setEditing({ index: null, copy: newItem(), checked: false })
  }
  const openEdit = (index: number) => {
    setFocusRequest(0)
    setEditing({ index, copy: structuredClone(items[index]), checked: false })
  }
  const done = () => {
    if (editing === null) return
    if (emptyRequired(editing.copy).length > 0) {
      setFocusRequest((count) => count + 1)
      setEditing({ ...editing, checked: true })
      return
    }
    setFocusRequest(0)
    if (editing.index === null) {
      onChange(pointer, [...items, editing.copy])
    } else {
      onChange(`${pointer}/${editing.index}`, editing.copy)
    }
    setEditing(null)
    onDone?.()
  }
  const cancel = () => {
    setFocusRequest(0)
    setEditing(null)
    onCancel?.()
  }

  const addButton = (
    <AxisButton role={addIn === undefined ? 'main' : 'secondary'} onClick={openAdd}>
      {named(t('shell.studio.list.add'))}
    </AxisButton>
  )

  const tableColumns: AxisColumn<Row>[] = shown.map((column, at) => ({
    key: column.key,
    title: column.title,
    kind: column.kind,
    render: (_value, row) => {
      const content = cellOf(column, row.item)
      const text = content === null || content === undefined || content === '' ? null : content
      const shownText = typeof text === 'object' ? (text as ReactNode) : text === null ? null : String(text)
      if (at > 0) return shownText
      const mark = firstCellMark?.(row.index)
      const link = (
        <AxisButton role="subtle" size="small" onClick={() => openEdit(row.index)}>
          {shownText ?? named(t('shell.studio.list.editTitle'))}
        </AxisButton>
      )
      return mark ? (
        <Flex gap="small" align="center" wrap>
          {link}
          {mark}
        </Flex>
      ) : (
        link
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
        toolbar={addIn === undefined ? { main: addButton } : {}}
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
      {/* The row wraps the button where the portal is made, as context follows the React tree, not the slot's DOM. */}
      {addIn && createPortal(<TitleRowActions>{addButton}</TitleRowActions>, addIn)}
      <Modal
        open={editing !== null}
        width={large ? size.dialog.large : size.dialog.medium}
        title={
          editing && named(t(editing.index === null ? 'shell.studio.list.addTitle' : 'shell.studio.list.editTitle'))
        }
        footer={
          <DialogFooter
            finishText={t('shell.studio.list.done')}
            cancelText={t('shell.studio.cancel')}
            onFinish={done}
            onCancel={cancel}
          />
        }
        onCancel={cancel}
        destroyOnHidden
      >
        <PartBoundary>
          <div ref={bodyRef}>
            {editing &&
              fields(
                editing.copy,
                (relative, next) =>
                  setEditing((current) => current && { ...current, copy: setAt(current.copy, relative, next) as Item }),
                editing.index ?? items.length,
                (name) => (missing.includes(name) ? t('shell.studio.list.required') : null),
              )}
          </div>
        </PartBoundary>
      </Modal>
    </>
  )
}
