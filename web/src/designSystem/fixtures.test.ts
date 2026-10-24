import { describe, expect, test } from 'vitest'
import type { RecordPage } from '../platform/records'
import { chartWidget, routes, siteTexts } from './fixtures'

const rowsOf = (rowsPath: string) => (routes[rowsPath]() as RecordPage).items

describe('design-system chart fixtures', () => {
  test.each(['bar', 'line'] as const)('the %s chart measures are on a similar scale', (kind) => {
    const widget = chartWidget(kind)
    const { measures } = widget.chart!
    const rows = rowsOf(widget.dataSource!.rowsPath)
    const maxima = measures.map((measure) => Math.max(...rows.map((row) => Number(row.values[measure.name]))))
    const largest = Math.max(...maxima)

    expect(measures.length).toBeGreaterThanOrEqual(2)
    for (const max of maxima) {
      expect(max).toBeGreaterThan(0)
      expect(max).toBeGreaterThanOrEqual(largest / 5)
    }
  })

  test.each(['bar', 'line', 'pie'] as const)('the %s chart reads columns and labels the fixture has', (kind) => {
    const widget = chartWidget(kind)
    const { chart, dataSource } = widget
    const columns = dataSource!.columns.map((column) => column.name)

    expect(columns).toContain(chart!.category)
    for (const measure of chart!.measures) {
      expect(columns).toContain(measure.name)
      expect(Object.keys(siteTexts)).toContain(measure.labelKey)
    }
    expect(Object.keys(siteTexts)).toContain(chart!.titleKey)
    for (const row of rowsOf(dataSource!.rowsPath)) {
      expect(Object.keys(row.values).sort()).toEqual([...columns].sort())
    }
  })

  test('the pie chart binds one measure', () => {
    expect(chartWidget('pie').chart!.measures).toHaveLength(1)
  })
})
