import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { anchorsOf, brokenLinks } from './check-issue-links.mjs'

let root
const base = 'https://github.com/phuongnse/axis/blob/main'

beforeEach(() => {
  root = mkdtempSync(path.join(tmpdir(), 'check-issue-links-'))
  mkdirSync(path.join(root, 'docs'))
  writeFileSync(path.join(root, 'docs/guide.md'), '# Guide\n\n## Out of scope\n\n## Rules\n\n## Rules\n\n```\n## Not a heading\n```\n')
})

afterEach(() => rmSync(root, { recursive: true, force: true }))

describe('anchorsOf', () => {
  test('numbers repeated headings and skips fenced lines', () => {
    assert.deepEqual([...anchorsOf('# A\n## B\n## B\n```\n## C\n```\n')], ['a', 'b', 'b-1'])
  })
})

describe('brokenLinks', () => {
  test('passes links to files and anchors that exist, and line anchors', () => {
    const issues = [{ number: 1, body: `[a](${base}/docs/guide.md#out-of-scope) [b](${base}/docs/guide.md#rules-1) [c](${base}/docs/guide.md#L3) [d](${base}/docs/guide.md)` }]
    assert.deepEqual(brokenLinks(issues, root), [])
  })

  test('reports a missing file and a missing anchor, with the issue', () => {
    const issues = [
      { number: 7, body: `[gone](${base}/docs/decisions/d1-gone.md)` },
      { number: 8, body: `[old](${base}/docs/guide.md#out-of-scope-for-now) and [fenced](${base}/docs/guide.md#not-a-heading)` },
    ]
    assert.deepEqual(brokenLinks(issues, root), [
      { issue: 7, link: 'docs/decisions/d1-gone.md' },
      { issue: 8, link: 'docs/guide.md#out-of-scope-for-now' },
      { issue: 8, link: 'docs/guide.md#not-a-heading' },
    ])
  })

  test('leaves out an issue that an open issue blocks', () => {
    const body = `[later](${base}/docs/guide.md#added-later)`
    const issues = [{ number: 9, body, blockedBy: [{ number: 3, state: 'CLOSED' }, { number: 4, state: 'OPEN' }] }]
    assert.deepEqual(brokenLinks(issues, root), [])
  })

  test('checks an issue whose blockers are all closed, or that has none', () => {
    const body = `[later](${base}/docs/guide.md#added-later)`
    const expected = [{ issue: 9, link: 'docs/guide.md#added-later' }]
    assert.deepEqual(brokenLinks([{ number: 9, body, blockedBy: [{ number: 3, state: 'CLOSED' }] }], root), expected)
    assert.deepEqual(brokenLinks([{ number: 9, body, blockedBy: [] }], root), expected)
  })

  test('ignores links to other repositories and to other branches', () => {
    const issues = [{ number: 2, body: 'https://github.com/phuongnse/nexkit/blob/main/docs/x.md https://github.com/phuongnse/axis/blob/v1/docs/x.md' }]
    assert.deepEqual(brokenLinks(issues, root), [])
  })
})
