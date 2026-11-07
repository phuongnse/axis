import { describe, expect, it } from 'vitest'
import en from '../../../src/Axis.Presentation/Texts/en.json'
import vi from '../../../src/Axis.Presentation/Texts/vi.json'
import { kindGroup, kindName, kindSingular, sidebarRank, studioKinds } from './studioKinds'
import { starterKinds } from './studioStarters'

const texts = en as Record<string, string>
const text = (key: string) => texts[key] ?? key
const namedGroups = ['data', 'pages', 'logic', 'access', 'integration', 'texts', 'tests']

describe('studioKinds', () => {
  it.each(starterKinds)('gives %s a singular name, a plural name and a named sidebar group', (kind) => {
    const entry = studioKinds.find((candidate) => candidate.kind === kind)

    expect(entry).toBeDefined()
    expect(kindSingular(kind, text)).not.toContain('shell.studio')
    expect(kindName(kind, text)).not.toContain('shell.studio')
    expect(kindSingular(kind, text)).not.toBe(kindName(kind, text))
    expect(namedGroups).toContain(kindGroup(kind))
  })

  it.each(starterKinds)('gives %s a create button and a dialog title in en and vi', (kind) => {
    for (const catalogue of [en, vi] as Record<string, string>[]) {
      expect(catalogue[`shell.studio.newResource.kindButton.${kind}`]).toMatch(/…$/)
      expect(catalogue[`shell.studio.newResource.kindTitle.${kind}`]).toBeTruthy()
    }
  })

  it('names a kind that is not in the catalogue by the kind and puts it under other', () => {
    expect(kindSingular('unknown', text)).toBe('unknown')
    expect(kindName('unknown', text)).toBe('unknown')
    expect(kindGroup('unknown')).toBe('other')
  })
})

describe('sidebarRank', () => {
  it('puts the manifest first, a named kind at its place in the sidebar and any other kind last', () => {
    expect(sidebarRank('application')).toBe(-1)
    expect(sidebarRank('entity')).toBe(0)
    expect(sidebarRank('page')).toBeGreaterThan(sidebarRank('entity'))
    expect(sidebarRank('unknown')).toBe(studioKinds.length)
    expect(sidebarRank(null)).toBe(studioKinds.length)
  })
})
