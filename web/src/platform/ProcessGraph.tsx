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
  EdgeLabelRenderer,
  type EdgeProps,
  Handle,
  MarkerType,
  type Node,
  type NodeProps,
  type OnConnectEnd,
  Position,
  ReactFlow,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Tooltip } from 'antd'
import { type ComponentType, type CSSProperties, type KeyboardEvent, type ReactNode, useMemo, useRef } from 'react'
import { type RunState, stepTypeText } from './inspection'
import { type GraphPoint, roundedPath, routeBetween, useGraphLayout } from './processLayout'
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

/** The size of a card's connect handle, in px. */
const handleSize = 10

/**
 * A step of a process, as the canvas draws it. A marked step has a diagnostic. `name` is the card's accessible name, the
 * label by default, and `mark` is a small line under the label. A `connectable` step has a connect handle when the
 * canvas has `onConnect`.
 */
export interface GraphStep {
  id: string
  label: string
  type: string
  marked?: boolean
  name?: string
  mark?: string
  connectable?: boolean
}

/** A transition from one step to another. Its name is the accessible name of its route, and `label` is drawn on it. */
export interface GraphTransition {
  id: string
  source: string
  target: string
  name: string
  label?: string
}

type StepCardData = {
  step: GraphStep
  chosen: boolean
  connectable: boolean
  testIdPrefix: string
  onChoose?: (id: string) => void
  /** The step's run state, in instance mode. `undefined` is a step not reached. */
  runState?: RunState
  instance: boolean
}

type StepNode = Node<StepCardData, 'step'>

type RouteData = { route?: GraphPoint[]; extra?: ReactNode }

type RouteEdge = Edge<RouteData, 'route'>

const anchorStyle: CSSProperties = { display: 'none' }

/**
 * A step as a card, see docs/reference/process-inspector-widget.md#process-graph. It is a button when the graph
 * chooses a step, and a group otherwise. The state badge is a sibling of the card, not inside the button, so the
 * button's name stays the step's label.
 */
function StepCard({ data }: NodeProps<StepNode>) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const { step, chosen, connectable, testIdPrefix, onChoose, runState, instance } = data
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
      <span style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <Tooltip title={step.label}>
          <strong
            style={{
              display: '-webkit-box',
              WebkitLineClamp: step.mark === undefined ? 2 : 1,
              WebkitBoxOrient: 'vertical',
              overflow: 'hidden',
              minWidth: 0,
              overflowWrap: 'anywhere',
            }}
          >
            {step.label}
          </strong>
        </Tooltip>
        {step.mark !== undefined && (
          <span
            style={{
              color: colour.textMuted,
              fontSize: 12,
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
            }}
          >
            {step.mark}
          </span>
        )}
      </span>
    </>
  )
  const testId = `${testIdPrefix}-node-${step.id}`
  const name = step.name ?? step.label
  return (
    <div style={{ position: 'relative', width: cardWidth, height: cardHeight }}>
      <Handle type="target" position={Position.Left} isConnectable={false} style={anchorStyle} aria-hidden />
      {onChoose ? (
        <button
          type="button"
          data-testid={testId}
          data-marked={step.marked ? 'true' : undefined}
          aria-label={name}
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
          data-marked={step.marked ? 'true' : undefined}
          aria-label={name}
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
      {connectable ? (
        <Handle
          type="source"
          position={Position.Right}
          isConnectable
          data-testid={`${testIdPrefix}-handle-${step.id}`}
          aria-hidden
          style={{
            width: handleSize,
            height: handleSize,
            background: colour.primary,
            border: `1px solid ${colour.surface}`,
            cursor: 'crosshair',
          }}
        />
      ) : (
        <Handle type="source" position={Position.Right} isConnectable={false} style={anchorStyle} aria-hidden />
      )}
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
 * docs/reference/process-inspector-widget.md#process-graph. Its label sits at the middle of the route, and so does the
 * extra content of its data, which takes no pointer events, so a click still reaches the route. Without a route, it is a
 * straight line between its ends.
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
    <>
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
      {data?.extra !== undefined && (
        <EdgeLabelRenderer>
          <div style={{ position: 'absolute', transform: `translate(${x}px, ${y}px)`, pointerEvents: 'none' }}>
            {data.extra}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  )
}

const edgeTypes = { route: ProcessRouteEdge }

const edgeTestIdPrefix = 'rf__edge-'

/**
 * A process drawn as cards laid out from left to right by ELK, see
 * docs/reference/process-inspector-widget.md#process-graph. `label` names the graph. A chosen step has a `primary`
 * border, and `onChoose` makes each card a button that chooses its step. Given the run state of each step reached, the
 * graph is an instance's: a reached step has its state badge, the steps run and the `taken` transitions are in
 * `successText`, and the rest are muted, with dashed and thinner transitions. The graph shows while its layout is
 * computed again, with the previous one. A transition is drawn along its route only while its ends are those the layout
 * was computed for, and as a straight line otherwise.
 *
 * An editor adds more. With `onChooseTransition`, each transition can be focused and is a button: a click, or Enter or
 * Space on it, calls it with the transition's id, and the `chosenTransition` is drawn in `primary`, 2 px wide, with
 * `aria-current`. With `onConnect`, a `connectable` step has a connect handle on its card's right edge, and a drag from
 * it onto another step calls `onConnect` with the two ids and the release point, relative to the canvas. No other step
 * has a handle. `transitionExtra` gives the content at the middle of a transition's route. A card is named by the test
 * id `{testIdPrefix}-node-{id}` and its handle by `{testIdPrefix}-handle-{id}`.
 */
export function ProcessGraphCanvas({
  steps,
  transitions,
  label,
  chosen,
  onChoose,
  runStates,
  taken,
  chosenTransition,
  onChooseTransition,
  onConnect,
  transitionExtra,
  testIdPrefix = 'process-graph',
}: {
  steps: GraphStep[]
  transitions: GraphTransition[]
  label: string
  chosen?: string
  onChoose?: (id: string) => void
  runStates?: ReadonlyMap<string, RunState>
  taken?: ReadonlySet<string>
  chosenTransition?: string
  onChooseTransition?: (id: string) => void
  onConnect?: (source: string, target: string, point: GraphPoint) => void
  transitionExtra?: (id: string) => ReactNode
  testIdPrefix?: string
}) {
  const { mode } = useThemeMode()
  const section = useRef<HTMLElement>(null)
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
    const nodes = placed.map((step): StepNode => {
      const connectable = onConnect !== undefined && step.connectable === true
      return {
        id: step.id,
        type: 'step',
        position: layout.positions.get(step.id)!,
        width: cardWidth,
        height: cardHeight,
        handles: [
          { type: 'target', position: Position.Left, x: 0, y: cardHeight / 2, width: 1, height: 1 },
          connectable
            ? {
                type: 'source',
                position: Position.Right,
                x: cardWidth - handleSize / 2,
                y: cardHeight / 2 - handleSize / 2,
                width: handleSize,
                height: handleSize,
              }
            : { type: 'source', position: Position.Right, x: cardWidth, y: cardHeight / 2, width: 1, height: 1 },
        ],
        data: {
          step,
          chosen: step.id === chosen,
          connectable,
          testIdPrefix,
          onChoose,
          runState: runStates?.get(step.id),
          instance,
        },
      }
    })
    const edges = transitions
      .filter((transition) => layout.positions.has(transition.source) && layout.positions.has(transition.target))
      .map((transition): RouteEdge => {
        const current = transition.id === chosenTransition
        const dimmed = instance && !taken?.has(transition.id)
        const stroke = current ? colour.primary : instance && !dimmed ? colour.successText : colour.textMuted
        return {
          id: transition.id,
          source: transition.source,
          target: transition.target,
          type: 'route',
          data: {
            route: routeBetween(layout, transition.id, transition.source, transition.target),
            extra: transitionExtra?.(transition.id),
          },
          ariaLabel: transition.name,
          ariaRole: onChooseTransition ? 'button' : undefined,
          domAttributes: current ? { 'aria-current': 'true' } : undefined,
          ...(transition.label !== undefined && {
            label: transition.label,
            labelStyle: { fill: colour.text },
            labelBgStyle: { fill: colour.fillSubtle },
          }),
          style: {
            stroke,
            strokeWidth: current ? 2 : instance ? (dimmed ? 1 : 2) : 1.5,
            strokeDasharray: dimmed && !current ? '4 4' : undefined,
          },
          markerEnd: { type: MarkerType.ArrowClosed, color: stroke },
        }
      })
    return { nodes, edges }
  }, [
    layout,
    steps,
    transitions,
    chosen,
    onChoose,
    runStates,
    taken,
    instance,
    colour,
    chosenTransition,
    onChooseTransition,
    onConnect,
    transitionExtra,
    testIdPrefix,
  ])

  // A drag ends on a node when React Flow snaps to its handle, and anywhere else on the node otherwise, so the node
  // under the pointer is the target. `onConnect` is not set on React Flow, or it would report a snapped drag twice.
  const connectEnd: OnConnectEnd = (event, state) => {
    const source = state.fromNode?.id
    if (source === undefined || onConnect === undefined) {
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
    onConnect(source, target, { x: x - (box?.left ?? 0), y: y - (box?.top ?? 0) })
  }

  // React Flow does not act on a key pressed on an edge while its elements are not selectable, so Enter and Space on a
  // focused transition choose it here.
  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (onChooseTransition === undefined || (event.key !== 'Enter' && event.key !== ' ')) {
      return
    }
    const element =
      event.target instanceof Element ? event.target.closest(`[data-testid^="${edgeTestIdPrefix}"]`) : null
    const id = element?.getAttribute('data-testid')?.slice(edgeTestIdPrefix.length)
    if (id !== undefined && transitions.some((transition) => transition.id === id)) {
      event.preventDefault()
      onChooseTransition(id)
    }
  }

  return (
    <section
      ref={section}
      aria-label={label}
      style={{ width: '100%', height: '100%', position: 'relative' }}
      onKeyDown={onKeyDown}
    >
      {flow && (
        <ReactFlow
          nodes={flow.nodes}
          edges={flow.edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          colorMode={mode}
          fitView
          fitViewOptions={{ maxZoom: 1 }}
          minZoom={0.1}
          maxZoom={1}
          proOptions={{ hideAttribution: true }}
          style={{ background: colour.fillSubtle }}
          nodesDraggable={false}
          nodesConnectable={onConnect !== undefined}
          nodesFocusable={false}
          elementsSelectable={false}
          edgesFocusable={onChooseTransition !== undefined}
          zoomOnScroll={false}
          onConnectEnd={onConnect ? connectEnd : undefined}
          onEdgeClick={onChooseTransition ? (_, edge) => onChooseTransition(edge.id) : undefined}
        />
      )}
    </section>
  )
}
