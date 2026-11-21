import { useMemo } from 'react'
import { fillText } from './fillText'
import { formatDuration, isoDurationMs } from './formatValue'
import {
  transitionId,
  type GraphStep as InstanceStep,
  type GraphTransition as InstanceTransition,
  type InstanceDetail,
  type InstanceRuns,
} from './inspection'
import { ProcessGraphCanvas, type GraphStep, type GraphTransition } from './ProcessGraph'
import { useText } from './texts'

interface InstanceDiagramProps {
  instance: InstanceDetail
  /** The runs of the instance, from `stepRuns`. */
  runs: InstanceRuns
  /** The step that is chosen, which has the border. */
  chosen: string
  /** Called with a step that a click or Find a step chooses, and with the current step that Go to the current step chooses. */
  onChoose: (step: string) => void
  /** The words of a step: its label, or its name where the page has no release texts. */
  stepLabel: (name: string) => string
  /** Shows the author's branch and outcome names and not their labels, for a page with no release texts. */
  stepNames: boolean
  locale: string
}

/**
 * The Diagram tab of an instance: the graph of the release it is pinned to, drawn by `ProcessGraphCanvas` with the run
 * state of each step reached, the transitions taken and the labels of the transitions, see
 * docs/reference/process-inspector-widget.md#process-graph. A card shows the duration of its step's last run, and the
 * count of its runs where there are two or more, such as "2 runs, last 2 sec". An open run has no duration. The page
 * chooses the step, so it can open that step's run in the side panel. The step the instance is at is in view when the page
 * opens. It fills the frame that holds it, and the page loads it in its own chunk, so React Flow stays out of the first load.
 */
export default function InstanceDiagram({
  instance,
  runs: { runs, runStates, taken },
  chosen,
  onChoose,
  stepLabel,
  stepNames,
  locale,
}: InstanceDiagramProps) {
  const t = useText()

  const steps = useMemo(
    () =>
      instance.graph.steps.map((step): GraphStep => {
        const own = runs.filter((run) => run.step === step.name)
        const last = own.at(-1)
        const duration =
          last?.startedAt && last.finishedAt
            ? formatDuration(Date.parse(last.finishedAt) - Date.parse(last.startedAt), locale, t)
            : undefined
        const mark =
          own.length < 2
            ? duration
            : duration === undefined
              ? fillText(t('shell.inspector.graph.runCount'), { count: own.length })
              : fillText(t('shell.inspector.graph.runs'), { count: own.length, duration })
        return { id: step.name, label: stepLabel(step.name), type: step.type, mark }
      }),
    [instance.graph.steps, runs, stepLabel, locale, t],
  )

  const edgeText = t('shell.studio.processGraph.to')
  const transitions = useMemo(
    () =>
      instance.graph.steps.flatMap((step) =>
        step.transitions.map((transition, index): GraphTransition => ({
          id: transitionId(step.name, index),
          source: step.name,
          target: transition.next,
          name: fillText(edgeText, { source: stepLabel(step.name), target: stepLabel(transition.next) }),
          label: transitionLabel(step, transition, stepNames, locale, t),
        })),
      ),
    [instance.graph.steps, edgeText, stepLabel, stepNames, locale, t],
  )

  return (
    <ProcessGraphCanvas
      steps={steps}
      transitions={transitions}
      label={t('shell.inspector.tabs.diagram')}
      testIdPrefix="inspector-graph"
      chosen={chosen}
      onChoose={onChoose}
      current={instance.step}
      runStates={runStates}
      taken={taken}
    />
  )
}

/**
 * The words on a transition, see docs/reference/process-inspector-widget.md#process-graph: the author's label of a branch
 * or an outcome where the release has its text, else a decision branch's name or a task outcome's name. The fallback
 * reads Otherwise, a wait's next line its event's name, and a timeout "After {duration}". A transition with none of
 * these has no label. With `names`, the author's label is left out, as the page has no release texts.
 */
function transitionLabel(
  step: InstanceStep,
  transition: InstanceTransition,
  names: boolean,
  locale: string,
  t: (key: string) => string,
): string | undefined {
  if (transition.on === 'otherwise') {
    return t('shell.inspector.graph.otherwise')
  }
  if (transition.on === 'timeout' && transition.after !== null) {
    const ms = isoDurationMs(transition.after)
    return fillText(t('shell.inspector.graph.after'), {
      duration: ms === null ? transition.after : formatDuration(ms, locale, t),
    })
  }
  // The catalog answers a key it does not hold with the key itself.
  const labelled = transition.labelKey === null || names ? undefined : t(transition.labelKey)
  if (labelled !== undefined && labelled !== transition.labelKey) {
    return labelled
  }
  if (transition.branch !== null) {
    return transition.branch
  }
  if (step.type === 'task' && transition.on !== null) {
    return transition.on
  }
  return step.type === 'waitEvent' && transition.on === null ? (step.event ?? undefined) : undefined
}
