import { UndoOutlined } from '@ant-design/icons'
import { Flex, Spin, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useContext, useEffect, useState, type HTMLAttributes } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { QuotaRefusedError } from '../platform/quota'
import {
  changeRows,
  fetchStudioRevision,
  fetchStudioRevisions,
  revisionAuthor,
  revisionClient,
  ServerUnreachableError,
  undoStudioRevision,
  type StudioDiagnostic,
  type StudioReleaseChange,
  type StudioResourceItem,
  type StudioRevision,
  type StudioRevisionChange,
  type StudioRevisionDiff,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { kindSingular } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { StudioPageHeading } from './StudioPageHeading'

/** What an undo left on the page: the later revisions that block it, or why it failed. */
type UndoOutcome = { kind: 'blocked'; revisions: StudioRevision[] } | { kind: 'failed'; message: string }

/** The address of the History view, or of one revision's page. */
function historyAddress(application: string, revision?: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'history', ...(revision ? { revision } : {}) })}`
}

/** A time in the UI locale, to the second, as the release list shows its times. */
function useRevisionTime(): (time: string) => string {
  const locale = useContext(PlatformContext)?.current.locale
  const format = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  return (time) => format.format(new Date(time))
}

/**
 * The History of the open application: one row for each revision, newest first, on one page. A row shows the summary,
 * which opens the revision's page, who made it, when, and the client it came through. Every row but the oldest has an
 * Undo… icon button, off while an undo runs: the oldest is the start of history, which nothing undoes. An undo runs at
 * once. When it succeeds the list reloads and `onUndone` gets the folder's diagnostics. When later revisions changed the
 * same files, a warning names each of them and nothing is undone.
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
  const [revisions, setRevisions] = useState<StudioRevision[] | 'failed'>()
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [outcome, setOutcome] = useState<UndoOutcome | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioRevisions(application, controller.signal).then(setRevisions, (error: unknown) => {
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
  }, [application, folderRevision, generation, reload, lost])

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
      render: (_, row) => (
        <Link data-testid="studio-revision-link" to={historyAddress(application, row.id)}>
          {row.summary}
        </Link>
      ),
    },
    {
      key: 'who',
      kind: 'freeText',
      title: t('shell.studio.history.who'),
      render: (_, row) => revisionAuthor(row, t),
    },
    {
      key: 'when',
      kind: 'fixed',
      title: t('shell.studio.history.when'),
      render: (_, row) => time(row.time),
    },
    {
      key: 'through',
      kind: 'freeText',
      title: t('shell.studio.history.through'),
      render: (_, row) => revisionClient(row, t),
    },
  ]
  const list = Array.isArray(revisions) ? revisions : []
  const oldest = list.at(-1)?.id

  return (
    <section data-testid="studio-history">
      <AxisTable<StudioRevision>
        columns={columns}
        rowKey={(row) => row.id}
        dataSource={list}
        loading={revisions === undefined}
        emptyText={t('shell.studio.history.empty')}
        pagination={false}
        toolbar={{}}
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
            row.id === oldest
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

/** The JSON text of one side of a change, indented when it parses, and as it is when it does not. */
function prettyJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2)
  } catch {
    return text
  }
}

/**
 * The page of one revision: its summary as the heading, who made it, when and through which client under it, and then
 * every resource it added, changed or removed with its name, kind and change, and its JSON before and after side by
 * side. A side where the file does not exist shows the em dash. A revision that cannot be loaded shows one line with a
 * link back to History.
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
      {back}
      <StudioChanges changes={loaded.resources} resources={resources} />
    </Flex>
  )
}

/**
 * Each resource a revision or a draft added, changed or removed: its name, kind and change, then, when given, its
 * summary in words, and its JSON before and after side by side. A side where the file does not exist shows the em dash.
 * Proposed: a draft's page gives each file the summary its request wrote, see the review on #3263.
 */
export function StudioChanges({
  changes,
  resources,
  summaries,
}: {
  changes: StudioRevisionChange[]
  resources: StudioResourceItem[]
  /** The summary in words of each file, by its file. */
  summaries?: ReadonlyMap<string, string>
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, radius, spacing, type } = useDesignTokens()
  const rows = changes.map((resource): StudioReleaseChange => ({
    path: resource.file,
    change: resource.change,
    kind: resource.kind,
    name: resource.name,
    titles: null,
    locale: null,
  }))
  const byKey = new Map(changes.map((resource) => [`${resource.change}:${resource.file}`, resource]))
  const block = (testId: string, title: string, text: string | null) => (
    <Flex vertical gap={spacing.xs} style={{ minWidth: 0 }}>
      <Typography.Text style={{ color: colour.textMuted }}>{title}</Typography.Text>
      <pre
        data-testid={testId}
        style={{
          margin: 0,
          padding: spacing.sm,
          overflow: 'auto',
          fontFamily: type.codeFontFamily,
          background: colour.surface,
          border: `1px solid ${colour.borderSubtle}`,
          borderRadius: radius.base,
        }}
      >
        {text === null ? <EmptyDash /> : prettyJson(text)}
      </pre>
    </Flex>
  )

  return (
    <>
      {changeRows(rows, resources, locale, t).map((row) => {
        const resource = byKey.get(row.key)
        const summary = resource === undefined ? undefined : summaries?.get(resource.file)
        return (
          <section key={row.key} data-testid="studio-revision-change" data-file={resource?.file}>
            <Flex vertical gap="small">
              <Flex wrap gap="small" align="baseline">
                <Typography.Text strong>{row.name}</Typography.Text>
                <Typography.Text style={{ color: colour.textMuted }}>
                  {row.kind === null ? '—' : kindSingular(row.kind, t)}
                </Typography.Text>
                <Typography.Text>{t(`shell.studio.overview.change.${row.change}`)}</Typography.Text>
              </Flex>
              {summary !== undefined && <Typography.Text>{summary}</Typography.Text>}
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: spacing.md }}>
                {block('studio-revision-before', t('shell.studio.history.before'), resource?.before ?? null)}
                {block('studio-revision-after', t('shell.studio.history.after'), resource?.after ?? null)}
              </div>
            </Flex>
          </section>
        )
      })}
    </>
  )
}
