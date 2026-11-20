import { render, screen } from '@testing-library/react'
import { Pagination } from 'antd'
import { describe, expect, it } from 'vitest'
import { designTokensFor } from './designTokens'
import { paginationItems } from './pagination'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

const names: Record<string, string> = {
  'shell.pagination.previous': 'Previous page',
  'shell.pagination.next': 'Next page',
}
const text = (key: string) => names[key] ?? key

function renderIn(mode: ThemeMode) {
  return render(
    <ThemeModeProvider initialMode={mode}>
      <Pagination total={50} current={2} itemRender={paginationItems(2, text)} />
    </ThemeModeProvider>,
  )
}

describe.each(['light', 'dark'] as const)('a pagination in %s mode', (mode) => {
  it.each([
    ['Previous page', '.ant-pagination-prev', 'rotate(90deg)'],
    ['Next page', '.ant-pagination-next', 'rotate(270deg)'],
  ] as const)('names the "%s" arrow and draws the chevron, pointing its way', (name, selector, transform) => {
    const { container } = renderIn(mode)
    const button = screen.getByRole('button', { name })
    const icons = button.querySelectorAll('.anticon-down')

    expect(container.querySelector(selector)).toContainElement(button)
    expect(icons).toHaveLength(1)
    expect(icons[0]).toHaveStyle({ fontSize: '12px', color: designTokensFor(mode).colour.textMuted })
    expect(icons[0].querySelector<SVGElement>('svg')?.style.transform).toBe(transform)
  })

  it('keeps aria-current on the current page', () => {
    const { container } = renderIn(mode)

    expect(container.querySelector('[aria-current="page"]')).toHaveTextContent(/^2$/)
  })
})
