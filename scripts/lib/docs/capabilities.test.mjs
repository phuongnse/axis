import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'
import { checkCapabilities } from './capabilities.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkCapabilities)
const page = 'docs/capabilities.md'

const writePage = (rows) => {
  const text = ['# Capabilities', '', '## Processes', '', '| Capability | Where |', '| --- | --- |', ...rows, ''].join('\n')
  write(page, text)
  return text
}
const lineOf = (text, needle) => text.split('\n').findIndex((line) => line.includes(needle)) + 1

describe('capability map', () => {
  beforeEach(() => {
    write('docs/roadmap.md', ['| | Milestone | Status | Done when |', '| --- | --- | --- | --- |', '| **M38** | Paths | Planned | Text. |', ''].join('\n'))
  })

  test('passes a row that links its rule and one planned for a listed milestone', () => {
    writePage(['| Choose one path | [Steps](reference/processes.md#steps) |', '| Run two paths at once | _(planned for M38)_ |'])

    assert.deepEqual(messages(), [])
  })

  test('passes when there is no capability map', () => {
    assert.deepEqual(messages(), [])
  })

  test('reports a row with neither a link nor a planned marker', () => {
    const text = writePage(['| Run two paths at once | Missing |'])

    assert.deepEqual(messages(), [
      `${page}:${lineOf(text, 'Run two')}: 'Run two paths at once' has no home: link the doc section that holds its rule, or put the planned marker of the milestone that builds it alone in the cell`,
    ])
  })

  test('reports a planned marker that shares its cell with other words', () => {
    const text = writePage(['| Run two paths at once | Later, _(planned for M38)_ |'])

    assert.equal(messages().length, 1)
    assert.match(messages()[0], new RegExp(`^${page}:${lineOf(text, 'Run two')}: 'Run two paths at once' has no home`))
  })

  test('reports a milestone the roadmap does not list', () => {
    const text = writePage(['| Run two paths at once | _(planned for M99)_ |'])

    assert.deepEqual(messages(), [`${page}:${lineOf(text, 'Run two')}: 'Run two paths at once' is planned for M99, which the roadmap does not list`])
  })

  test('reports a capability named twice, whatever its case', () => {
    const text = writePage(['| Run two paths at once | _(planned for M38)_ |', '| run two paths at once | _(planned for M38)_ |'])

    assert.deepEqual(messages(), [`${page}:${lineOf(text, '| run two')}: 'run two paths at once' has a row already, on line ${lineOf(text, '| Run two')}`])
  })
})
