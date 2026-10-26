import dagre from '@dagrejs/dagre'
import { useDroppable } from '@dnd-kit/core'
import {
  BaseEdge,
  type Edge,
  EdgeLabelRenderer,
  type EdgeProps,
  getBezierPath,
  Handle,
  MarkerType,
  type Node,
  type NodeProps,
  type OnConnectEnd,
  Position,
  ReactFlow,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { type KeyboardEvent, useMemo, useRef } from 'react'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { type ProcessGraph, type ProcessGraphTransition, transitionName } from './processGraph'

const nodeWidth = 200
const nodeHeight = 48
const handleSize = 10

type StepNodeData = {
  index: number
  name: string
  type: string
  start: boolean
  current: boolean
  marked: boolean
  connectable: boolean
}

type StepNode = Node<StepNodeData, 'step'>

function placeSteps(graph: ProcessGraph): (index: number) => { x: number; y: number } {
  const layout = new dagre.graphlib.Graph()
  layout.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 56 })
  layout.setDefaultEdgeLabel(() => ({}))
  for (const step of graph.steps) {
    layout.setNode(String(step.index), { width: nodeWidth, height: nodeHeight })
  }
  for (const transition of graph.transitions) {
    layout.setEdge(String(transition.source), String(transition.target))
  }
  dagre.layout(layout)
  return (index) => {
    const placed = layout.node(String(index))
    return { x: placed.x - nodeWidth / 2, y: placed.y - nodeHeight / 2 }
  }
}

function StepNodeView({ data }: NodeProps<StepNode>) {
  const t = useText()
  const { colour, radius } = useDesignTokens()
  const label = `${data.name} · ${data.type}`
  return (
    <>
      <Handle type="target" position={Position.Top} isConnectable={false} />
      <button
        type="button"
        data-testid={`studio-graph-node-${data.index}`}
        aria-label={data.marked ? `${label} · ${t('shell.studio.processGraph.hasDiagnostics')}` : label}
        aria-current={data.current ? 'true' : undefined}
        data-marked={data.marked ? 'true' : undefined}
        style={{
          width: '100%',
          height: '100%',
          padding: '0 8px',
          overflow: 'hidden',
          textAlign: 'left',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          font: 'inherit',
          color: colour.text,
          background: colour.surface,
          border: `${data.start ? 3 : 1}px solid ${data.marked ? colour.danger : data.current ? colour.primary : colour.border}`,
          borderRadius: radius.base,
          cursor: 'pointer',
        }}
      >
        <strong>{data.name}</strong> <span>{data.type}</span>
      </button>
      <Handle
        type="source"
        position={Position.Bottom}
        isConnectable={data.connectable}
        data-testid={`studio-graph-handle-${data.index}`}
        aria-hidden
        style={{
          width: handleSize,
          height: handleSize,
          background: colour.primary,
          border: `1px solid ${colour.surface}`,
          cursor: data.connectable ? 'crosshair' : 'default',
          display: data.connectable ? undefined : 'none',
        }}
      />
    </>
  )
}

const nodeTypes = { step: StepNodeView }

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

const edgeTypes = { transition: TransitionEdge }
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
 * panel's pickers give.
 */
export default function StudioProcessGraph({
  graph,
  selected,
  selectedEdge,
  onSelect,
  onSelectEdge,
  onRemove,
  onConnect,
}: {
  graph: ProcessGraph
  selected: number | null
  selectedEdge: string | null
  onSelect: (index: number) => void
  onSelectEdge: (transition: ProcessGraphTransition) => void
  onRemove: () => void
  onConnect: (source: number, target: number, point: { x: number; y: number }) => void
}) {
  const t = useText()
  const { mode } = useThemeMode()
  const { colour } = useDesignTokens()
  const to = t('shell.studio.processGraph.to')
  const section = useRef<HTMLElement>(null)
  const { setNodeRef } = useDroppable({ id: 'process-canvas', data: { pointer: '/steps' } })

  const { nodes, edges } = useMemo(() => {
    const place = placeSteps(graph)
    return {
      nodes: graph.steps.map((step): StepNode => ({
        id: String(step.index),
        type: 'step',
        position: place(step.index),
        width: nodeWidth,
        height: nodeHeight,
        handles: [
          { type: 'target', position: Position.Top, x: nodeWidth / 2, y: 0, width: 1, height: 1 },
          {
            type: 'source',
            position: Position.Bottom,
            x: nodeWidth / 2 - handleSize / 2,
            y: nodeHeight - handleSize / 2,
            width: handleSize,
            height: handleSize,
          },
        ],
        data: {
          index: step.index,
          name: step.name ?? '',
          type: step.type ?? '',
          start: graph.start === step.index,
          current: selected === step.index,
          marked: step.marked,
          connectable: step.type !== 'end',
        },
      })),
      edges: graph.transitions.map((transition): Edge => {
        const current = transition.path === selectedEdge
        const stroke = current ? colour.primary : colour.textMuted
        return {
          id: transition.path,
          type: 'transition',
          source: String(transition.source),
          target: String(transition.target),
          label: transition.label ?? transition.kind,
          ariaLabel: transitionName(graph, transition, to),
          ariaRole: 'button',
          domAttributes: current ? { 'aria-current': 'true' } : undefined,
          style: { stroke, strokeWidth: current ? 2 : undefined },
          markerEnd: { type: MarkerType.ArrowClosed, color: stroke },
          labelStyle: { fill: colour.text },
          labelBgStyle: { fill: colour.surface },
        }
      }),
    }
  }, [graph, selected, selectedEdge, colour, to])

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
      onRemove()
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
    <section
      ref={(element) => {
        section.current = element
        setNodeRef(element)
      }}
      data-testid="studio-process-graph"
      aria-label={t('shell.studio.processGraph')}
      style={{ height: 480, position: 'relative' }}
      onKeyDown={onKeyDown}
    >
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        colorMode={mode}
        fitView
        nodesDraggable={false}
        nodesConnectable
        nodesFocusable={false}
        elementsSelectable={false}
        edgesFocusable
        deleteKeyCode={null}
        zoomOnScroll={false}
        onConnectEnd={onConnectEnd}
        onNodeClick={(_, node) => onSelect(Number(node.id))}
        onEdgeClick={(_, edge) => {
          const transition = graph.transitions.find((candidate) => candidate.path === edge.id)
          if (transition) {
            onSelectEdge(transition)
          }
        }}
      />
    </section>
  )
}
