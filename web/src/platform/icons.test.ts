import { describe, expect, it } from 'vitest'

const svgElement = /<svg\b/
const iconImport = /import\s+(?!type\b)\{([^}]*)\}\s+from\s+['"]@ant-design\/icons['"]/g
const deepIconImport = /from\s+['"]@ant-design\/icons\//
/** The arrows that Chevron draws in their place. Only Chevron.tsx and antTheme.ts may import them. */
const arrowIcons = ['DownOutlined', 'UpOutlined', 'LeftOutlined', 'RightOutlined']
// The glob's keys are relative to this folder, so these are the files of platform/.
const arrowFiles = ['./Chevron.tsx', './antTheme.ts']

/** The names a source imports from the icon library, by the outlined style's exact names. */
function iconNames(text: string): string[] {
  return [...text.matchAll(iconImport)].flatMap((match) =>
    match[1]
      .split(',')
      .map(
        (name) =>
          name
            .trim()
            .replace(/^type\s+/, '')
            .split(/\s+as\s+/)[0],
      )
      .filter((name) => name !== ''),
  )
}

describe('icons', () => {
  const components = import.meta.glob(['../**/*.tsx', '!../**/*.test.tsx'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>
  const sources = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  it('finds the sources', () => {
    expect(Object.keys(components).length).toBeGreaterThan(50)
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('catches an svg element', () => {
    expect(svgElement.test('<svg viewBox="0 0 1 1" />')).toBe(true)
  })

  it('are not drawn by hand in a component', () => {
    const drawn = Object.entries(components)
      .filter(([, text]) => svgElement.test(text))
      .map(([file]) => file)
    expect(drawn).toEqual([])
  })

  it('come from the icon library in the outlined style only', () => {
    const names = Object.values(sources).flatMap(iconNames)
    expect(names).toContain('BellOutlined')
    expect(names.filter((name) => !name.endsWith('Outlined'))).toEqual([])
  })

  it('draw the arrows only through Chevron', () => {
    const drawn = Object.entries(sources)
      .filter(([file]) => !arrowFiles.includes(file))
      .filter(([, text]) => iconNames(text).some((name) => arrowIcons.includes(name)))
      .map(([file]) => file)
    expect(drawn).toEqual([])
  })

  it('catch an arrow imported by name, and not an arrow with a longer name', () => {
    expect(iconNames("import { DownOutlined } from '@ant-design/icons'")).toContain('DownOutlined')
    expect(iconNames("import { ArrowDownOutlined } from '@ant-design/icons'")).not.toContain('DownOutlined')
  })

  it('are imported from the package root', () => {
    const deep = Object.entries(sources)
      .filter(([, text]) => deepIconImport.test(text))
      .map(([file]) => file)
    expect(deep).toEqual([])
  })
})
