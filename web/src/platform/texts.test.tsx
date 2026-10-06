import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TextProvider } from './TextProvider'
import { resolveText, useText } from './texts'

const fallbackTexts = { 'shell.home.title': 'Welcome to Axis', 'shell.nav.home': 'Home' }
const texts = { 'shell.nav.home': 'Trang chủ' }

describe('resolveText', () => {
  it('returns the text of the current locale', () => {
    expect(resolveText('shell.nav.home', texts, fallbackTexts, true)).toBe('Trang chủ')
    expect(resolveText('shell.nav.home', texts, fallbackTexts, false)).toBe('Trang chủ')
  })

  it('shows the key name for a key missing from the current locale in development', () => {
    expect(resolveText('shell.home.title', texts, fallbackTexts, true)).toBe('shell.home.title')
  })

  it('shows the fallback-locale text for a key missing from the current locale otherwise', () => {
    expect(resolveText('shell.home.title', texts, fallbackTexts, false)).toBe('Welcome to Axis')
  })

  it('shows the key name for a key missing from both locales', () => {
    expect(resolveText('shell.unknown', texts, fallbackTexts, true)).toBe('shell.unknown')
    expect(resolveText('shell.unknown', texts, fallbackTexts, false)).toBe('shell.unknown')
  })
})

function Title() {
  const t = useText()
  return <h1>{t('shell.home.title')}</h1>
}

describe('TextProvider', () => {
  it('renders the key name of a missing text in development', () => {
    render(
      <TextProvider texts={texts} fallbackTexts={fallbackTexts} development>
        <Title />
      </TextProvider>,
    )

    expect(screen.getByRole('heading')).toHaveTextContent('shell.home.title')
  })

  it('renders the fallback-locale value of a missing text otherwise', () => {
    render(
      <TextProvider texts={texts} fallbackTexts={fallbackTexts} development={false}>
        <Title />
      </TextProvider>,
    )

    expect(screen.getByRole('heading')).toHaveTextContent('Welcome to Axis')
  })

  it('fails outside the provider', () => {
    expect(() => render(<Title />)).toThrow('useText must be used inside TextProvider.')
  })
})
