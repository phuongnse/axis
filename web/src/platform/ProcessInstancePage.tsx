import { Button, Descriptions, Flex, Typography } from 'antd'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useLinkClickHandler } from 'react-router'
import { AxisTable, type AxisColumn } from './AxisTable'
import { CopyableId } from './CopyableId'
import { EmptyState } from './EmptyState'
import { NotSet } from './EmptyValue'
import { fillText } from './fillText'
import { formatDuration, formatValue } from './formatValue'
import {
  attemptKinds,
  attemptOutcomes,
  fetchInstance,
  forbidden,
  instanceCommands,
  callStateText,
  callStateTone,
  instanceStates,
  problemText,
  processTitle,
  retryWarns,
  sendCommand,
  startKindText,
  stateText,
  stateTone,
  taskStates,
  type CallAttempt,
  type InstanceCommand,
  type InstanceCall,
  type InstanceDetail,
  type InstanceTask,
  type PathEntry,
  type SubProcess,
} from './inspection'
import { inspectorPath, useInspectorPages } from './inspectorPages'
import { PageActions, PageLayout, type BreadcrumbStep } from './PageLayout'
import { PersonName } from './PersonName'
import { RecordLink } from './RecordLink'
import { RecordNotFound } from './RecordNotFound'
import { ReasonDialog } from './ReasonDialog'
import { ResolveDialog } from './ResolveDialog'
import { StatusTag } from './StatusTag'
import { SectionPart, SectionParts } from './SectionParts'
import { studioResourceAddress } from './studioAddress'
import { TermList, type Term } from './TermList'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'
import { localDay } from './tableFilter'
import { recordPageSize } from './tableQuery'
import { useText, useTextCatalogs } from './texts'
import { useDesignTokens } from './themeMode'
import { useReleaseTexts } from './useReleaseTexts'
import { useTableView } from './useTableView'

interface ProcessInstancePageProps {
  sitePath: string
  /** The names of the site's navigation pages, in navigation order, for the links to other inspector pages. */
  pages: readonly string[]
  /** The process inspector widget of the page the instance's address is under. */
  widget: WidgetMetadata
  instanceId: string
  /** The page's title. It heads the page while the instance loads and when the instance is not found. */
  title: string
  /** The UI locale, for formatting the times. */
  locale: string
  /** The site's fallback locale, for the texts of the instance's release. */
  fallbackLocale: string
}

/** The instance read, `null` when the process has none with the id, or why it could not be shown. */
type InstanceState = InstanceDetail | null | typeof forbidden | 'failed'

const dateTime = (name: string) => ({ name, type: 'date-time' }) as const

// The tables filter the columns that they show. Stable arrays keep useTableView from recomputing on each render.
const historyFields = [
  { name: 'step', type: 'text' },
  { name: 'startedAt', type: 'date' },
  { name: 'finishedAt', type: 'date' },
  { name: 'decision', type: 'text' },
  { name: 'error', type: 'text' },
  { name: 'traceId', type: 'text' },
] as const
const taskFields = [
  { name: 'step', type: 'text' },
  { name: 'assignee', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'outcome', type: 'text' },
  { name: 'completedBy', type: 'text' },
  { name: 'createdAt', type: 'date' },
  { name: 'completedAt', type: 'date' },
] as const
const attemptFields = [
  { name: 'kind', type: 'enum' },
  { name: 'number', type: 'integer' },
  { name: 'startedAt', type: 'date' },
  { name: 'finishedAt', type: 'date' },
  { name: 'outcome', type: 'enum' },
  { name: 'httpStatus', type: 'integer' },
  { name: 'error', type: 'text' },
  { name: 'traceId', type: 'text' },
] as const
const subProcessFields = [
  { name: 'process', type: 'text' },
  { name: 'step', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'createdAt', type: 'date' },
  { name: 'endedAt', type: 'date' },
] as const
// The tables have no parameters of their own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/** The options of an enum column that shows the API's word as it is. */
const wordOptions = (words: readonly string[]) => words.map((word) => ({ value: word, label: word }))

/**
 * Shows one instance under the page of its process inspector: its heading with the process's title and state, its
 * facts, the steps of the release it is pinned to with the path marked, the history, the tasks, the calls with their
 * attempts and the sub-processes. A failed instance has a banner that names the step it stopped at. The parent
 * and each sub-process link to their instance on the first navigation page whose process inspector
 * names their process, and show without a link when there is none. A step the path holds is marked as taken, and the step the instance is at is marked as
 * current. A call whose input is hidden shows the fixed "hidden" text and no value.
 *
 * The History, Tasks and Sub-processes tables and each call's Attempts table hold all their rows, show them with no
 * pager and have Filter on every column but the Open link. Each keeps its filter in the address under a prefix of its
 * own: `history.`, `tasks.`, `subProcesses.` and `attempts.{callId}.`, and a date filters on the local day of its time,
 * see docs/reference/process-inspector-widget.md#instance-page.
 *
 * The heading takes the process's title from the texts of the instance's release, so it shows `title` until they load.
 * Step labels come from the same texts, then the site texts and the platform
 * texts. A step without a label shows its name. If the release texts fail to load, the page shows
 * its load error. A missing instance keeps the heading and shows the not-found line.
 */
export function ProcessInstancePage({
  sitePath,
  pages,
  widget,
  instanceId,
  title,
  locale,
  fallbackLocale,
}: ProcessInstancePageProps) {
  const t = useText()
  // The server sets the path on every process inspector.
  const instancesPath = widget.instancesPath!
  const [instance, setInstance] = useState<InstanceState>()
  const outer = useTextCatalogs()
  const releaseIds = useMemo(() => (isInstanceDetail(instance) ? [instance.releaseId] : []), [instance])
  const releaseTexts = useReleaseTexts(sitePath, releaseIds, locale, fallbackLocale)
  const releaseCatalog = isInstanceDetail(instance) ? releaseTexts.catalogs.get(instance.releaseId) : undefined
  const catalogs = useMemo(
    () => (releaseCatalog ? [releaseCatalog, ...outer.catalogs] : outer.catalogs),
    [releaseCatalog, outer.catalogs],
  )

  const hasRelatives = isInstanceDetail(instance) && (instance.parent !== null || instance.subProcesses.length > 0)
  const inspectors = useInspectorPages(sitePath, pages, hasRelatives)

  useEffect(() => {
    const controller = new AbortController()
    fetchInstance(instancesPath, instanceId, controller.signal)
      .then(setInstance)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the instance '${instanceId}' failed`, error)
          setInstance('failed')
        }
      })
    return () => controller.abort()
  }, [instancesPath, instanceId])

  // After a command, the instance is read again. The page keeps showing the old one until it arrives.
  const reload = useCallback(() => {
    fetchInstance(instancesPath, instanceId)
      .then(setInstance)
      .catch((error: unknown) => {
        console.warn(`Loading the instance '${instanceId}' failed`, error)
        setInstance('failed')
      })
  }, [instancesPath, instanceId])

  // The previous release texts stay on screen while a new locale loads. The heading takes the title once they are in.
  const detail = isInstanceDetail(instance) && releaseCatalog ? instance : undefined
  let body: ReactNode = null
  if (instance === null) {
    body = <RecordNotFound textKey="shell.inspector.notFound" />
  } else if (instance === forbidden) {
    body = <AxisAlert data-testid="inspector-forbidden" kind="warning" message={t('shell.inspector.forbidden')} />
  } else if (instance === 'failed' || releaseTexts.failed) {
    body = <AxisAlert data-testid="inspector-error" kind="error" message={t('shell.inspector.loadFailed')} />
  } else if (detail) {
    body = (
      <InstanceDetailView
        instance={detail}
        instancesPath={instancesPath}
        sitePath={sitePath}
        inspectors={inspectors}
        locale={locale}
        onChanged={reload}
      />
    )
  }

  return (
    <TextProvider catalogs={catalogs} development={outer.development}>
      <InstanceLayout instance={detail} title={title}>
        {body}
      </InstanceLayout>
    </TextProvider>
  )
}

interface InstanceLayoutProps {
  /** The instance, whose process title, state and line head the page. Without it the page heads with `title`. */
  instance?: InstanceDetail
  /** The page's title, which heads the page without an instance. */
  title: string
  /** The steps above the heading, for a page that is in a site's navigation or the operator console. */
  breadcrumb?: readonly BreadcrumbStep[]
  children?: ReactNode
}

/**
 * The heading of an instance's page, shared by a site's page and the operator console's page, see
 * docs/reference/process-inspector-widget.md#instance-page: the process's title, its state as a tag beside it, and
 * Process as its line. Without an instance the heading is the given title, with no tag.
 */
export function InstanceLayout({ instance, title, breadcrumb, children }: InstanceLayoutProps) {
  const t = useText()
  return (
    <PageLayout
      title={instance ? processTitle(t, instance.processTitleKey, instance.process) : title}
      tags={instance && <StatusTag tone={stateTone(instance.state)}>{stateText(t, instance.state)}</StatusTag>}
      line={t('shell.inspector.process')}
      breadcrumb={breadcrumb}
    >
      {children}
    </PageLayout>
  )
}

function isInstanceDetail(instance: InstanceState | undefined): instance is InstanceDetail {
  return typeof instance === 'object' && instance !== null
}

/** The top-level properties of a call's input as text. Objects are JSON, and numbers keep their digits. */
function inputEntries(input: unknown): [string, string][] {
  if (typeof input !== 'object' || input === null || Array.isArray(input)) {
    return []
  }
  return Object.entries(input).map(([key, value]) => [
    key,
    typeof value === 'object' && value !== null ? JSON.stringify(value) : String(value),
  ])
}

interface InstanceDetailViewProps {
  instance: InstanceDetail
  instancesPath: string
  sitePath: string
  /** The inspector page of each process, once loaded. Without it, the parent and the sub-processes show without links. */
  inspectors: ReadonlyMap<string, string> | undefined
  locale: string
  /** Called after a command was accepted, so the page reads the instance again. */
  onChanged: () => void
  /** Shows the name of a step and not its label, for a page outside every site, which has no release texts. */
  stepNames?: boolean
}

/** The sections of an instance. It reads the release texts, unless `stepNames` is set. */
export function InstanceDetailView({
  instance,
  instancesPath,
  sitePath,
  inspectors,
  locale,
  onChanged,
  stepNames = false,
}: InstanceDetailViewProps) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const historyView = useTableView(historyFields, recordPageSize, noParameters, false, true, false, 'history.')
  const taskView = useTableView(taskFields, recordPageSize, noParameters, false, true, false, 'tasks.')
  const subProcessView = useTableView(
    subProcessFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    'subProcesses.',
  )
  const rowStyle = { paddingBlock: spacing.sm, borderBlockEnd: `1px solid ${colour.divider}` }
  const stepsByName = useMemo(
    () => new Map(instance.graph.steps.map((step) => [step.name, step])),
    [instance.graph.steps],
  )
  const stepLabel = (name: string) => {
    const labelKey = stepNames ? undefined : stepsByName.get(name)?.labelKey
    return labelKey ? t(labelKey) : name
  }
  const time = (name: string, value: string | null) =>
    value === null ? null : formatValue(dateTime(name), value, {}, { locale, text: t })

  const parentPage = instance.parent === null ? undefined : inspectors?.get(instance.parent.process)

  const traceIdColumn = {
    key: 'traceId',
    kind: 'fixed' as const,
    title: t('shell.inspector.traceId'),
    filter: { kind: 'text' as const, value: (row: { traceId: string | null }) => row.traceId },
    render: (_v: unknown, row: { traceId: string | null }) =>
      row.traceId === null ? null : <CopyableId id={row.traceId} />,
  }
  const historyColumns: AxisColumn<PathEntry>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.inspector.step'),
      render: (_v, row) => stepLabel(row.step),
      filter: { kind: 'text', value: (row) => stepLabel(row.step) },
    },
    {
      key: 'startedAt',
      kind: 'fixed',
      title: t('shell.inspector.startedAt'),
      render: (_v, row) => time('startedAt', row.startedAt),
      filter: { kind: 'date', value: (row) => localDay(row.startedAt) },
    },
    {
      key: 'finishedAt',
      kind: 'fixed',
      title: t('shell.inspector.finishedAt'),
      render: (_v, row) => time('finishedAt', row.finishedAt),
      filter: { kind: 'date', value: (row) => localDay(row.finishedAt) },
    },
    {
      key: 'decision',
      kind: 'fixed',
      title: t('shell.inspector.decision'),
      render: (_v, row) => row.decision ?? null,
      filter: { kind: 'text', value: (row) => row.decision ?? null },
    },
    {
      key: 'error',
      kind: 'freeText',
      title: t('shell.inspector.error'),
      render: (_v, row) => problemText(t, locale, row.problem, row.error),
      filter: { kind: 'text', value: (row) => problemText(t, locale, row.problem, row.error) },
    },
    traceIdColumn,
  ]
  const taskColumns: AxisColumn<InstanceTask>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.inspector.step'),
      render: (_v, row) => stepLabel(row.step),
      filter: { kind: 'text', value: (row) => stepLabel(row.step) },
    },
    {
      key: 'assignee',
      kind: 'freeText',
      title: t('shell.inspector.assignee'),
      render: (_v, row) => `${row.assigneeKind}: ${row.assignee}`,
      filter: { kind: 'text', value: (row) => `${row.assigneeKind}: ${row.assignee}` },
    },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.inspector.state'),
      render: (_v, row) => row.state,
      filter: { kind: 'enum', value: (row) => row.state, options: wordOptions(taskStates) },
    },
    {
      key: 'outcome',
      kind: 'fixed',
      title: t('shell.inspector.outcome'),
      render: (_v, row) => row.outcome ?? null,
      filter: { kind: 'text', value: (row) => row.outcome ?? null },
    },
    {
      key: 'completedBy',
      kind: 'freeText',
      title: t('shell.inspector.completedBy'),
      render: (_v, row) => row.completedBy ?? null,
      filter: { kind: 'text', value: (row) => row.completedBy ?? null },
    },
    {
      key: 'createdAt',
      kind: 'fixed',
      title: t('shell.inspector.createdAt'),
      render: (_v, row) => time('createdAt', row.createdAt),
      filter: { kind: 'date', value: (row) => localDay(row.createdAt) },
    },
    {
      key: 'completedAt',
      kind: 'fixed',
      title: t('shell.inspector.completedAt'),
      render: (_v, row) => time('completedAt', row.completedAt),
      filter: { kind: 'date', value: (row) => localDay(row.completedAt) },
    },
  ]
  const attemptColumns: AxisColumn<CallAttempt>[] = [
    {
      key: 'kind',
      kind: 'fixed',
      title: t('shell.inspector.kind'),
      render: (_v, row) => row.kind,
      filter: { kind: 'enum', value: (row) => row.kind, options: wordOptions(attemptKinds) },
    },
    {
      key: 'number',
      kind: 'fixed',
      title: t('shell.inspector.number'),
      render: (_v, row) => row.number,
      filter: { kind: 'number', value: (row) => row.number },
    },
    {
      key: 'startedAt',
      kind: 'fixed',
      title: t('shell.inspector.startedAt'),
      render: (_v, row) => time('startedAt', row.startedAt),
      filter: { kind: 'date', value: (row) => localDay(row.startedAt) },
    },
    {
      key: 'finishedAt',
      kind: 'fixed',
      title: t('shell.inspector.finishedAt'),
      render: (_v, row) => time('finishedAt', row.finishedAt),
      filter: { kind: 'date', value: (row) => localDay(row.finishedAt) },
    },
    {
      key: 'outcome',
      kind: 'fixed',
      title: t('shell.inspector.outcome'),
      render: (_v, row) => row.outcome,
      filter: { kind: 'enum', value: (row) => row.outcome, options: wordOptions(attemptOutcomes) },
    },
    {
      key: 'httpStatus',
      kind: 'fixed',
      title: t('shell.inspector.httpStatus'),
      render: (_v, row) => row.httpStatus ?? null,
      filter: { kind: 'number', value: (row) => row.httpStatus ?? null },
    },
    {
      key: 'error',
      kind: 'freeText',
      title: t('shell.inspector.error'),
      render: (_v, row) => problemText(t, locale, row.problem, row.error),
      filter: { kind: 'text', value: (row) => problemText(t, locale, row.problem, row.error) },
    },
    traceIdColumn,
  ]

  const subProcessColumns: AxisColumn<SubProcess>[] = [
    {
      key: 'process',
      kind: 'freeText',
      title: t('shell.inspector.process'),
      render: (_v, row) => processTitle(t, row.processTitleKey, row.process),
      filter: { kind: 'text', value: (row) => processTitle(t, row.processTitleKey, row.process) },
    },
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.inspector.step'),
      render: (_v, row) => stepLabel(row.step),
      filter: { kind: 'text', value: (row) => stepLabel(row.step) },
    },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.inspector.state'),
      render: (_v, row) => stateText(t, row.state),
      filter: {
        kind: 'enum',
        value: (row) => row.state,
        options: instanceStates.map((value) => ({ value, label: stateText(t, value) })),
      },
    },
    {
      key: 'createdAt',
      kind: 'fixed',
      title: t('shell.inspector.createdAt'),
      render: (_v, row) => time('createdAt', row.createdAt),
      filter: { kind: 'date', value: (row) => localDay(row.createdAt) },
    },
    {
      key: 'endedAt',
      kind: 'fixed',
      title: t('shell.inspector.endedAt'),
      render: (_v, row) => time('endedAt', row.endedAt),
      filter: { kind: 'date', value: (row) => localDay(row.endedAt) },
    },
    {
      key: 'open',
      kind: 'fixed',
      render: (_v, row) => {
        const page = inspectors?.get(row.process)
        return page === undefined ? null : (
          <Link to={inspectorPath(sitePath, page, row.id)} data-testid={`inspector-subprocess-${row.id}`}>
            {t('shell.inspector.open')}
          </Link>
        )
      },
    },
  ]

  // The starter: a person by name, the parent process that started the instance, or the name of a trigger, endpoint or
  // schedule. A parent the person may not inspect has no name in the detail, so it is not set.
  const startedByValue = (): ReactNode => {
    const by = instance.startedBy
    if (by?.kind === 'parent') {
      if (instance.parent === null) {
        return <NotSet />
      }
      const title = processTitle(t, instance.parent.processTitleKey, instance.parent.process)
      return parentPage === undefined ? (
        title
      ) : (
        <Link to={inspectorPath(sitePath, parentPage, instance.parent.id)} data-testid="inspector-parent-link">
          {title}
        </Link>
      )
    }
    if (by?.kind === 'person') {
      return by.id !== null && by.name !== null ? <PersonName id={by.id} name={by.name} /> : <NotSet />
    }
    return by?.name ? by.name : <NotSet />
  }
  // How the instance started: the label of the action for a person, else the kind of the start.
  const startedViaValue = (): ReactNode => {
    const by = instance.startedBy
    if (by === null) {
      return <NotSet />
    }
    if (by.kind === 'person') {
      return by.via === null ? <NotSet /> : t(by.via)
    }
    return startKindText(t, by.kind)
  }
  // The step a failed instance stopped at, and why: the latest entry of that step with a problem or an error, or else the
  // last attempt with one of the last failed call on that step. See docs/reference/process-inspector-widget.md#instance-page.
  const stopReason = (): string | null => {
    const entry = instance.path.findLast((row) => row.step === instance.step && (row.problem || row.error))
    if (entry) {
      return problemText(t, locale, entry.problem, entry.error)
    }
    const call = instance.calls.findLast((row) => row.step === instance.step && row.state === 'failed')
    const attempt = call?.attempts.findLast((row) => row.problem || row.error)
    return attempt ? problemText(t, locale, attempt.problem, attempt.error) : null
  }
  const stopped = fillText(t('shell.inspector.stoppedAt'), { step: stepLabel(instance.step) })
  const reason = instance.state === 'failed' ? stopReason() : null
  const facts: Term[] = [
    ...(instance.subjectId !== null && instance.subjectEntity !== null
      ? [
          {
            key: 'subject',
            term: t('shell.inspector.subject'),
            value: (
              <RecordLink
                entity={instance.subjectEntity}
                id={instance.subjectId}
                label={instance.subjectLabel ?? t('shell.inspector.open')}
              />
            ),
          },
        ]
      : []),
    { key: 'startedBy', term: t('shell.inspector.startedBy'), value: startedByValue() },
    { key: 'startedVia', term: t('shell.inspector.startedVia'), value: startedViaValue() },
    { key: 'createdAt', term: t('shell.inspector.createdAt'), value: time('createdAt', instance.createdAt) },
    { key: 'endedAt', term: t('shell.inspector.endedAt'), value: time('endedAt', instance.endedAt) ?? <NotSet /> },
    {
      key: 'duration',
      term: t('shell.inspector.duration'),
      value:
        instance.endedAt === null ? (
          <NotSet />
        ) : (
          formatDuration(Date.parse(instance.endedAt) - Date.parse(instance.createdAt), locale, t)
        ),
    },
    { key: 'release', term: t('shell.inspector.release'), value: instance.releaseNumber ?? <NotSet /> },
    { key: 'instanceId', term: t('shell.inspector.instanceId'), value: <CopyableId id={instance.id} /> },
  ]

  const taken = new Set(instance.path.map((entry) => entry.step))
  return (
    <div data-testid="inspector-instance">
      <PageActions>
        <Flex gap="small" align="center">
          {instance.studio && <OpenInStudioButton studio={instance.studio} />}
          <InstanceCommands instance={instance} instancesPath={instancesPath} onChanged={onChanged} />
        </Flex>
      </PageActions>
      {instance.state === 'failed' && (
        <AxisAlert data-testid="inspector-failure" kind="error" message={reason ? `${stopped} ${reason}` : stopped} />
      )}
      <SectionParts>
        <SectionPart testName="inspector-facts">
          <TermList row terms={facts} />
        </SectionPart>
        <SectionPart testName="inspector-steps" heading={{ title: t('shell.inspector.steps') }}>
          <Flex vertical>
            {instance.graph.steps.map((step) => (
              <Flex key={step.name} vertical gap="small" data-testid={`inspector-step-${step.name}`} style={rowStyle}>
                <Flex gap="small" align="center">
                  <Typography.Text strong>{stepLabel(step.name)}</Typography.Text>
                  <Typography.Text type="secondary">{step.type}</Typography.Text>
                  {taken.has(step.name) && <StatusTag tone="muted">{t('shell.inspector.taken')}</StatusTag>}
                  {step.name === instance.step && <StatusTag tone="neutral">{t('shell.inspector.current')}</StatusTag>}
                </Flex>
                {step.transitions.map((transition) => (
                  <Typography.Text key={`${transition.next}|${transition.on ?? ''}`} type="secondary">
                    {`→ ${stepLabel(transition.next)}${transition.on === null ? '' : ` (${transition.on})`}`}
                  </Typography.Text>
                ))}
              </Flex>
            ))}
          </Flex>
        </SectionPart>
        <SectionPart testName="inspector-history" heading={{ title: t('shell.inspector.history') }}>
          <AxisTable<PathEntry>
            rowKey={(row) => `${row.step}|${row.startedAt}|${row.finishedAt}`}
            columns={historyColumns}
            dataSource={instance.path}
            view={{ query: historyView.view, onChange: historyView.setView, paging: false }}
            emptyText={t('shell.inspector.noHistory')}
          />
        </SectionPart>
        <SectionPart testName="inspector-tasks" heading={{ title: t('shell.inspector.tasks') }}>
          <AxisTable<InstanceTask>
            rowKey={(row) => row.id}
            columns={taskColumns}
            dataSource={instance.tasks}
            view={{ query: taskView.view, onChange: taskView.setView, paging: false }}
            emptyText={t('shell.inspector.noTasks')}
          />
        </SectionPart>
        <SectionPart testName="inspector-calls" heading={{ title: t('shell.inspector.calls') }}>
          {instance.calls.length === 0 ? (
            <EmptyState text={t('shell.inspector.noCalls')} />
          ) : (
            <Flex vertical>
              {instance.calls.map((call) => (
                <CallItem
                  key={call.id}
                  call={call}
                  instanceId={instance.id}
                  instancesPath={instancesPath}
                  onChanged={onChanged}
                  stepLabel={stepLabel}
                  attemptColumns={attemptColumns}
                />
              ))}
            </Flex>
          )}
        </SectionPart>
        <SectionPart testName="inspector-sub-processes" heading={{ title: t('shell.inspector.subProcesses') }}>
          <AxisTable<SubProcess>
            rowKey={(row) => row.id}
            columns={subProcessColumns}
            dataSource={instance.subProcesses}
            view={{ query: subProcessView.view, onChange: subProcessView.setView, paging: false }}
            emptyText={t('shell.inspector.noSubProcesses')}
          />
        </SectionPart>
      </SectionParts>
    </div>
  )
}

interface InstanceCommandsProps {
  instance: InstanceDetail
  instancesPath: string
  onChanged: () => void
}

/** One secondary button for each command the instance detail allows. A command this build does not know is left out. */
function InstanceCommands({ instance, instancesPath, onChanged }: InstanceCommandsProps) {
  const t = useText()
  const [open, setOpen] = useState<InstanceCommand>()
  const allowed = instanceCommands.filter((command) => instance.commands.includes(command))
  return (
    <>
      {allowed.map((command) => (
        <AxisButton
          key={command}
          role="secondary"
          data-testid={`inspector-command-${command}`}
          onClick={() => setOpen(command)}
        >
          {t(`shell.inspector.commands.${command}`)}
        </AxisButton>
      ))}
      {open && (
        <ReasonDialog
          title={t(`shell.inspector.commands.${open}`)}
          onSend={(reason) => sendCommand(instancesPath, instance.id, open, reason)}
          onClose={() => setOpen(undefined)}
          onChanged={onChanged}
        >
          {open === 'retry' && retryWarns(instance) && (
            <AxisAlert
              data-testid="inspector-retry-warning"
              kind="warning"
              message={t('shell.inspector.commands.retryWarning')}
            />
          )}
        </ReasonDialog>
      )}
    </>
  )
}

interface OpenInStudioButtonProps {
  studio: NonNullable<InstanceDetail['studio']>
}

/**
 * The button that opens the process in Studio, a named secondary button and never a link in the data, see
 * docs/reference/actions-and-controls.md#opening-things. It shows for a person who may author the application.
 */
function OpenInStudioButton({ studio }: OpenInStudioButtonProps) {
  const t = useText()
  const address = studioResourceAddress(studio.app, studio.resource)
  const go = useLinkClickHandler(address)
  return (
    <AxisButton
      role="secondary"
      data-testid="inspector-open-studio"
      href={address}
      onClick={(event) => go(event as never)}
    >
      {t('shell.inspector.openInStudio')}
    </AxisButton>
  )
}

interface CallItemProps {
  call: InstanceCall
  instanceId: string
  instancesPath: string
  /** Called after a resolve was accepted, so the page reads the instance again. */
  onChanged: () => void
  stepLabel: (name: string) => string
  attemptColumns: AxisColumn<CallAttempt>[]
}

/**
 * One call: where it goes, its state, its input and its attempts. The Attempts table filters on each column and keeps
 * its filter in the address under `attempts.{callId}.`, see docs/reference/process-inspector-widget.md#instance-page.
 * A call the server marks resolvable also has a Resolve button. The page never decides who may resolve.
 */
function CallItem({ call, instanceId, instancesPath, onChanged, stepLabel, attemptColumns }: CallItemProps) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [resolving, setResolving] = useState(false)
  const attemptView = useTableView(
    attemptFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    `attempts.${call.id}.`,
  )
  const target = call.connector ?? `${call.package}@${call.version}`
  const input = call.inputHidden ? null : inputEntries(call.input)
  return (
    <Flex
      vertical
      gap="small"
      data-testid={`inspector-call-${call.id}`}
      style={{ width: '100%', paddingBlock: spacing.sm, borderBlockEnd: `1px solid ${colour.divider}` }}
    >
      <Flex gap="small" align="center" wrap>
        <Typography.Text>{call.kind}</Typography.Text>
        <Typography.Text strong>{stepLabel(call.step)}</Typography.Text>
        <Typography.Text>{target}</Typography.Text>
        <Typography.Text>{call.operation}</Typography.Text>
        <StatusTag tone={callStateTone(call.state)}>{callStateText(t, call.state)}</StatusTag>
        {call.resolvable && call.outputs && (
          // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
          <Button size="small" data-testid={`inspector-resolve-${call.id}`} onClick={() => setResolving(true)}>
            {t('shell.inspector.commands.resolve')}
          </Button>
        )}
      </Flex>
      {resolving && call.outputs && (
        <ResolveDialog
          call={call}
          outputs={call.outputs}
          instanceId={instanceId}
          instancesPath={instancesPath}
          onClose={() => setResolving(false)}
          onChanged={onChanged}
        />
      )}
      {call.inputHidden && (
        <Typography.Text type="secondary">{`${t('shell.inspector.input')}: ${t('shell.value.hidden')}`}</Typography.Text>
      )}
      {input !== null && input.length > 0 && (
        <Descriptions
          size="small"
          column={1}
          title={t('shell.inspector.input')}
          items={input.map(([key, value]) => ({ key, label: key, children: value }))}
        />
      )}
      <Typography.Text type="secondary">{t('shell.inspector.attempts')}</Typography.Text>
      <AxisTable<CallAttempt>
        rowKey={(row) => `${row.kind}|${row.number}|${row.startedAt}`}
        columns={attemptColumns}
        dataSource={call.attempts}
        view={{ query: attemptView.view, onChange: attemptView.setView, paging: false }}
        emptyText={t('shell.inspector.noAttempts')}
      />
    </Flex>
  )
}
