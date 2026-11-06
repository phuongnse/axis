import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { storeLocale } from './locale'
import { ThemeModeProvider } from './theme'
import { useThemeMode } from './themeMode'

const storageApi = /\b(localStorage|sessionStorage|indexedDB)\b|document\.cookie|\bcaches\b/

function ChooseDark() {
  const { choose } = useThemeMode()
  return <button onClick={() => choose('dark')}>Dark</button>
}

describe('browser storage', () => {
  const sources = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}', '!../test/**'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  it('finds the sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('catches a use of browser storage', () => {
    expect(storageApi.test("localStorage.setItem('a', 'b')")).toBe(true)
    expect(storageApi.test("sessionStorage.getItem('a')")).toBe(true)
    expect(storageApi.test('document.cookie = x')).toBe(true)
    expect(storageApi.test('await caches.keys()')).toBe(true)
    expect(storageApi.test('const stored = readStoredLocale()')).toBe(false)
  })

  it('is used only by the theme, the locale and the export section', () => {
    const users = Object.entries(sources)
      .filter(([, text]) => storageApi.test(text))
      .map(([file]) => file)
      .sort()
    expect(users).toEqual(['../pages/TenantExportSection.tsx', './locale.ts', './theme.tsx'])
  })

  it('keeps only the theme, the locale and the export id in local storage and nothing in session storage', async () => {
    localStorage.clear()
    sessionStorage.clear()

    render(
      <ThemeModeProvider>
        <ChooseDark />
      </ThemeModeProvider>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Dark' }))
    storeLocale('vi')
    localStorage.setItem('axis.tenantExport', 'export-1')

    expect(Object.keys(localStorage).sort()).toEqual(['axis.locale', 'axis.tenantExport', 'axis.themeMode'])
    expect(sessionStorage.length).toBe(0)
  })
})
