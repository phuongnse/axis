import type { EChartsCoreOption } from 'echarts/core'
import type { DesignTokens } from './designTokens'

export type ChartKind = 'bar' | 'line' | 'pie'

export interface ChartSeries {
  label: string
  values: (number | null)[]
}

export interface ChartData {
  kind: ChartKind
  categories: string[]
  series: ChartSeries[]
}

/** The chart options for the data, with every colour, the font and the spacing taken from the Axis tokens. */
export function chartOptionFor(data: ChartData, tokens: DesignTokens): EChartsCoreOption {
  const { colour, type, spacing } = tokens
  const base = {
    color: [colour.primary, colour.success, colour.warning, colour.danger, colour.info],
    backgroundColor: 'transparent',
    textStyle: { fontFamily: type.fontFamily, fontSize: type.fontSize, color: colour.text },
    legend: { bottom: 0, textStyle: { color: colour.text } },
    tooltip: {
      backgroundColor: colour.surface,
      borderColor: colour.border,
      textStyle: { color: colour.text },
    },
  }

  if (data.kind === 'pie') {
    const first = data.series[0]
    return {
      ...base,
      tooltip: { ...base.tooltip, trigger: 'item' },
      series: [
        {
          type: 'pie',
          name: first?.label,
          radius: '65%',
          label: { color: colour.text },
          data: data.categories.map((category, index) => ({ name: category, value: first?.values[index] ?? null })),
        },
      ],
    }
  }

  return {
    ...base,
    tooltip: { ...base.tooltip, trigger: 'axis' },
    grid: {
      top: spacing.md,
      left: spacing.md,
      right: spacing.lg,
      bottom: spacing.xl + spacing.xs,
      containLabel: true,
    },
    xAxis: {
      type: 'category',
      data: data.categories,
      axisLine: { lineStyle: { color: colour.border } },
      axisLabel: { color: colour.textMuted },
    },
    yAxis: {
      type: 'value',
      axisLine: { show: true, lineStyle: { color: colour.border } },
      axisLabel: { color: colour.textMuted },
      splitLine: { lineStyle: { color: colour.divider } },
    },
    series: data.series.map((series) => ({ type: data.kind, name: series.label, data: series.values })),
  }
}
