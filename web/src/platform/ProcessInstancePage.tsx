import { Flex, Tabs } from 'antd'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useLinkClickHandler } from 'react-router'
import { CopyableId } from './CopyableId'
import { NotSet } from './EmptyValue'
import { fillText } from './fillText'
import { formatDuration, formatValue } from './formatValue'
import {
  fetchInstance,
  forbidden,
  instanceCommands,
  processTitle,
  retryWarns,
  runProblem,
  sendCommand,
  startKindText,
  stateText,
  stateTone,
  stepRuns,
  type InstanceCommand,
  type InstanceDetail,
} from './inspection'
import { InstanceHistory } from './InstanceHistory'
import { inspectorPath, useInspectorPages } from './inspectorPages'
import { PageActions, PageLayout } from './PageLayout'
import { PersonName } from './PersonName'
import { RecordLink } from './RecordLink'
import { RecordNotFound } from './RecordNotFound'
import { ReasonDialog } from './ReasonDialog'
import { StatusTag } from './StatusTag'
import { StepRunPanel } from './StepRunPanel'
import { SectionPart, SectionParts } from './SectionParts'
import { studioResourceAddress } from './studioAddress'
import { TermList, type Term } from './TermList'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'
import { useText, useTextCatalogs } from './texts'
import { useAddressTab } from './useAddressTab'
import { useEmptyStyle } from './useEmptyStyle'
import { useReleaseTexts } from './useReleaseTexts'
import { useStepRunAddress } from './useStepRunAddress'
import { namedValues } from './valueRows'
import { ValueTable } from './ValueTable'

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

// React Flow draws the graph, so its code loads only when a page opens the Diagram tab.
const LazyInstanceDiagram = lazy(() => import('./InstanceDiagram'))

/** The height of the Diagram's frame, in px, as the process designer's graph has. */
const diagramHeight = 480

/** The tabs of an instance's page, Diagram first, which the address leaves out. */
const instanceTabs = ['diagram', 'history', 'data'] as const

/**
 * Shows one instance under the page of its process inspector: its heading with the process's title and state, its
 * facts, and three tabs, Diagram, History and Data, see docs/reference/process-inspector-widget.md#instance-page. The
 * open tab is kept in the address as `tab`. Diagram draws the process graph with the steps the instance ran, History is one
 * timeline of the instance's runs, newest first, which each fold open to the tasks, calls and sub-processes they started,
 * see `InstanceHistory`, and Data holds the instance's input and output. A failed instance has a
 * banner that names the step it stopped at. The parent and each sub-process link to their instance on the first
 * navigation page whose process inspector names their process, and show without a link when there is none.
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
  /** Draws the heading in a band, for the operator console's page. */
  band?: boolean
  children?: ReactNode
}

/**
 * The heading of an instance's page, shared by a site's page and the operator console's page, see
 * docs/reference/process-inspector-widget.md#instance-page: the process's title, its state as a tag beside it, and
 * Process as its line. Without an instance the heading is the given title, with no tag.
 */
export function InstanceLayout({ instance, title, band, children }: InstanceLayoutProps) {
  const t = useText()
  return (
    <PageLayout
      title={instance ? processTitle(t, instance.processTitleKey, instance.process) : title}
      tags={instance && <StatusTag tone={stateTone(instance.state)}>{stateText(t, instance.state)}</StatusTag>}
      line={t('shell.inspector.process')}
      band={band}
    >
      {children}
    </PageLayout>
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

/** The facts and the tabs of an instance. It reads the release texts, unless `stepNames` is set. */
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
  const [tab, selectTab] = useAddressTab('tab', instanceTabs)
  const instanceRuns = useMemo(() => stepRuns(instance), [instance])
  const stepKeys = useMemo(() => instance.graph.steps.map((step) => step.name), [instance.graph.steps])
  const address = useStepRunAddress(instanceRuns.runs, stepKeys)
  // The step with the border: the one the address names, else the one chosen last, which starts as the current step.
  // Close removes `step` from the address, and the step stays chosen.
  const [picked, setPicked] = useState(instance.step)
  const chosen = address.step ?? picked
  const choose = (step: string) => {
    setPicked(step)
    address.open(step)
  }
  const stepsByName = useMemo(
    () => new Map(instance.graph.steps.map((step) => [step.name, step])),
    [instance.graph.steps],
  )
  const stepLabel = useCallback(
    (name: string) => {
      const labelKey = stepNames ? undefined : stepsByName.get(name)?.labelKey
      return labelKey ? t(labelKey) : name
    },
    [stepNames, stepsByName, t],
  )
  const time = (name: string, value: string | null) =>
    value === null ? null : formatValue(dateTime(name), value, {}, { locale, text: t })

  const parentPage = instance.parent === null ? undefined : inspectors?.get(instance.parent.process)

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
  // The step a failed instance stopped at, and why, from its current step's last run, as the panel's Error tab words it.
  // See docs/reference/process-inspector-widget.md#instance-page.
  const stopReason = (): string | null => runProblem(t, locale, instance, instanceRuns.runs, instanceRuns.runs.at(-1)!)
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

  const history = (
    <InstanceHistory
      instance={instance}
      runs={instanceRuns.runs}
      stepLabel={stepLabel}
      inspectors={inspectors}
      sitePath={sitePath}
      instancesPath={instancesPath}
      locale={locale}
      search={address.search}
      onChanged={onChanged}
    />
  )
  // Only the open tab is drawn, so the graph is not laid out while History or Data is open.
  const panes: Record<(typeof instanceTabs)[number], ReactNode> = {
    diagram: (
      <div data-testid="inspector-diagram" style={{ height: diagramHeight, position: 'relative' }}>
        <Suspense fallback={null}>
          <LazyInstanceDiagram
            instance={instance}
            runs={instanceRuns}
            chosen={chosen}
            onChoose={choose}
            stepLabel={stepLabel}
            stepNames={stepNames}
            locale={locale}
          />
        </Suspense>
      </div>
    ),
    history,
    data: <InstanceData instance={instance} stepLabel={stepLabel} />,
  }
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
        <SectionPart testName="inspector-tabs">
          <Tabs
            activeKey={tab}
            onChange={selectTab}
            items={instanceTabs.map((key) => ({
              key,
              label: t(`shell.inspector.tabs.${key}`),
              children: key === tab ? panes[key] : null,
            }))}
          />
        </SectionPart>
      </SectionParts>
      {address.step !== undefined && (
        <StepRunPanel
          instance={instance}
          runs={instanceRuns.runs}
          step={address.step}
          run={address.run}
          stepLabel={stepLabel}
          inspectors={inspectors}
          sitePath={sitePath}
          locale={locale}
          search={address.search}
          onChooseRun={(number) => address.open(address.step!, number)}
          onClose={() => {
            setPicked(chosen)
            address.close()
          }}
        />
      )}
    </div>
  )
}

interface InstanceDataProps {
  instance: InstanceDetail
  stepLabel: (name: string) => string
}

/**
 * The Data tab: a `TermList` of Input and Output, each name above its value, see
 * docs/reference/process-inspector-widget.md#instance-page. Input is the parameters the instance started with in a
 * `ValueTable`, where a hidden value reads Hidden, or "It started with no input.". Output is the end step and the values
 * the instance ended with, "The instance has not ended yet." while it runs or waits, and Not set for an instance that
 * failed or was cancelled, or that completed before the engine kept its values.
 */
function InstanceData({ instance, stepLabel }: InstanceDataProps) {
  const t = useText()
  const empty = useEmptyStyle()
  const { input, result } = instance
  let output: ReactNode = <NotSet />
  if (instance.state === 'running' || instance.state === 'waiting') {
    output = <span style={empty}>{t('shell.inspector.data.notEnded')}</span>
  } else if (result !== null) {
    output = (
      <Flex vertical gap="small">
        <span>{fillText(t('shell.inspector.data.endedAt'), { step: stepLabel(result.step) })}</span>
        {result.values === null ? (
          <NotSet />
        ) : (
          result.values.length > 0 && <ValueTable value={namedValues(result.values)} testId="inspector-output" />
        )}
      </Flex>
    )
  }
  const terms: Term[] = [
    {
      key: 'input',
      term: t('shell.inspector.input'),
      value:
        input === null || input.length === 0 ? (
          <span style={empty}>{t('shell.inspector.data.noInput')}</span>
        ) : (
          <ValueTable value={namedValues(input)} testId="inspector-input" />
        ),
    },
    { key: 'output', term: t('shell.inspector.output'), value: output },
  ]
  return (
    <div data-testid="inspector-data">
      <TermList terms={terms} />
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
