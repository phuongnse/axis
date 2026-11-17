import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import {
  fetchConsoleFailedWork,
  type ConsoleApplication,
  type ConsoleFailedWorkItem,
  type ConsoleFailedWorkPage,
} from '../platform/console'
import { SectionPart } from '../platform/SectionParts'
import { recordPageSize, recordQuery } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'

interface ConsoleFailedWorkProps {
  /** The applications of the tenant, which fill the Application filter. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The columns the view's filter can name, see docs/reference/operator-console.md#api. */
const failedWorkFields = [
  { name: 'application', type: 'text' },
  { name: 'process', type: 'text' },
  { name: 'instance', type: 'text' },
  { name: 'stepOrCall', type: 'text' },
  { name: 'failedAt', type: 'date' },
  { name: 'error', type: 'text' },
] as const
// The view has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/**
 * The Failed work view of the operator console: the failed instances and the calls with an unknown outcome, newest first,
 * paged on the server. The table has Filter on each column and a pager, both kept in the address under the `failedWork.`
 * prefix, because the page has other tables, see docs/reference/operator-console.md#views and
 * docs/reference/actions-and-controls.md#tables. The Application and Process columns filter by name, so the view has no
 * picker above the table. A failure, a 403 included, shows an error in this part only.
 */
export function ConsoleFailedWork({ applications, locale }: ConsoleFailedWorkProps) {
  const t = useText()
  const { view, setView } = useTableView(
    failedWorkFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    'failedWork.',
  )
  const query = recordQuery(view)
  const [failedWork, setFailedWork] = useState<ConsoleFailedWorkPage | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleFailedWork(query, controller.signal)
      .then((result) => setFailedWork(result.kind === 'ok' ? result.page : 'failed'))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console failed work failed', error)
        setFailedWork('failed')
      })
    return () => controller.abort()
  }, [query])

  const page = typeof failedWork === 'object' ? failedWork : undefined
  const processes = page?.processes

  const columns = useMemo<AxisColumn<ConsoleFailedWorkItem>[]>(
    () => [
      {
        key: 'application',
        kind: 'freeText',
        title: t('shell.console.failedWork.application'),
        filter: { kind: 'enum', options: applications.map((row) => ({ value: row.id, label: row.name })) },
        render: (_value, row) => row.application,
      },
      {
        key: 'process',
        kind: 'freeText',
        title: t('shell.console.failedWork.process'),
        filter: {
          kind: 'enum',
          options: (processes ?? []).map((row) => ({ value: row.id, label: `${row.application} / ${row.name}` })),
        },
        render: (_value, row) => row.process,
      },
      {
        key: 'instance',
        kind: 'fixed',
        title: t('shell.console.failedWork.instance'),
        filter: { kind: 'text' },
        render: (_value, row) => (
          <Link to={`/console/instances/${row.instanceId}`} data-testid={`console-failed-work-open-${row.instanceId}`}>
            {row.instanceId}
          </Link>
        ),
      },
      {
        key: 'stepOrCall',
        kind: 'freeText',
        title: t('shell.console.failedWork.stepOrCall'),
        filter: { kind: 'text' },
        render: (_value, row) =>
          row.kind === 'instance' ? row.step : `${row.step}: ${row.connector ?? row.package} ${row.operation}`,
      },
      {
        key: 'failedAt',
        kind: 'fixed',
        title: t('shell.console.failedWork.failedAt'),
        filter: { kind: 'date' },
        render: (_value, row) => new Date(row.failedAt).toLocaleString(locale),
      },
      {
        key: 'error',
        kind: 'freeText',
        title: t('shell.console.failedWork.error'),
        filter: { kind: 'text' },
        render: (_value, row) => row.error,
      },
    ],
    [applications, locale, processes, t],
  )

  return (
    <SectionPart testId="console-failed-work" heading={{ title: t('shell.console.failedWork.title') }}>
      <AxisTable<ConsoleFailedWorkItem>
        rowKey={(row) => row.callId ?? row.instanceId}
        columns={columns}
        dataSource={page?.items ?? []}
        loading={failedWork === undefined}
        emptyText={t('shell.console.failedWork.empty')}
        view={{ query: view, onChange: setView, total: page?.totalCount ?? 0 }}
        failure={
          failedWork === 'failed' ? (
            <AxisAlert
              data-testid="console-failed-work-error"
              kind="error"
              message={t('shell.console.failedWork.loadFailed')}
            />
          ) : undefined
        }
      />
    </SectionPart>
  )
}
