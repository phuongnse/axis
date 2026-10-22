import { Alert, Button, Dropdown, Flex, Modal, Table, type TableColumnsType, type TableProps } from 'antd'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { FilterBar } from './FilterBar'
import { formatValue } from './formatValue'
import { navigatePath } from './navigateAction'
import { markCurrentPage } from './pagination'
import { newIdempotencyKey, startProcess } from './processes'
import {
  deleteRecord,
  fetchRecords,
  RecordQueryProblem,
  type RecordItem,
  type RecordPage,
  type RecordValue,
} from './records'
import type {
  DataSourceColumn,
  DataSourceParameter,
  DeleteAction,
  FieldMetadata,
  RowAction,
  StartProcessAction,
  WidgetMetadata,
} from './site'
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
import { usePhoneWidth } from './usePhoneWidth'
import { isVisible } from './visibleWhen'

interface TableWidgetProps {
  sitePath: string
  widget: WidgetMetadata
  /** The UI locale, for formatting values. */
  locale: string
  /** The values of the page's parameters from its URL, by page parameter name. Absent ones are not sent. */
  pageValues?: Record<string, string>
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

/** A table row: a record, a data source row, or a group of a grouped data source, which has no id. */
type TableRow = Omit<RecordItem, 'id'> & { id: string | null }

// Field names start with a letter, so this key never names a field column.
const actionsColumnKey = '$actions'

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
 * carries the id of its root record, so a row action can open a form for it. A row of a grouped
 * data source is a group with no id, so it has no row action buttons. Each `navigate` row action is
 * a button on every row with an id, and each toolbar action a button above the table. Both open
 * the target page with the parameter values in the URL query. Each `startProcess` row action is a
 * button on every row with an id that starts the process with that row's record as subject, with an
 * `Idempotency-Key` kept until the start has a final answer. A `201` loads the current page of rows
 * again, and a failure shows above the table. Each `delete` row action is a button on every row with an
 * id that opens a confirmation dialog, then sends `DELETE` to the record API. A `204` shows a success
 * message above the table and loads the current page of rows again. A `403`, `404` or `409` shows its own
 * message above the table and keeps the rows, and any other answer shows a generic failure. A toolbar action to a form page
 * without `record` needs the user's `create` grant on the entity, as the widget's access says, and
 * a button whose expressions the SPA cannot work out is hidden. Below 768 px, a row's actions fold
 * into one "Actions" menu.
 * Paging and sorting live in the URL as the API's `page`, `pageSize` and `sort`, so reload, sharing
 * and the back button keep them. A data source table also has a filter bar, whose values live in the
 * URL under the parameter names. A data source parameter that the widget binds to a page parameter has
 * no filter input: the table sends the page parameter's value under the data source parameter's name,
 * and leaves that page parameter in the URL as it is.
 */
export function TableWidget({ sitePath, widget, locale, pageValues }: TableWidgetProps) {
  const t = useText()
  const phone = usePhoneWidth()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { rowActions, toolbarActions } = widget
  // The form returns to this address, with its paging and sorting, after save or cancel.
  const location = useLocation()
  const from = `${location.pathname}${location.search}`

  const {
    name,
    path,
    columns: fields,
    parameters: allParameters,
    defaultPageSize,
  } = useMemo(() => tableSource(widget), [widget])
  // The filter bar, the URL and the server's errors cover only the parameters that no page parameter binds.
  const parameters = useMemo(
    () => allParameters.filter((parameter) => !Object.hasOwn(widget.parameters, parameter.name)),
    [allParameters, widget.parameters],
  )
  const parameterNames = useMemo(() => parameters.map((parameter) => parameter.name), [parameters])
  const sizes = useMemo(() => tablePageSizes(defaultPageSize), [defaultPageSize])
  const query = useMemo(
    () => parseTableQuery(searchParams, fields, defaultPageSize, parameterNames),
    [searchParams, fields, defaultPageSize, parameterNames],
  )
  // The bound values and the filter values, in declaration order.
  const requestQuery = useMemo(() => {
    const values: Record<string, string> = {}
    for (const parameter of allParameters) {
      const value = Object.hasOwn(widget.parameters, parameter.name)
        ? pageValues?.[widget.parameters[parameter.name]]
        : query.parameters?.[parameter.name]
      if (value) {
        values[parameter.name] = value
      }
    }
    return recordQuery({ ...query, parameters: values }, defaultPageSize)
  }, [allParameters, widget.parameters, pageValues, query, defaultPageSize])
  const requestUrl = `${path}?${requestQuery}`
  const [state, setState] = useState<LoadState>({ failed: false })
  // Bumped after a row start, so the same URL is requested again.
  const [reloads, setReloads] = useState(0)
  // A key is kept per process and row while its start has no final answer: a network error or a 5xx.
  const startKeys = useRef<Record<string, string>>({})
  const [starting, setStarting] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [actionDone, setActionDone] = useState<string | null>(null)
  // The delete waiting for the user's confirmation.
  const [confirming, setConfirming] = useState<{ action: DeleteAction; rowId: string } | null>(null)
  const [deleting, setDeleting] = useState(false)
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
  }, [path, name, requestQuery, parameterNames, reloads])

  const runStart = useCallback(
    async (action: StartProcessAction, rowId: string) => {
      if (starting) {
        return
      }
      setStarting(true)
      setActionError(null)
      setActionDone(null)
      const keyName = `${action.instancesPath} ${rowId}`
      try {
        const key = (startKeys.current[keyName] ??= newIdempotencyKey())
        let result
        try {
          result = await startProcess(action.instancesPath, rowId, key)
        } catch (error) {
          console.warn(`Starting the process '${action.process}' for '${rowId}' failed`, error)
          setActionError(t('shell.form.startFailed'))
          return
        }
        delete startKeys.current[keyName]
        if (result.ok) {
          setReloads((count) => count + 1)
          return
        }
        const { problem } = result
        const messages = Object.values(problem.errors).flat()
        if (messages.length > 0) {
          // A start condition's message is a text key. A fixed message is no key, so it shows as is.
          setActionError(messages.map((message) => t(message)).join(' '))
        } else if (problem.status === 409) {
          setActionError(t('shell.form.processRunning'))
        } else {
          setActionError(t('shell.form.startFailed'))
        }
      } finally {
        setStarting(false)
      }
    },
    [starting, t],
  )

  // A 403, 404 and 409 each have their own message. Only a 204 deletes the record.
  const runDelete = useCallback(async () => {
    if (!confirming || deleting) {
      return
    }
    const { action, rowId } = confirming
    setDeleting(true)
    setActionError(null)
    setActionDone(null)
    try {
      let status
      try {
        status = await deleteRecord(action.recordsPath, rowId)
      } catch (error) {
        console.warn(`Deleting the record '${rowId}' of '${action.recordsPath}' failed`, error)
        setActionError(t('shell.table.deleteFailed'))
        return
      }
      if (status === 204) {
        setActionDone(t('shell.table.deleted'))
        setReloads((count) => count + 1)
      } else if (status === 403) {
        setActionError(t('shell.table.deleteDenied'))
      } else if (status === 404) {
        setActionError(t('shell.table.deleteMissing'))
      } else if (status === 409) {
        setActionError(t('shell.table.deleteInUse'))
      } else {
        setActionError(t('shell.table.deleteFailed'))
      }
    } finally {
      setDeleting(false)
      setConfirming(null)
    }
  }, [confirming, deleting, t])

  const columns = useMemo<TableColumnsType<TableRow>>(() => {
    const sortOrder = (field: string) =>
      query.sort?.field === field ? (query.sort.descending ? 'descend' : 'ascend') : null
    const fieldColumns: TableColumnsType<TableRow> = fields.map((field) => ({
      key: field.name,
      dataIndex: ['values', field.name],
      title: field.labelKey ? t(field.labelKey) : field.name,
      // The record API sorts a reference by its id, which is not the label people see.
      sorter: field.type !== 'reference',
      sortOrder: field.type === 'reference' ? undefined : sortOrder(field.name),
      align: field.type === 'integer' || field.type === 'decimal' ? 'right' : undefined,
      render: (value: RecordValue, record: TableRow) => formatValue(field, value, record.labels, { locale, text: t }),
    }))
    const result = [...fieldColumns]
    if (rowActions.length > 0) {
      result.push({
        key: actionsColumnKey,
        render: (_value: unknown, record: TableRow) => {
          // A group row has no id and no buttons.
          if (record.id === null) {
            return null
          }
          const scope = { id: record.id, values: record.values }
          // A start action runs for the row. A delete action asks for confirmation first. A navigate action opens its page, or is dropped when its path is unknown.
          // An action whose visibleWhen is not plainly true is dropped.
          const list = rowActions.flatMap((action, index): { action: RowAction; index: number; run: () => void }[] => {
            if (!isVisible(action.visibleWhen, scope)) {
              return []
            }
            if (action.type === 'startProcess') {
              return [{ action, index, run: () => void runStart(action, record.id!) }]
            }
            if (action.type === 'delete') {
              return [{ action, index, run: () => setConfirming({ action, rowId: record.id! }) }]
            }
            const path = navigatePath(sitePath, action, scope)
            return path ? [{ action, index, run: () => navigate(path, { state: { from } }) }] : []
          })
          if (list.length === 0) {
            return null
          }
          if (phone) {
            return (
              <Dropdown
                trigger={['click']}
                autoFocus
                menu={{
                  items: list.map(({ action, index, run }) => ({
                    key: String(index),
                    label: t(action.labelKey),
                    disabled: action.type !== 'navigate' && (starting || deleting),
                    onClick: run,
                  })),
                  'aria-label': t('shell.table.rowActions'),
                }}
              >
                <Button size="small">{t('shell.table.rowActions')}</Button>
              </Dropdown>
            )
          }
          return (
            <Flex gap="small">
              {list.map(({ action, index, run }) => (
                <Button
                  key={index}
                  size="small"
                  disabled={action.type !== 'navigate' && (starting || deleting)}
                  onClick={run}
                >
                  {t(action.labelKey)}
                </Button>
              ))}
            </Flex>
          )
        },
      })
    }
    return result
  }, [fields, from, locale, navigate, phone, query.sort, rowActions, runStart, sitePath, starting, deleting, t])

  // A new sort or page size starts again at page 1. Paging and sorting add history entries.
  const onChange: TableProps<TableRow>['onChange'] = (pagination, _filters, sorter) => {
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

  // A toolbar has no row, so only the literals of its expressions resolve. An action to a form page
  // without a record creates one, so it needs the `create` grant.
  const toolbarButtons = toolbarActions.flatMap((action, index) => {
    const path = navigatePath(sitePath, action, null)
    const creates = action.opensForm && action.record === null
    return path && (!creates || widget.access?.actions.includes('create')) ? [{ action, path, index }] : []
  })

  return (
    <Flex vertical gap="middle" aria-busy={loading}>
      {toolbarButtons.length > 0 && (
        <Flex justify="flex-end" gap="small">
          {toolbarButtons.map(({ action, path, index }) => (
            <Button key={index} onClick={() => navigate(path, { state: { from } })}>
              {t(action.labelKey)}
            </Button>
          ))}
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
      {actionError !== null && <Alert data-testid="table-action-error" type="error" message={actionError} />}
      {actionDone !== null && <Alert data-testid="table-action-done" type="success" message={actionDone} />}
      {confirming !== null && (
        <Modal
          open
          title={t('shell.table.deleteConfirm')}
          okText={t('shell.table.delete')}
          okButtonProps={{ danger: true }}
          cancelText={t('shell.form.cancel')}
          confirmLoading={deleting}
          onOk={() => void runDelete()}
          onCancel={() => setConfirming(null)}
        />
      )}
      {failed ? (
        <Alert data-testid="records-error" type="error" message={t('shell.table.loadFailed')} />
      ) : (
        <Table<TableRow>
          // The group values are unique per group row.
          rowKey={(row) => row.id ?? JSON.stringify(row.values)}
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
            itemRender: markCurrentPage(query.page),
          }}
          onChange={onChange}
        />
      )}
    </Flex>
  )
}
