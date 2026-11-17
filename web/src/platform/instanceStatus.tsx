import { Fragment, type ReactNode } from 'react'
import type { InstanceDetail } from './inspection'
import { stepRuns } from './instanceRuns'
import { useText } from './texts'
import { TimeAgo } from './TimeAgo'

/** One text whose placeholders, such as `{process}`, take a node, so a link stays inside its sentence. */
export function filled(text: string, values: Record<string, ReactNode>): ReactNode {
  const parts = text.split(/(\{[a-z]+\})/i)
  return parts.map((part, index) => {
    const name = /^\{([a-z]+)\}$/i.exec(part)?.[1]
    return <Fragment key={index}>{name !== undefined && name in values ? values[name] : part}</Fragment>
  })
}

/**
 * Proposed, see the review on #3650: where the instance stands, in one sentence, for the top of its page: the step a
 * failed instance stopped at and why, whom or what a waiting instance waits for and since when, or nothing once it
 * has ended.
 */
export function useInstanceStatus(
  instance: InstanceDetail,
  stepLabel: (name: string) => string,
  locale: string,
): { kind: 'error' | 'info'; text: ReactNode } | null {
  const t = useText()
  if (instance.endedAt !== null && instance.state !== 'failed') {
    return null
  }
  const runs = stepRuns(instance)
  const current = runs.at(-1)
  if (current === undefined) {
    return null
  }
  const since = <TimeAgo at={current.startedAt} locale={locale} />
  if (instance.state === 'failed') {
    const call = current.calls.findLast((candidate) => candidate.state === 'failed')
    const error = current.error ?? call?.attempts.at(-1)?.error ?? null
    return {
      kind: 'error',
      text: filled(t('shell.inspector.status.failed'), {
        step: stepLabel(current.step),
        error: error ?? t('shell.inspector.status.noError'),
      }),
    }
  }
  const task = current.tasks.find((candidate) => candidate.outcome === null)
  return {
    kind: 'info',
    text: filled(task === undefined ? t('shell.inspector.status.waiting') : t('shell.inspector.status.waitingFor'), {
      step: stepLabel(current.step),
      since,
      assignee:
        task === undefined
          ? ''
          : task.assigneeKind === 'role'
            ? t('shell.inspector.timeline.role').replace('{role}', task.assignee)
            : task.assignee,
    }),
  }
}
