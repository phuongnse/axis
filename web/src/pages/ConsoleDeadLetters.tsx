import { SendOutlined } from '@ant-design/icons'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import type { AxisAction } from '../platform/menuActions'
import {
  consoleDeadLettersPath,
  fetchConsoleDeadLetters,
  type ConsoleApplication,
  type ConsoleDeadLetterItem,
  type ConsoleDeadLetterPage,
} from '../platform/console'
import { formatValue } from '../platform/formatValue'
import { deadLetterCommands, forbidden, sendDeadLetterCommand, type DeadLetterCommand } from '../platform/inspection'
import { ReasonDialog } from '../platform/ReasonDialog'
import { SectionPart } from '../platform/SectionParts'
import { StatusTag, type StatusTone } from '../platform/StatusTag'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'

interface ConsoleDeadLettersProps {
  /** The applications of the tenant, which name the applications of the dead letters and fill the Application filter. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The command a user opened the dialog for, and the dead letter it is for. */
interface OpenCommand {
  command: DeadLetterCommand
  item: ConsoleDeadLetterItem
}

const createdAtField = { name: 'createdAt', type: 'date-time' } as const

/** The columns the view's search, filter and sort can name, see docs/reference/operator-console.md#api. */
const deadLetterFields = [
  { name: 'application', type: 'text' },
  { name: 'kind', type: 'text' },
  { name: 'event', type: 'text' },
  { name: 'target', type: 'text' },
  { name: 'attempts', type: 'integer' },
  { name: 'lastError', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'createdAt', type: 'date' },
] as const
// The view has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []
const deadLetterStates = ['open', 'redelivered', 'discarded']

const deadLetterTones: Record<string, StatusTone> = { open: 'attention', redelivered: 'success', discarded: 'muted' }

/**
 * The Dead letters view of the operator console: the dead letters of every application of the tenant in every state,
 * newest first until the view sorts them, including those that belong to no process, paged on the server. The table has
 * Search, Filter, Columns, Refresh, a sort on Kind, Attempts, State and Created, and a pager with a page size, all kept in
 * the address under the `deadLetters.` prefix, because Schedules on the same page keeps the plain names, see
 * docs/reference/operator-console.md#views and docs/reference/actions-and-controls.md#tables.
 * A row has a button for each command the server lists in the item's `commands`, and the view never decides which
 * letter may take one. A button opens the reason dialog, and a success reads the list again. A failure, a 403
 * included, shows an error in this part only.
 */
export function ConsoleDeadLetters({ applications, locale }: ConsoleDeadLettersProps) {
  const t = useText()
  const { view, setView, saved } = useTableView(
    deadLetterFields,
    recordPageSize,
    noParameters,
    true,
    true,
    true,
    'deadLetters.',
    'console.deadLetters',
  )
  const query = recordQuery(view)
  const [reload, setReload] = useState(0)
  const [open, setOpen] = useState<OpenCommand>()
  const [letters, setLetters] = useState<ConsoleDeadLetterPage | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleDeadLetters(query, controller.signal)
      .then((result) => setLetters(result === forbidden ? 'failed' : result))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console dead letters failed', error)
        setLetters('failed')
      })
    return () => controller.abort()
  }, [query, reload])

  const columns = useMemo<AxisColumn<ConsoleDeadLetterItem>[]>(() => {
    const names = new Map(applications.map((row) => [row.id, row.name]))
    return [
      {
        key: 'application',
        kind: 'freeText',
        title: t('shell.console.deadLetters.application'),
        filter: { kind: 'enum', options: applications.map((row) => ({ value: row.id, label: row.name })) },
        render: (_value, item) => names.get(item.applicationId) ?? item.applicationId,
      },
      {
        key: 'kind',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.kind'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, item) => item.kind,
      },
      {
        key: 'event',
        kind: 'fixed',
        title: t('shell.console.deadLetters.event'),
        filter: { kind: 'text' },
        render: (_value, item) => item.eventId,
      },
      {
        key: 'target',
        kind: 'freeText',
        title: t('shell.console.deadLetters.target'),
        filter: { kind: 'text' },
        render: (_value, item) => item.triggerId ?? item.webhookId,
      },
      {
        key: 'attempts',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.attempts'),
        filter: { kind: 'number' },
        sorter: true,
        render: (_value, item) => item.attempts,
      },
      {
        key: 'lastError',
        kind: 'freeText',
        title: t('shell.inspector.deadLetters.lastError'),
        filter: { kind: 'text' },
        render: (_value, item) => item.lastError,
      },
      {
        key: 'state',
        kind: 'fixed',
        title: t('shell.console.deadLetters.state'),
        filter: {
          kind: 'enum',
          options: deadLetterStates.map((value) => ({ value, label: t(`shell.console.deadLetters.state.${value}`) })),
        },
        sorter: true,
        render: (_value, item) => (
          <StatusTag tone={deadLetterTones[item.state] ?? 'neutral'}>
            {t(`shell.console.deadLetters.state.${item.state}`)}
          </StatusTag>
        ),
      },
      {
        key: 'createdAt',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.createdAt'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, item) => formatValue(createdAtField, item.createdAt, {}, { locale, text: t }),
      },
    ]
  }, [applications, locale, t])
  const page = typeof letters === 'object' ? letters : undefined

  return (
    <SectionPart testId="console-dead-letters" heading={{ title: t('shell.console.deadLetters.title') }}>
      <AxisTable<ConsoleDeadLetterItem>
        rowKey={(item) => item.id}
        columns={columns}
        dataSource={page?.items ?? []}
        loading={letters === undefined}
        emptyText={t('shell.console.deadLetters.empty')}
        view={{
          query: view,
          onChange: setView,
          saved,
          pageSizes: tablePageSizes(),
          total: page?.totalCount ?? 0,
          search: true,
          columns: true,
        }}
        toolbar={{ onRefresh: () => setReload((count) => count + 1) }}
        failure={
          letters === 'failed' ? (
            <AxisAlert
              data-testid="console-dead-letters-error"
              kind="error"
              message={t('shell.console.deadLetters.loadFailed')}
            />
          ) : undefined
        }
        rowActions={{
          configured: deadLetterCommands.length,
          actions: (item) =>
            deadLetterCommands
              .filter((command) => item.commands.includes(command))
              .map((command): AxisAction => ({
                key: command,
                labelKey: `shell.inspector.commands.${command}`,
                ...(command === 'redeliver' ? { icon: <SendOutlined /> } : { meaning: 'remove' as const }),
                onClick: () => setOpen({ command, item }),
                attributes: { 'data-testid': `console-dead-letter-${command}-${item.id}` },
              })),
        }}
      />
      {open && (
        <ReasonDialog
          title={t(`shell.inspector.commands.${open.command}`)}
          onSend={(reason) => sendDeadLetterCommand(consoleDeadLettersPath, open.item.id, open.command, reason)}
          onClose={() => setOpen(undefined)}
          onChanged={() => setReload((count) => count + 1)}
        />
      )}
    </SectionPart>
  )
}
