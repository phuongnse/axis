import { AimOutlined, FullscreenExitOutlined, FullscreenOutlined, MinusOutlined, PlusOutlined } from '@ant-design/icons'
import { Panel, useReactFlow } from '@xyflow/react'
import { useEffect, useState, type RefObject } from 'react'
import { AxisButton } from './AxisButton'
import { useDesignTokens } from './themeMode'

/**
 * Proposed, see the review on #3650: the controls of a process graph too large to read whole, Axis's own icon buttons
 * with their names in the reader's language as tooltips: Zoom in, Zoom out, Show the whole process, and Full screen,
 * which gives the graph the whole screen through the browser's full screen until Esc or Exit full screen.
 */
export function ProcessGraphControls({ area }: { area: RefObject<HTMLElement | null> }) {
  const { zoomIn, zoomOut, fitView } = useReactFlow()
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
        <AxisButton role="icon" nameKey="shell.graph.zoomIn" icon={<PlusOutlined />} onClick={() => void zoomIn()} />
        <AxisButton role="icon" nameKey="shell.graph.zoomOut" icon={<MinusOutlined />} onClick={() => void zoomOut()} />
        <AxisButton role="icon" nameKey="shell.graph.fit" icon={<AimOutlined />} onClick={() => void fitView()} />
        <AxisButton
          role="icon"
          nameKey={full ? 'shell.graph.exitFullScreen' : 'shell.graph.fullScreen'}
          icon={full ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
          aria-pressed={full}
          onClick={toggle}
        />
      </div>
    </Panel>
  )
}
