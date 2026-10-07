import { render, screen } from '@testing-library/react'
import { DatePicker } from 'antd'
import dayjs from 'dayjs'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it } from 'vitest'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import { Shell } from './Shell'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const catalogs = [{ texts: {}, fallbackTexts: {} }]

function renderShell(locale: string) {
  return render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={catalogs} development={false}>
          <Shell titleKey="site.title" navigation={[]} locales={['en', 'vi']} locale={locale} onLocaleChange={() => {}}>
            <DatePicker />
          </Shell>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  dayjs.locale('en')
})

describe('Shell', () => {
  it('shows Ant Design components in English for en', () => {
    renderShell('en')

    expect(screen.getByRole('textbox')).toHaveAttribute('placeholder', 'Select date')
    expect(dayjs.locale()).toBe('en')
  })

  it('shows Ant Design components in Vietnamese for vi', () => {
    renderShell('vi')

    // The date placeholder of Ant Design's vi_VN locale.
    expect(screen.getByRole('textbox')).toHaveAttribute('placeholder', 'Chọn thời điểm')
    expect(dayjs.locale()).toBe('vi')
  })
})

describe('antLocaleFor', () => {
  it('maps en and vi in any letter case, and anything else to en_US', () => {
    expect(antLocaleFor('en').locale).toBe('en')
    expect(antLocaleFor('VI').locale).toBe('vi')
    expect(antLocaleFor('fr').locale).toBe('en')
    expect(dayjsLocaleFor('Vi')).toBe('vi')
    expect(dayjsLocaleFor('fr')).toBe('en')
  })
})
