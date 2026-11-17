import { Fragment, type ReactNode } from 'react'
import type { InstanceDetail } from './inspection'
import { durationText, stepRuns } from './instanceRuns'
import { useMinuteClock } from './relativeTime'
import { problemText } from './stepProblems'
import { useText } from './texts'

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
 * failed instance stopped at and why, whom or what a waiting instance waits for and for how long, or nothing once it
 * has ended.
 */
export function useInstanceStatus(
  instance: InstanceDetail,
  stepLabel: (name: string) => string,
  locale: string,
): { kind: 'error' | 'info'; text: ReactNode } | null {
  const t = useText()
  const now = useMinuteClock()
  if (instance.endedAt !== null && instance.state !== 'failed') {
    return null
  }
  const runs = stepRuns(instance)
  const current = runs.at(-1)
  if (current === undefined) {
    return null
  }
  // How long it has waited reads in one sentence at any age, where a time ago or a date would not.
  const since = durationText(current.startedAt, now.toISOString(), locale)
  if (instance.state === 'failed') {
    const call = current.calls.findLast((candidate) => candidate.state === 'failed')
    const last = call?.attempts.at(-1)
    const error =
      problemText(t, current.problem, current.error) ?? (last ? problemText(t, last.problem, last.error) : null)
    return {
      kind: 'error',
      text: filled(t('shell.inspector.status.failed'), {
        step: stepLabel(current.step),
        error: error ?? t('shell.inspector.status.noError'),
      }),
    }
  }
  // Proposed, see D155 and the review on #3650: an instance at several steps names each, with whom it waits for.
  const waiting = runs.filter((run) => run.finishedAt === null)
  const whom = (run: (typeof runs)[number]) => {
    const open = run.tasks.find((candidate) => candidate.outcome === null)
    return open === undefined
      ? stepLabel(run.step)
      : t('shell.inspector.status.stepFor')
          .replace('{step}', stepLabel(run.step))
          .replace(
            '{assignee}',
            open.assigneeKind === 'role'
              ? t('shell.inspector.timeline.role').replace('{role}', open.assignee)
              : open.assignee,
          )
  }
  if (waiting.length > 1) {
    return {
      kind: 'info',
      text: t('shell.inspector.status.waitingAtSteps')
        .replace('{count}', String(waiting.length))
        .replace('{steps}', new Intl.ListFormat(locale, { type: 'conjunction' }).format(waiting.map(whom)))
        .replace('{since}', since),
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
