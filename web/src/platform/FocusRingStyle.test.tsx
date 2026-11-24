import { render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { designTokensFor } from './designTokens'
import { FocusRingStyle } from './FocusRingStyle'
import { ThemeModeProvider } from './theme'

const { colour } = designTokensFor('light')

/** The text of every style element in the document. */
function styleTexts() {
  return [...document.querySelectorAll('style')].map((style) => style.textContent ?? '')
}

describe('FocusRingStyle', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => localStorage.clear())

  it('draws a 2px focus ring outside the element the selector matches', () => {
    render(<FocusRingStyle selector="[data-x]:focus-visible" />)

    expect(styleTexts()).toContain(
      `[data-x]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: 2px; }`,
    )
  })

  it('draws the same ring 2px inside the element with inset', () => {
    render(<FocusRingStyle selector="[data-x]:focus-visible" inset />)

    expect(styleTexts()).toContain(
      `[data-x]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: -2px; }`,
    )
  })

  it('takes the focus ring of dark mode', () => {
    localStorage.setItem('axis.themeMode', 'dark')
    render(
      <ThemeModeProvider>
        <FocusRingStyle selector="[data-x]:focus-visible" />
      </ThemeModeProvider>,
    )

    expect(styleTexts().join('\n')).toContain(`outline: 2px solid ${designTokensFor('dark').colour.focusRing};`)
  })
})
