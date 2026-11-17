import { Flex, Typography } from 'antd'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisButton } from './AxisButton'
import { AxisTable, type AxisColumn } from './AxisTable'
import { tablePageSizes, type TableQuery } from './tableQuery'
import { inspectorPath } from './inspectorPages'
import type { CallAttempt, InstanceCall, InstanceDetail, InstanceTask, SubProcess } from './inspection'
import { durationText, stepRuns, type StepRun } from './instanceRuns'
import { ResolveDialog } from './ResolveDialog'
import { useRunWords } from './runWords'
import { problemText } from './stepProblems'
import { useText } from './texts'
import { EventTime } from './EventTime'

/** One row of the timeline, with the rows inside it, which the table folds. */
interface TimelineRow {
  key: string
  /** The step's name, its label and the words of the row and the rows inside it, for filter, sort and search. */
  step: string
  stepText: string
  stateKey: 'done' | 'failed' | 'waiting' | 'running' | 'retried'
  words: string
  name: ReactNode
  what: ReactNode
  state: ReactNode
  startedAt: string | null
  finishedAt: string | null
  children?: TimelineRow[]
}

interface InstanceTimelineProps {
  instance: InstanceDetail
  instancesPath: string
  sitePath: string
  inspectors: ReadonlyMap<string, string> | undefined
  locale: string
  stepLabel: (name: string) => string
  /** The label of a transition out of a step, such as a task's outcome, from the process's texts, else its name. */
  outcomeLabel: (step: string, name: string) => string
  /** The table's search, filter, sort and page, kept in the address by the page. */
  view: TableQuery
  onViewChange: (next: TableQuery) => void
  onChanged: () => void
  /** Proposed, see the review on #3650: opens a step's run in the side panel, from the run's step label. */
  onOpenRun?: (step: string, run: number) => void
}

/**
 * Proposed, see the review on #3650: what an instance did, one row for each run of a step, newest first, as event
 * histories show it. A row says what happened in a sentence: who chose which outcome, where a decision went, how a
 * call went over its attempts, which sub-process ran; a failed or waiting run is marked. A run is a group row, as a
 * request's history in Studio has, that opens to a row for each task, call and sub-process it started, in the same
 * columns, and a call opens to a row for each attempt, with its trace ID; a call's input and Resolve are in its row.
 */
export function InstanceTimeline({
  instance,
  instancesPath,
  sitePath,
  inspectors,
  locale,
  stepLabel,
  outcomeLabel,
  view,
  onViewChange,
  onChanged,
  onOpenRun,
}: InstanceTimelineProps) {
  const t = useText()
  const [resolving, setResolving] = useState<InstanceCall | null>(null)
  const runs = stepRuns(instance)
  const stepsByName = Object.fromEntries(instance.graph.steps.map((step) => [step.name, step]))
  const lasted = (row: TimelineRow) =>
    row.startedAt === null ? null : Date.parse(row.finishedAt ?? now) - Date.parse(row.startedAt)
  const stateRank = { failed: 0, waiting: 1, running: 2, retried: 3, done: 4 }
  const stateOptions = (['done', 'failed', 'waiting', 'running'] as const).map((value) => ({
    value,
    label: t(`shell.inspector.timeline.state${value[0].toUpperCase()}${value.slice(1)}`),
  }))
  // A run not yet finished lasts until the page was drawn.
  const [now] = useState(() => new Date().toISOString())
  const { typeOf, typeText, assigneeText, targetOf, callSummary, summary, stateWords } = useRunWords({
    instance,
    stepLabel,
    outcomeLabel,
    inspectors,
    sitePath,
  })
  const callState = (call: InstanceCall) =>
    call.state === 'succeeded' ? 'done' : call.state === 'failed' ? 'failed' : ('running' as const)
  const named = (name: ReactNode, kind: string) => (
    <Flex vertical>
      <Typography.Text strong>{name}</Typography.Text>
      <Typography.Text type="secondary">{kind}</Typography.Text>
    </Flex>
  )
  const traceId = (value: string | null) =>
    value === null ? null : (
      <Typography.Text type="secondary">
        {t('shell.inspector.traceId')}{' '}
        <Typography.Text code copyable>
          {value}
        </Typography.Text>
      </Typography.Text>
    )

  // A row of the timeline: a run of a step, or, inside it, a task, a call, an attempt of a call or a sub-process.
  const attemptRow = (call: InstanceCall, attempt: CallAttempt): TimelineRow => {
    const error = problemText(t, attempt.problem, attempt.error)
    return {
      key: `${call.id}/${attempt.number}`,
      step: call.step,
      stepText: stepLabel(call.step),
      stateKey: attempt.outcome === 'succeeded' ? 'done' : attempt.outcome === 'retryable' ? 'retried' : 'failed',
      words: [error ?? '', attempt.httpStatus ?? '', attempt.traceId ?? ''].join(' '),
      name: named(t('shell.inspector.timeline.attempt').replace('{number}', String(attempt.number)), ''),
      what: (
        <Flex vertical>
          <span>
            {error === null
              ? attempt.httpStatus === null
                ? t('shell.inspector.timeline.attemptOk')
                : t('shell.inspector.timeline.attemptStatus').replace('{status}', String(attempt.httpStatus))
              : attempt.httpStatus === null
                ? error
                : t('shell.inspector.timeline.attemptError')
                    .replace('{status}', String(attempt.httpStatus))
                    .replace('{error}', error)}
          </span>
          {traceId(attempt.traceId)}
        </Flex>
      ),
      state: stateWords(
        attempt.outcome === 'succeeded' ? 'done' : attempt.outcome === 'retryable' ? 'retried' : 'failed',
      ),
      startedAt: attempt.startedAt,
      finishedAt: attempt.finishedAt,
    }
  }
  const callRow = (call: InstanceCall): TimelineRow => {
    const input =
      call.inputHidden || call.input === null || typeof call.input !== 'object'
        ? []
        : Object.entries(call.input as Record<string, unknown>)
    return {
      key: call.id,
      step: call.step,
      stepText: stepLabel(call.step),
      stateKey: callState(call),
      words: [
        targetOf(call),
        callSummary(call),
        ...input.map(([name, value]) => `${name} ${String(value)}`),
        ...call.attempts.map(
          (attempt) => `${attempt.error ?? ''} ${attempt.httpStatus ?? ''} ${attempt.traceId ?? ''}`,
        ),
      ].join(' '),
      name: named(
        targetOf(call),
        t(call.kind === 'package' ? 'shell.inspector.timeline.packageCall' : 'shell.inspector.timeline.connectorCall'),
      ),
      what: (
        <Flex vertical align="flex-start">
          <span>{callSummary(call)}</span>
          {call.inputHidden ? (
            <Typography.Text type="secondary">{t('shell.inspector.timeline.inputHidden')}</Typography.Text>
          ) : (
            input.map(([name, value]) => (
              <Typography.Text key={name} type="secondary">
                {`${name}: ${typeof value === 'object' ? JSON.stringify(value) : String(value)}`}
              </Typography.Text>
            ))
          )}
          {call.resolvable && call.outputs && (
            <AxisButton
              role="secondary"
              size="small"
              data-testid={`inspector-resolve-${call.id}`}
              onClick={() => setResolving(call)}
            >
              {t('shell.inspector.commands.resolve')}
            </AxisButton>
          )}
        </Flex>
      ),
      state: stateWords(callState(call)),
      startedAt: call.attempts[0]?.startedAt ?? null,
      finishedAt: call.attempts.at(-1)?.finishedAt ?? null,
      children:
        call.attempts.length > 0 ? call.attempts.map((attempt) => attemptRow(call, attempt)).reverse() : undefined,
    }
  }
  const taskRow = (task: InstanceTask): TimelineRow => ({
    key: task.id,
    step: task.step,
    stepText: stepLabel(task.step),
    stateKey: task.completedAt === null ? 'waiting' : 'done',
    words: [
      assigneeText(task),
      task.completedBy ?? '',
      task.outcome === null ? '' : outcomeLabel(task.step, task.outcome),
    ].join(' '),
    name: named(assigneeText(task), t('shell.inspector.stepType.task')),
    what:
      task.outcome === null
        ? t('shell.inspector.timeline.taskOpen').replace('{assignee}', assigneeText(task))
        : t('shell.inspector.timeline.taskDone')
            .replace('{person}', task.completedBy ?? assigneeText(task))
            .replace('{outcome}', outcomeLabel(task.step, task.outcome)),
    state: stateWords(task.completedAt === null ? 'waiting' : 'done', true),
    startedAt: task.createdAt,
    finishedAt: task.completedAt,
  })
  const childRow = (child: SubProcess): TimelineRow => {
    const page = inspectors?.get(child.process)
    const key = `shell.inspector.states.${child.state}`
    return {
      key: child.id,
      step: child.step,
      stepText: stepLabel(child.step),
      stateKey: child.endedAt === null ? 'running' : child.state === 'failed' ? 'failed' : 'done',
      words: `${child.process} ${child.state}`,
      name: named(
        page === undefined ? (
          child.process
        ) : (
          <Link to={inspectorPath(sitePath, page, child.id)} data-testid={`inspector-subprocess-${child.id}`}>
            {child.process}
          </Link>
        ),
        t('shell.inspector.stepType.subProcess'),
      ),
      what: t(key) === key ? child.state : t(key),
      state: stateWords(child.endedAt === null ? 'running' : child.state === 'failed' ? 'failed' : 'done'),
      startedAt: child.createdAt,
      finishedAt: child.endedAt,
    }
  }
  // The branch a step sits in, as the block names it, or nothing outside a block.
  const branchOf = (step: string) => {
    const inBlock = instance.graph.steps.find((candidate) => candidate.name === step)?.inBlock
    const branch = instance.graph.steps
      .find((candidate) => candidate.name === inBlock?.block)
      ?.parallel?.branches.find((candidate) => candidate.name === inBlock?.branch)
    return branch === undefined ? '' : branch.labelKey ? t(branch.labelKey) : branch.name
  }
  const runRow = (run: StepRun): TimelineRow => {
    const children = [...run.tasks.map(taskRow), ...run.calls.map(callRow), ...run.subProcesses.map(childRow)].sort(
      (a, b) => (b.startedAt ?? '').localeCompare(a.startedAt ?? ''),
    )
    return {
      key: run.key,
      step: run.step,
      stepText: stepLabel(run.step),
      stateKey: run.state,
      // A run is found by its own words and by those of the rows inside it.
      words: [
        stepLabel(run.step),
        typeText(typeOf(run.step)),
        run.error ?? '',
        run.decision === null
          ? ''
          : run.decision in stepsByName
            ? stepLabel(run.decision)
            : outcomeLabel(run.step, run.decision),
        ...children.map((child) => `${child.stepText} ${child.words}`),
      ].join(' '),
      name: named(
        onOpenRun ? (
          <AxisButton role="link" inline onClick={() => onOpenRun(run.step, run.number)}>
            {stepLabel(run.step)}
          </AxisButton>
        ) : (
          stepLabel(run.step)
        ),
        [
          run.number > 1
            ? t('shell.inspector.timeline.typeRun')
                .replace('{type}', typeText(typeOf(run.step)))
                .replace('{number}', String(run.number))
            : typeText(typeOf(run.step)),
          // Proposed, see D155 and the review on #3650: a run on a branch of a block names its branch.
          branchOf(run.step),
        ]
          .filter(Boolean)
          .join(' · '),
      ),
      what: summary(run),
      state: stateWords(
        run.state,
        run.tasks.some((task) => task.completedAt === null),
      ),
      startedAt: run.startedAt,
      finishedAt: run.finishedAt,
      children: children.length > 0 ? children : undefined,
    }
  }

  const columns: AxisColumn<TimelineRow>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.inspector.step'),
      render: (_v, row) => row.name,
      // Proposed: the timeline has the standard controls its growth asks for, see the review on #3650: search finds a
      // run by its words and those of the rows inside it, and a step filters and sorts by its label.
      searchText: (row) => row.words,
      // A step has no order a person sorts by: the process gives its order, and Filter shows one step's runs.
      filter: {
        kind: 'enum',
        value: (row) => row.step,
        options: instance.graph.steps.map((step) => ({ value: step.name, label: stepLabel(step.name) })),
      },
    },
    { key: 'what', kind: 'freeText', title: t('shell.inspector.timeline.what'), render: (_v, row) => row.what },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.inspector.state'),
      render: (_v, row) => row.state,
      // States sort by what they ask of the reader, a failure first and done last.
      sortValue: (row) => stateRank[row.stateKey],
      filter: { kind: 'enum', value: (row) => row.stateKey, options: stateOptions },
    },
    {
      key: 'startedAt',
      kind: 'fixed',
      title: t('shell.inspector.startedAt'),
      sortValue: (row) => row.startedAt,
      filter: { kind: 'date', value: (row) => row.startedAt?.slice(0, 10) ?? null },
      // Proposed: a run's start is how long ago it was, the exact time its tooltip, see the review on #3650.
      render: (_v, row) => (row.startedAt === null ? null : <EventTime at={row.startedAt} locale={locale} />),
    },
    {
      // Proposed: how long a run, a call or an attempt lasted is its Duration, the one term for it, see the review on
      // #3650.
      key: 'duration',
      kind: 'fixed',
      title: t('shell.inspector.timeline.duration'),
      sortValue: lasted,
      render: (_v, row) => (row.startedAt === null ? null : durationText(row.startedAt, row.finishedAt ?? now, locale)),
    },
  ]

  return (
    <Flex vertical gap="middle" data-testid="inspector-timeline">
      <AxisTable<TimelineRow>
        rowKey={(row) => row.key}
        columns={columns}
        // Records in time are newest first, as docs/reference/actions-and-controls.md#list-order says.
        dataSource={runs.map(runRow).reverse()}
        emptyText={t('shell.inspector.noHistory')}
        view={{ query: view, onChange: onViewChange, pageSizes: tablePageSizes(), search: true }}
        toolbar={{ onRefresh: onChanged }}
      />
      {resolving && resolving.outputs && (
        <ResolveDialog
          call={resolving}
          outputs={resolving.outputs}
          instanceId={instance.id}
          instancesPath={instancesPath}
          onClose={() => setResolving(null)}
          onChanged={onChanged}
        />
      )}
    </Flex>
  )
}
