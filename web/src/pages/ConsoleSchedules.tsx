import { Alert, Card, Flex, Select, Table, type TableColumnsType } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { fetchConsoleSchedules, type ConsoleApplication, type ConsoleScheduleItem } from '../platform/console'
import { EmptyDash } from '../platform/EmptyValue'
import { useText } from '../platform/texts'

interface ConsoleSchedulesProps {
  /** The applications of the tenant, which fill the filter. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The application filter of the request, so a result for an older filter shows as loading. */
  application?: string
  /** Whether the result belongs to a request at all, because the first state has none. */
  done: boolean
  /** The last list loaded. It stays while the next one loads. */
  items?: ConsoleScheduleItem[]
  failed: boolean
}

/**
 * The Schedules view of the operator console: the schedule triggers of the active release of every application, ordered
 * by application and trigger name and not paged, each with its process, its cron schedule and time zone, the last
 * occurrence that ran and the next occurrence. The application filter asks the server for one application. A failure, a
 * 403 included, shows an error in this card only.
 */
export function ConsoleSchedules({ applications, locale }: ConsoleSchedulesProps) {
  const t = useText()
  const [application, setApplication] = useState<string>()
  const [load, setLoad] = useState<LoadState>({ done: false, failed: false })
  const loading = !load.done || load.application !== application

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleSchedules(application, controller.signal)
      .then((result) => {
        setLoad(
          result.kind === 'ok'
            ? { application, done: true, items: result.schedules.items, failed: false }
            : { application, done: true, failed: true },
        )
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console schedules failed', error)
        setLoad({ application, done: true, failed: true })
      })
    return () => controller.abort()
  }, [application])

  const columns = useMemo<TableColumnsType<ConsoleScheduleItem>>(
    () => [
      {
        key: 'application',
        title: t('shell.console.schedules.application'),
        render: (_value, item) => item.application,
      },
      { key: 'trigger', title: t('shell.console.schedules.trigger'), render: (_value, item) => item.trigger },
      { key: 'process', title: t('shell.console.schedules.process'), render: (_value, item) => item.process },
      {
        key: 'schedule',
        title: t('shell.console.schedules.schedule'),
        render: (_value, item) => `${item.schedule} ${item.timeZone}`,
      },
      {
        key: 'lastOccurrence',
        title: t('shell.console.schedules.lastOccurrence'),
        render: (_value, item) =>
          item.lastOccurrenceAt === null ? <EmptyDash /> : new Date(item.lastOccurrenceAt).toLocaleString(locale),
      },
      {
        key: 'nextOccurrence',
        title: t('shell.console.schedules.nextOccurrence'),
        render: (_value, item) => new Date(item.nextOccurrenceAt).toLocaleString(locale),
      },
    ],
    [locale, t],
  )

  return (
    <Card data-testid="console-schedules" title={t('shell.console.schedules.title')}>
      <Flex vertical gap="middle">
        <Select
          data-testid="console-schedules-application"
          aria-label={t('shell.console.schedules.application')}
          style={{ maxWidth: 320 }}
          value={application ?? ''}
          options={[
            { value: '', label: t('shell.console.schedules.allApplications') },
            ...applications.map((row) => ({ value: row.id, label: row.name })),
          ]}
          onChange={(value) => setApplication(value || undefined)}
        />
        {load.failed && !loading ? (
          <Alert data-testid="console-schedules-error" type="error" title={t('shell.console.schedules.loadFailed')} />
        ) : (
          <Table<ConsoleScheduleItem>
            rowKey={(item) => `${item.applicationId}:${item.triggerId}`}
            columns={columns}
            scroll={{ x: 'max-content' }}
            dataSource={load.items}
            loading={loading}
            locale={{ emptyText: t('shell.console.schedules.empty') }}
            pagination={false}
          />
        )}
      </Flex>
    </Card>
  )
}
