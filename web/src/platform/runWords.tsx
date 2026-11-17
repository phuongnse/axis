import { Flex } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { inspectorPath } from './inspectorPages'
import type { InstanceCall, InstanceDetail, InstanceTask } from './inspection'
import type { StepRun } from './instanceRuns'
import { filled } from './instanceStatus'
import { StatusTag } from './StatusTag'
import { problemText } from './stepProblems'
import { useText } from './texts'

/**
 * Proposed, see the review on #3650: the words that tell what a run of a step did, which the timeline and the graph's
 * panel of a step share: who chose which outcome, where a decision went, how a call went over its attempts, which
 * sub-process ran, and each state as a `StatusTag` in the tone of what it asks of the reader.
 */
export function useRunWords({
  instance,
  stepLabel,
  outcomeLabel,
  inspectors,
  sitePath,
}: {
  instance: InstanceDetail
  stepLabel: (name: string) => string
  outcomeLabel: (step: string, name: string) => string
  inspectors: ReadonlyMap<string, string> | undefined
  sitePath: string
}) {
  const t = useText()
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
    const error = problemText(t, run.problem, run.error)
    if (error !== null) {
      lines.push(error)
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

  // Proposed: every state of the timeline is a StatusTag, its tone saying what it asks of the reader, as the
  // instance's own state is, see the reviews on #3263 and #3650: done ended well, retried and failed what they say, a
  // wait for a person asks for attention, and running or a wait for an event needs nobody.
  const stateWords = (value: 'done' | 'failed' | 'waiting' | 'running' | 'retried', forPerson = false) => (
    <StatusTag
      tone={
        value === 'done'
          ? 'success'
          : value === 'failed'
            ? 'danger'
            : value === 'retried'
              ? 'muted'
              : value === 'waiting' && forPerson
                ? 'attention'
                : 'neutral'
      }
    >
      {t(`shell.inspector.timeline.state${value[0].toUpperCase()}${value.slice(1)}`)}
    </StatusTag>
  )
  return { typeOf, typeText, assigneeText, targetOf, callSummary, summary, stateWords }
}
