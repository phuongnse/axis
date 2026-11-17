import { Flex, Tag, Typography } from 'antd'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisButton } from './AxisButton'
import { AxisTable, type AxisColumn } from './AxisTable'
import { inspectorPath } from './inspectorPages'
import type { CallAttempt, InstanceCall, InstanceDetail, InstanceTask, SubProcess } from './inspection'
import { durationText, stepRuns, type StepRun } from './instanceRuns'
import { filled } from './instanceStatus'
import { ResolveDialog } from './ResolveDialog'
import { useText } from './texts'
import { TimeAgo } from './TimeAgo'

/** One row of the timeline, with the rows inside it, which the table folds. */
interface TimelineRow {
  key: string
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
  /** Shows only the runs of this step, until Show all clears it. */
  only: string | null
  onShowAll: () => void
  onChanged: () => void
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
  only,
  onShowAll,
  onChanged,
}: InstanceTimelineProps) {
  const t = useText()
  const [resolving, setResolving] = useState<InstanceCall | null>(null)
  const runs = stepRuns(instance).filter((run) => only === null || run.step === only)
  // A run not yet finished lasts until the page was drawn.
  const [now] = useState(() => new Date().toISOString())
  const typeOf = (step: string) => instance.graph.steps.find((candidate) => candidate.name === step)?.type ?? ''
  const typeText = (type: string) => {
    const key = `shell.inspector.stepType.${type}`
    const text = t(key)
    return text === key ? type : text
  }
  const assigneeText = (task: InstanceTask) =>
    task.assigneeKind === 'role' ? t('shell.inspector.timeline.role').replace('{role}', task.assignee) : task.assignee
  const targetOf = (call: InstanceCall) => `${call.connector ?? `${call.package}@${call.version}`} · ${call.operation}`

  const callSummary = (call: InstanceCall) => {
    const target = targetOf(call)
    const count = call.attempts.length
    if (call.state === 'failed') {
      return t('shell.inspector.timeline.callFailed').replace('{call}', target).replace('{count}', String(count))
    }
    if (call.state === 'succeeded') {
      return count > 1
        ? t('shell.inspector.timeline.callRetried').replace('{call}', target).replace('{number}', String(count))
        : t('shell.inspector.timeline.callSucceeded').replace('{call}', target)
    }
    return t('shell.inspector.timeline.callPending').replace('{call}', target)
  }

  const summary = (run: StepRun): ReactNode => {
    const lines: ReactNode[] = []
    for (const task of run.tasks) {
      lines.push(
        task.outcome === null
          ? t('shell.inspector.timeline.taskOpen').replace('{assignee}', assigneeText(task))
          : t('shell.inspector.timeline.taskDone')
              .replace('{person}', task.completedBy ?? assigneeText(task))
              .replace('{outcome}', outcomeLabel(run.step, task.outcome)),
      )
    }
    for (const call of run.calls) {
      lines.push(callSummary(call))
    }
    for (const child of run.subProcesses) {
      const page = inspectors?.get(child.process)
      const name =
        page === undefined ? (
          child.process
        ) : (
          <Link to={inspectorPath(sitePath, page, child.id)} data-testid={`inspector-subprocess-${child.id}`}>
            {child.process}
          </Link>
        )
      const key = `shell.inspector.states.${child.state}`
      lines.push(
        filled(t('shell.inspector.timeline.subProcess'), {
          process: name,
          state: t(key) === key ? child.state : t(key).toLowerCase(),
        }),
      )
    }
    if (run.error !== null) {
      lines.push(run.error)
    } else if (run.decision !== null && run.tasks.length === 0) {
      const target = instance.graph.steps.some((step) => step.name === run.decision)
      lines.push(
        target
          ? t('shell.inspector.timeline.wentTo').replace('{step}', stepLabel(run.decision))
          : outcomeLabel(run.step, run.decision),
      )
    }
    if (lines.length === 0) {
      lines.push(run.finishedAt === null ? t('shell.inspector.timeline.waiting') : t('shell.inspector.timeline.done'))
    }
    return (
      <Flex vertical>
        {lines.map((line, index) => (
          <span key={index}>{line}</span>
        ))}
      </Flex>
    )
  }

  const stateWords = (value: 'done' | 'failed' | 'waiting' | 'running' | 'retried') =>
    value === 'done' ? (
      <Typography.Text type="secondary">{t('shell.inspector.timeline.stateDone')}</Typography.Text>
    ) : value === 'retried' ? (
      <Typography.Text type="secondary">{t('shell.inspector.timeline.stateRetried')}</Typography.Text>
    ) : (
      // oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token.
      <Tag color={value === 'failed' ? 'error' : 'processing'}>
        {t(`shell.inspector.timeline.state${value[0].toUpperCase()}${value.slice(1)}`)}
      </Tag>
    )
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
  const attemptRow = (call: InstanceCall, attempt: CallAttempt): TimelineRow => ({
    key: `${call.id}/${attempt.number}`,
    name: named(t('shell.inspector.timeline.attempt').replace('{number}', String(attempt.number)), ''),
    what: (
      <Flex vertical>
        <span>
          {attempt.error === null
            ? attempt.httpStatus === null
              ? t('shell.inspector.timeline.attemptOk')
              : t('shell.inspector.timeline.attemptStatus').replace('{status}', String(attempt.httpStatus))
            : attempt.httpStatus === null
              ? attempt.error
              : t('shell.inspector.timeline.attemptError')
                  .replace('{status}', String(attempt.httpStatus))
                  .replace('{error}', attempt.error)}
        </span>
        {traceId(attempt.traceId)}
      </Flex>
    ),
    state: stateWords(
      attempt.outcome === 'succeeded' ? 'done' : attempt.outcome === 'retryable' ? 'retried' : 'failed',
    ),
    startedAt: attempt.startedAt,
    finishedAt: attempt.finishedAt,
  })
  const callRow = (call: InstanceCall): TimelineRow => {
    const input =
      call.inputHidden || call.input === null || typeof call.input !== 'object'
        ? []
        : Object.entries(call.input as Record<string, unknown>)
    return {
      key: call.id,
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
    name: named(assigneeText(task), t('shell.inspector.stepType.task')),
    what:
      task.outcome === null
        ? t('shell.inspector.timeline.taskOpen').replace('{assignee}', assigneeText(task))
        : t('shell.inspector.timeline.taskDone')
            .replace('{person}', task.completedBy ?? assigneeText(task))
            .replace('{outcome}', outcomeLabel(task.step, task.outcome)),
    state: stateWords(task.completedAt === null ? 'waiting' : 'done'),
    startedAt: task.createdAt,
    finishedAt: task.completedAt,
  })
  const childRow = (child: SubProcess): TimelineRow => {
    const page = inspectors?.get(child.process)
    const key = `shell.inspector.states.${child.state}`
    return {
      key: child.id,
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
  const runRow = (run: StepRun): TimelineRow => {
    const children = [...run.tasks.map(taskRow), ...run.calls.map(callRow), ...run.subProcesses.map(childRow)].sort(
      (a, b) => (b.startedAt ?? '').localeCompare(a.startedAt ?? ''),
    )
    return {
      key: run.key,
      name: named(
        stepLabel(run.step),
        run.number > 1
          ? t('shell.inspector.timeline.typeRun')
              .replace('{type}', typeText(typeOf(run.step)))
              .replace('{number}', String(run.number))
          : typeText(typeOf(run.step)),
      ),
      what: summary(run),
      state: stateWords(run.state),
      startedAt: run.startedAt,
      finishedAt: run.finishedAt,
      children: children.length > 0 ? children : undefined,
    }
  }

  const columns: AxisColumn<TimelineRow>[] = [
    { key: 'step', kind: 'freeText', title: t('shell.inspector.step'), render: (_v, row) => row.name },
    { key: 'what', kind: 'freeText', title: t('shell.inspector.timeline.what'), render: (_v, row) => row.what },
    { key: 'state', kind: 'fixed', title: t('shell.inspector.state'), render: (_v, row) => row.state },
    {
      key: 'startedAt',
      kind: 'fixed',
      title: t('shell.inspector.startedAt'),
      // Proposed: a run's start is how long ago it was, the exact time its tooltip, see the review on #3650.
      render: (_v, row) => (row.startedAt === null ? null : <TimeAgo at={row.startedAt} locale={locale} />),
    },
    {
      // Proposed: how long a run, a call or an attempt lasted is its Duration, the one term for it, see the review on
      // #3650.
      key: 'duration',
      kind: 'fixed',
      title: t('shell.inspector.timeline.duration'),
      render: (_v, row) => (row.startedAt === null ? null : durationText(row.startedAt, row.finishedAt ?? now, locale)),
    },
  ]

  return (
    <Flex vertical gap="middle" data-testid="inspector-timeline">
      {only !== null && (
        <Flex align="center" gap="small">
          <span>{t('shell.inspector.timeline.only').replace('{step}', stepLabel(only))}</span>
          <AxisButton role="link" onClick={onShowAll}>
            {t('shell.inspector.timeline.showAll')}
          </AxisButton>
        </Flex>
      )}
      <AxisTable<TimelineRow>
        rowKey={(row) => row.key}
        columns={columns}
        // Records in time are newest first, as docs/reference/actions-and-controls.md#list-order says.
        dataSource={runs.map(runRow).reverse()}
        emptyText={t('shell.inspector.noHistory')}
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
