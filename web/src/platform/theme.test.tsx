import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ThemeModeProvider } from './theme'
import { useThemeMode } from './themeMode'

function Mode() {
  return <output>{useThemeMode().mode}</output>
}

describe('ThemeModeProvider', () => {
  it('starts in the stored mode', () => {
    localStorage.setItem('axis.themeMode', 'dark')

    render(
      <ThemeModeProvider>
        <Mode />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('status')).toHaveTextContent('dark')
  })

  it('starts in the given mode instead of the stored one', () => {
    localStorage.setItem('axis.themeMode', 'dark')

    render(
      <ThemeModeProvider initialMode="light">
        <Mode />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('status')).toHaveTextContent('light')
  })
})
