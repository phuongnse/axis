import { UndoOutlined } from '@ant-design/icons'
import { Flex, Spin, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useContext, useEffect, useState, type HTMLAttributes } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { QuotaRefusedError } from '../platform/quota'
import {
  fetchStudioRevision,
  fetchStudioRevisions,
  revisionAuthor,
  revisionClient,
  ServerUnreachableError,
  undoStudioRevision,
  type StudioDiagnostic,
  type StudioResourceItem,
  type StudioRevision,
  type StudioRevisionDiff,
  type StudioRevisionList,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { PlatformContext } from './context'
import { StudioChanges } from './StudioChanges'
import { StudioPageHeading } from './StudioPageHeading'

/** What an undo left on the page: the later revisions that block it, or why it failed. */
type UndoOutcome = { kind: 'blocked'; revisions: StudioRevision[] } | { kind: 'failed'; message: string }

/** The address of the History view, or of one revision's page. */
function historyAddress(application: string, revision?: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'history', ...(revision ? { revision } : {}) })}`
}

/** The columns the view's search, filter and sort can name, see docs/reference/authoring-revisions.md#list. */
const revisionFields = [
  { name: 'summary', type: 'text' },
  { name: 'author', type: 'text' },
  { name: 'time', type: 'date' },
  { name: 'client', type: 'text' },
] as const
// History has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/** A time in the UI locale, to the second, as the release list shows its times. */
function useRevisionTime(): (time: string) => string {
  const locale = useContext(PlatformContext)?.current.locale
  const format = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  return (time) => format.format(new Date(time))
}

/**
 * The History of the open application: one row for each revision, newest first unless the view sorts them, paged on the
 * server. A row shows the summary, which opens the revision's page, who made it, when, and the client it came through.
 * The table has Search, Filter, Columns, Refresh, a sort on Who, When and Through and a pager with a page size, all kept
 * in the address, see docs/reference/actions-and-controls.md#tables and docs/reference/authoring-revisions.md#list.
 * Every row but the start of history, which the server names, has an Undo… icon button, off while an undo runs: nothing
 * undoes the start of history. An undo runs at once. When it succeeds the list reloads and `onUndone` gets the folder's
 * diagnostics. When later revisions changed the same files, a warning names each of them and nothing is undone.
 */
export function StudioHistory({
  application,
  folderRevision,
  onUndone,
}: {
  application: string
  folderRevision: number
  onUndone: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const time = useRevisionTime()
  const { generation, lost } = useStudioConnection()
  const { view, setView } = useTableView(revisionFields, recordPageSize, noParameters, true, true, true)
  const query = recordQuery(view)
  const [revisions, setRevisions] = useState<StudioRevisionList | 'failed'>()
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [outcome, setOutcome] = useState<UndoOutcome | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioRevisions(application, query, controller.signal).then(setRevisions, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the revisions of '${application}' failed`, error)
      setRevisions('failed')
    })
    return () => controller.abort()
  }, [application, query, folderRevision, generation, reload, lost])

  async function undo(revision: StudioRevision) {
    setBusy(true)
    setOutcome(null)
    try {
      const result = await undoStudioRevision(application, revision.id)
      if (result.status === 'undone') {
        onUndone(result.diagnostics)
        setReload((value) => value + 1)
      } else if (result.status === 'blocked') {
        setOutcome({ kind: 'blocked', revisions: result.revisions })
      } else {
        setOutcome({ kind: 'failed', message: t('shell.studio.history.startOfHistory') })
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        setOutcome({
          kind: 'failed',
          message: error instanceof QuotaRefusedError ? error.message : t('shell.studio.history.undoFailed'),
        })
      }
    } finally {
      setBusy(false)
    }
  }

  const columns: AxisColumn<StudioRevision>[] = [
    {
      key: 'summary',
      kind: 'freeText',
      title: t('shell.studio.history.summary'),
      filter: { kind: 'text' },
      render: (_, row) => (
        <Link data-testid="studio-revision-link" to={historyAddress(application, row.id)}>
          {row.summary}
        </Link>
      ),
    },
    {
      key: 'author',
      kind: 'freeText',
      title: t('shell.studio.history.who'),
      filter: { kind: 'text' },
      sorter: true,
      render: (_, row) => revisionAuthor(row, t),
    },
    {
      key: 'time',
      kind: 'fixed',
      title: t('shell.studio.history.when'),
      filter: { kind: 'date' },
      sorter: true,
      render: (_, row) => time(row.time),
    },
    {
      key: 'client',
      kind: 'freeText',
      title: t('shell.studio.history.through'),
      filter: { kind: 'text' },
      sorter: true,
      render: (_, row) => revisionClient(row, t),
    },
  ]
  const list = typeof revisions === 'object' ? revisions : undefined

  return (
    <section data-testid="studio-history">
      <AxisTable<StudioRevision>
        columns={columns}
        rowKey={(row) => row.id}
        dataSource={list?.items ?? []}
        loading={revisions === undefined}
        emptyText={t('shell.studio.history.empty')}
        view={{
          query: view,
          onChange: setView,
          pageSizes: tablePageSizes(),
          total: list?.totalCount ?? 0,
          search: true,
          columns: true,
        }}
        toolbar={{ onRefresh: () => setReload((value) => value + 1) }}
        failure={
          revisions === 'failed' ? (
            <AxisAlert data-testid="studio-history-error" kind="error" message={t('shell.studio.history.loadFailed')} />
          ) : undefined
        }
        below={
          outcome === null ? undefined : outcome.kind === 'blocked' ? (
            <AxisAlert
              data-testid="studio-history-blocked"
              kind="warning"
              message={t('shell.studio.history.blocked')}
              description={
                <Flex vertical>
                  {outcome.revisions.map((blocker) => (
                    <Flex key={blocker.id} vertical data-testid="studio-history-blocker" data-id={blocker.id}>
                      <Typography.Text>{blocker.summary}</Typography.Text>
                      <Typography.Text>{revisionAuthor(blocker, t)}</Typography.Text>
                      <Typography.Text>{time(blocker.time)}</Typography.Text>
                    </Flex>
                  ))}
                </Flex>
              }
            />
          ) : (
            <AxisAlert data-testid="studio-history-failed" kind="error" message={outcome.message} />
          )
        }
        rowAttributes={(row) =>
          ({ 'data-testid': 'studio-revision-row', 'data-id': row.id }) as HTMLAttributes<HTMLElement>
        }
        rowActions={{
          configured: 1,
          actions: (row) =>
            row.id === list?.start
              ? null
              : [
                  {
                    key: 'undo',
                    labelKey: 'shell.studio.history.undo',
                    nameSuffix: row.summary,
                    icon: <UndoOutlined />,
                    disabled: busy,
                    onClick: () => void undo(row),
                  },
                ],
        }}
      />
    </section>
  )
}

/**
 * The page of one revision: its summary as the heading, who made it, when and through which client under it, and then
 * every resource it added, changed or removed as `StudioChanges` draws it. A loaded revision has no
 * link back, because the sidebar's marked History entry and the browser's Back lead back, see docs/reference/studio.md. A revision that
 * cannot be loaded shows one line with a link to History.
 */
export function StudioRevisionPage({
  application,
  revisionId,
  resources,
}: {
  application: string
  revisionId: string
  resources: StudioResourceItem[]
}) {
  const t = useText()
  const time = useRevisionTime()
  const { generation, lost } = useStudioConnection()
  const [diff, setDiff] = useState<{ revisionId: string; value: StudioRevisionDiff | null }>()

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioRevision(application, revisionId, controller.signal).then(
      (value) => setDiff({ revisionId, value }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the revision '${revisionId}' of '${application}' failed`, error)
        setDiff({ revisionId, value: null })
      },
    )
    return () => controller.abort()
  }, [application, revisionId, generation, lost])

  const back = <Link to={historyAddress(application)}>{t('shell.studio.history.back')}</Link>
  const loaded = diff?.revisionId === revisionId ? diff.value : undefined
  if (diff?.revisionId !== revisionId) {
    return <Spin />
  }
  if (loaded === null || loaded === undefined) {
    return (
      <Flex vertical gap="small" data-testid="studio-revision-not-found">
        <Typography.Text>{t('shell.studio.history.notFound')}</Typography.Text>
        {back}
      </Flex>
    )
  }

  const { revision } = loaded
  const line = [revisionAuthor(revision, t), time(revision.time), revisionClient(revision, t)].filter(
    (part): part is string => part !== null,
  )
  return (
    <Flex vertical gap="middle" data-testid="studio-revision">
      <StudioPageHeading title={revision.summary} line={line} />
      <StudioChanges files={loaded.resources} resources={resources} />
    </Flex>
  )
}
