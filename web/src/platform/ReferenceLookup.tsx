import { Modal } from 'antd'
import { AxisAlert } from './AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from './AxisTable'
import { fetchRecords, type RecordItem, type RecordPage, type RecordValue } from './records'
import type { FieldMetadata } from './site'
import { recordQuery, type TableQuery } from './tableQuery'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

interface ReferenceLookupProps {
  /** The reference field to choose a record for. Its `target` is not `null`. */
  field: FieldMetadata
  /** The field's label, as the dialog title. */
  title: string
  open: boolean
  onPick: (record: RecordItem) => void
  onClose: () => void
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The request URL, so a result for an older request shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: RecordPage
  failed: boolean
}

const pageSize = 20

/**
 * A dialog that lists the records of a reference field's target entity, sorted by its display
 * field, to pick one. Its Search box is AxisTable's, so the search applies on Enter, on the search
 * button or when the box is cleared, as every table's search does. The trimmed text filters the
 * records to those whose display field contains it, ignoring case. Paging and search are the
 * dialog's own state, not the page's address, see docs/reference/form-widget.md#lookup.
 */
export function ReferenceLookup({ field, title, open, onPick, onClose }: ReferenceLookupProps) {
  const { size } = useDesignTokens()
  return (
    <Modal open={open} width={size.dialog.large} title={title} footer={null} onCancel={onClose} destroyOnHidden>
      {/* The dialog destroys its content when hidden, so each opening starts again at page 1 with no search. */}
      <LookupList field={field} onPick={onPick} />
    </Modal>
  )
}

function LookupList({ field, onPick }: Pick<ReferenceLookupProps, 'field' | 'onPick'>) {
  const t = useText()
  const target = field.target!
  const { displayField, recordsPath } = target
  // The view is the dialog's own state. The sort stays out of it, as the column has no sorter to set it.
  const [query, setQuery] = useState<TableQuery>({ page: 1, pageSize, sort: null })
  const requestQuery = recordQuery({
    ...query,
    // AxisTable passes the search as typed, so the spaces around it are taken off here.
    search: query.search?.trim() || undefined,
    sort: displayField ? { field: displayField, descending: false } : null,
  })
  const requestUrl = `${recordsPath}?${requestQuery}`
  const [state, setState] = useState<LoadState>({ failed: false })
  const loading = state.url !== requestUrl
  const failed = state.failed && !loading

  useEffect(() => {
    const controller = new AbortController()
    const url = `${recordsPath}?${requestQuery}`
    fetchRecords(recordsPath, requestQuery, controller.signal)
      .then((result) => setState({ url, page: result, failed: false }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the records of '${target.entity}' failed`, error)
          setState({ url, failed: true })
        }
      })
    return () => controller.abort()
  }, [recordsPath, target.entity, requestQuery])

  const columns = useMemo<AxisColumn<RecordItem>[]>(
    () => [
      {
        key: displayField ?? 'id',
        dataIndex: displayField ? ['values', displayField] : 'id',
        kind: 'freeText',
        title: field.labelKey ? t(field.labelKey) : field.name,
        render: (value) =>
          value === null || value === undefined || value === '' ? null : String(value as RecordValue),
      },
    ],
    [displayField, field.labelKey, field.name, t],
  )

  return (
    <AxisTable<RecordItem>
      testId="reference-lookup"
      rowKey={(record) => record.id}
      columns={columns}
      dataSource={state.page?.items}
      loading={loading}
      emptyText={t('shell.table.empty')}
      view={{ query, onChange: setQuery, total: state.page?.totalCount ?? 0, search: true }}
      failure={
        failed ? (
          <AxisAlert data-testid="reference-lookup-error" kind="error" message={t('shell.table.loadFailed')} />
        ) : undefined
      }
      rowAttributes={(record) => ({
        onClick: () => onPick(record),
        // A row can be reached with Tab and picked with Enter, as with a click.
        tabIndex: 0,
        onKeyDown: (event) => {
          if (event.key === 'Enter') {
            // Focus returns to the Choose button as the dialog closes, and the Enter must not press it again.
            event.preventDefault()
            onPick(record)
          }
        },
        style: { cursor: 'pointer' },
      })}
    />
  )
}
