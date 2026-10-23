import { BarChart, LineChart, PieChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { init, use as registerComponents, type EChartsType } from 'echarts/core'
import { SVGRenderer } from 'echarts/renderers'
import { useEffect, useRef } from 'react'
import { chartOptionFor, type ChartData } from './chartOption'
import { useDesignTokens } from './themeMode'

registerComponents([BarChart, LineChart, PieChart, GridComponent, TooltipComponent, LegendComponent, SVGRenderer])

export interface ChartDrawingProps extends ChartData {
  title: string
  height: number
}

/**
 * Draws the chart with the chart library. This is the only file that imports it, and `Chart` reaches it through a
 * dynamic import, so the library loads in its own chunk.
 */
export default function ChartDrawing({ title, height, kind, categories, series }: ChartDrawingProps) {
  const element = useRef<HTMLDivElement>(null)
  const chart = useRef<EChartsType | null>(null)
  const tokens = useDesignTokens()

  useEffect(() => {
    const target = element.current
    if (!target) {
      return
    }
    const created = init(target, null, { renderer: 'svg' })
    chart.current = created
    const observer = new ResizeObserver(() => created.resize())
    observer.observe(target)
    return () => {
      observer.disconnect()
      created.dispose()
      chart.current = null
    }
  }, [])

  useEffect(() => {
    chart.current?.setOption(chartOptionFor({ kind, categories, series }, tokens), { notMerge: true })
  }, [kind, categories, series, tokens])

  return <div ref={element} aria-label={title} role="img" style={{ height }} />
}
