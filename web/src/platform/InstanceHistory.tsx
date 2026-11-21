import { Flex } from 'antd'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisButton } from './AxisButton'
import { AxisTable, type AxisColumn } from './AxisTable'
import { EventTime } from './EventTime'
import { formatDuration } from './formatValue'
import {
  attemptOutcomeText,
  attemptOutcomeTone,
  callStateText,
  callStateTone,
  historyStateRank,
  processTitle,
  runParts,
  runStates,
  runStateText,
  runStateTone,
  stateText,
  stateTone,
  stepTypeText,
  taskStateText,
  taskStateTone,
  type CallAttempt,
  type InstanceCall,
  type InstanceDetail,
  type InstanceTask,
  type StepRun,
  type SubProcess,
} from './inspection'
import { inspectorPath } from './inspectorPages'
import { ResolveDialog } from './ResolveDialog'
import { runKindText } from './runPanelParts'
import { StatusTag } from './StatusTag'
import { localDay } from './tableFilter'
import { recordPageSize, tablePageSizes } from './tableQuery'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useRunWords } from './useRunWords'
import { useTableView } from './useTableView'
import { fillText } from './fillText'

/**
 * A row of the timeline. Every row but a run is inside another: a run holds the tasks, calls and sub-processes it
 * started, and a call holds its attempts. `key` is unique in the table, as `AxisTable`'s `rowsIn` asks.
 */
type TimelineRow = { key: string; rows: TimelineRow[] } & (
  | { kind: 'run'; run: StepRun }
  | { kind: 'task'; task: InstanceTask }
  | { kind: 'call'; call: InstanceCall }
  | { kind: 'subProcess'; child: SubProcess }
  | { kind: 'attempt'; attempt: CallAttempt }
)

// The table filters and sorts the columns that it shows. A stable array keeps useTableView from recomputing on each render.
const historyFields = [
  { name: 'step', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'startedAt', type: 'date' },
  { name: 'duration', type: 'integer' },
] as const
// The timeline has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/** When a call began: its first attempt's start, and none yet for a call that has not been sent. */
const callStart = (call: InstanceCall): string | null => call.attempts[0]?.startedAt ?? null

/** The rows of the timeline, newest run first and each run's rows newest first, see {@link InstanceHistory}. */
function timelineRows(instance: InstanceDetail, runs: readonly StepRun[]): TimelineRow[] {
  // A call that has no attempt yet is the newest.
  const at = (time: string | null) => (time === null ? Number.POSITIVE_INFINITY : Date.parse(time))
  const attemptRows = (call: InstanceCall, key: string): TimelineRow[] =>
    [...call.attempts]
      .sort((left, right) => at(right.startedAt) - at(left.startedAt) || right.number - left.number)
      .map((attempt) => ({ kind: 'attempt', key: `${key}/${attempt.kind}:${attempt.number}`, attempt, rows: [] }))
  return runs
    .map((run): TimelineRow => {
      const key = `${run.step}|${run.number}`
      const { tasks, calls, subProcesses } = runParts(instance, runs, run)
      const inside = [
        ...tasks.map((task) => ({
          time: at(task.createdAt),
          row: { kind: 'task', key: `${key}/task:${task.id}`, task, rows: [] } satisfies TimelineRow,
        })),
        ...calls.map((call) => ({
          time: at(callStart(call)),
          row: {
            kind: 'call',
            key: `${key}/call:${call.id}`,
            call,
            rows: attemptRows(call, `${key}/call:${call.id}`),
          } satisfies TimelineRow,
        })),
        ...subProcesses.map((child) => ({
          time: at(child.createdAt),
          row: { kind: 'subProcess', key: `${key}/sub:${child.id}`, child, rows: [] } satisfies TimelineRow,
        })),
      ]
      return { kind: 'run', key, run, rows: inside.sort((a, b) => b.time - a.time).map((item) => item.row) }
    })
    .reverse()
}

/** How long a span lasted, in milliseconds, or `null` while it has no end. */
function span(from: string | null, to: string | null): number | null {
  return from === null || to === null ? null : Date.parse(to) - Date.parse(from)
}

/** How long a row lasted, in milliseconds, or `null` for a row that has not started or not ended. */
function lasted(row: TimelineRow): number | null {
  switch (row.kind) {
    case 'run':
      return span(row.run.startedAt, row.run.finishedAt)
    case 'task':
      return span(row.task.createdAt, row.task.completedAt)
    case 'call':
      return ['pending', 'sending'].includes(row.call.state)
        ? null
        : span(callStart(row.call), row.call.attempts.at(-1)?.finishedAt ?? null)
    case 'subProcess':
      return span(row.child.createdAt, row.child.endedAt)
    case 'attempt':
      return span(row.attempt.startedAt, row.attempt.finishedAt)
  }
}

/** When a row started, or `null` for a row that has not. */
function startedAt(row: TimelineRow): string | null {
  switch (row.kind) {
    case 'run':
      return row.run.startedAt
    case 'task':
      return row.task.createdAt
    case 'call':
      return callStart(row.call)
    case 'subProcess':
      return row.child.createdAt
    case 'attempt':
      return row.attempt.startedAt
  }
}

/** The row's own text of a column and that of every row inside it, for the search. */
function textsOf(row: TimelineRow, own: (row: TimelineRow) => string): string {
  return [own(row), ...row.rows.map((inner) => textsOf(inner, own))].join('\n')
}

interface InstanceHistoryProps {
  instance: InstanceDetail
  /** Every run of the instance, in the order they began. */
  runs: readonly StepRun[]
  /** The words of a step: its label, or its name where the page has no release texts. */
  stepLabel: (name: string) => string
  /** The inspector page of each process, once loaded. Without it, a sub-process shows without a link. */
  inspectors: ReadonlyMap<string, string> | undefined
  sitePath: string
  instancesPath: string
  locale: string
  /** The address of a step's run, see `useStepRunAddress`. */
  search: (step: string, number: number) => string
  /** Called after a resolve was accepted, so the page reads the instance again. */
  onChanged: () => void
}

/**
 * The History tab: one timeline of the instance's runs, newest first, see
 * docs/reference/process-inspector-widget.md#instance-page. Each run is a row of Step, What happened, State, Started and
 * Duration, closed at first. It folds open to a row for each task, call and sub-process it started, and a call folds open
 * to a row for each attempt or lookup. A run's step is a link that opens the run in the side panel, a call that the server
 * marks resolvable has a Resolve button on its row, and a sub-process links to its instance where `inspectors` knows its
 * process. What happened is a sentence of fixed texts from `useRunWords`, never a stored error message. The table holds its
 * rows: search, filter, sort and paging work on the runs, and a run is found by its own words or by those of the rows
 * inside it. They live in the address under `history.`.
 */
export function InstanceHistory({
  instance,
  runs,
  stepLabel,
  inspectors,
  sitePath,
  instancesPath,
  locale,
  search,
  onChanged,
}: InstanceHistoryProps) {
  const t = useText()
  const { colour } = useDesignTokens()
  const words = useRunWords(instance, runs, stepLabel, locale)
  const view = useTableView(historyFields, recordPageSize, noParameters, true, true, false, 'history.')
  const [resolving, setResolving] = useState<InstanceCall>()
  const rows = useMemo(() => timelineRows(instance, runs), [instance, runs])
  const types = useMemo(
    () => new Map(instance.graph.steps.map((step) => [step.name, step.type])),
    [instance.graph.steps],
  )

  const muted = { color: colour.textMuted }
  const stepText = (row: TimelineRow): string => {
    switch (row.kind) {
      case 'run':
        return stepLabel(row.run.step)
      case 'task':
        return stepTypeText(t, 'task')
      case 'call':
        return row.call.operation
      case 'subProcess':
        return processTitle(t, row.child.processTitleKey, row.child.process)
      case 'attempt':
        return words.attemptName(row.attempt)
    }
  }
  const happened = (row: TimelineRow): string => {
    switch (row.kind) {
      case 'run':
        return words.run(row.run)
      case 'task':
        return words.task(row.task)
      case 'call':
        return words.call(row.call)
      case 'subProcess':
        return words.subProcess(row.child)
      case 'attempt':
        return words.attempt(row.attempt)
    }
  }

  const columns: AxisColumn<TimelineRow>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.inspector.step'),
      render: (_v, row) => {
        if (row.kind === 'run') {
          const { run } = row
          const repeated = runs.filter((candidate) => candidate.step === run.step).length > 1
          const kind = runKindText(t, instance, runs, run, types.get(run.step) ?? '')
          return (
            <Flex vertical>
              <Link
                to={search(run.step, run.number)}
                data-testid={`inspector-history-step-${run.step}-${run.number}`}
                style={{ alignSelf: 'flex-start' }}
              >
                {stepLabel(run.step)}
              </Link>
              <span style={muted}>
                {repeated ? `${kind} ${fillText(t('shell.inspector.run.number'), { number: run.number })}` : kind}
              </span>
            </Flex>
          )
        }
        if (row.kind === 'subProcess') {
          const page = inspectors?.get(row.child.process)
          return page === undefined ? (
            stepText(row)
          ) : (
            <Link to={inspectorPath(sitePath, page, row.child.id)} data-testid={`inspector-subprocess-${row.child.id}`}>
              {stepText(row)}
            </Link>
          )
        }
        return stepText(row)
      },
      searchText: (row) => textsOf(row, stepText),
      filter: { kind: 'text', value: (row) => stepText(row) },
    },
    {
      key: 'whatHappened',
      kind: 'freeText',
      title: t('shell.inspector.whatHappened'),
      render: (_v, row) => (
        <Flex gap="small" align="center" wrap>
          <span>{happened(row)}</span>
          {row.kind === 'call' && row.call.resolvable && row.call.outputs && (
            <AxisButton
              role="secondary"
              size="small"
              data-testid={`inspector-resolve-${row.call.id}`}
              onClick={() => setResolving(row.call)}
            >
              {t('shell.inspector.commands.resolve')}
            </AxisButton>
          )}
        </Flex>
      ),
      searchText: (row) => textsOf(row, happened),
    },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.inspector.state'),
      render: (_v, row) => {
        switch (row.kind) {
          case 'run':
            return <StatusTag tone={runStateTone(row.run.state)}>{runStateText(t, row.run.state)}</StatusTag>
          case 'task':
            return <StatusTag tone={taskStateTone(row.task.state)}>{taskStateText(t, row.task.state)}</StatusTag>
          case 'call':
            return <StatusTag tone={callStateTone(row.call.state)}>{callStateText(t, row.call.state)}</StatusTag>
          case 'subProcess':
            return <StatusTag tone={stateTone(row.child.state)}>{stateText(t, row.child.state)}</StatusTag>
          case 'attempt':
            return (
              <StatusTag tone={attemptOutcomeTone(row.attempt.outcome)}>
                {attemptOutcomeText(t, row.attempt.outcome)}
              </StatusTag>
            )
        }
      },
      sortValue: (row) => (row.kind === 'run' ? historyStateRank(row.run.state) : null),
      filter: {
        kind: 'enum',
        value: (row) => (row.kind === 'run' ? row.run.state : null),
        options: runStates.map((value) => ({ value, label: runStateText(t, value) })),
      },
    },
    {
      key: 'startedAt',
      kind: 'fixed',
      title: t('shell.inspector.startedAt'),
      render: (_v, row) => {
        const time = startedAt(row)
        return time === null ? null : <EventTime value={time} />
      },
      sortValue: (row) => startedAt(row),
      filter: { kind: 'date', value: (row) => localDay(startedAt(row)) },
    },
    {
      key: 'duration',
      kind: 'fixed',
      title: t('shell.inspector.duration'),
      render: (_v, row) => {
        const ms = lasted(row)
        return ms === null ? null : formatDuration(ms, locale, t)
      },
      sortValue: (row) => {
        const ms = lasted(row)
        return ms === null ? null : Math.floor(ms / 1000)
      },
      // The filter compares seconds, the smallest unit a person types.
      filter: {
        kind: 'number',
        value: (row) => {
          const ms = lasted(row)
          return ms === null ? null : Math.floor(ms / 1000)
        },
      },
    },
  ]

  return (
    <>
      <AxisTable<TimelineRow>
        testId="inspector-history"
        rowKey={(row) => row.key}
        columns={columns}
        dataSource={rows}
        rowsIn={(row) => (row.rows.length > 0 ? row.rows : null)}
        view={{ query: view.view, onChange: view.setView, pageSizes: tablePageSizes(recordPageSize) }}
        toolbar={{ onRefresh: onChanged }}
        emptyText={t('shell.inspector.noHistory')}
      />
      {resolving?.outputs && (
        <ResolveDialog
          call={resolving}
          outputs={resolving.outputs}
          instanceId={instance.id}
          instancesPath={instancesPath}
          onClose={() => setResolving(undefined)}
          onChanged={onChanged}
        />
      )}
    </>
  )
}
