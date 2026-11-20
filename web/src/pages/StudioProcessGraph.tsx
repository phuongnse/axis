import { useDroppable } from '@dnd-kit/core'
import { type KeyboardEvent, useCallback, useMemo } from 'react'
import { fillText } from '../platform/fillText'
import { type GraphStep, type GraphTransition, ProcessGraphCanvas } from '../platform/ProcessGraph'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { type ProcessGraph, type ProcessGraphTransition, transitionName } from './processGraph'

const dropWidth = 56
const dropHeight = 28

/**
 * The drop target at the middle of a transition's route: a transition that a palette entry can be dropped on. It takes
 * no pointer events, so a click still reaches the route, and the drag library measures it all the same. It has no
 * transform of its own, because the drag library measures an element without it.
 */
function TransitionDropTarget({ path }: { path: string }) {
  const { colour } = useDesignTokens()
  const { setNodeRef, isOver } = useDroppable({ id: `edge:${path}`, data: { pointer: path } })
  return (
    <div
      ref={setNodeRef}
      data-testid={`studio-process-edge-drop-${path}`}
      style={{
        position: 'absolute',
        left: -dropWidth / 2,
        top: -dropHeight / 2,
        width: dropWidth,
        height: dropHeight,
        pointerEvents: 'none',
        outline: isOver ? `2px solid ${colour.primary}` : undefined,
      }}
    />
  )
}

/**
 * The graph of the draft's steps and transitions, drawn by `ProcessGraphCanvas` with its steps laid out from left to
 * right, see docs/reference/process-designer.md#canvas and #transitions. A card selects its step, and a transition
 * selects itself and the step it leaves. The selected step has `aria-current`, and the selected transition is drawn in
 * the primary colour. A step that a diagnostic path names has an error-coloured border and "has problems" in its name.
 * Tab reaches the transitions, which come before the cards, and each transition is named by its two steps and its branch
 * or outcome. Enter or Space on a transition selects it. Delete or Backspace in the graph calls `onRemove`, except in a
 * text field, such as Find a step's search. The graph and the middle of each transition's route are drop targets of the
 * process designer's palette: the graph for `/steps`, and a transition for its path. Every step but an `end` step has a handle on its card's right edge. Dragging
 * from it and releasing on a step, anywhere on the card, calls `onConnect` with the two step numbers and the release
 * point, relative to the graph. The graph sets no transition itself. It has no keyboard way to connect, which the step
 * panel's pickers give. With `readOnly` no step has a handle and Delete or Backspace calls nothing, so the graph only
 * shows the process. `layerMarks` names the steps, by number, that a customization layer added or changed. Such a card
 * says so in its name and in a visible line.
 */
export default function StudioProcessGraph({
  graph,
  selected,
  selectedEdge,
  onSelect,
  onSelectEdge,
  onRemove,
  onConnect,
  readOnly = false,
  layerMarks,
}: {
  graph: ProcessGraph
  selected: number | null
  selectedEdge: string | null
  onSelect: (index: number) => void
  onSelectEdge: (transition: ProcessGraphTransition) => void
  onRemove: () => void
  onConnect: (source: number, target: number, point: { x: number; y: number }) => void
  readOnly?: boolean
  layerMarks?: ReadonlyMap<number, 'added' | 'changed'>
}) {
  const t = useText()
  const edgeText = t('shell.studio.processGraph.to')
  const { setNodeRef } = useDroppable({ id: 'process-canvas', data: { pointer: '/steps' } })

  const steps = useMemo(
    () =>
      graph.steps.map((step): GraphStep => {
        const mark = layerMarks?.get(step.index)
        const word = mark && t(`shell.studio.module.${mark}`)
        const base = `${step.name ?? ''}, ${step.type ?? ''}${word ? `, ${word}` : ''}`
        return {
          id: String(step.index),
          label: step.name ?? '',
          type: step.type ?? '',
          marked: step.marked,
          name: step.marked ? fillText(t('shell.studio.processGraph.hasDiagnostics'), { step: base }) : base,
          mark: word,
          connectable: step.type !== 'end',
        }
      }),
    [graph, layerMarks, t],
  )
  const transitions = useMemo(
    () =>
      graph.transitions.map((transition): GraphTransition => ({
        id: transition.path,
        source: String(transition.source),
        target: String(transition.target),
        name: transitionName(graph, transition, edgeText),
        label: transition.label ?? transition.kind,
      })),
    [graph, edgeText],
  )

  const choose = useCallback((id: string) => onSelect(Number(id)), [onSelect])
  const chooseTransition = useCallback(
    (id: string) => {
      const transition = graph.transitions.find((candidate) => candidate.path === id)
      if (transition) {
        onSelectEdge(transition)
      }
    },
    [graph, onSelectEdge],
  )
  const connect = useCallback(
    (source: string, target: string, point: { x: number; y: number }) =>
      onConnect(Number(source), Number(target), point),
    [onConnect],
  )
  const transitionExtra = useCallback((id: string) => <TransitionDropTarget path={id} />, [])

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    // A key typed into Find a step's search is for the text, so it removes nothing.
    if (event.target instanceof Element && event.target.closest('input, textarea, [contenteditable]') !== null) {
      return
    }
    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault()
      if (!readOnly) {
        onRemove()
      }
    }
  }

  return (
    <div
      ref={setNodeRef}
      data-testid="studio-process-graph"
      style={{ height: 480, position: 'relative' }}
      onKeyDown={onKeyDown}
    >
      <ProcessGraphCanvas
        steps={steps}
        transitions={transitions}
        label={t('shell.studio.processGraph')}
        testIdPrefix="studio-graph"
        chosen={selected === null ? undefined : String(selected)}
        onChoose={choose}
        chosenTransition={selectedEdge ?? undefined}
        onChooseTransition={chooseTransition}
        onConnect={readOnly ? undefined : connect}
        transitionExtra={transitionExtra}
      />
    </div>
  )
}
