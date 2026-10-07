import { Alert, Button, Flex, Table, type TableColumnsType, type TableProps } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Link, useHref, useNavigate, useSearchParams } from 'react-router'
import { formatValue } from './formatValue'
import { fetchRecords, type RecordItem, type RecordPage, type RecordValue } from './records'
import type { WidgetMetadata } from './site'
import { pageSizes, parseTableQuery, recordQuery, writeTableQuery, type TableQuery, type TableSort } from './tableQuery'
import { useText } from './texts'

interface TableWidgetProps {
  sitePath: string
  widget: WidgetMetadata
  /** The UI locale, for formatting values. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The request URL, so a result for an older request shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: RecordPage
  failed: boolean
}

function sameSort(a: TableSort | null, b: TableSort | null): boolean {
  return a?.field === b?.field && a?.descending === b?.descending
}

/**
 * Shows the records of the widget's entity. Paging and sorting live in the URL as the record API's
 * `page`, `pageSize` and `sort`, so reload, sharing and the back button keep them.
 */
export function TableWidget({ sitePath, widget, locale }: TableWidgetProps) {
  const t = useText()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { entity, formPage } = widget
  const formPath = formPage && `/${sitePath}/${formPage.toLowerCase()}`
  const createPath = formPath ? `${formPath}/new` : ''
  const createHref = useHref(createPath)

  const query = useMemo(() => parseTableQuery(searchParams, entity.fields), [searchParams, entity.fields])
  const requestQuery = recordQuery(query)
  const requestUrl = `${entity.recordsPath}?${requestQuery}`
  const [state, setState] = useState<LoadState>({ failed: false })
  const loading = state.url !== requestUrl
  const failed = state.failed && !loading

  // An invalid or out-of-order parameter is rewritten without a history entry.
  useEffect(() => {
    const canonical = writeTableQuery(searchParams, query)
    if (canonical.toString() !== searchParams.toString()) {
      setSearchParams(canonical, { replace: true })
    }
  }, [searchParams, query, setSearchParams])

  useEffect(() => {
    const controller = new AbortController()
    const url = `${entity.recordsPath}?${requestQuery}`
    fetchRecords(entity.recordsPath, requestQuery, controller.signal)
      .then((page) => setState({ url, page, failed: false }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the records of '${entity.name}' failed`, error)
          setState({ url, failed: true })
        }
      })
    return () => controller.abort()
  }, [entity.recordsPath, entity.name, requestQuery])

  const columns = useMemo<TableColumnsType<RecordItem>>(() => {
    const sortOrder = (field: string) =>
      query.sort?.field === field ? (query.sort.descending ? 'descend' : 'ascend') : null
    const fieldColumns: TableColumnsType<RecordItem> = entity.fields.map((field) => ({
      key: field.name,
      dataIndex: ['values', field.name],
      title: field.labelKey ? t(field.labelKey) : field.name,
      // The record API sorts a reference by its id, which is not the label people see.
      sorter: field.type !== 'reference',
      sortOrder: field.type === 'reference' ? undefined : sortOrder(field.name),
      align: field.type === 'integer' || field.type === 'decimal' ? 'right' : undefined,
      render: (value: RecordValue, record: RecordItem) => formatValue(field, value, record.labels, { locale, text: t }),
    }))
    if (!formPath) {
      return fieldColumns
    }
    return [
      ...fieldColumns,
      {
        key: 'open',
        render: (_value: unknown, record: RecordItem) => (
          <Link to={`${formPath}/${record.id}`}>{t('shell.table.open')}</Link>
        ),
      },
    ]
  }, [entity.fields, formPath, locale, query.sort, t])

  // A new sort or page size starts again at page 1. Paging and sorting add history entries.
  const onChange: TableProps<RecordItem>['onChange'] = (pagination, _filters, sorter) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter
    const sort = single?.order ? { field: String(single.columnKey), descending: single.order === 'descend' } : null
    const pageSize = pagination.pageSize ?? query.pageSize
    const reset = !sameSort(sort, query.sort) || pageSize !== query.pageSize
    const next: TableQuery = { page: reset ? 1 : (pagination.current ?? 1), pageSize, sort }
    setSearchParams(writeTableQuery(searchParams, next))
  }

  return (
    <Flex vertical gap="middle">
      {formPath && (
        <Flex justify="flex-end">
          <Button
            type="primary"
            href={createHref}
            onClick={(event) => {
              event.preventDefault()
              navigate(createPath)
            }}
          >
            {t('shell.table.create')}
          </Button>
        </Flex>
      )}
      {failed ? (
        <Alert data-testid="records-error" type="error" message={t('shell.table.loadFailed')} />
      ) : (
        <Table<RecordItem>
          rowKey="id"
          columns={columns}
          dataSource={state.page?.items}
          loading={loading}
          locale={{ emptyText: t('shell.table.empty') }}
          pagination={{
            current: query.page,
            pageSize: query.pageSize,
            total: state.page?.totalCount ?? 0,
            pageSizeOptions: pageSizes,
            showSizeChanger: true,
          }}
          onChange={onChange}
        />
      )}
    </Flex>
  )
}
