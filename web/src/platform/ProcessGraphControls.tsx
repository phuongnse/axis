import {
  AimOutlined,
  ExpandOutlined,
  FullscreenExitOutlined,
  FullscreenOutlined,
  MinusOutlined,
  PlusOutlined,
} from '@ant-design/icons'
import { MiniMap, Panel, useReactFlow } from '@xyflow/react'
import { Select } from 'antd'
import { useEffect, useState, type RefObject } from 'react'
import { AxisButton } from './AxisButton'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** How long a move of the view takes, in milliseconds. */
const moving = 300

/**
 * Proposed, see the review on #3650: the controls of a process graph too large to read whole, Axis's own icon buttons
 * with their names in the reader's language as tooltips: Zoom in, Zoom out, Show the whole process, Go to the current
 * step where there is one, and Full screen, which gives the graph the whole screen through the browser's full screen
 * until Esc or Exit full screen. Find a step moves the view to a step chosen by its label and marks it. The keys +, −,
 * 0 and F do the same while the graph has the focus. An overview
 * at the corner, as Camunda Operate and Signavio show a long diagram, marks the part in view and moves the view where
 * it is dragged or clicked.
 */
export function ProcessGraphControls({
  area,
  focus,
  steps,
  onFound,
}: {
  area: RefObject<HTMLElement | null>
  focus?: string
  /** The steps Find a step offers, by their labels. */
  steps: readonly { id: string; label: string }[]
  /** Called with the step Find a step moved to, so the graph marks it. */
  onFound: (id: string) => void
}) {
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

  const fitAll = () => void fitView({ padding: 0.1, duration: moving })
  const find = (id: string) => {
    void fitView({ nodes: [{ id }], minZoom: 1, maxZoom: 1, duration: moving })
    onFound(id)
  }

  // The keys work while the graph or something in it has the focus, never while a box takes typing.
  useEffect(() => {
    const element = area.current
    if (!element) {
      return
    }
    const press = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement
      if (event.ctrlKey || event.metaKey || event.altKey || target.closest('input, textarea, .ant-select')) {
        return
      }
      const act: Record<string, () => void> = {
        '+': () => void zoomIn({ duration: moving }),
        '=': () => void zoomIn({ duration: moving }),
        '-': () => void zoomOut({ duration: moving }),
        '0': fitAll,
        f: toggle,
        F: toggle,
      }
      const run = act[event.key]
      if (run) {
        event.preventDefault()
        run()
      }
    }
    element.addEventListener('keydown', press)
    return () => element.removeEventListener('keydown', press)
  })

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
      {/* Proposed: Find a step, a searchable list of the steps by label, see the review on #3650. */}
      <Panel position="top-left">
        <Select
          showSearch
          allowClear
          value={null}
          placeholder={t('shell.graph.find')}
          aria-label={t('shell.graph.find')}
          style={{ width: 240 }}
          optionFilterProp="label"
          options={steps.map((step) => ({ value: step.id, label: step.label }))}
          onChange={(id: string | null) => {
            if (id !== null && id !== undefined) {
              find(id)
            }
          }}
        />
      </Panel>
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
            aria-keyshortcuts="+"
            onClick={() => void zoomIn({ duration: moving })}
          />
          <AxisButton
            role="icon"
            nameKey="shell.graph.zoomOut"
            icon={<MinusOutlined />}
            aria-keyshortcuts="-"
            onClick={() => void zoomOut({ duration: moving })}
          />
          <AxisButton
            role="icon"
            nameKey="shell.graph.fit"
            icon={<ExpandOutlined />}
            aria-keyshortcuts="0"
            onClick={fitAll}
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
            aria-keyshortcuts="F"
            icon={full ? <FullscreenExitOutlined /> : <FullscreenOutlined />}
            aria-pressed={full}
            onClick={toggle}
          />
        </div>
      </Panel>
    </>
  )
}
