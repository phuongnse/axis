import { DeleteOutlined, SendOutlined } from '@ant-design/icons'
import { Card, Flex, Select, Tag } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
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
import { useText } from '../platform/texts'

interface ConsoleDeadLettersProps {
  /** The applications of the tenant, which name the applications of the dead letters and fill the filter. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The request URL, so a result for an older request shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: ConsoleDeadLetterPage
  failed: boolean
}

/** The command a user opened the dialog for, and the dead letter it is for. */
interface OpenCommand {
  command: DeadLetterCommand
  item: ConsoleDeadLetterItem
}

const createdAtField = { name: 'createdAt', type: 'date-time' } as const

/**
 * The Dead letters view of the operator console: the dead letters of every application of the tenant in every state,
 * newest first, including those that belong to no process. The application filter asks the server for one application.
 * A row has a button for each command the server lists in the item's `commands`, and the view never decides which
 * letter may take one. A button opens the reason dialog, and a success reads the list again. A failure, a 403
 * included, shows an error in this card only.
 */
export function ConsoleDeadLetters({ applications, locale }: ConsoleDeadLettersProps) {
  const t = useText()
  const [application, setApplication] = useState<string>()
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [open, setOpen] = useState<OpenCommand>()
  const [load, setLoad] = useState<LoadState>({ failed: false })
  const query = [application ? `application=${application}` : '', page > 1 ? `page=${page}` : '']
    .filter(Boolean)
    .join('&')
  const loadKey = `${query}#${reload}`
  const loading = load.url !== loadKey

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleDeadLetters(query, controller.signal)
      .then((result) => {
        if (result === forbidden) {
          setLoad({ url: loadKey, failed: true })
          return
        }
        // A page past the end becomes the last page once the total is known.
        setPage((current) => Math.min(current, Math.max(1, Math.ceil(result.totalCount / result.pageSize))))
        setLoad({ url: loadKey, page: result, failed: false })
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console dead letters failed', error)
        setLoad({ url: loadKey, failed: true })
      })
    return () => controller.abort()
  }, [query, loadKey])

  const columns = useMemo<AxisColumn<ConsoleDeadLetterItem>[]>(() => {
    const names = new Map(applications.map((row) => [row.id, row.name]))
    return [
      {
        key: 'application',
        kind: 'freeText',
        title: t('shell.console.deadLetters.application'),
        render: (_value, item) => names.get(item.applicationId) ?? item.applicationId,
      },
      { key: 'kind', kind: 'fixed', title: t('shell.inspector.deadLetters.kind'), render: (_value, item) => item.kind },
      {
        key: 'event',
        kind: 'fixed',
        title: t('shell.console.deadLetters.event'),
        render: (_value, item) => item.eventId,
      },
      {
        key: 'target',
        kind: 'freeText',
        title: t('shell.console.deadLetters.target'),
        render: (_value, item) => item.triggerId ?? item.webhookId,
      },
      {
        key: 'attempts',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.attempts'),
        render: (_value, item) => item.attempts,
      },
      {
        key: 'lastError',
        kind: 'freeText',
        title: t('shell.inspector.deadLetters.lastError'),
        render: (_value, item) => item.lastError,
      },
      {
        key: 'state',
        kind: 'fixed',
        title: t('shell.console.deadLetters.state'),
        render: (_value, item) => <Tag>{t(`shell.console.deadLetters.state.${item.state}`)}</Tag>,
      },
      {
        key: 'createdAt',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.createdAt'),
        render: (_value, item) => formatValue(createdAtField, item.createdAt, {}, { locale, text: t }),
      },
    ]
  }, [applications, locale, t])

  return (
    <Card data-testid="console-dead-letters" title={t('shell.console.deadLetters.title')}>
      <Flex vertical gap="middle">
        <Select
          data-testid="console-dead-letters-application"
          aria-label={t('shell.console.deadLetters.application')}
          style={{ maxWidth: 320 }}
          value={application ?? ''}
          options={[
            { value: '', label: t('shell.console.deadLetters.allApplications') },
            ...applications.map((row) => ({ value: row.id, label: row.name })),
          ]}
          onChange={(value) => {
            setApplication(value || undefined)
            setPage(1)
          }}
        />
        {load.failed && !loading ? (
          <AxisAlert
            data-testid="console-dead-letters-error"
            kind="error"
            message={t('shell.console.deadLetters.loadFailed')}
          />
        ) : (
          <AxisTable<ConsoleDeadLetterItem>
            rowKey={(item) => item.id}
            columns={columns}
            dataSource={load.page?.items}
            loading={loading}
            emptyText={t('shell.console.deadLetters.empty')}
            pagination={{
              current: page,
              pageSize: load.page?.pageSize ?? 20,
              total: load.page?.totalCount ?? 0,
            }}
            onChange={(pagination) => setPage(pagination.current ?? 1)}
            rowActions={{
              configured: deadLetterCommands.length,
              actions: (item) =>
                deadLetterCommands
                  .filter((command) => item.commands.includes(command))
                  .map((command) => ({
                    key: command,
                    labelKey: `shell.inspector.commands.${command}`,
                    icon: command === 'redeliver' ? <SendOutlined /> : <DeleteOutlined />,
                    destructive: command === 'discard',
                    onClick: () => setOpen({ command, item }),
                    attributes: { 'data-testid': `console-dead-letter-${command}-${item.id}` },
                  })),
            }}
          />
        )}
      </Flex>
      {open && (
        <ReasonDialog
          title={t(`shell.inspector.commands.${open.command}`)}
          onSend={(reason) => sendDeadLetterCommand(consoleDeadLettersPath, open.item.id, open.command, reason)}
          onClose={() => setOpen(undefined)}
          onChanged={() => setReload((count) => count + 1)}
        />
      )}
    </Card>
  )
}
