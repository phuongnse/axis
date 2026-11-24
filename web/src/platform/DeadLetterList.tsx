import { SendOutlined } from '@ant-design/icons'
import { AxisAlert } from './AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from './AxisTable'
import type { AxisAction } from './menuActions'
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
import { ReasonDialog } from './ReasonDialog'
import { SectionPart } from './SectionParts'
import { recordPageSize, recordQuery } from './tableQuery'
import { useText } from './texts'
import { useTableView } from './useTableView'

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

// The route's fields in column order, see docs/reference/process-events.md#routes. useTableView needs stable arrays.
const deadLetterFields = [
  { name: 'kind', type: 'text' },
  { name: 'attempts', type: 'integer' },
  { name: 'lastError', type: 'text' },
  { name: 'createdAt', type: 'date' },
] as const
// The list has no parameter of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/**
 * Lists the open dead letters of a process, newest first, under the instance list. The table has Filter on Kind,
 * Attempts, Last error and Created and a pager, and the server applies the filter. The view lives in the address
 * under the `deadLetters.` prefix, because the instance list keeps the plain names, see
 * docs/reference/process-inspector-widget.md#dead-letters and docs/reference/tables.md#tables. A row has a
 * button for each command the server lists in the item's `commands`, and the widget never decides who may take one.
 * A button opens the reason dialog, and a success reads the list again. A failure, a 403 included, shows an error in
 * this section only.
 */
export function DeadLetterList({ deadLettersPath, locale }: DeadLetterListProps) {
  const t = useText()
  const { view, setView } = useTableView(
    deadLetterFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    'deadLetters.',
  )
  const [reload, setReload] = useState(0)
  const [open, setOpen] = useState<OpenCommand>()
  const [load, setLoad] = useState<LoadState>({ failed: false })
  const filterQuery = recordQuery(view)
  const query = filterQuery ? `state=open&${filterQuery}` : 'state=open'
  const loadKey = `${deadLettersPath}?${query}#${reload}`
  const loading = load.url !== loadKey
  const totalCount = loading ? undefined : load.page?.totalCount

  // A page past the end, such as after the last row of the last page is settled, becomes the last page once the
  // total is known, so the address and the pager agree.
  useEffect(() => {
    if (totalCount === undefined) {
      return
    }
    const lastPage = Math.max(1, Math.ceil(totalCount / view.pageSize))
    if (view.page > lastPage) {
      setView({ ...view, page: lastPage }, { replace: true })
    }
  }, [totalCount, view, setView])

  useEffect(() => {
    const controller = new AbortController()
    fetchDeadLetters(deadLettersPath, query, controller.signal)
      .then((result) => {
        if (result === forbidden) {
          setLoad({ url: loadKey, failed: true })
          return
        }
        setLoad({ url: loadKey, page: result, failed: false })
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the dead letters of '${deadLettersPath}' failed`, error)
        setLoad({ url: loadKey, failed: true })
      })
    return () => controller.abort()
  }, [deadLettersPath, query, loadKey])

  const columns = useMemo<AxisColumn<DeadLetterItem>[]>(
    () => [
      {
        key: 'kind',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.kind'),
        filter: { kind: 'text' },
        render: (_value, item) => item.kind,
      },
      {
        key: 'attempts',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.attempts'),
        filter: { kind: 'number' },
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
        key: 'createdAt',
        kind: 'fixed',
        title: t('shell.inspector.deadLetters.createdAt'),
        filter: { kind: 'date' },
        render: (_value, item) => formatValue(createdAtField, item.createdAt, {}, { locale, text: t }),
      },
    ],
    [locale, t],
  )

  return (
    <>
      <SectionPart testId="inspector-dead-letters" heading={{ title: t('shell.inspector.deadLetters.title') }}>
        {load.failed && !loading ? (
          <AxisAlert
            data-testid="inspector-dead-letters-error"
            kind="error"
            message={t('shell.inspector.deadLetters.loadFailed')}
          />
        ) : (
          <AxisTable<DeadLetterItem>
            rowKey={(item) => item.id}
            columns={columns}
            dataSource={load.page?.items}
            loading={loading}
            emptyText={t('shell.inspector.deadLetters.empty')}
            view={{ query: view, onChange: setView, total: load.page?.totalCount ?? 0 }}
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
                    attributes: { 'data-testid': `inspector-dead-letter-${command}-${item.id}` },
                  })),
            }}
          />
        )}
      </SectionPart>
      {open && (
        <ReasonDialog
          title={t(`shell.inspector.commands.${open.command}`)}
          onSend={(reason) => sendDeadLetterCommand(deadLettersPath, open.item.id, open.command, reason)}
          onClose={() => setOpen(undefined)}
          onChanged={() => setReload((count) => count + 1)}
        />
      )}
    </>
  )
}
