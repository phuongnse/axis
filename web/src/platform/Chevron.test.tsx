import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Chevron, type ChevronPoint } from './Chevron'
import { designTokensFor } from './designTokens'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

function renderIn(mode: ThemeMode, point?: ChevronPoint) {
  return render(
    <ThemeModeProvider initialMode={mode}>
      <Chevron point={point} />
    </ThemeModeProvider>,
  )
}

describe.each(['light', 'dark'] as const)('the chevron in %s mode', (mode) => {
  it('draws DownOutlined at 12 px in the muted text colour', () => {
    const { container } = renderIn(mode)
    const icon = container.querySelector('.anticon-down')

    expect(icon).toHaveAttribute('aria-hidden', 'true')
    expect(icon).toHaveStyle({ fontSize: '12px', color: designTokensFor(mode).colour.textMuted })
  })

  it.each([
    ['down', undefined],
    ['left', 'rotate(90deg)'],
    ['up', 'rotate(180deg)'],
    ['right', 'rotate(270deg)'],
  ] as const)('points %s', (point, transform) => {
    const { container } = renderIn(mode, point)
    const svg = container.querySelector<SVGElement>('.anticon-down svg')

    expect(svg?.style.transform).toBe(transform ?? '')
  })
})
