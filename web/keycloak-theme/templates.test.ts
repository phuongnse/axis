/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/** The [template, page] pairs of the table in the identity provider's reference, sorted. */
function documentedPages(markdown: string): [string, string][] {
  return [...markdown.matchAll(/^ *\| `([\w-]+\.ftl)` +\| `(\w+)` +\|/gm)]
    .map((row): [string, string] => [row[1], row[2]])
    .sort()
}

describe('documentedPages', () => {
  it('reads each row of the template table and nothing else', () => {
    const markdown =
      '| Template | `page` |\n| --- | --- |\n| `a.ftl` | `one` |\n| `b-c.ftl`   | `two`   |\n| `x` | `y` |\n'

    expect(documentedPages(markdown)).toEqual([
      ['a.ftl', 'one'],
      ['b-c.ftl', 'two'],
    ])
  })
})

describe('the theme templates', () => {
  it('are the table of docs/reference/identity-provider.md, each with its page', () => {
    // The folders sit in variables because Vite rewrites a literal `new URL('...', import.meta.url)` into an asset.
    const document = '../../docs/reference/identity-provider.md'
    const theme = './'
    const documentFile = fileURLToPath(new URL(document, import.meta.url))
    const themeFolder = fileURLToPath(new URL(theme, import.meta.url))
    // frame.ftl holds the shared macros and writes no page.
    const templates = readdirSync(themeFolder)
      .filter((name) => name.endsWith('.ftl') && name !== 'frame.ftl')
      .map((name): [string, string] => [
        name,
        /"page": "(\w+)"/.exec(readFileSync(`${themeFolder}${name}`, 'utf8'))?.[1] ?? '',
      ])
      .sort()

    expect(templates.length).toBeGreaterThan(0)
    expect(documentedPages(readFileSync(documentFile, 'utf8'))).toEqual(templates)
    expect(templates).toContainEqual(['login-oauth-grant.ftl', 'consent'])
  })
})
