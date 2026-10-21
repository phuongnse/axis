import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { checkDocs, maxAgentsLines, maxDocLines } from './check-docs.mjs'

const script = fileURLToPath(new URL('./check-docs.mjs', import.meta.url))
const repositoryRoot = fileURLToPath(new URL('..', import.meta.url))

let root

function write(file, text) {
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true })
  writeFileSync(path.join(root, file), text)
}

const index = (...rows) =>
  ['# Decisions', '', '| Decision | Title | Status |', '| --- | --- | --- |', ...rows].join('\n') + '\n'

beforeEach(() => {
  root = mkdtempSync(path.join(tmpdir(), 'check-docs-'))
  write('docs/README.md', '# Documentation\n\n| [Decisions](decisions.md) | [Guide](reference/guide.md) |\n')
  write('docs/reference/guide.md', '# Guide\n')
  write('docs/decisions/d1-files-first.md', '# D1. Files first\n\nStatus: **Agreed**\n\nText.\n')
  write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expressions\n\nStatus: **Proposed**\n')
  write(
    'docs/decisions.md',
    index(
      '| [D1](decisions/d1-files-first.md) | Files first | **Agreed** |',
      '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
    ),
  )
  write('AGENTS.md', '# Agents\n')
})

afterEach(() => rmSync(root, { recursive: true, force: true }))

const messages = () => checkDocs(root).map(({ file, line, message }) => `${file}:${line}: ${message}`)

describe('checkDocs', () => {
  test('passes on matching docs', () => {
    assert.deepEqual(messages(), [])
  })

  test('reports a doc that the docs map does not list', () => {
    write('docs/reference/orphan.md', '# Orphan\n')

    assert.deepEqual(messages(), ['docs/README.md:1: does not list docs/reference/orphan.md'])
  })

  test('reports a doc and AGENTS.md over their size limits', () => {
    write('docs/reference/guide.md', '# Guide\n' + 'line\n'.repeat(maxDocLines))
    write('AGENTS.md', 'line\n'.repeat(maxAgentsLines + 1))

    const found = messages()
    assert.equal(found.length, 2)
    assert.match(found[0], /^docs\/reference\/guide\.md:1001: 1001 lines, over the limit of 1000/)
    assert.match(found[1], /^AGENTS\.md:101: 101 lines, over the limit of 100/)
  })

  test('reports a decision file without a row and a row without a file', () => {
    write('docs/decisions/d3-new-thing.md', '# D3. New thing\n\nStatus: **Proposed**\n')
    write(
      'docs/decisions.md',
      index(
        '| [D1](decisions/d1-files-first.md) | Files first | **Agreed** |',
        '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
        '| [D4](decisions/d4-missing.md) | Missing | **Agreed** |',
      ),
    )

    assert.deepEqual(messages().sort(), [
      'docs/decisions.md:7: D4 has no file in docs/decisions/',
      'docs/decisions.md:7: row D4 should be D3: decisions are numbered in order with no gaps',
      'docs/decisions/d3-new-thing.md:1: D3 has no row in docs/decisions.md',
    ])
  })

  test('reports a row whose title or status differs from its file', () => {
    write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expressions\n\nStatus: **Agreed**\n')
    write(
      'docs/decisions.md',
      index(
        '| [D1](decisions/d1-files-first.md) | Files come first | **Agreed** |',
        '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
      ),
    )

    assert.deepEqual(messages(), [
      "docs/decisions.md:5: D1 title 'Files come first' differs from the file's 'Files first'",
      "docs/decisions.md:6: D2 status 'Proposed' differs from the file's 'Agreed'",
    ])
  })

  test('reports a decision file with a wrong name, title or status', () => {
    write('docs/decisions/d1-files-first.md', '# D1. Files first\n\nStatus: Agreed\n')
    write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expression language\n\nStatus: **Proposed**\n')
    write('docs/decisions/notes.md', '# Notes\n')

    assert.deepEqual(messages().sort(), [
      "docs/decisions.md:6: D2 title 'Typed expressions' differs from the file's 'Typed expression language'",
      "docs/decisions/d1-files-first.md:3: needs a 'Status: **Agreed**' or 'Status: **Proposed**' line",
      'docs/decisions/d2-typed-expressions.md:1: the file name should end in d2-typed-expression-language.md',
      'docs/decisions/notes.md:1: a decision file is named docs/decisions/d<n>-<slug>.md',
    ])
  })
})

describe('CLI', () => {
  test('prints each problem with file and line and exits 1', () => {
    write('docs/reference/orphan.md', '# Orphan\n')

    const result = spawnSync(process.execPath, [script, '--root', root], { encoding: 'utf8' })

    assert.equal(result.status, 1)
    assert.equal(result.stderr, 'docs/README.md:1: does not list docs/reference/orphan.md\n')
  })

  test('passes on the repository', () => {
    const result = spawnSync(process.execPath, [script, '--root', repositoryRoot], { encoding: 'utf8' })

    assert.equal(result.stderr, '')
    assert.equal(result.status, 0)
  })
})
