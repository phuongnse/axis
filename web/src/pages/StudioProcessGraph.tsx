import { useDroppable } from '@dnd-kit/core'
import { BaseEdge, EdgeLabelRenderer, type EdgeProps, getBezierPath, type OnConnectEnd } from '@xyflow/react'
import { type KeyboardEvent, useMemo, useRef } from 'react'
import { ProcessGraphCanvas, type ProcessGraphStep, type ProcessGraphTransitionView } from '../platform/ProcessGraph'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { type ProcessGraph, type ProcessGraphTransition, transitionName } from './processGraph'

/**
 * An edge drawn as the default edge is, with a drop target at its midpoint: a transition that a palette entry can be
 * dropped on. The target takes no pointer events, so a click still reaches the edge, and the drag library measures it
 * all the same. The drop target has no transform of its own, because the drag library measures an element without it.
 */
function TransitionEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  label,
  labelStyle,
  labelBgStyle,
  markerEnd,
  style,
}: EdgeProps) {
  const [path, labelX, labelY] = getBezierPath({ sourceX, sourceY, sourcePosition, targetX, targetY, targetPosition })
  const { colour } = useDesignTokens()
  const { setNodeRef, isOver } = useDroppable({ id: `edge:${id}`, data: { pointer: id } })
  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        label={label}
        labelX={labelX}
        labelY={labelY}
        labelStyle={labelStyle}
        labelBgStyle={labelBgStyle}
        markerEnd={markerEnd}
        style={style}
      />
      <EdgeLabelRenderer>
        <div
          style={{
            position: 'absolute',
            transform: `translate(${labelX}px, ${labelY}px)`,
            pointerEvents: 'none',
          }}
        >
          <div
            ref={setNodeRef}
            data-testid={`studio-process-edge-drop-${id}`}
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
        </div>
      </EdgeLabelRenderer>
    </>
  )
}

const dropWidth = 56
const dropHeight = 28

const edgeTestIdPrefix = 'rf__edge-'

/**
 * The graph of the draft's steps and transitions, laid out again after every edit. A node selects its step, and an
 * edge selects itself and the step it leaves. The selected step is the node marked current, and the selected edge is
 * drawn in the primary colour. A step that a diagnostic path names has an error-coloured border and "has diagnostics"
 * in its label. Tab reaches the edges, which come before the nodes, and each edge is named by its two steps and its
 * branch or outcome. Enter or Space on an edge selects it. Delete or Backspace in the canvas calls `onRemove`. The
 * canvas and the midpoint of each edge are drop targets of the process designer's palette: the canvas for `/steps`, and
 * an edge for the path of its transition. Every step but an `end` step has a handle at its bottom edge. Dragging from
 * it and releasing on a step, anywhere on the node, calls `onConnect` with the two step numbers and the release point,
 * relative to the canvas. The graph sets no transition itself. It has no keyboard way to connect, which the step
 * panel's pickers give. With `readOnly` no step has a handle, a connect does nothing and Delete or Backspace calls
 * nothing, so the graph only shows the process. `layerMarks` names the steps, by number, that a customization layer added
 * or changed. Such a node says so in its label and in a visible mark.
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
  const to = t('shell.studio.processGraph.to')
  const section = useRef<HTMLElement>(null)
  const { setNodeRef } = useDroppable({ id: 'process-canvas', data: { pointer: '/steps' } })

  // Proposed: the designer edits the one drawing of a process, which a process instance's page shows too, see the
  // review on #3650.
  const steps = useMemo(
    () =>
      graph.steps.map((step): ProcessGraphStep => {
        const layerMark = layerMarks?.get(step.index)
        const note =
          layerMark === undefined ? undefined : { text: t(`shell.studio.module.${layerMark}`), kind: layerMark }
        const label = `${step.name ?? ''}, ${step.type ?? ''}${note === undefined ? '' : `, ${note.text}`}`
        return {
          id: String(step.index),
          name: step.name ?? '',
          type: step.type ?? '',
          start: graph.start === step.index,
          tone: step.marked ? 'error' : selected === step.index ? 'selected' : 'plain',
          ariaLabel: step.marked ? `${label}, ${t('shell.studio.processGraph.hasDiagnostics')}` : label,
          ariaCurrent: selected === step.index,
          note,
          connectable: !readOnly && step.type !== 'end',
          testId: `studio-graph-node-${step.index}`,
          handleTestId: `studio-graph-handle-${step.index}`,
        }
      }),
    [graph, selected, readOnly, layerMarks, t],
  )
  const transitions = useMemo(
    () =>
      graph.transitions.map((transition): ProcessGraphTransitionView => ({
        id: transition.path,
        source: String(transition.source),
        target: String(transition.target),
        label: transition.label ?? transition.kind,
        ariaLabel: transitionName(graph, transition, to),
        tone: transition.path === selectedEdge ? 'selected' : 'plain',
        ariaCurrent: transition.path === selectedEdge,
      })),
    [graph, selectedEdge, to],
  )

  // A drag ends on a node when React Flow snaps to its handle, and anywhere else on the node otherwise, so the node
  // under the pointer is the target. `onConnect` is not set, or React Flow would report a snapped drag twice.
  const onConnectEnd: OnConnectEnd = (event, state) => {
    const source = state.fromNode?.id
    if (source === undefined) {
      return
    }
    const touch = 'changedTouches' in event ? event.changedTouches[0] : undefined
    const x = touch?.clientX ?? (event as MouseEvent).clientX
    const y = touch?.clientY ?? (event as MouseEvent).clientY
    const target =
      state.toNode?.id ??
      document.elementFromPoint(x, y)?.closest('.react-flow__node')?.getAttribute('data-id') ??
      undefined
    if (target === undefined) {
      return
    }
    const box = section.current?.getBoundingClientRect()
    onConnect(Number(source), Number(target), { x: x - (box?.left ?? 0), y: y - (box?.top ?? 0) })
  }

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault()
      if (!readOnly) {
        onRemove()
      }
      return
    }
    if (event.key !== 'Enter' && event.key !== ' ') {
      return
    }
    const element =
      event.target instanceof Element ? event.target.closest(`[data-testid^="${edgeTestIdPrefix}"]`) : null
    const path = element?.getAttribute('data-testid')?.slice(edgeTestIdPrefix.length)
    const transition = graph.transitions.find((candidate) => candidate.path === path)
    if (transition) {
      event.preventDefault()
      onSelectEdge(transition)
    }
  }

  return (
    <ProcessGraphCanvas
      steps={steps}
      transitions={transitions}
      ariaLabel={t('shell.studio.processGraph')}
      testId="studio-process-graph"
      edgeType={TransitionEdge}
      sectionRef={(element: HTMLElement | null) => {
        section.current = element
        setNodeRef(element)
      }}
      onKeyDown={onKeyDown}
      onStepClick={(id) => onSelect(Number(id))}
      onTransitionClick={(id) => {
        const transition = graph.transitions.find((candidate) => candidate.path === id)
        if (transition) {
          onSelectEdge(transition)
        }
      }}
      onConnectEnd={readOnly ? undefined : onConnectEnd}
    />
  )
}
