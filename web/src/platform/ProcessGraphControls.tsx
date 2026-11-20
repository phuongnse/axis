import {
  AimOutlined,
  ExpandOutlined,
  FullscreenExitOutlined,
  FullscreenOutlined,
  ZoomInOutlined,
  ZoomOutOutlined,
} from '@ant-design/icons'
import { MiniMap, Panel } from '@xyflow/react'
import { Flex } from 'antd'
import { type ReactNode, useMemo } from 'react'
import { ReferencePicker } from '../pages/ReferencePicker'
import { AxisButton } from './AxisButton'
import { runStateTone, type RunState } from './inspection'
import type { GraphStep } from './ProcessGraph'
import { toneIconColours } from './statusTones'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useGraphMoves } from './useGraphMoves'

/**
 * The controls of a process graph, drawn inside its `ReactFlow`, in place of React Flow's own, see
 * docs/reference/process-inspector-widget.md#process-graph. At the top left, Find a step lists the `steps` in the order
 * given, as wide as a card. Choosing one moves the view to it and calls `onChoose` with its id. At the bottom left, icon
 * buttons zoom in and out, show the whole process, go to the `current` step, on an instance only, and go full screen.
 * Each but Go to the current step names its key in `aria-keyshortcuts`. The overview draws each step in the tone of its run state, and the muted
 * colour for a step not reached. The graph handles the keys.
 */
export function ProcessGraphControls({
  steps,
  cardWidth,
  chosen,
  onChoose,
  current,
  runStates,
  fullScreen,
  onFullScreen,
}: {
  steps: readonly GraphStep[]
  /** The width of a card, in px, which Find a step is as wide as. */
  cardWidth: number
  chosen?: string
  onChoose?: (id: string) => void
  current?: string
  runStates?: ReadonlyMap<string, RunState>
  fullScreen: boolean
  onFullScreen: () => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const moves = useGraphMoves()
  const options = useMemo(() => steps.map((step) => ({ name: step.id, label: step.label })), [steps])
  const findStep = t('shell.inspector.graph.findStep')
  const button = (nameKey: string, icon: ReactNode, key: string | undefined, onClick: () => void) => (
    <AxisButton role="icon" nameKey={nameKey} icon={icon} aria-keyshortcuts={key} onClick={onClick} />
  )
  return (
    <>
      <Panel position="top-left">
        <ReferencePicker
          name={findStep}
          placeholder={findStep}
          width={cardWidth}
          required
          value={chosen}
          options={options}
          onChange={(id) => {
            if (id) {
              moves.goTo(id)
              onChoose?.(id)
            }
          }}
        />
      </Panel>
      <Panel position="bottom-left">
        <Flex
          vertical
          style={{
            background: colour.surface,
            border: `1px solid ${colour.borderSubtle}`,
            padding: spacing.xs,
          }}
        >
          {button('shell.inspector.graph.zoomIn', <ZoomInOutlined />, 'Plus', moves.zoomIn)}
          {button('shell.inspector.graph.zoomOut', <ZoomOutOutlined />, '-', moves.zoomOut)}
          {button('shell.inspector.graph.showAll', <ExpandOutlined />, '0', moves.showAll)}
          {runStates !== undefined &&
            current !== undefined &&
            button('shell.inspector.graph.current', <AimOutlined />, undefined, () => moves.goTo(current))}
          {button(
            fullScreen ? 'shell.inspector.graph.exitFullScreen' : 'shell.inspector.graph.fullScreen',
            fullScreen ? <FullscreenExitOutlined /> : <FullscreenOutlined />,
            'F',
            onFullScreen,
          )}
        </Flex>
      </Panel>
      <MiniMap
        pannable
        ariaLabel={t('shell.inspector.graph.overview')}
        onClick={(_, point) => moves.centre(point.x, point.y)}
        style={{ background: colour.surface, border: `1px solid ${colour.borderSubtle}` }}
        nodeColor={(node) => {
          const state = runStates?.get(node.id)
          return state === undefined ? colour.textMuted : colour[toneIconColours[runStateTone(state)]]
        }}
      />
    </>
  )
}
