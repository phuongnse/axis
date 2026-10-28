import { describe, expect, it } from 'vitest'
import siteSchema from '../../../src/Axis.Configuration/Schemas/site.schema.json'
import { navigationIcon, navigationIcons, pageIcon } from './navigationIcons'

const names = siteSchema.properties.navigation.items.properties.icon.enum

describe('navigationIcon', () => {
  it('maps exactly the names the site schema lists', () => {
    expect(Object.keys(navigationIcons).toSorted()).toEqual(names.toSorted())
  })

  it('maps every name to its own icon, not the page icon', () => {
    const icons = names.map((name) => navigationIcon(name))

    expect(icons.every((icon) => icon !== undefined && icon !== pageIcon)).toBe(true)
    expect(new Set(icons).size).toBe(names.length)
  })

  it.each([undefined, null, 'rocket', 'Home', 'toString', '__proto__'])('maps %s to the page icon', (name) => {
    expect(navigationIcon(name)).toBe(pageIcon)
  })
})
