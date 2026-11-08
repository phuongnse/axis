import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import english from '../../../src/Axis.Presentation/Texts/en.json'
import vietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { relationTextKey } from './studio'

// Vitest runs from the web folder, as `npm test --prefix web` does.
const root = join(process.cwd(), '..')
const compiled = [
  ...readFileSync(join(root, 'src/Axis.Configuration/Compilation/ReferenceRelations.cs'), 'utf8').matchAll(
    /const string \w+ = "([^"]+)";/g,
  ),
].map((match) => match[1])

const section = (() => {
  const doc = readFileSync(join(root, 'docs/reference/configuration.md'), 'utf8')
  const start = doc.indexOf('## Reference index')
  const end = doc.indexOf('\n## ', start + 1)
  return doc.slice(start, end === -1 ? undefined : end)
})()
const documented = [...section.matchAll(/^\| `([^`]+)` \|/gm)].map((match) => match[1])

const keys = compiled.map(relationTextKey).sort()

function relationKeys(texts: Record<string, string>) {
  return Object.keys(texts)
    .filter((key) => key.startsWith('shell.studio.relation.'))
    .sort()
}

describe('the relation texts', () => {
  it('builds the text key of a relation in camel case', () => {
    expect(relationTextKey('field target')).toBe('shell.studio.relation.fieldTarget')
    expect(relationTextKey('grant')).toBe('shell.studio.relation.grant')
  })

  it('lists the same relations in the compiler and in the configuration doc', () => {
    expect(new Set(documented)).toEqual(new Set(compiled))
  })

  it('holds one text for each relation in each locale and no other relation text', () => {
    expect(relationKeys(english)).toEqual(keys)
    expect(relationKeys(vietnamese)).toEqual(keys)
  })

  it('gives each relation a non-empty text in each locale', () => {
    const texts = (locale: Record<string, string>) => keys.filter((key) => !locale[key]?.trim())
    expect(texts(english)).toEqual([])
    expect(texts(vietnamese)).toEqual([])
  })
})
