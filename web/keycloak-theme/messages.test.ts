/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/** The keys of a `.properties` file: the text before the first `=` of each line that is not blank or a comment. */
function keysOf(properties: string): string[] {
  return properties
    .split(/\r?\n/)
    .filter((line) => line.trim() !== '' && !line.startsWith('#') && line.includes('='))
    .map((line) => line.slice(0, line.indexOf('=')).trim())
}

/** What is missing from the theme: a locale without a bundle or in no `locales=` entry, and a key the English bundle has. */
function missingMessages(locales: string[], themeLocales: string[], bundles: Record<string, string[] | undefined>) {
  const problems: string[] = []
  const english = bundles.en ?? []
  for (const locale of locales) {
    if (!themeLocales.includes(locale)) {
      problems.push(`theme.properties does not list the locale ${locale}`)
    }
    const keys = bundles[locale]
    if (!keys) {
      problems.push(`messages_${locale}.properties is missing`)
      continue
    }
    for (const key of english.filter((key) => !keys.includes(key))) {
      problems.push(`messages_${locale}.properties is missing ${key}`)
    }
  }
  return problems
}

describe('missingMessages', () => {
  it('reports a missing key, a missing bundle and a locale that theme.properties does not list', () => {
    const bundles = { en: ['a', 'b'], vi: ['a'] }

    expect(missingMessages(['en', 'vi'], ['en', 'vi'], bundles)).toEqual(['messages_vi.properties is missing b'])
    expect(missingMessages(['en', 'vi', 'fr'], ['en', 'vi'], bundles)).toEqual([
      'messages_vi.properties is missing b',
      'theme.properties does not list the locale fr',
      'messages_fr.properties is missing',
    ])
  })

  it('reports nothing when every locale has every key', () => {
    expect(missingMessages(['en', 'vi'], ['en', 'vi'], { en: ['a'], vi: ['a', 'extra'] })).toEqual([])
  })
})

describe('the theme messages', () => {
  it('cover every platform locale with every key of the English bundle', () => {
    // The folders sit in variables because Vite rewrites a literal `new URL('...', import.meta.url)` into an asset.
    const [texts, theme] = ['../../src/Axis.Presentation/Texts/', './']
    const textsFolder = fileURLToPath(new URL(texts, import.meta.url))
    const themeFolder = fileURLToPath(new URL(theme, import.meta.url))
    const locales = readdirSync(textsFolder)
      .filter((name) => name.endsWith('.json'))
      .map((name) => name.slice(0, -'.json'.length))
      .sort()
    const themeLocales = (/^locales=(.*)$/m.exec(readFileSync(`${themeFolder}theme.properties`, 'utf8'))?.[1] ?? '')
      .split(',')
      .map((locale) => locale.trim())
    const bundles = Object.fromEntries(
      locales.map((locale) => {
        try {
          return [locale, keysOf(readFileSync(`${themeFolder}messages/messages_${locale}.properties`, 'utf8'))]
        } catch {
          return [locale, undefined]
        }
      }),
    )

    expect(locales.length).toBeGreaterThanOrEqual(2)
    expect(bundles.en?.length).toBeGreaterThan(0)
    expect(missingMessages(locales, themeLocales, bundles)).toEqual([])
  })
})
