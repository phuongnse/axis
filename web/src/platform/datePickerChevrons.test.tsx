import { render } from '@testing-library/react'
import { DatePicker } from 'antd'
import dayjs from 'dayjs'
import { describe, expect, it } from 'vitest'
import { designTokensFor } from './designTokens'
import { datePickerChevrons } from './datePickerChevrons'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

// The picker's popup opens in the document, not in the render's container.
function renderIn(mode: ThemeMode) {
  return render(
    <ThemeModeProvider initialMode={mode}>
      <DatePicker open defaultValue={dayjs('2026-03-04')} {...datePickerChevrons} />
    </ThemeModeProvider>,
  )
}

describe.each(['light', 'dark'] as const)('an open date picker in %s mode', (mode) => {
  it.each([
    ['previous month', '.ant-picker-header-prev-btn'],
    ['next month', '.ant-picker-header-next-btn'],
  ])('draws the %s arrow as the chevron at 12 px in the muted text colour', (_, selector) => {
    renderIn(mode)
    const icons = document.querySelectorAll(`${selector} .anticon-down`)

    expect(icons).toHaveLength(1)
    expect(icons[0]).toHaveStyle({ fontSize: '12px', color: designTokensFor(mode).colour.textMuted })
  })

  it("keeps Ant Design's double arrows, which jump a year", () => {
    renderIn(mode)

    expect(document.querySelector('.ant-picker-header-super-prev-btn .anticon-down')).toBeNull()
    expect(document.querySelector('.ant-picker-header-super-next-btn .anticon-down')).toBeNull()
  })
})
