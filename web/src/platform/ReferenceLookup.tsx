import { Alert, Input, Modal, Table, type TableColumnsType } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { fetchRecords, type RecordItem, type RecordPage, type RecordValue } from './records'
import type { FieldMetadata } from './site'
import { recordQuery } from './tableQuery'
import { useText } from './texts'

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

/** How long after the last key press the search applies, in milliseconds. */
const searchDelay = 300

/**
 * A dialog that lists the records of a reference field's target entity, sorted by its display
 * field, to pick one. A search box filters the records to those whose display field contains the
 * trimmed text, ignoring case. It applies once typing pauses and starts again at page 1. Paging and
 * search are the dialog's own and stay out of the URL.
 */
export function ReferenceLookup({ field, title, open, onPick, onClose }: ReferenceLookupProps) {
  return (
    <Modal open={open} title={title} footer={null} onCancel={onClose} destroyOnHidden>
      {/* The dialog destroys its content when hidden, so each opening starts again at page 1 with no search. */}
      <LookupList field={field} onPick={onPick} />
    </Modal>
  )
}

function LookupList({ field, onPick }: Pick<ReferenceLookupProps, 'field' | 'onPick'>) {
  const t = useText()
  const target = field.target!
  const { displayField, recordsPath } = target
  const [page, setPage] = useState(1)
  // The typed text, and the search applied to the request once typing pauses.
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const requestQuery = recordQuery({
    page,
    pageSize,
    sort: displayField ? { field: displayField, descending: false } : null,
    parameters: search ? { search } : {},
  })
  const requestUrl = `${recordsPath}?${requestQuery}`
  const [state, setState] = useState<LoadState>({ failed: false })
  const loading = state.url !== requestUrl
  const failed = state.failed && !loading

  useEffect(() => {
    const next = text.trim()
    if (next === search) {
      return
    }
    const timer = setTimeout(() => {
      setSearch(next)
      setPage(1)
    }, searchDelay)
    return () => clearTimeout(timer)
  }, [text, search])

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

  const columns = useMemo<TableColumnsType<RecordItem>>(
    () => [
      {
        key: displayField ?? 'id',
        dataIndex: displayField ? ['values', displayField] : 'id',
        title: field.labelKey ? t(field.labelKey) : field.name,
        render: (value: RecordValue) => (value === null ? '' : String(value)),
      },
    ],
    [displayField, field.labelKey, field.name, t],
  )

  return (
    <div data-testid="reference-lookup">
      <Input
        allowClear
        value={text}
        onChange={(event) => setText(event.target.value)}
        aria-label={t('shell.lookup.search')}
        placeholder={t('shell.lookup.search')}
        data-testid="reference-lookup-search"
        style={{ marginBottom: 12 }}
      />
      {failed ? (
        <Alert data-testid="reference-lookup-error" type="error" message={t('shell.table.loadFailed')} />
      ) : (
        <Table<RecordItem>
          rowKey="id"
          columns={columns}
          dataSource={state.page?.items}
          loading={loading}
          locale={{ emptyText: t('shell.table.empty') }}
          pagination={{
            current: page,
            pageSize,
            total: state.page?.totalCount ?? 0,
            showSizeChanger: false,
            onChange: setPage,
          }}
          onRow={(record) => ({
            onClick: () => onPick(record),
            // A row can be reached with Tab and picked with Enter, as with a click.
            tabIndex: 0,
            onKeyDown: (event) => {
              if (event.key === 'Enter') {
                onPick(record)
              }
            },
            style: { cursor: 'pointer' },
          })}
        />
      )}
    </div>
  )
}
