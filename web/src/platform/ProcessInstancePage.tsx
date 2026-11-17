import { Flex, Tabs } from 'antd'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { NotSet } from './EmptyValue'
import { formatValue } from './formatValue'
import {
  fetchInstance,
  forbidden,
  instanceCommands,
  retryWarns,
  sendCommand,
  stateText,
  timerDueAt,
  type InstanceCommand,
  type InstanceDetail,
} from './inspection'
import { inspectorPath, useInspectorPages } from './inspectorPages'
import { InstanceTimeline } from './InstanceTimeline'
import { durationText, instanceTone } from './instanceRuns'
import { useMinuteClock } from './relativeTime'
import { StatusTag } from './StatusTag'
import { useInstanceStatus } from './instanceStatus'
import { PageActions, PageHeading } from './PageLayout'
import { ProcessGraphCanvas, type ProcessGraphStep, type ProcessGraphTransitionView } from './ProcessGraph'
import { RecordLink } from './RecordLink'
import { RecordNotFound } from './RecordNotFound'
import { StepRunPanel } from './StepRunPanel'
import { stepRunTabs, type StepRunTab } from './stepRunTabs'
import { TermList, type Term } from './TermList'
import { ReasonDialog } from './ReasonDialog'
import { SectionPart, SectionParts } from './SectionParts'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'
import { useText, useTextCatalogs } from './texts'
import { useAddressTab } from './addressTab'
import { recordPageSize, writeTableQuery } from './tableQuery'
import { useTableView } from './useTableView'
import { useReleaseTexts } from './useReleaseTexts'

interface ProcessInstancePageProps {
  sitePath: string
  /** The names of the site's navigation pages, in navigation order, for the links to other inspector pages. */
  pages: readonly string[]
  /** The process inspector widget of the page the instance's address is under. */
  widget: WidgetMetadata
  instanceId: string
  /** The UI locale, for formatting the times. */
  locale: string
  /** The site's fallback locale, for the texts of the instance's release. */
  fallbackLocale: string
}

/** The instance read, `null` when the process has none with the id, or why it could not be shown. */
type InstanceState = InstanceDetail | null | typeof forbidden | 'failed'

const dateTime = (name: string) => ({ name, type: 'date-time' }) as const

/**
 * The tabs of an instance, in order: its facts, its steps on the process's graph, then its history, tasks, calls and
 * sub-processes.
 */
const instanceTabs = ['general', 'timeline', 'steps']

/** How tall the graph of an instance's steps is drawn. */
const graphHeight = 560

/** The timeline's columns that its filter and sort may name, kept in the address by `useTableView`. */
const timelineFields = [
  { name: 'step', type: 'enum' },
  { name: 'state', type: 'enum' },
  { name: 'startedAt', type: 'date' },
  { name: 'duration', type: 'integer' },
] as const
// The timeline has no parameter of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/**
 * Shows one instance under the page of its process inspector: its state and step, the steps of
 * the release it is pinned to with the path marked, the history, the tasks, the calls with their
 * attempts and the sub-processes. An instance that waits at a timer shows when it is due. The parent
 * and each sub-process link to their instance on the first navigation page whose process inspector
 * names their process, and show without a link when there is none. A step the path holds is marked as taken, and the step the instance is at is marked as
 * current. A call whose input is hidden shows the fixed "hidden" text and no value.
 *
 * Step labels come from the texts of the instance's release, then the site texts and the platform
 * texts. A step without a label shows its name. If the release texts fail to load, the page shows
 * its load error.
 */
export function ProcessInstancePage({
  sitePath,
  pages,
  widget,
  instanceId,
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

  if (instance === undefined) {
    return null
  }
  if (instance === null) {
    // Proposed: a missing instance says so in one line with a link to its list, as a missing record does, see the review
    // on #3650.
    return <RecordNotFound textKey="shell.inspector.notFound" />
  }
  if (instance === forbidden) {
    return <AxisAlert data-testid="inspector-forbidden" kind="warning" message={t('shell.inspector.forbidden')} />
  }
  if (instance === 'failed' || releaseTexts.failed) {
    return <AxisAlert data-testid="inspector-error" kind="error" message={t('shell.inspector.loadFailed')} />
  }
  // The previous release texts stay on screen while a new locale loads.
  if (!releaseCatalog) {
    return null
  }

  return (
    <TextProvider catalogs={catalogs} development={outer.development}>
      <InstanceDetailView
        instance={instance}
        instancesPath={instancesPath}
        sitePath={sitePath}
        inspectors={inspectors}
        locale={locale}
        onChanged={reload}
      />
    </TextProvider>
  )
}

function isInstanceDetail(instance: InstanceState | undefined): instance is InstanceDetail {
  return typeof instance === 'object' && instance !== null
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

  const due = timerDueAt(instance)
  const parentPage = instance.parent === null ? undefined : inspectors?.get(instance.parent.process)

  // The label of a transition out of a step, such as a task's outcome, from the process's texts, else its name.
  const outcomeLabel = (step: string, name: string) => {
    const labelKey = stepNames
      ? undefined
      : stepsByName.get(step)?.transitions.find((transition) => transition.on === name)?.labelKey
    return labelKey ? t(labelKey) : name
  }
  const status = useInstanceStatus(instance, stepLabel, locale)
  // Proposed: the timeline's search, filter, sort and page live in the address, and a click on a step in the graph
  // opens the timeline filtered to that step through the same filter, see the review on #3650.
  const { view: timelineView, setView: setTimelineView } = useTableView(
    timelineFields,
    recordPageSize,
    noParameters,
    true,
    true,
  )
  const [searchParams, setSearchParams] = useSearchParams()
  // The step chosen on the graph, first the one the instance is at.
  const [chosenStep, setChosenStep] = useState<string | null>(instance.step)
  // Proposed, see the review on #3650: the step run in the side panel, kept in the address as `step`, `run` and
  // `stepTab`, so a reload, Back or a link shows the same run. Only a click on a step or on its run opens it; while it is
  // open it follows the step chosen on the graph.
  const addressStep = searchParams.get('step')
  const panelStep = instance.graph.steps.some((step) => step.name === addressStep) ? addressStep : null
  const panelRun = Number(searchParams.get('run')) || null
  const addressTab = searchParams.get('stepTab')
  const panelTab: StepRunTab = stepRunTabs.find((tab) => tab === addressTab) ?? 'overview'
  const showRun = (step: string | null, run: number | null = null, tab: StepRunTab = panelTab) => {
    const next = new URLSearchParams(searchParams)
    for (const name of ['step', 'run', 'stepTab']) {
      next.delete(name)
    }
    if (step !== null) {
      next.set('step', step)
      if (run !== null) {
        next.set('run', String(run))
      }
      if (tab !== 'overview') {
        next.set('stepTab', tab)
      }
    }
    setSearchParams(next, { preventScrollReset: true })
  }
  const openRun = (step: string, run: number | null = null) => {
    setChosenStep(step)
    showRun(step, run)
  }
  const showStep = (step: string) => {
    const next = writeTableQuery(
      searchParams,
      { ...timelineView, page: 1, filter: { all: [{ field: 'step', op: 'eq', value: step }] } },
      recordPageSize,
    )
    next.set('tab', 'timeline')
    setSearchParams(next, { preventScrollReset: true })
  }

  // Proposed, see the review on #3650: the instance is headed by its process, its state a tag beside it and its kind the
  // line; what the platform keeps of it is the first part, each fact on a row of its own, and each part after it is a
  // part with a rule above it.
  // A fact every instance has always shows, and reads Not set when empty. Only what does not apply is left out: the
  // record of a process with no subject, and the due time of an instance not waiting at a timer, as
  // docs/reference/actions-and-controls.md#showing-what-is-empty says. Started and Ended belong together, so they share
  // a row.
  const subject = instance.subject ?? null
  const facts: Term[] = [
    ...(subject === null
      ? []
      : [
          {
            key: 'subject',
            term: t('shell.inspector.subject'),
            value: (
              <RecordLink entity={subject.entity} id={subject.id}>
                {subject.label ?? subject.id}
              </RecordLink>
            ),
          },
        ]),
    { key: 'step', term: t('shell.inspector.step'), value: stepLabel(instance.step) },
    {
      key: 'parent',
      term: t('shell.inspector.parent'),
      value:
        instance.parent === null ? (
          <NotSet />
        ) : parentPage === undefined ? (
          instance.parent.process
        ) : (
          <Link to={inspectorPath(sitePath, parentPage, instance.parent.id)} data-testid="inspector-parent-link">
            {instance.parent.process}
          </Link>
        ),
    },
    { key: 'createdAt', term: t('shell.inspector.createdAt'), value: time('createdAt', instance.createdAt), width: 6 },
    {
      key: 'endedAt',
      term: t('shell.inspector.endedAt'),
      value: instance.endedAt === null ? <NotSet /> : time('endedAt', instance.endedAt),
      width: 6,
    },
    ...(due === null ? [] : [{ key: 'dueAt', term: t('shell.inspector.dueAt'), value: time('dueAt', due) }]),
    { key: 'release', term: t('shell.inspector.release'), value: instance.releaseNumber ?? instance.releaseId },
  ]
  const taken = new Set(instance.path.map((entry) => entry.step))
  // How many times each step ran, so a step run again shows it.
  const runCounts = new Map<string, number>()
  for (const entry of instance.path) {
    runCounts.set(entry.step, (runCounts.get(entry.step) ?? 0) + 1)
  }
  // The transitions the instance took: from each step of its path to the next, and from the last to the step it is at.
  const walked = [...instance.path.map((entry) => entry.step), instance.step]
  const tookTransition = new Set(walked.slice(1).map((step, index) => `${walked[index]}>${step}`))
  const to = t('shell.studio.processGraph.to')
  // A step's type in the reader's words, as the timeline names it.
  const stepTypeText = (type: string) => {
    const key = `shell.inspector.stepType.${type}`
    return t(key) === key ? type : t(key)
  }
  const now = useMinuteClock()
  const graphSteps = instance.graph.steps.map((step): ProcessGraphStep => {
    const failed = instance.state === 'failed' && step.name === instance.step
    const tone = failed ? 'error' : step.name === instance.step ? 'current' : taken.has(step.name) ? 'taken' : 'idle'
    const runs = runCounts.get(step.name) ?? 0
    const state =
      tone === 'current'
        ? `, ${t('shell.inspector.current')}`
        : tone === 'taken'
          ? `, ${t('shell.inspector.taken')}`
          : ''
    // Proposed: a step's node says its state on the instance and how long its last run lasted, see the review on
    // #3650; a step not reached shows its type.
    const last = instance.path.findLast((entry) => entry.step === step.name)
    // A step run more than once says how many times, and how long its last run lasted.
    const lasted = (duration: string) =>
      runs > 1
        ? t('shell.inspector.graph.runsLast').replace('{count}', String(runs)).replace('{duration}', duration)
        : duration
    const status: ProcessGraphStep['status'] =
      tone === 'current' || tone === 'error'
        ? {
            tone: instanceTone(instance),
            text: stateText(t, instance.state),
            detail: lasted(
              durationText(instance.path.at(-1)?.finishedAt ?? instance.createdAt, now.toISOString(), locale),
            ),
          }
        : tone === 'taken' && last !== undefined
          ? {
              tone: 'success',
              text: t('shell.inspector.timeline.stateDone'),
              detail: lasted(durationText(last.startedAt, last.finishedAt, locale)),
            }
          : undefined
    return {
      id: step.name,
      name: stepLabel(step.name),
      type: stepTypeText(step.type),
      kind: step.type,
      status,
      start: step.name === instance.graph.start,
      tone,
      ariaLabel: `${stepLabel(step.name)}, ${stepTypeText(step.type)}${state}${runs > 1 ? `, ${t('shell.inspector.graph.runs').replace('{count}', String(runs))}` : ''}`,
      ariaCurrent: tone === 'current',
      testId: `inspector-step-${step.name}`,
    }
  })
  const graphTransitions = instance.graph.steps.flatMap((step) =>
    step.transitions.map((transition): ProcessGraphTransitionView => {
      const took = tookTransition.has(`${step.name}>${transition.next}`)
      return {
        id: `${step.name}>${transition.next}|${transition.on ?? ''}`,
        source: step.name,
        target: transition.next,
        label: transition.on ?? '',
        ariaLabel: `${stepLabel(step.name)} ${to} ${stepLabel(transition.next)}${transition.on === null ? '' : `, ${transition.on}`}`,
        tone: took ? 'taken' : 'idle',
      }
    }),
  )

  const [tab, selectTab] = useAddressTab('tab', instanceTabs)
  return (
    <div data-testid="inspector-instance">
      <PageHeading
        // Proposed: a process is named by its title in each language, see the review on #3650.
        title={instance.processTitleKey ? t(instance.processTitleKey) : instance.process}
        line={t('shell.inspector.process')}
        tags={<StatusTag tone={instanceTone(instance)}>{stateText(t, instance.state)}</StatusTag>}
      />
      <PageActions>
        <InstanceCommands instance={instance} instancesPath={instancesPath} onChanged={onChanged} />
      </PageActions>
      {/* Proposed: where the instance stands, a failure or what it waits for, is said at the top of its page, see the
          review on #3650. */}
      {status && (
        <div style={{ marginBottom: 16 }}>
          <AxisAlert data-testid="inspector-status" kind={status.kind} message={status.text} />
        </div>
      )}
      <SectionParts>
        {/* Proposed: a page with nothing to fill in has everything under its heading in tabs, its facts first as General,
            the tab kept in the address, see the review on #3650. */}
        <SectionPart testName="inspector-tabs">
          <Tabs
            activeKey={tab}
            onChange={selectTab}
            items={[
              {
                key: 'general',
                label: t('shell.inspector.general'),
                // Proposed: what the platform keeps of the instance, each fact on a row of its own, is its first tab,
                // see the review on #3650.
                children: (
                  <div data-testid="studio-section-inspector-details">
                    <TermList terms={facts} />
                  </div>
                ),
              },
              {
                key: 'timeline',
                label: t('shell.inspector.timeline'),
                // Proposed: what the instance did is one timeline of its step runs, with the tasks, calls and
                // sub-processes each started inside it, see the review on #3650.
                children: (
                  <InstanceTimeline
                    instance={instance}
                    instancesPath={instancesPath}
                    sitePath={sitePath}
                    inspectors={inspectors}
                    locale={locale}
                    stepLabel={stepLabel}
                    outcomeLabel={outcomeLabel}
                    view={timelineView}
                    onViewChange={setTimelineView}
                    onChanged={onChanged}
                    onOpenRun={openRun}
                  />
                ),
              },
              {
                key: 'steps',
                label: t('shell.inspector.steps'),
                // Proposed: the steps are the process's graph, drawn as the process designer draws it, focused on the
                // step the instance is at, each step run more than once marked with its count; a click on a step
                // shows its runs in the timeline, see the review on #3650.
                children: (
                  <ProcessGraphCanvas
                    steps={graphSteps}
                    transitions={graphTransitions}
                    ariaLabel={t('shell.inspector.steps')}
                    testId="inspector-graph"
                    height={graphHeight}
                    focus={instance.step}
                    controls
                    // Proposed: a click on a step chooses it and opens its last run in the side panel, while the graph
                    // stays in view, as AWS Step Functions and Camunda Operate show a state's details beside its
                    // diagram, see the review on #3650.
                    selected={panelStep ?? chosenStep}
                    onSelect={(id) => {
                      setChosenStep(id)
                      if (panelStep !== null) {
                        showRun(id)
                      }
                    }}
                    onStepClick={(id) => openRun(id)}
                  />
                ),
              },
            ]}
          />
        </SectionPart>
      </SectionParts>
      {panelStep !== null && (
        <StepRunPanel
          instance={instance}
          step={panelStep}
          run={panelRun}
          tab={panelTab}
          onRun={(run) => showRun(panelStep, run)}
          onTab={(tab) => showRun(panelStep, panelRun, tab)}
          onOpen={openRun}
          onClose={() => showRun(null)}
          onShowInTimeline={showStep}
          stepLabel={stepLabel}
          outcomeLabel={outcomeLabel}
          inspectors={inspectors}
          sitePath={sitePath}
          locale={locale}
        />
      )}
    </div>
  )
}

interface InstanceCommandsProps {
  instance: InstanceDetail
  instancesPath: string
  onChanged: () => void
}

/** One button for each command the instance detail allows. A command this build does not know is left out. */
function InstanceCommands({ instance, instancesPath, onChanged }: InstanceCommandsProps) {
  const t = useText()
  const [open, setOpen] = useState<InstanceCommand>()
  const allowed = instanceCommands.filter((command) => instance.commands.includes(command))
  if (allowed.length === 0) {
    return null
  }
  return (
    <Flex gap="small">
      {allowed.map((command) => (
        <AxisButton
          role="secondary"
          key={command}
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
    </Flex>
  )
}
