import assert from 'node:assert/strict'
import { rmSync } from 'node:fs'
import path from 'node:path'
import { beforeEach, describe, test } from 'node:test'
import { checkDecisions } from './decisions.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkDecisions)

const index = (...rows) =>
  ['# Decisions', '', '| Decision | Title | Status |', '| --- | --- | --- |', ...rows].join('\n') + '\n'

describe('decisions', () => {
  beforeEach(() => {
    write('docs/decisions/d1-files-first.md', '# D1. Files first\n\nStatus: **Agreed**\n\nText.\n')
    write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expressions\n\nStatus: **Proposed**\n')
    write(
      'docs/decisions.md',
      index(
        '| [D1](decisions/d1-files-first.md) | Files first | **Agreed** |',
        '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
      ),
    )
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

  const sha = 'a'.repeat(40)
  const lastText = (name) => `[last text](https://github.com/owner/axis/blob/${sha}/docs/decisions/${name})`

  test('passes a superseded row that names a decision in force and has no file', () => {
    write('docs/decisions/d3-files-and-folders.md', '# D3. Files and folders\n\nStatus: **Agreed**\n')
    rmSync(path.join(fixture.root(), 'docs/decisions/d1-files-first.md'))
    write(
      'docs/decisions.md',
      index(
        `| D1 | Files first | Superseded by [D3](decisions/d3-files-and-folders.md), ${lastText('d1-files-first.md')} |`,
        '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
        '| [D3](decisions/d3-files-and-folders.md) | Files and folders | **Agreed** |',
      ),
    )

    assert.deepEqual(messages(), [])
  })

  test('passes a decision superseded by two decisions in force', () => {
    write('docs/decisions/d3-files-and-folders.md', '# D3. Files and folders\n\nStatus: **Agreed**\n')
    rmSync(path.join(fixture.root(), 'docs/decisions/d1-files-first.md'))
    write(
      'docs/decisions.md',
      index(
        `| D1 | Files first | Superseded by [D2](decisions/d2-typed-expressions.md) and [D3](decisions/d3-files-and-folders.md), ${lastText('d1-files-first.md')} |`,
        '| [D2](decisions/d2-typed-expressions.md) | Typed expressions | **Proposed** |',
        '| [D3](decisions/d3-files-and-folders.md) | Files and folders | **Agreed** |',
      ),
    )

    assert.deepEqual(messages(), [])
  })

  test('reports a superseded decision that keeps its file, links another last text or names no decision in force', () => {
    write(
      'docs/decisions.md',
      index(
        `| D1 | Files first | Superseded by [D2](decisions/d2-typed-expressions.md), ${lastText('d2-typed-expressions.md')} |`,
        `| D2 | Typed expressions | Superseded by [D1](decisions/d1-files-first.md), ${lastText('d2-typed-expressions.md')} |`,
      ),
    )

    assert.deepEqual(messages().sort(), [
      "docs/decisions.md:5: D1 is superseded by D2, which is not a decision in force",
      "docs/decisions.md:5: D1 is superseded, so delete docs/decisions/d1-files-first.md",
      "docs/decisions.md:5: D1's last text links the file of D2",
      "docs/decisions.md:6: D2 is superseded by D1, which is not a decision in force",
      "docs/decisions.md:6: D2 is superseded, so delete docs/decisions/d2-typed-expressions.md",
    ])
  })
})
