import { describe, expect, it } from 'vitest'

const svgElement = /<svg\b/
const iconImport = /import\s+(?!type\b)\{([^}]*)\}\s+from\s+['"]@ant-design\/icons['"]/g
const deepIconImport = /from\s+['"]@ant-design\/icons\//

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
    const names = Object.values(sources).flatMap((text) =>
      [...text.matchAll(iconImport)].flatMap((match) =>
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
      ),
    )
    expect(names).toContain('BellOutlined')
    expect(names.filter((name) => !name.endsWith('Outlined'))).toEqual([])
  })

  it('are imported from the package root', () => {
    const deep = Object.entries(sources)
      .filter(([, text]) => deepIconImport.test(text))
      .map(([file]) => file)
    expect(deep).toEqual([])
  })
})
