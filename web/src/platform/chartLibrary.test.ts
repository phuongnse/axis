/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

describe('the chart library', () => {
  const sources = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  it('finds the sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('is imported by one file only, apart from types', () => {
    const importers = Object.entries(sources)
      .filter(([, text]) => /^(?!import type\b).*\bfrom\s+['"]echarts/m.test(text))
      .map(([file]) => file)
    expect(importers).toEqual(['./ChartDrawing.tsx'])
  })

  it('is reached through a dynamic import only', () => {
    const users = Object.entries(sources).filter(
      ([file, text]) => file !== './ChartDrawing.tsx' && /ChartDrawing/.test(text),
    )
    expect(users.map(([file]) => file)).toEqual(['./Chart.tsx'])
    const [[, text]] = users
    expect(text.match(/import\(\s*['"]\.\/ChartDrawing['"]\s*\)/g)).toHaveLength(1)
    expect(text).not.toMatch(/^\s*import\b[^(\n]*\bfrom\s+['"]\.\/ChartDrawing['"]/m)
  })

  it.each([
    ['the configuration project', '../../../src/Axis.Configuration'],
    ['the presentation project', '../../../src/Axis.Presentation'],
  ])('is not named in %s', (_name, folder) => {
    const root = fileURLToPath(new URL(folder, import.meta.url))
    const files = readdirSync(root, { recursive: true, encoding: 'utf8' }).filter(
      (file) => /\.(cs|json)$/.test(file) && !/(^|[\\/])(bin|obj)[\\/]/.test(file),
    )
    expect(files.length).toBeGreaterThan(0)
    expect(files.filter((file) => /echarts/i.test(readFileSync(join(root, file), 'utf8')))).toEqual([])
  })

  it('is not named in the page metadata types', () => {
    const files = import.meta.glob('./site.ts', { query: '?raw', import: 'default', eager: true }) as Record<
      string,
      string
    >
    expect(Object.keys(files)).toEqual(['./site.ts'])
    expect(/echarts/i.test(files['./site.ts'])).toBe(false)
  })
})
