import { Flex, Select, Tabs } from 'antd'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { CopyableId } from './CopyableId'
import { EventTime } from './EventTime'
import { NotSet } from './EmptyValue'
import { fillText } from './fillText'
import { formatDuration, formatValue } from './formatValue'
import {
  processTitle,
  runParts,
  runProblem,
  runStateText,
  runStateTone,
  stateText,
  stateTone,
  stepTypeText,
  takenTransition,
  type InstanceDetail,
  type StepRun,
} from './inspection'
import { inspectorPath } from './inspectorPages'
import { PersonName } from './PersonName'
import { SidePanel } from './SidePanel'
import { runHasInput, runHasOutput } from './runPanelParts'
import { RunAttempts, RunInput, RunOutput } from './StepRunParts'
import { StatusTag } from './StatusTag'
import { TermList, type Term } from './TermList'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useAddressTab } from './useAddressTab'
import { useEmptyStyle } from './useEmptyStyle'

interface StepRunPanelProps {
  instance: InstanceDetail
  /** Every run of the instance, in the order they began. */
  runs: readonly StepRun[]
  /** The step whose run the panel shows. */
  step: string
  /** The run the panel shows, or `undefined` for a step the instance has not reached. */
  run: StepRun | undefined
  /** The words of a step: its label, or its name where the page has no release texts. */
  stepLabel: (name: string) => string
  /** The inspector page of each process, once loaded. Without it, a sub-process shows without a link. */
  inspectors: ReadonlyMap<string, string> | undefined
  sitePath: string
  locale: string
  /** The address of a step's run, for the links to the runs before and after, see `useStepRunAddress`. */
  search: (step: string, number: number) => string
  /** Called with the number of the run that the run chooser picks. */
  onChooseRun: (number: number) => void
  onClose: () => void
}

/**
 * The tabs of the panel for a run, in their order: Overview, Input for a run with a call or a decision's read values,
 * Output for a run that made something, Attempts for a run with a call, and Error for a failed run.
 */
function runTabs(instance: InstanceDetail, runs: readonly StepRun[], run: StepRun): string[] {
  return [
    'overview',
    ...(runHasInput(instance, runs, run) ? ['input'] : []),
    ...(runHasOutput(instance, runs, run) ? ['output'] : []),
    ...(runParts(instance, runs, run).calls.length > 0 ? ['attempts'] : []),
    ...(run.state === 'failed' ? ['error'] : []),
  ]
}

/**
 * The side panel of a step's run on a process instance's page, shared by the site's page and the operator console's,
 * see docs/reference/process-inspector-widget.md#instance-page. Its title is the step's label with the run's state as a
 * `StatusTag`, and its line is the step's kind, or what its operation does. A step run more than once has a run chooser
 * on top. The tabs are Overview, Input, Output, Attempts and, for a failed run, Error, those that apply to the run, kept
 * in the address as `stepTab`, and a run with one tab has no tab bar. A step the instance has not reached shows "This step has not run." Overview is a `TermList` of the run's
 * facts. A sub-process names its child by its process title, as a link where `inspectors` knows its process.
 */
export function StepRunPanel({
  instance,
  runs,
  step,
  run,
  stepLabel,
  inspectors,
  sitePath,
  locale,
  search,
  onChooseRun,
  onClose,
}: StepRunPanelProps) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const empty = useEmptyStyle()
  const keys = run ? runTabs(instance, runs, run) : ['overview']
  const [tab, selectTab] = useAddressTab('stepTab', keys)

  const type = instance.graph.steps.find((candidate) => candidate.name === step)?.type ?? ''
  const own = runs.filter((candidate) => candidate.step === step)

  let body: ReactNode
  if (run === undefined) {
    body = <span style={empty}>{t('shell.inspector.run.notRun')}</span>
  } else {
    const overview = (
      <RunOverview
        instance={instance}
        runs={runs}
        run={run}
        stepLabel={stepLabel}
        inspectors={inspectors}
        sitePath={sitePath}
        locale={locale}
        search={search}
      />
    )
    const pane = { instance, runs, run, locale }
    const panes: Record<string, ReactNode> = {
      overview,
      input: <RunInput {...pane} />,
      output: <RunOutput {...pane} />,
      attempts: <RunAttempts {...pane} />,
      error: <div>{runProblem(t, locale, instance, runs, run) ?? <NotSet />}</div>,
    }
    body =
      keys.length === 1 ? (
        overview
      ) : (
        <Tabs
          activeKey={tab}
          onChange={selectTab}
          items={keys.map((key) => ({
            key,
            label: t(`shell.inspector.run.tabs.${key}`),
            children: key === tab ? panes[key] : null,
          }))}
        />
      )
  }

  return (
    <SidePanel
      id="inspector-step-run"
      testId="inspector-step-run"
      title={stepLabel(step)}
      status={run && <StatusTag tone={runStateTone(run.state)}>{runStateText(t, run.state)}</StatusTag>}
      line={stepLine(t, instance, runs, run, type)}
      onClose={onClose}
      closeNameKey="shell.inspector.run.close"
      closeTestId="inspector-step-run-close"
    >
      <Flex vertical gap={spacing.sm}>
        {run !== undefined && own.length > 1 && (
          <Select
            style={{ alignSelf: 'flex-start', minWidth: 120 }}
            aria-label={t('shell.inspector.run.choose')}
            value={run.number}
            options={own.map((candidate) => ({
              value: candidate.number,
              label: fillText(t('shell.inspector.run.option'), { number: candidate.number }),
            }))}
            onChange={onChooseRun}
          />
        )}
        {body}
      </Flex>
    </SidePanel>
  )
}

/**
 * The panel's line: what an operation step's run did, such as "Calls a connector", worked out from the run's call, its
 * child instance and what it made, since the graph gives every operation the type `operation`. Any other step, and an
 * operation that shows none of these, reads its type's word.
 */
function stepLine(
  t: (key: string) => string,
  instance: InstanceDetail,
  runs: readonly StepRun[],
  run: StepRun | undefined,
  type: string,
): string {
  if (type === 'operation' && run !== undefined) {
    const { calls, subProcesses } = runParts(instance, runs, run)
    const made = run.entries.at(-1)?.made
    const operation = calls[0]
      ? calls[0].kind === 'package'
        ? 'package'
        : 'connector'
      : subProcesses.length > 0
        ? 'subProcess'
        : made?.event
          ? 'event'
          : made?.notified
            ? 'notify'
            : made?.created
              ? 'create'
              : made?.changes
                ? 'change'
                : undefined
    if (operation !== undefined) {
      return t(`shell.inspector.run.operations.${operation}`)
    }
  }
  return stepTypeText(t, type)
}

interface RunOverviewProps {
  instance: InstanceDetail
  runs: readonly StepRun[]
  run: StepRun
  stepLabel: (name: string) => string
  inspectors: ReadonlyMap<string, string> | undefined
  sitePath: string
  locale: string
  search: (step: string, number: number) => string
}

/** The Overview tab: the run's facts as the Overview rule gives them, each a term above its value. */
function RunOverview({ instance, runs, run, stepLabel, inspectors, sitePath, locale, search }: RunOverviewProps) {
  const t = useText()
  // When the panel opened, to tell an open task that is past its due time.
  const [now] = useState(() => Date.now())
  const graphStep = instance.graph.steps.find((candidate) => candidate.name === run.step)
  const { tasks, calls, subProcesses } = runParts(instance, runs, run)
  const time = (value: string) => formatValue({ name: 'time', type: 'date-time' }, value, {}, { locale, text: t })

  // A step the instance reached is a link to its run, with its number where it ran more than once.
  const stepLink = (name: string, number?: number): ReactNode => {
    const own = runs.filter((candidate) => candidate.step === name)
    const target = number === undefined ? own.at(-1) : own.find((candidate) => candidate.number === number)
    if (target === undefined) {
      return stepLabel(name)
    }
    return (
      <>
        <Link to={search(name, target.number)}>{stepLabel(name)}</Link>
        {own.length > 1 && ` ${fillText(t('shell.inspector.run.number'), { number: target.number })}`}
      </>
    )
  }

  const terms: Term[] = [
    {
      key: 'startedAt',
      term: t('shell.inspector.startedAt'),
      value: run.startedAt === null ? <NotSet /> : <EventTime value={run.startedAt} />,
      width: 6,
    },
    {
      key: 'endedAt',
      term: t('shell.inspector.endedAt'),
      value: run.finishedAt === null ? <NotSet /> : <EventTime value={run.finishedAt} />,
      width: 6,
    },
    {
      key: 'duration',
      term: t('shell.inspector.duration'),
      value:
        run.startedAt === null || run.finishedAt === null ? (
          <NotSet />
        ) : (
          formatDuration(Date.parse(run.finishedAt) - Date.parse(run.startedAt), locale, t)
        ),
    },
  ]

  for (const task of tasks) {
    const open = task.state === 'open'
    terms.push({
      key: `assignedTo-${task.id}`,
      term: t('shell.inspector.run.assignedTo'),
      value:
        task.assigneeKind === 'user' && task.assigneeId !== null ? (
          <PersonName id={task.assigneeId} name={task.assignee} />
        ) : (
          task.assignee
        ),
    })
    if (task.dueAt !== null) {
      terms.push({
        key: `due-${task.id}`,
        term: t('shell.tasks.dueAt'),
        value: (
          <Flex gap="small" align="center">
            {time(task.dueAt)}
            {open && Date.parse(task.dueAt) < now && <StatusTag tone="attention">{t('shell.tasks.overdue')}</StatusTag>}
          </Flex>
        ),
      })
    }
    terms.push({
      key: `completedBy-${task.id}`,
      term: t('shell.inspector.completedBy'),
      value:
        task.completedById !== null && task.completedBy !== null ? (
          <PersonName id={task.completedById} name={task.completedBy} />
        ) : (
          <NotSet />
        ),
    })
  }

  for (const call of calls) {
    terms.push(
      {
        key: `callTarget-${call.id}`,
        term: call.kind === 'package' ? t('shell.inspector.run.package') : t('shell.inspector.run.connector'),
        value: call.kind === 'package' ? `${call.package ?? ''} ${call.version ?? ''}`.trim() : call.connector,
        width: 6,
      },
      { key: `operation-${call.id}`, term: t('shell.inspector.operation'), value: call.operation, width: 6 },
      {
        key: `attempts-${call.id}`,
        term: t('shell.inspector.attempts'),
        value: call.attempts.filter((attempt) => attempt.kind === 'attempt').length,
      },
    )
  }

  for (const child of subProcesses) {
    const page = inspectors?.get(child.process)
    const title = processTitle(t, child.processTitleKey, child.process)
    terms.push({
      key: `subProcess-${child.id}`,
      term: t('shell.inspector.process'),
      value: (
        <Flex gap="small" align="center">
          {page === undefined ? (
            title
          ) : (
            <Link to={inspectorPath(sitePath, page, child.id)} data-testid={`inspector-run-subprocess-${child.id}`}>
              {title}
            </Link>
          )}
          <StatusTag tone={stateTone(child.state)}>{stateText(t, child.state)}</StatusTag>
        </Flex>
      ),
    })
  }

  // The run that still waits: the instance's open run at the step it waits on.
  const waiting = instance.state === 'waiting' && run.finishedAt === null && run.step === instance.step
  if (graphStep?.type === 'waitEvent') {
    terms.push({
      key: 'waitsFor',
      term: t('shell.inspector.run.waitsFor'),
      value: graphStep.event ?? <NotSet />,
    })
    const timeout = graphStep.transitions.find((transition) => transition.on === 'timeout')
    if (waiting && instance.timerDueAt !== null && timeout !== undefined) {
      terms.push(
        {
          key: 'givesUpAt',
          term: t('shell.inspector.run.givesUpAt'),
          value: time(instance.timerDueAt),
          width: 6,
        },
        { key: 'thenGoesTo', term: t('shell.inspector.run.thenGoesTo'), value: stepLink(timeout.next), width: 6 },
      )
    }
  }
  if (graphStep?.type === 'timer' && waiting && instance.timerDueAt !== null) {
    terms.push({ key: 'goesOnAt', term: t('shell.inspector.run.goesOnAt'), value: time(instance.timerDueAt) })
  }

  // Came from and Went to are the runs before and after. A decision's labelled branch or a task's outcome goes between.
  const place = runs.indexOf(run)
  const before = runs[place - 1]
  const after = runs[place + 1]
  const transition = after === undefined ? undefined : takenTransition(instance, run, after.step)
  // The catalog answers a key it does not hold with the key itself, so a branch without the author's label has no text.
  const labelKey = transition?.labelKey ?? null
  const labelled = labelKey === null ? undefined : t(labelKey)
  const branch = graphStep?.type === 'decision' && labelled !== labelKey ? labelled : undefined
  const outcome = tasks.findLast((task) => task.outcome !== null)?.outcome ?? undefined
  const between = branch ?? outcome
  terms.push({
    key: 'cameFrom',
    term: t('shell.inspector.run.cameFrom'),
    value: before === undefined ? <NotSet /> : stepLink(before.step, before.number),
    width: between === undefined ? 6 : 12,
  })
  if (between !== undefined) {
    terms.push({
      key: 'between',
      term: branch === undefined ? t('shell.inspector.outcome') : t('shell.inspector.run.branch'),
      value: between,
      width: 6,
    })
  }
  terms.push({
    key: 'wentTo',
    term: t('shell.inspector.run.wentTo'),
    value: after === undefined ? <NotSet /> : stepLink(after.step, after.number),
    width: 6,
  })

  const traceId = run.entries.at(-1)?.traceId
  if (traceId !== undefined && traceId !== null) {
    terms.push({ key: 'traceId', term: t('shell.inspector.traceId'), value: <CopyableId id={traceId} /> })
  }

  return <TermList terms={terms} />
}
