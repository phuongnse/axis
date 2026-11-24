import { useEffect, useMemo, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { fetchConsoleSchedules, type ConsoleSchedulePage, type ConsoleScheduleItem } from '../platform/console'
import { SectionPart } from '../platform/SectionParts'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'

interface ConsoleSchedulesProps {
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The columns the view's search, filter and sort can name, see docs/reference/operator-console.md#api. */
const scheduleFields = [
  { name: 'application', type: 'text' },
  { name: 'trigger', type: 'text' },
  { name: 'process', type: 'text' },
  { name: 'schedule', type: 'text' },
  { name: 'lastOccurrenceAt', type: 'date' },
  { name: 'nextOccurrenceAt', type: 'date' },
] as const
// The view has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/**
 * The Schedules view of the operator console: the schedule triggers of the active release of every application, ordered
 * by application and trigger name until the view sorts them, paged on the server, each with its process, its cron
 * schedule and time zone, the last occurrence that ran and the next occurrence. The table has Search, Filter, Columns,
 * Refresh, a sort on every column but Schedule, and a pager with a page size, all kept in the address, see
 * docs/reference/operator-console.md#views and docs/reference/tables.md#tables. A failure, a 403
 * included, shows an error in this part only.
 */
export function ConsoleSchedules({ locale }: ConsoleSchedulesProps) {
  const t = useText()
  const { view, setView, saved } = useTableView(
    scheduleFields,
    recordPageSize,
    noParameters,
    true,
    true,
    true,
    '',
    'console.schedules',
  )
  const query = recordQuery(view)
  const [schedules, setSchedules] = useState<ConsoleSchedulePage | 'failed'>()
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleSchedules(query, controller.signal)
      .then((result) => setSchedules(result.kind === 'ok' ? result.schedules : 'failed'))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console schedules failed', error)
        setSchedules('failed')
      })
    return () => controller.abort()
  }, [query, reload])

  const columns = useMemo<AxisColumn<ConsoleScheduleItem>[]>(
    () => [
      {
        key: 'application',
        kind: 'freeText',
        title: t('shell.console.schedules.application'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, item) => item.application,
      },
      {
        key: 'trigger',
        kind: 'freeText',
        title: t('shell.console.schedules.trigger'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, item) => item.trigger,
      },
      {
        key: 'process',
        kind: 'freeText',
        title: t('shell.console.schedules.process'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, item) => item.process,
      },
      {
        key: 'schedule',
        kind: 'fixed',
        title: t('shell.console.schedules.schedule'),
        filter: { kind: 'text' },
        render: (_value, item) => `${item.schedule} ${item.timeZone}`,
      },
      {
        key: 'lastOccurrenceAt',
        kind: 'fixed',
        title: t('shell.console.schedules.lastOccurrence'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, item) =>
          item.lastOccurrenceAt === null ? null : new Date(item.lastOccurrenceAt).toLocaleString(locale),
      },
      {
        key: 'nextOccurrenceAt',
        kind: 'fixed',
        title: t('shell.console.schedules.nextOccurrence'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, item) => new Date(item.nextOccurrenceAt).toLocaleString(locale),
      },
    ],
    [locale, t],
  )
  const page = typeof schedules === 'object' ? schedules : undefined

  return (
    <SectionPart testId="console-schedules" heading={{ title: t('shell.console.schedules.title') }}>
      <AxisTable<ConsoleScheduleItem>
        rowKey={(item) => `${item.applicationId}:${item.triggerId}`}
        columns={columns}
        dataSource={page?.items ?? []}
        loading={schedules === undefined}
        emptyText={t('shell.console.schedules.empty')}
        view={{
          query: view,
          onChange: setView,
          saved,
          pageSizes: tablePageSizes(),
          total: page?.totalCount ?? 0,
          search: true,
          columns: true,
        }}
        toolbar={{ onRefresh: () => setReload((value) => value + 1) }}
        failure={
          schedules === 'failed' ? (
            <AxisAlert
              data-testid="console-schedules-error"
              kind="error"
              message={t('shell.console.schedules.loadFailed')}
            />
          ) : undefined
        }
      />
    </SectionPart>
  )
}
