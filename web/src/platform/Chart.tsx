import { lazy, Suspense } from 'react'
import type { ChartData } from './chartOption'

export type { ChartData, ChartKind, ChartSeries } from './chartOption'

const LazyChartDrawing = lazy(() => import('./ChartDrawing'))

export interface ChartProps extends ChartData {
  /** The chart's accessible name. */
  title: string
  /** The height in pixels. */
  height?: number
}

/** A bar, line or pie chart in the Axis theme. The chart library loads when the first chart shows. */
export function Chart({ title, height = 320, ...data }: ChartProps) {
  return (
    <Suspense fallback={<div aria-busy="true" style={{ height }} />}>
      <LazyChartDrawing title={title} height={height} {...data} />
    </Suspense>
  )
}
