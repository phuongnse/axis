import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Chart, type ChartKind } from './Chart'
import { designTokensFor } from './designTokens'
import { ThemeModeProvider } from './theme'

const categories = ['Hardware', 'Software']
const series = [
  { label: 'Approved', values: [4, 7] },
  { label: 'Rejected', values: [1, 2] },
]

async function drawn(kind: ChartKind) {
  render(
    <ThemeModeProvider>
      <Chart title="Requests by category" kind={kind} categories={categories} series={series} />
    </ThemeModeProvider>,
  )
  const chart = await screen.findByLabelText('Requests by category')
  const svg = await waitFor(() => {
    const found = chart.querySelector('svg')
    expect(found).not.toBeNull()
    return found as SVGSVGElement
  })
  return { chart, svg }
}

function texts(svg: SVGSVGElement) {
  return Array.from(svg.querySelectorAll('text')).map((text) => text.textContent)
}

describe('the shared chart', () => {
  // jsdom reports a zero size, so the library hides the axis labels. Give the chart's element a size.
  beforeEach(() => {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(600)
    vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(320)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows a busy placeholder while the library loads', () => {
    const { container } = render(<Chart title="Requests" kind="bar" categories={categories} series={series} />)
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull()
  })

  it.each(['bar', 'line'] as const)('draws a %s chart with its categories and series labels', async (kind) => {
    const { svg } = await drawn(kind)
    expect(svg.querySelectorAll('path').length).toBeGreaterThan(0)
    expect(texts(svg)).toEqual(expect.arrayContaining(['Hardware', 'Software', 'Approved', 'Rejected']))
  })

  it('draws a pie chart with its categories', async () => {
    const { svg } = await drawn('pie')
    expect(svg.querySelectorAll('path').length).toBeGreaterThan(0)
    expect(texts(svg)).toEqual(expect.arrayContaining(['Hardware', 'Software']))
  })

  it('fills the first series with the primary colour', async () => {
    const { svg } = await drawn('bar')
    const fills = Array.from(svg.querySelectorAll('path')).map((path) => path.getAttribute('fill'))
    expect(fills).toContain(designTokensFor('light').colour.primary)
  })
})
