import { Flex, Tag, Typography } from 'antd'
import { useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisButton } from './AxisButton'
import { AxisTable, type AxisColumn } from './AxisTable'
import { NotSet } from './EmptyValue'
import { inspectorPath } from './inspectorPages'
import type { CallAttempt, InstanceCall, InstanceDetail, InstanceTask } from './inspection'
import { durationText, stepRuns, type StepRun } from './instanceRuns'
import { filled } from './instanceStatus'
import { ResolveDialog } from './ResolveDialog'
import { SectionPart, SectionParts } from './SectionParts'
import { TermList, type Term } from './TermList'
import { useText } from './texts'

interface InstanceTimelineProps {
  instance: InstanceDetail
  instancesPath: string
  sitePath: string
  inspectors: ReadonlyMap<string, string> | undefined
  locale: string
  stepLabel: (name: string) => string
  /** The label of a transition out of a step, such as a task's outcome, from the process's texts, else its name. */
  outcomeLabel: (step: string, name: string) => string
  time: (name: string, value: string | null) => ReactNode
  /** Shows only the runs of this step, until Show all clears it. */
  only: string | null
  onShowAll: () => void
  onChanged: () => void
}

/**
 * Proposed, see the review on #3650: what an instance did, one row for each run of a step, oldest first, as event
 * histories show it. A row says what happened in a sentence: who chose which outcome, where a decision went, how a
 * call went over its attempts, which sub-process ran; a failed or waiting run is marked. Opening a row shows its
 * times, its trace ID, and each task, call and sub-process it started, with a call's input, attempts and Resolve.
 */
export function InstanceTimeline({
  instance,
  instancesPath,
  sitePath,
  inspectors,
  locale,
  stepLabel,
  outcomeLabel,
  time,
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

  const state = (run: StepRun) =>
    run.state === 'done' ? (
      <Typography.Text type="secondary">{t('shell.inspector.timeline.stateDone')}</Typography.Text>
    ) : (
      // oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token.
      <Tag color={run.state === 'failed' ? 'error' : 'processing'}>
        {t(`shell.inspector.timeline.state${run.state[0].toUpperCase()}${run.state.slice(1)}`)}
      </Tag>
    )

  const columns: AxisColumn<StepRun>[] = [
    {
      key: 'step',
      kind: 'freeText',
      title: t('shell.inspector.step'),
      render: (_v, run) => (
        <Flex vertical>
          <Typography.Text strong>{stepLabel(run.step)}</Typography.Text>
          <Typography.Text type="secondary">
            {run.number > 1
              ? t('shell.inspector.timeline.typeRun')
                  .replace('{type}', typeText(typeOf(run.step)))
                  .replace('{number}', String(run.number))
              : typeText(typeOf(run.step))}
          </Typography.Text>
        </Flex>
      ),
    },
    { key: 'what', kind: 'freeText', title: t('shell.inspector.timeline.what'), render: (_v, run) => summary(run) },
    { key: 'state', kind: 'fixed', title: t('shell.inspector.state'), render: (_v, run) => state(run) },
    {
      key: 'startedAt',
      kind: 'fixed',
      title: t('shell.inspector.startedAt'),
      render: (_v, run) => time('startedAt', run.startedAt),
    },
    {
      key: 'duration',
      kind: 'fixed',
      title: t('shell.inspector.timeline.duration'),
      render: (_v, run) => durationText(run.startedAt, run.finishedAt ?? now, locale),
    },
  ]

  const attemptColumns: AxisColumn<CallAttempt>[] = [
    { key: 'number', kind: 'fixed', title: t('shell.inspector.number'), render: (_v, row) => row.number },
    {
      key: 'startedAt',
      kind: 'fixed',
      title: t('shell.inspector.startedAt'),
      render: (_v, row) => time('startedAt', row.startedAt),
    },
    {
      key: 'duration',
      kind: 'fixed',
      title: t('shell.inspector.timeline.duration'),
      render: (_v, row) => durationText(row.startedAt, row.finishedAt, locale),
    },
    { key: 'outcome', kind: 'fixed', title: t('shell.inspector.outcome'), render: (_v, row) => row.outcome },
    {
      key: 'httpStatus',
      kind: 'fixed',
      title: t('shell.inspector.httpStatus'),
      render: (_v, row) => row.httpStatus ?? null,
    },
    { key: 'error', kind: 'freeText', title: t('shell.inspector.error'), render: (_v, row) => row.error ?? null },
  ]

  const traceId = (value: string | null) =>
    value === null ? (
      <NotSet />
    ) : (
      <Typography.Text code copyable>
        {value}
      </Typography.Text>
    )

  const details = (run: StepRun): ReactNode => (
    <SectionParts>
      <SectionPart testId={`inspector-run-${run.key}`}>
        <TermList
          terms={[
            {
              key: 'startedAt',
              term: t('shell.inspector.startedAt'),
              value: time('startedAt', run.startedAt),
              width: 6,
            },
            {
              key: 'finishedAt',
              term: t('shell.inspector.finishedAt'),
              value: run.finishedAt === null ? <NotSet /> : time('finishedAt', run.finishedAt),
              width: 6,
            },
            { key: 'traceId', term: t('shell.inspector.traceId'), value: traceId(run.traceId) },
          ]}
        />
      </SectionPart>
      {run.tasks.map((task) => (
        <SectionPart key={task.id} testId={`inspector-task-${task.id}`} heading={{ title: stepLabel(task.step) }}>
          <TermList
            terms={[
              { key: 'assignee', term: t('shell.inspector.assignee'), value: assigneeText(task) },
              {
                key: 'outcome',
                term: t('shell.inspector.outcome'),
                value: task.outcome === null ? <NotSet /> : outcomeLabel(task.step, task.outcome),
                width: 6,
              },
              {
                key: 'completedBy',
                term: t('shell.inspector.completedBy'),
                value: task.completedBy ?? <NotSet />,
                width: 6,
              },
            ]}
          />
        </SectionPart>
      ))}
      {run.calls.map((call) => {
        const input =
          call.inputHidden || call.input === null || typeof call.input !== 'object'
            ? []
            : Object.entries(call.input as Record<string, unknown>)
        const inputTerms: Term[] = call.inputHidden
          ? [{ key: 'input', term: t('shell.inspector.input'), value: t('shell.inspector.hidden') }]
          : input.map(([name, value]) => ({
              key: `input-${name}`,
              term: name,
              value: typeof value === 'object' ? JSON.stringify(value) : String(value),
            }))
        return (
          <SectionPart
            key={call.id}
            testId={`inspector-call-${call.id}`}
            heading={{ title: targetOf(call), line: callSummary(call) }}
            action={
              call.resolvable && call.outputs ? (
                <AxisButton
                  role="secondary"
                  data-testid={`inspector-resolve-${call.id}`}
                  onClick={() => setResolving(call)}
                >
                  {t('shell.inspector.commands.resolve')}
                </AxisButton>
              ) : undefined
            }
          >
            <Flex vertical gap="middle">
              {inputTerms.length > 0 && <TermList terms={inputTerms} />}
              <AxisTable<CallAttempt>
                rowKey={(row) => `${row.kind}|${row.number}|${row.startedAt}`}
                columns={attemptColumns}
                dataSource={call.attempts}
                emptyText={t('shell.inspector.noAttempts')}
              />
            </Flex>
          </SectionPart>
        )
      })}
    </SectionParts>
  )

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
      <AxisTable<StepRun>
        rowKey={(run) => run.key}
        columns={columns}
        dataSource={runs}
        emptyText={t('shell.inspector.noHistory')}
        details={{
          content: (run) => (run.tasks.length + run.calls.length > 0 || run.traceId !== null ? details(run) : null),
          name: (run) => stepLabel(run.step),
        }}
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
