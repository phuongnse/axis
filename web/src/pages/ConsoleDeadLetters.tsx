import { DeleteOutlined, SendOutlined } from '@ant-design/icons'
import { Alert, Card, Flex, Select, Table, Tag, type TableColumnsType } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import {
  consoleDeadLettersPath,
  fetchConsoleDeadLetters,
  type ConsoleApplication,
  type ConsoleDeadLetterItem,
  type ConsoleDeadLetterPage,
} from '../platform/console'
import { formatValue } from '../platform/formatValue'
import { deadLetterCommands, forbidden, sendDeadLetterCommand, type DeadLetterCommand } from '../platform/inspection'
import { markCurrentPage } from '../platform/pagination'
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

  const columns = useMemo<TableColumnsType<ConsoleDeadLetterItem>>(() => {
    const names = new Map(applications.map((row) => [row.id, row.name]))
    return [
      {
        key: 'application',
        title: t('shell.console.deadLetters.application'),
        render: (_value: unknown, item) => names.get(item.applicationId) ?? item.applicationId,
      },
      { key: 'kind', title: t('shell.inspector.deadLetters.kind'), render: (_value: unknown, item) => item.kind },
      { key: 'event', title: t('shell.console.deadLetters.event'), render: (_value: unknown, item) => item.eventId },
      {
        key: 'target',
        title: t('shell.console.deadLetters.target'),
        render: (_value: unknown, item) => item.triggerId ?? item.webhookId,
      },
      {
        key: 'attempts',
        title: t('shell.inspector.deadLetters.attempts'),
        render: (_value: unknown, item) => item.attempts,
      },
      {
        key: 'lastError',
        title: t('shell.inspector.deadLetters.lastError'),
        render: (_value: unknown, item) => item.lastError,
      },
      {
        key: 'state',
        title: t('shell.console.deadLetters.state'),
        render: (_value: unknown, item) => <Tag>{t(`shell.console.deadLetters.state.${item.state}`)}</Tag>,
      },
      {
        key: 'createdAt',
        title: t('shell.inspector.deadLetters.createdAt'),
        render: (_value: unknown, item) => formatValue(createdAtField, item.createdAt, {}, { locale, text: t }),
      },
      {
        key: 'commands',
        render: (_value: unknown, item) => (
          <Flex gap="small">
            {deadLetterCommands
              .filter((command) => item.commands.includes(command))
              .map((command) => (
                <AxisButton
                  key={command}
                  role="icon"
                  nameKey={`shell.inspector.commands.${command}`}
                  destructive={command === 'discard'}
                  icon={command === 'redeliver' ? <SendOutlined /> : <DeleteOutlined />}
                  data-testid={`console-dead-letter-${command}-${item.id}`}
                  onClick={() => setOpen({ command, item })}
                />
              ))}
          </Flex>
        ),
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
          <Alert
            data-testid="console-dead-letters-error"
            type="error"
            title={t('shell.console.deadLetters.loadFailed')}
          />
        ) : (
          <Table<ConsoleDeadLetterItem>
            rowKey="id"
            columns={columns}
            scroll={{ x: 'max-content' }}
            dataSource={load.page?.items}
            loading={loading}
            locale={{ emptyText: t('shell.console.deadLetters.empty') }}
            pagination={{
              current: page,
              pageSize: load.page?.pageSize,
              total: load.page?.totalCount ?? 0,
              showSizeChanger: false,
              hideOnSinglePage: true,
              itemRender: markCurrentPage(page),
            }}
            onChange={(pagination) => setPage(pagination.current ?? 1)}
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
