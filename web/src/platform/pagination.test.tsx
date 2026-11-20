import { render } from '@testing-library/react'
import { Pagination } from 'antd'
import { describe, expect, it } from 'vitest'
import { designTokensFor } from './designTokens'
import { paginationItems } from './pagination'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

function renderIn(mode: ThemeMode) {
  return render(
    <ThemeModeProvider initialMode={mode}>
      <Pagination total={50} current={2} itemRender={paginationItems(2)} />
    </ThemeModeProvider>,
  )
}

describe.each(['light', 'dark'] as const)('a pagination in %s mode', (mode) => {
  it.each([
    ['previous', '.ant-pagination-prev', 'rotate(90deg)'],
    ['next', '.ant-pagination-next', 'rotate(270deg)'],
  ] as const)('draws the %s arrow as the chevron, pointing its way', (_, selector, transform) => {
    const { container } = renderIn(mode)
    const button = container.querySelector(`${selector} button`)
    const icons = button?.querySelectorAll('.anticon-down') ?? []

    expect(icons).toHaveLength(1)
    expect(icons[0]).toHaveStyle({ fontSize: '12px', color: designTokensFor(mode).colour.textMuted })
    expect(icons[0].querySelector<SVGElement>('svg')?.style.transform).toBe(transform)
  })

  it('keeps aria-current on the current page', () => {
    const { container } = renderIn(mode)

    expect(container.querySelector('[aria-current="page"]')).toHaveTextContent(/^2$/)
  })
})
