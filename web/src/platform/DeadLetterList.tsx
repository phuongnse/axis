import { Alert, Button, Flex, Table, Typography, type TableColumnsType } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { formatValue } from './formatValue'
import {
  deadLetterCommands,
  fetchDeadLetters,
  forbidden,
  sendDeadLetterCommand,
  type DeadLetterCommand,
  type DeadLetterItem,
  type DeadLetterPage,
} from './inspection'
import { markCurrentPage } from './pagination'
import { ReasonDialog } from './ReasonDialog'
import { useText } from './texts'

interface DeadLetterListProps {
  deadLettersPath: string
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The request URL, so a result for an older request shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: DeadLetterPage
  failed: boolean
}

/** The command a user opened the dialog for, and the dead letter it is for. */
interface OpenCommand {
  command: DeadLetterCommand
  item: DeadLetterItem
}

const createdAtField = { name: 'createdAt', type: 'date-time' } as const

/**
 * Lists the open dead letters of a process, newest first, under the instance list. A row has a
 * button for each command the server lists in the item's `commands`, and the widget never decides
 * who may take one. A button opens the reason dialog, and a success reads the list again. The page
 * number lives in the component, because the URL's page belongs to the instance list. A failure,
 * a 403 included, shows an error in this section only.
 */
export function DeadLetterList({ deadLettersPath, locale }: DeadLetterListProps) {
  const t = useText()
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [open, setOpen] = useState<OpenCommand>()
  const [load, setLoad] = useState<LoadState>({ failed: false })
  const query = `state=open${page > 1 ? `&page=${page}` : ''}`
  const loadKey = `${deadLettersPath}?${query}#${reload}`
  const loading = load.url !== loadKey

  useEffect(() => {
    const controller = new AbortController()
    fetchDeadLetters(deadLettersPath, query, controller.signal)
      .then((result) =>
        setLoad(result === forbidden ? { url: loadKey, failed: true } : { url: loadKey, page: result, failed: false }),
      )
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the dead letters of '${deadLettersPath}' failed`, error)
        setLoad({ url: loadKey, failed: true })
      })
    return () => controller.abort()
  }, [deadLettersPath, page, query, loadKey])

  const columns = useMemo<TableColumnsType<DeadLetterItem>>(
    () => [
      { key: 'kind', title: t('shell.inspector.deadLetters.kind'), render: (_value: unknown, item) => item.kind },
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
                <Button
                  key={command}
                  data-testid={`inspector-dead-letter-${command}-${item.id}`}
                  onClick={() => setOpen({ command, item })}
                >
                  {t(`shell.inspector.commands.${command}`)}
                </Button>
              ))}
          </Flex>
        ),
      },
    ],
    [locale, t],
  )

  return (
    <section>
      <Flex vertical gap="small">
        <Typography.Title level={5}>{t('shell.inspector.deadLetters.title')}</Typography.Title>
        {load.failed && !loading ? (
          <Alert
            data-testid="inspector-dead-letters-error"
            type="error"
            title={t('shell.inspector.deadLetters.loadFailed')}
          />
        ) : (
          <Table<DeadLetterItem>
            rowKey="id"
            columns={columns}
            scroll={{ x: 'max-content' }}
            dataSource={load.page?.items}
            loading={loading}
            locale={{ emptyText: t('shell.inspector.deadLetters.empty') }}
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
          onSend={(reason) => sendDeadLetterCommand(deadLettersPath, open.item.id, open.command, reason)}
          onClose={() => setOpen(undefined)}
          onChanged={() => setReload((count) => count + 1)}
        />
      )}
    </section>
  )
}
