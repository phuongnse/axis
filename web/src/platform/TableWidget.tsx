import { Alert, Button, Flex, Table, type TableColumnsType, type TableProps } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Link, useHref, useLocation, useNavigate, useSearchParams } from 'react-router'
import { FilterBar } from './FilterBar'
import { formatValue } from './formatValue'
import { fetchRecords, RecordQueryProblem, type RecordItem, type RecordPage, type RecordValue } from './records'
import type { DataSourceColumn, DataSourceParameter, FieldMetadata, WidgetMetadata } from './site'
import {
  parseTableQuery,
  recordPageSize,
  recordQuery,
  tablePageSizes,
  writeTableQuery,
  type TableQuery,
  type TableSort,
} from './tableQuery'
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
  /** The server's messages for the data source parameter values it rejected, by parameter name. */
  parameterErrors?: Record<string, string[]>
}

// Field names start with a letter, so this key never names a field column.
const openColumnKey = '$open'

function sameSort(a: TableSort | null, b: TableSort | null): boolean {
  return a?.field === b?.field && a?.descending === b?.descending
}

/** What a table reads, from its entity or its data source. */
interface TableSource {
  /** The entity or data source name, for messages. */
  name: string
  /** The record API or the data source rows endpoint. */
  path: string
  columns: readonly (FieldMetadata | DataSourceColumn)[]
  /** The data source parameters. An entity has none. */
  parameters: readonly DataSourceParameter[]
  defaultPageSize: number
}

function tableSource({ entity, dataSource }: WidgetMetadata): TableSource {
  if (dataSource) {
    return {
      name: dataSource.name,
      path: dataSource.rowsPath,
      columns: dataSource.columns,
      parameters: dataSource.parameters,
      defaultPageSize: dataSource.pageSize,
    }
  }
  // The server sets exactly one of the two.
  const { name, recordsPath, fields } = entity!
  return {
    name,
    path: recordsPath,
    // A child collection has no column and cannot be sorted by.
    columns: fields.filter((field) => field.type !== 'child-collection'),
    parameters: [],
    defaultPageSize: recordPageSize,
  }
}

/**
 * Shows the records of the widget's entity, or the rows of its data source. A row of a data source
 * carries the id of its root record, so it opens the same form. Paging and sorting live in the URL
 * as the API's `page`, `pageSize` and `sort`, so reload, sharing and the back button keep them. A
 * data source table also has a filter bar, whose values live in the URL under the parameter names.
 */
export function TableWidget({ sitePath, widget, locale }: TableWidgetProps) {
  const t = useText()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { formPage } = widget
  const formPath = formPage && `/${sitePath}/${formPage.toLowerCase()}`
  const createPath = formPath ? `${formPath}/new` : ''
  const createHref = useHref(createPath)
  // The form returns to this address, with its paging and sorting, after save or cancel.
  const location = useLocation()
  const from = `${location.pathname}${location.search}`

  const { name, path, columns: fields, parameters, defaultPageSize } = useMemo(() => tableSource(widget), [widget])
  const parameterNames = useMemo(() => parameters.map((parameter) => parameter.name), [parameters])
  const sizes = useMemo(() => tablePageSizes(defaultPageSize), [defaultPageSize])
  const query = useMemo(
    () => parseTableQuery(searchParams, fields, defaultPageSize, parameterNames),
    [searchParams, fields, defaultPageSize, parameterNames],
  )
  const requestQuery = recordQuery(query, defaultPageSize)
  const requestUrl = `${path}?${requestQuery}`
  const [state, setState] = useState<LoadState>({ failed: false })
  const loading = state.url !== requestUrl
  const failed = state.failed && !loading
  const parameterErrors = loading ? undefined : state.parameterErrors
  const totalCount = loading ? undefined : state.page?.totalCount

  // An invalid, empty, repeated or out-of-order parameter is rewritten without a history entry.
  useEffect(() => {
    const canonical = writeTableQuery(searchParams, query, defaultPageSize, parameterNames)
    if (canonical.toString() !== searchParams.toString()) {
      setSearchParams(canonical, { replace: true })
    }
  }, [searchParams, query, defaultPageSize, parameterNames, setSearchParams])

  // A page past the end becomes the last page once the total is known, so the URL and the
  // pagination agree.
  useEffect(() => {
    if (totalCount === undefined) {
      return
    }
    const lastPage = Math.max(1, Math.ceil(totalCount / query.pageSize))
    if (query.page > lastPage) {
      setSearchParams(writeTableQuery(searchParams, { ...query, page: lastPage }, defaultPageSize, parameterNames), {
        replace: true,
      })
    }
  }, [totalCount, searchParams, query, defaultPageSize, parameterNames, setSearchParams])

  // Data source rows have the shape of records without a version, which the table never reads. A
  // rejected parameter value shows under its filter input, with no rows, rather than as a failure.
  useEffect(() => {
    const controller = new AbortController()
    const url = `${path}?${requestQuery}`
    fetchRecords(path, requestQuery, controller.signal)
      .then((page) => setState({ url, page, failed: false }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        const errors =
          error instanceof RecordQueryProblem
            ? Object.fromEntries(Object.entries(error.errors).filter(([key]) => parameterNames.includes(key)))
            : {}
        if (Object.keys(errors).length > 0) {
          setState({ url, failed: false, parameterErrors: errors })
          return
        }
        console.warn(`Loading the records of '${name}' failed`, error)
        setState({ url, failed: true })
      })
    return () => controller.abort()
  }, [path, name, requestQuery, parameterNames])

  const columns = useMemo<TableColumnsType<RecordItem>>(() => {
    const sortOrder = (field: string) =>
      query.sort?.field === field ? (query.sort.descending ? 'descend' : 'ascend') : null
    const fieldColumns: TableColumnsType<RecordItem> = fields.map((field) => ({
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
        key: openColumnKey,
        render: (_value: unknown, record: RecordItem) => (
          <Link to={`${formPath}/${record.id}`} state={{ from }}>
            {t('shell.table.open')}
          </Link>
        ),
      },
    ]
  }, [fields, formPath, from, locale, query.sort, t])

  // A new sort or page size starts again at page 1. Paging and sorting add history entries.
  const onChange: TableProps<RecordItem>['onChange'] = (pagination, _filters, sorter) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter
    const sort = single?.order ? { field: String(single.columnKey), descending: single.order === 'descend' } : null
    const pageSize = pagination.pageSize ?? query.pageSize
    const reset = !sameSort(sort, query.sort) || pageSize !== query.pageSize
    const next: TableQuery = {
      page: reset ? 1 : (pagination.current ?? 1),
      pageSize,
      sort,
      parameters: query.parameters,
    }
    setSearchParams(writeTableQuery(searchParams, next, defaultPageSize, parameterNames))
  }

  // A new filter value starts again at page 1 and adds a history entry. The values keep the
  // declaration order, so the URL order is stable.
  const onFilter = (changed: string, value: string | null) => {
    const current = { ...query.parameters, [changed]: value ?? '' }
    const next: Record<string, string> = {}
    for (const parameter of parameterNames) {
      if (current[parameter]) {
        next[parameter] = current[parameter]
      }
    }
    setSearchParams(
      writeTableQuery(searchParams, { ...query, page: 1, parameters: next }, defaultPageSize, parameterNames),
    )
  }

  return (
    <Flex vertical gap="middle">
      {formPath && (
        <Flex justify="flex-end">
          <Button
            type="primary"
            href={createHref}
            onClick={(event) => {
              // A modified click keeps the link behaviour, such as opening a new tab.
              if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
                return
              }
              event.preventDefault()
              navigate(createPath, { state: { from } })
            }}
          >
            {t('shell.table.create')}
          </Button>
        </Flex>
      )}
      {parameters.length > 0 && (
        <FilterBar
          parameters={parameters}
          values={query.parameters ?? {}}
          errors={parameterErrors ?? {}}
          onChange={onFilter}
        />
      )}
      {failed ? (
        <Alert data-testid="records-error" type="error" message={t('shell.table.loadFailed')} />
      ) : (
        <Table<RecordItem>
          rowKey="id"
          columns={columns}
          dataSource={parameterErrors ? [] : state.page?.items}
          loading={loading}
          locale={{ emptyText: t('shell.table.empty') }}
          pagination={{
            current: query.page,
            pageSize: query.pageSize,
            total: parameterErrors ? 0 : (state.page?.totalCount ?? 0),
            pageSizeOptions: sizes,
            showSizeChanger: true,
          }}
          onChange={onChange}
        />
      )}
    </Flex>
  )
}
