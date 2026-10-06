import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TextProvider } from './TextProvider'
import { resolveText, useText, type TextCatalog } from './texts'

const fallbackTexts = { 'shell.home.title': 'Welcome to Axis', 'shell.nav.home': 'Home' }
const texts = { 'shell.nav.home': 'Trang chủ' }
const platform: TextCatalog[] = [{ texts, fallbackTexts }]

const site: TextCatalog = {
  texts: { 'nav.notes': 'Ghi chú', 'shell.nav.home': 'Trang đầu' },
  fallbackTexts: { 'nav.notes': 'Notes', 'nav.categories': 'Categories', 'shell.nav.home': 'Start' },
}
const siteThenPlatform: TextCatalog[] = [site, ...platform]

describe('resolveText', () => {
  it('returns the text of the current locale', () => {
    expect(resolveText('shell.nav.home', platform, true)).toBe('Trang chủ')
    expect(resolveText('shell.nav.home', platform, false)).toBe('Trang chủ')
  })

  it('shows the key name for a key missing from the current locale in development', () => {
    expect(resolveText('shell.home.title', platform, true)).toBe('shell.home.title')
  })

  it('shows the fallback-locale text for a key missing from the current locale otherwise', () => {
    expect(resolveText('shell.home.title', platform, false)).toBe('Welcome to Axis')
  })

  it('shows the key name for a key missing from both locales', () => {
    expect(resolveText('shell.unknown', platform, true)).toBe('shell.unknown')
    expect(resolveText('shell.unknown', platform, false)).toBe('shell.unknown')
  })

  it('prefers the site text over the platform text for a key both have', () => {
    expect(resolveText('shell.nav.home', siteThenPlatform, true)).toBe('Trang đầu')
    expect(resolveText('shell.nav.home', siteThenPlatform, false)).toBe('Trang đầu')
  })

  it('falls through to the platform texts for a key the site lacks in both locales', () => {
    expect(resolveText('shell.home.title', siteThenPlatform, false)).toBe('Welcome to Axis')
    expect(resolveText('shell.nav.home', [{ texts: {}, fallbackTexts: {} }, ...platform], true)).toBe('Trang chủ')
  })

  it('applies the development rule within the catalog that has the key', () => {
    expect(resolveText('nav.categories', siteThenPlatform, true)).toBe('nav.categories')
    expect(resolveText('nav.categories', siteThenPlatform, false)).toBe('Categories')
  })

  it('ignores keys inherited from the object prototype', () => {
    expect(resolveText('constructor', siteThenPlatform, false)).toBe('constructor')
  })
})

function Title() {
  const t = useText()
  return <h1>{t('shell.home.title')}</h1>
}

describe('TextProvider', () => {
  it('renders the key name of a missing text in development', () => {
    render(
      <TextProvider catalogs={platform} development>
        <Title />
      </TextProvider>,
    )

    expect(screen.getByRole('heading')).toHaveTextContent('shell.home.title')
  })

  it('renders the fallback-locale value of a missing text otherwise', () => {
    render(
      <TextProvider catalogs={platform} development={false}>
        <Title />
      </TextProvider>,
    )

    expect(screen.getByRole('heading')).toHaveTextContent('Welcome to Axis')
  })

  it('fails outside the provider', () => {
    expect(() => render(<Title />)).toThrow('useText must be used inside TextProvider.')
  })
})
