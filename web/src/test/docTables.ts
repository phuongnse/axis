/// <reference types="node" />
// Reads the tables of docs/, so a test that compares a doc's list with the code finds the table by its header.
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

/** The rows under each table whose header matches `header`, without the separator line. A table ends at the first line that is not a row. */
export function tablesIn(markdown: string, header: RegExp): string[][] {
  const lines = markdown.split('\n')
  const tables: string[][] = []
  lines.forEach((line, index) => {
    if (!header.test(line)) return
    const rows: string[] = []
    for (const row of lines.slice(index + 1)) {
      if (!row.startsWith('|')) break
      if (!/^\|\s*-+/.test(row)) rows.push(row)
    }
    tables.push(rows)
  })
  return tables
}

/** The tables under docs/ whose header matches `header`, in the order of the files. Vitest runs from web/, so the docs are one folder up. */
export function docsTables(header: RegExp): string[][] {
  const docsRoot = join(process.cwd(), '../docs')
  return readdirSync(docsRoot, { recursive: true, encoding: 'utf8' })
    .filter((file) => file.endsWith('.md'))
    .flatMap((file) => tablesIn(readFileSync(join(docsRoot, file), 'utf8'), header))
}
