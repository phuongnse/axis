import {
  AimOutlined,
  ExpandOutlined,
  FullscreenExitOutlined,
  FullscreenOutlined,
  MinusOutlined,
  PlusOutlined,
} from '@ant-design/icons'
import { MiniMap, Panel, useReactFlow } from '@xyflow/react'
import { useEffect, useState, type RefObject } from 'react'
import { AxisButton } from './AxisButton'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** How long a move of the view takes, in milliseconds. */
const moving = 300

/**
 * Proposed, see the review on #3650: the controls of a process graph too large to read whole, Axis's own icon buttons
 * with their names in the reader's language as tooltips: Zoom in, Zoom out, Show the whole process, Go to the current
 * step where there is one, and Full screen, which gives the graph the whole screen through the browser's full screen until Esc or Exit full screen; and an
 * overview at the corner, as Camunda Operate and Signavio show a long diagram, which marks the part in view and moves
 * the view where it is dragged or clicked.
 */
export function ProcessGraphControls({ area, focus }: { area: RefObject<HTMLElement | null>; focus?: string }) {
  const { zoomIn, zoomOut, fitView } = useReactFlow()
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const [full, setFull] = useState(false)

  useEffect(() => {
    const follow = () => setFull(document.fullscreenElement !== null && document.fullscreenElement === area.current)
    document.addEventListener('fullscreenchange', follow)
    return () => document.removeEventListener('fullscreenchange', follow)
  }, [area])

  const toggle = () => {
    if (document.fullscreenElement) {
      void document.exitFullscreen()
    } else {
      void area.current?.requestFullscreen()
    }
  }

  return (
    <>
      <MiniMap
        ariaLabel={t('shell.graph.overview')}
        pannable
        zoomable
        position="bottom-right"
        // Each step in the tone it has on the graph, so the overview shows where the instance went and where it is.
        nodeColor={(node) => {
          const tone = (node.data as { tone?: string }).tone
          return tone === 'current' || tone === 'selected'
            ? colour.primary
            : tone === 'taken'
              ? colour.successText
              : tone === 'error'
                ? colour.danger
                : colour.border
        }}
        nodeStrokeWidth={0}
        nodeBorderRadius={4}
        bgColor={colour.canvas}
        maskColor={`${colour.canvas}99`}
        maskStrokeColor={colour.primary}
        maskStrokeWidth={2}
        style={{ border: `1px solid ${colour.borderSubtle}`, borderRadius: radius.base }}
      />
      <Panel position="bottom-left">
        <div
          role="group"
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: spacing.xs / 2,
            padding: spacing.xs / 2,
            background: colour.surface,
            border: `1px solid ${colour.borderSubtle}`,
            borderRadius: radius.base,
          }}
        >
          {/* Every move of the view is animated, so a click always shows what it did, even when little changes. */}
          <AxisButton
            role="icon"
            nameKey="shell.graph.zoomIn"
            icon={<PlusOutlined />}
            onClick={() => void zoomIn({ duration: moving })}
          />
          <AxisButton
            role="icon"
            nameKey="shell.graph.zoomOut"
            icon={<MinusOutlined />}
            onClick={() => void zoomOut({ duration: moving })}
          />
          <AxisButton
            role="icon"
            nameKey="shell.graph.fit"
            icon={<ExpandOutlined />}
            onClick={() => void fitView({ padding: 0.1, duration: moving })}
          />
          {focus !== undefined && (
            <AxisButton
              role="icon"
              nameKey="shell.graph.current"
              icon={<AimOutlined />}
              onClick={() => void fitView({ nodes: [{ id: focus }], minZoom: 1, maxZoom: 1, duration: moving })}
            />
          )}
          <AxisButton
            role="icon"
            nameKey={full ? 'shell.graph.exitFullScreen' : 'shell.graph.fullScreen'}
            icon={full ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
            aria-pressed={full}
            onClick={toggle}
          />
        </div>
      </Panel>
    </>
  )
}
