import dagre from '@dagrejs/dagre'
import {
  type Edge,
  type EdgeTypes,
  Handle,
  MarkerType,
  type Node,
  type NodeProps,
  type OnConnectEnd,
  Position,
  ReactFlow,
  Controls,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { type KeyboardEvent, type Ref, useMemo } from 'react'
import { useDesignTokens, useThemeMode } from './themeMode'

const nodeWidth = 240
const nodeHeight = 56
const handleSize = 10

/**
 * How a step's node is drawn. `selected` is the step a designer edits, in the primary colour; `error` has a diagnostic,
 * in the danger colour; `current` is the step an instance is at, in the primary colour and twice as thick; `taken` is a
 * step an instance has run, in the success text colour; `idle` is a step an instance has not reached, its border and
 * text muted; `plain` is any other.
 */
export type ProcessGraphTone = 'plain' | 'selected' | 'error' | 'current' | 'taken' | 'idle'

/** One step of a `ProcessGraphCanvas`: its node's id, its texts, its look and its accessible name. */
export type ProcessGraphStep = {
  id: string
  name: string
  type: string
  /** The process's start step, drawn with a thick border. */
  start: boolean
  tone: ProcessGraphTone
  ariaLabel: string
  ariaCurrent?: boolean
  /** Proposed, see the review on #3650: a count drawn at the node's corner, such as how many times an instance ran it. */
  badge?: string
  /** A mark shown after the type, such as a customization layer's Added, and its value for `data-layer-mark`. */
  note?: { text: string; kind: string }
  /** Whether a transition can be drawn from the step's bottom handle. */
  connectable?: boolean
  testId?: string
  handleTestId?: string
}

/** One transition of a `ProcessGraphCanvas`, between two steps' ids. */
export interface ProcessGraphTransitionView {
  id: string
  source: string
  target: string
  label: string
  ariaLabel: string
  tone: Exclude<ProcessGraphTone, 'error'>
  ariaCurrent?: boolean
}

type StepNodeData = ProcessGraphStep
type StepNode = Node<StepNodeData, 'step'>

function placeSteps(
  steps: readonly ProcessGraphStep[],
  transitions: readonly ProcessGraphTransitionView[],
): (id: string) => { x: number; y: number } {
  const layout = new dagre.graphlib.Graph()
  layout.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 56 })
  layout.setDefaultEdgeLabel(() => ({}))
  for (const step of steps) {
    layout.setNode(step.id, { width: nodeWidth, height: nodeHeight })
  }
  for (const transition of transitions) {
    layout.setEdge(transition.source, transition.target)
  }
  dagre.layout(layout)
  return (id) => {
    const placed = layout.node(id)
    return { x: placed.x - nodeWidth / 2, y: placed.y - nodeHeight / 2 }
  }
}

function StepNodeView({ data }: NodeProps<StepNode>) {
  const { colour, radius } = useDesignTokens()
  const edge = {
    plain: colour.border,
    selected: colour.primary,
    error: colour.danger,
    current: colour.primary,
    taken: colour.successText,
    idle: colour.borderSubtle,
  }[data.tone]
  const width = data.start || data.tone === 'current' ? 3 : 1
  return (
    <>
      <Handle type="target" position={Position.Top} isConnectable={false} />
      <button
        type="button"
        data-testid={data.testId}
        aria-label={data.ariaLabel}
        aria-current={data.ariaCurrent ? 'true' : undefined}
        data-marked={data.tone === 'error' ? 'true' : undefined}
        data-tone={data.tone}
        style={{
          width: '100%',
          height: '100%',
          padding: '8px',
          overflow: 'hidden',
          textAlign: 'left',
          // A long name takes two lines before it is cut, so a step's label reads whole.
          display: '-webkit-box',
          WebkitLineClamp: 2,
          WebkitBoxOrient: 'vertical',
          font: 'inherit',
          lineHeight: '20px',
          color: data.tone === 'idle' ? colour.textMuted : colour.text,
          background: colour.surface,
          border: `${width}px solid ${edge}`,
          borderRadius: radius.base,
          cursor: 'pointer',
        }}
      >
        <strong>{data.name}</strong> <span>{data.type}</span>
        {data.note !== undefined && (
          <>
            {' '}
            <span data-layer-mark={data.note.kind}>{data.note.text}</span>
          </>
        )}
      </button>
      {/* Outside the button, whose overflow is hidden, so the count overlaps the node's corner. */}
      {data.badge !== undefined && (
        <span
          aria-hidden
          style={{
            position: 'absolute',
            top: -10,
            right: -10,
            minWidth: 20,
            padding: '0 6px',
            borderRadius: 10,
            fontSize: 12,
            lineHeight: '20px',
            textAlign: 'center',
            color: colour.onPrimary,
            background: colour.primary,
          }}
        >
          {data.badge}
        </span>
      )}
      <Handle
        type="source"
        position={Position.Bottom}
        isConnectable={data.connectable === true}
        data-testid={data.handleTestId}
        aria-hidden
        style={{
          width: handleSize,
          height: handleSize,
          background: colour.primary,
          border: `1px solid ${colour.surface}`,
          cursor: data.connectable ? 'crosshair' : 'default',
          // Hidden, not removed, so React Flow still measures where a transition leaves the step.
          visibility: data.connectable ? undefined : 'hidden',
        }}
      />
    </>
  )
}

const nodeTypes = { step: StepNodeView }

/**
 * Proposed, see the review on #3650: the one drawing of a process's steps and transitions, React Flow laid out by dagre
 * from the top, which the process designer edits and a process instance's page shows. It stores no position: every
 * change lays the graph out again. Each step is a node named by its accessible name, its look by its tone, and each
 * transition an edge named by its two steps and its branch or outcome, which Tab reaches before the nodes. A designer
 * passes its own `edgeType`, which adds its drop targets, and the handlers that edit; without them the graph only
 * shows the process, and a click on a node or an edge does nothing.
 */
export function ProcessGraphCanvas({
  steps,
  transitions,
  ariaLabel,
  testId,
  height = 480,
  edgeType,
  sectionRef,
  onKeyDown,
  onStepClick,
  onTransitionClick,
  onConnectEnd,
  focus,
  controls = false,
}: {
  steps: readonly ProcessGraphStep[]
  transitions: readonly ProcessGraphTransitionView[]
  ariaLabel: string
  testId?: string
  height?: number
  edgeType?: EdgeTypes[string]
  sectionRef?: Ref<HTMLElement>
  onKeyDown?: (event: KeyboardEvent<HTMLElement>) => void
  onStepClick?: (id: string) => void
  onTransitionClick?: (id: string) => void
  onConnectEnd?: OnConnectEnd
  /** Proposed, see the review on #3650: the step the view opens on, at the size it is drawn at, else the whole graph. */
  focus?: string
  /** Proposed, see the review on #3650: zoom in, zoom out and fit buttons, for a graph too large to read whole. */
  controls?: boolean
}) {
  const { mode } = useThemeMode()
  const { colour } = useDesignTokens()
  const edgeTypes = useMemo(() => (edgeType ? { transition: edgeType } : undefined), [edgeType])

  const { nodes, edges } = useMemo(() => {
    const place = placeSteps(steps, transitions)
    return {
      nodes: steps.map((step): StepNode => ({
        id: step.id,
        type: 'step',
        position: place(step.id),
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
        data: step,
      })),
      edges: transitions.map((transition): Edge => {
        const stroke =
          transition.tone === 'selected' || transition.tone === 'current'
            ? colour.primary
            : transition.tone === 'taken'
              ? colour.successText
              : colour.textMuted
        const thick = transition.tone !== 'plain' && transition.tone !== 'idle'
        return {
          id: transition.id,
          type: edgeType ? 'transition' : 'default',
          source: transition.source,
          target: transition.target,
          label: transition.label,
          ariaLabel: transition.ariaLabel,
          ariaRole: onTransitionClick ? 'button' : undefined,
          domAttributes: transition.ariaCurrent ? { 'aria-current': 'true' } : undefined,
          style: {
            stroke,
            strokeWidth: thick ? 2 : undefined,
            strokeDasharray: transition.tone === 'idle' ? '4 4' : undefined,
          },
          markerEnd: { type: MarkerType.ArrowClosed, color: stroke },
          labelStyle: { fill: colour.text },
          labelBgStyle: { fill: colour.surface },
        }
      }),
    }
  }, [steps, transitions, colour, edgeType, onTransitionClick])

  return (
    <section
      ref={sectionRef}
      data-testid={testId}
      aria-label={ariaLabel}
      style={{ height, position: 'relative' }}
      onKeyDown={onKeyDown}
    >
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        colorMode={mode}
        fitView
        fitViewOptions={focus === undefined ? undefined : { nodes: [{ id: focus }], minZoom: 1, maxZoom: 1 }}
        minZoom={0.1}
        // A small process is drawn at its own size, never zoomed in past it, so its text keeps the body size.
        maxZoom={1}
        nodesDraggable={false}
        nodesConnectable={onConnectEnd !== undefined}
        nodesFocusable={false}
        elementsSelectable={false}
        edgesFocusable
        deleteKeyCode={null}
        zoomOnScroll={false}
        onConnectEnd={onConnectEnd}
        onNodeClick={onStepClick ? (_, node) => onStepClick(node.id) : undefined}
        onEdgeClick={onTransitionClick ? (_, edge) => onTransitionClick(edge.id) : undefined}
      >
        {controls && <Controls showInteractive={false} />}
      </ReactFlow>
    </section>
  )
}
