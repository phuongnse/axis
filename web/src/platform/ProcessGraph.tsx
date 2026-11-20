import {
  BellOutlined,
  BranchesOutlined,
  FieldTimeOutlined,
  PartitionOutlined,
  QuestionOutlined,
  StopOutlined,
  ThunderboltOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  BaseEdge,
  type Edge,
  type EdgeProps,
  Handle,
  MarkerType,
  type Node,
  type NodeProps,
  Position,
  ReactFlow,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Tooltip } from 'antd'
import { type ComponentType, type CSSProperties, useMemo } from 'react'
import { type RunState, stepTypeText } from './inspection'
import { type GraphPoint, roundedPath, useGraphLayout } from './processLayout'
import { StateBadge } from './StateBadge'
import { useText } from './texts'
import { useDesignTokens, useThemeMode } from './themeMode'

/** The width of every card, in px. */
export const cardWidth = 200

/** The height of every card, in px: its icon tile, or two lines of its label. */
export const cardHeight = 64

/** The step type icons, outlined. A type this build does not know has the question mark. */
const typeIcons: Record<string, ComponentType> = {
  decision: BranchesOutlined,
  task: UserOutlined,
  operation: ThunderboltOutlined,
  subProcess: PartitionOutlined,
  timer: FieldTimeOutlined,
  waitEvent: BellOutlined,
  end: StopOutlined,
}

/** A step of a process, as the canvas draws it. A marked step has a diagnostic. */
export interface GraphStep {
  id: string
  label: string
  type: string
  marked?: boolean
}

/** A transition from one step to another. Its name is the accessible name of its route. */
export interface GraphTransition {
  id: string
  source: string
  target: string
  name: string
}

type StepCardData = {
  step: GraphStep
  chosen: boolean
  onChoose?: (id: string) => void
  /** The step's run state, in instance mode. `undefined` is a step not reached. */
  runState?: RunState
  instance: boolean
}

type StepNode = Node<StepCardData, 'step'>

type RouteData = { route?: GraphPoint[] }

type RouteEdge = Edge<RouteData, 'route'>

const handleStyle: CSSProperties = { display: 'none' }

/**
 * A step as a card, see docs/reference/process-inspector-widget.md#process-graph. It is a button when the graph
 * chooses a step, and a group otherwise. The state badge is a sibling of the card, not inside the button, so the
 * button's name stays the step's label.
 */
function StepCard({ data }: NodeProps<StepNode>) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const { step, chosen, onChoose, runState, instance } = data
  const reached = runState !== undefined
  const muted = instance && !reached
  const Icon = typeIcons[step.type] ?? QuestionOutlined
  const typeWords = stepTypeText(t, step.type)
  const style: CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: spacing.sm,
    width: cardWidth,
    height: cardHeight,
    boxSizing: 'border-box',
    padding: `0 ${spacing.sm}px`,
    textAlign: 'start',
    font: 'inherit',
    color: muted ? colour.textMuted : colour.text,
    background: colour.surface,
    border: `1px solid ${step.marked ? colour.danger : colour.borderSubtle}`,
    borderRadius: radius.base,
    boxShadow: chosen ? `inset 0 0 0 2px ${colour.primary}` : undefined,
  }
  const content = (
    <>
      <Tooltip title={typeWords}>
        <span
          role="img"
          aria-label={typeWords}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: 'none',
            width: spacing.xl + spacing.xs,
            height: spacing.xl + spacing.xs,
            borderRadius: radius.base,
            background: colour.fillSubtle,
            color: instance ? (reached ? colour.successText : colour.textMuted) : colour.text,
            fontSize: spacing.md + 2,
          }}
        >
          <Icon />
        </span>
      </Tooltip>
      <Tooltip title={step.label}>
        <strong
          style={{
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden',
            minWidth: 0,
            overflowWrap: 'anywhere',
          }}
        >
          {step.label}
        </strong>
      </Tooltip>
    </>
  )
  const testId = `process-graph-step-${step.id}`
  return (
    <div style={{ position: 'relative', width: cardWidth, height: cardHeight }}>
      <Handle type="target" position={Position.Left} isConnectable={false} style={handleStyle} aria-hidden />
      {onChoose ? (
        <button
          type="button"
          data-testid={testId}
          aria-label={step.label}
          aria-current={chosen ? 'true' : undefined}
          onClick={() => onChoose(step.id)}
          style={{ ...style, cursor: 'pointer' }}
        >
          {content}
        </button>
      ) : (
        <div
          role="group"
          data-testid={testId}
          aria-label={step.label}
          aria-current={chosen ? 'true' : undefined}
          style={style}
        >
          {content}
        </div>
      )}
      {instance && (
        <span style={{ position: 'absolute', top: 0, right: 0, transform: 'translate(50%, -50%)', display: 'flex' }}>
          <StateBadge state={runState} />
        </span>
      )}
      <Handle type="source" position={Position.Right} isConnectable={false} style={handleStyle} aria-hidden />
    </div>
  )
}

const nodeTypes = { step: StepCard }

/** The midpoint of the middle segment of a route, where its label sits. */
function labelPoint(route: GraphPoint[]): GraphPoint {
  const index = Math.floor((route.length - 1) / 2)
  const from = route[index]!
  const to = route[index + 1] ?? from
  return { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 }
}

/**
 * A transition's route in right angles with rounded corners, from the points ELK found, see
 * docs/reference/process-inspector-widget.md#process-graph. Its label sits at the middle of the route. Without a route,
 * it is a straight line between its ends.
 */
export function ProcessRouteEdge({
  sourceX,
  sourceY,
  targetX,
  targetY,
  data,
  markerEnd,
  style,
  label,
  labelStyle,
  labelBgStyle,
}: EdgeProps<RouteEdge>) {
  const route = data?.route ?? [
    { x: sourceX, y: sourceY },
    { x: targetX, y: targetY },
  ]
  const { x, y } = labelPoint(route)
  return (
    <BaseEdge
      path={roundedPath(route)}
      markerEnd={markerEnd}
      style={style}
      label={label}
      labelX={x}
      labelY={y}
      labelStyle={labelStyle}
      labelBgStyle={labelBgStyle}
    />
  )
}

const edgeTypes = { route: ProcessRouteEdge }

/**
 * A process drawn as cards laid out from left to right by ELK, see
 * docs/reference/process-inspector-widget.md#process-graph. `label` names the graph. A chosen step has a `primary`
 * border, and `onChoose` makes each card a button that chooses its step. Given the run state of each step reached, the
 * graph is an instance's: a reached step has its state badge, the steps run and the `taken` transitions are in
 * `successText`, and the rest are muted, with dashed and thinner transitions. The graph shows while its layout is
 * computed again, with the previous one.
 */
export function ProcessGraphCanvas({
  steps,
  transitions,
  label,
  chosen,
  onChoose,
  runStates,
  taken,
}: {
  steps: GraphStep[]
  transitions: GraphTransition[]
  label: string
  chosen?: string
  onChoose?: (id: string) => void
  runStates?: ReadonlyMap<string, RunState>
  taken?: ReadonlySet<string>
}) {
  const { mode } = useThemeMode()
  const { colour } = useDesignTokens()
  const layout = useGraphLayout({
    nodes: steps.map((step) => ({ id: step.id, width: cardWidth, height: cardHeight })),
    edges: transitions.map(({ id, source, target }) => ({ id, source, target })),
  })
  const instance = runStates !== undefined

  const flow = useMemo(() => {
    if (layout === null) {
      return null
    }
    const placed = steps.filter((step) => layout.positions.has(step.id))
    const nodes = placed.map((step): StepNode => ({
      id: step.id,
      type: 'step',
      position: layout.positions.get(step.id)!,
      width: cardWidth,
      height: cardHeight,
      handles: [
        { type: 'target', position: Position.Left, x: 0, y: cardHeight / 2, width: 1, height: 1 },
        { type: 'source', position: Position.Right, x: cardWidth, y: cardHeight / 2, width: 1, height: 1 },
      ],
      data: { step, chosen: step.id === chosen, onChoose, runState: runStates?.get(step.id), instance },
    }))
    const edges = transitions
      .filter((transition) => layout.positions.has(transition.source) && layout.positions.has(transition.target))
      .map((transition): RouteEdge => {
        const dimmed = instance && !taken?.has(transition.id)
        const stroke = instance && !dimmed ? colour.successText : colour.textMuted
        return {
          id: transition.id,
          source: transition.source,
          target: transition.target,
          type: 'route',
          data: { route: layout.routes.get(transition.id) },
          ariaLabel: transition.name,
          style: {
            stroke,
            strokeWidth: instance ? (dimmed ? 1 : 2) : 1.5,
            strokeDasharray: dimmed ? '4 4' : undefined,
          },
          markerEnd: { type: MarkerType.ArrowClosed, color: stroke },
        }
      })
    return { nodes, edges }
  }, [layout, steps, transitions, chosen, onChoose, runStates, taken, instance, colour])

  return (
    <section aria-label={label} style={{ width: '100%', height: '100%' }}>
      {flow && (
        <ReactFlow
          nodes={flow.nodes}
          edges={flow.edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          colorMode={mode}
          fitView
          fitViewOptions={{ maxZoom: 1 }}
          maxZoom={1}
          proOptions={{ hideAttribution: true }}
          style={{ background: colour.fillSubtle }}
          nodesDraggable={false}
          nodesConnectable={false}
          nodesFocusable={false}
          elementsSelectable={false}
          edgesFocusable={false}
          zoomOnScroll={false}
        />
      )}
    </section>
  )
}
