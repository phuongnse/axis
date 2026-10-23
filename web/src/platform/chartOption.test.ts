import { describe, expect, it } from 'vitest'
import { chartOptionFor, type ChartData } from './chartOption'
import { designTokensFor } from './designTokens'

const categories = ['Hardware', 'Software', 'Services']
const series = [
  { label: 'Approved', values: [4, 7, null] },
  { label: 'Rejected', values: [1, 2, 3] },
]

/** Reads a nested value from a chart option, which is untyped. */
function at(value: unknown, ...path: string[]): unknown {
  return path.reduce<unknown>((current, key) => (current as Record<string, unknown>)[key], value)
}

describe.each(['light', 'dark'] as const)('the chart options in %s mode', (mode) => {
  const tokens = designTokensFor(mode)
  const { colour, type } = tokens

  it.each(['bar', 'line', 'pie'] as const)('take the %s colours and font from the tokens', (kind) => {
    const option = chartOptionFor({ kind, categories, series }, tokens)
    expect(at(option, 'color')).toEqual([colour.primary, colour.success, colour.warning, colour.danger, colour.info])
    expect(at(option, 'backgroundColor')).toBe('transparent')
    expect(at(option, 'textStyle', 'fontFamily')).toBe(type.fontFamily)
    expect(at(option, 'textStyle', 'fontSize')).toBe(type.fontSize)
    expect(at(option, 'textStyle', 'color')).toBe(colour.text)
    expect(at(option, 'legend', 'textStyle', 'color')).toBe(colour.text)
    expect(at(option, 'tooltip', 'backgroundColor')).toBe(colour.surface)
    expect(at(option, 'tooltip', 'borderColor')).toBe(colour.border)
    expect(at(option, 'tooltip', 'textStyle', 'color')).toBe(colour.text)
  })

  it.each(['bar', 'line'] as const)('colour the %s axes and grid from the tokens', (kind) => {
    const option = chartOptionFor({ kind, categories, series }, tokens)
    expect(at(option, 'xAxis', 'axisLine', 'lineStyle', 'color')).toBe(colour.border)
    expect(at(option, 'yAxis', 'axisLine', 'lineStyle', 'color')).toBe(colour.border)
    expect(at(option, 'yAxis', 'splitLine', 'lineStyle', 'color')).toBe(colour.divider)
    expect(at(option, 'xAxis', 'axisLabel', 'color')).toBe(colour.textMuted)
    expect(at(option, 'grid', 'left')).toBe(tokens.spacing.md)
  })
})

describe('the chart options for the data', () => {
  const tokens = designTokensFor('light')

  it.each(['bar', 'line'] as const)('make one %s series for each input series', (kind) => {
    const option = chartOptionFor({ kind, categories, series }, tokens)
    expect(at(option, 'xAxis', 'data')).toEqual(categories)
    expect(at(option, 'series')).toEqual([
      { type: kind, name: 'Approved', data: [4, 7, null] },
      { type: kind, name: 'Rejected', data: [1, 2, 3] },
    ])
  })

  it('makes one pie series from the first input series', () => {
    const data: ChartData = { kind: 'pie', categories, series }
    const pie = (at(chartOptionFor(data, tokens), 'series') as Record<string, unknown>[])[0]
    expect(pie.type).toBe('pie')
    expect(pie.data).toEqual([
      { name: 'Hardware', value: 4 },
      { name: 'Software', value: 7 },
      { name: 'Services', value: null },
    ])
  })

  it('makes an empty pie when there is no series', () => {
    const option = chartOptionFor({ kind: 'pie', categories, series: [] }, tokens)
    expect((at(option, 'series') as Record<string, unknown>[])[0].data).toEqual(
      categories.map((name) => ({ name, value: null })),
    )
  })
})
