import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'
import { checkDocsMap } from './docs-map.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkDocsMap)

describe('docs map', () => {
  beforeEach(() => {
    write('docs/README.md', '# Documentation\n\n| [Decisions](decisions.md) | [Guide](reference/guide.md) |\n')
    write('docs/reference/guide.md', '# Guide\n')
    write('docs/decisions/d1-files-first.md', '# D1. Files first\n\nStatus: **Agreed**\n\nText.\n')
    write('docs/decisions/d2-typed-expressions.md', '# D2. Typed expressions\n\nStatus: **Proposed**\n')
  })

  test('passes on matching docs', () => {
    assert.deepEqual(messages(), [])
  })

  test('reports a doc that the docs map does not list', () => {
    write('docs/reference/orphan.md', '# Orphan\n')

    assert.deepEqual(messages(), ['docs/README.md:1: does not list docs/reference/orphan.md'])
  })
})
