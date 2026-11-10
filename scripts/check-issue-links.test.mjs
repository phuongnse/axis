import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, renameSync, rmSync, unlinkSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { afterEach, beforeEach, describe, test } from 'node:test'
import { anchorsOf, brokenLinks, relinkBody, relinkIssues, whereWent } from './check-issue-links.mjs'

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

  test('keeps the anchor of a link to a missing file', () => {
    const issues = [{ number: 7, body: `[gone](${base}/docs/old.md#rules)` }]
    assert.deepEqual(brokenLinks(issues, root), [{ issue: 7, link: 'docs/old.md#rules' }])
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

describe('relinkBody', () => {
  test('replaces only the link, and keeps every other byte, CRLF line endings and other links', () => {
    const body = `Read [the rules](${base}/docs/old.md#rules).\r\nSee [guide](${base}/docs/guide.md#rules) too.\r\n`
    assert.equal(
      relinkBody(body, 'docs/old.md#rules', 'docs/guide.md#rules-1'),
      `Read [the rules](${base}/docs/guide.md#rules-1).\r\nSee [guide](${base}/docs/guide.md#rules) too.\r\n`,
    )
  })

  test('drops the old anchor when the home has none', () => {
    assert.equal(relinkBody(`(${base}/docs/old.md#rules)`, 'docs/old.md#rules', 'docs/guide.md'), `(${base}/docs/guide.md)`)
  })
})

describe('relinkIssues', () => {
  const effects = (bodies) => {
    const edits = []
    const lines = []
    const view = (n) => bodies[n]
    const edit = (n, body) => edits.push({ n, body })
    const log = (line) => lines.push(line)
    return { edits, lines, options: { root, view, edit, log } }
  }

  test('edits an issue with its checked home and logs the change', () => {
    const { edits, lines, options } = effects({ 7: `x (${base}/docs/old.md#out-of-scope) y` })
    const links = [{ issue: 7, link: 'docs/old.md#out-of-scope', renamedTo: 'docs/guide.md' }]
    const homes = [{ issue: 7, link: 'docs/old.md#out-of-scope', home: 'docs/guide.md#out-of-scope' }]
    assert.deepEqual(relinkIssues(links, homes, options), { fixed: 1, skipped: 0 })
    assert.deepEqual(edits, [{ n: 7, body: `x (${base}/docs/guide.md#out-of-scope) y` }])
    assert.deepEqual(lines, ['Relinked #7: docs/old.md#out-of-scope -> docs/guide.md#out-of-scope'])
  })

  test('leaves the issue when the home file or heading is missing on main, and prints the link', () => {
    const { edits, lines, options } = effects({ 7: `(${base}/docs/old.md)`, 8: `(${base}/docs/old.md#rules)` })
    const links = [
      { issue: 7, link: 'docs/old.md' },
      { issue: 8, link: 'docs/old.md#rules' },
    ]
    const homes = [
      { issue: 7, link: 'docs/old.md', home: 'docs/missing.md' },
      { issue: 8, link: 'docs/old.md#rules', home: 'docs/guide.md#no-such-heading' },
    ]
    assert.deepEqual(relinkIssues(links, homes, options), { fixed: 0, skipped: 2 })
    assert.deepEqual(edits, [])
    assert.deepEqual(lines, [
      'Not relinked: #7 docs/old.md: home not on main',
      'Not relinked: #8 docs/old.md#rules: home not on main',
    ])
  })

  test('refuses a home outside the repository', () => {
    const { edits, lines, options } = effects({ 7: `(${base}/docs/old.md)` })
    const homes = [{ issue: 7, link: 'docs/old.md', home: '../outside.md' }]
    relinkIssues([{ issue: 7, link: 'docs/old.md' }], homes, options)
    assert.deepEqual(edits, [])
    assert.deepEqual(lines, ['Not relinked: #7 docs/old.md: home not on main'])
  })

  test('leaves the issue when the answer is no home, and prints the link', () => {
    const { edits, lines, options } = effects({ 7: `(${base}/docs/old.md)` })
    const homes = [{ issue: 7, link: 'docs/old.md', home: null }]
    assert.deepEqual(relinkIssues([{ issue: 7, link: 'docs/old.md' }], homes, options), { fixed: 0, skipped: 1 })
    assert.deepEqual(edits, [])
    assert.deepEqual(lines, ['Not relinked: #7 docs/old.md: no home'])
  })

  test('ignores an answer for an issue or a link that the listing did not report', () => {
    const bodies = { 7: `(${base}/docs/old.md) (${base}/docs/other.md)`, 9: `(${base}/docs/old.md)` }
    const { edits, lines, options } = effects(bodies)
    const homes = [
      { issue: 9, link: 'docs/old.md', home: 'docs/guide.md' },
      { issue: 7, link: 'docs/other.md', home: 'docs/guide.md' },
    ]
    assert.deepEqual(relinkIssues([{ issue: 7, link: 'docs/old.md' }], homes, options), { fixed: 0, skipped: 1 })
    assert.deepEqual(edits, [])
    assert.deepEqual(lines, ['Not relinked: #7 docs/old.md: no home'])
  })

  test('prints a listed link that has no answer, and every link when there are no answers', () => {
    const { edits, lines, options } = effects({ 7: `(${base}/docs/old.md) (${base}/docs/other.md)` })
    const links = [
      { issue: 7, link: 'docs/old.md' },
      { issue: 7, link: 'docs/other.md' },
    ]
    relinkIssues(links, [{ issue: 7, link: 'docs/old.md', home: 'docs/guide.md' }], options)
    assert.deepEqual(lines, ['Not relinked: #7 docs/other.md: no home', 'Relinked #7: docs/old.md -> docs/guide.md'])
    assert.equal(edits.length, 1)
    lines.length = 0
    assert.deepEqual(relinkIssues(links, [], options), { fixed: 0, skipped: 2 })
    assert.deepEqual(lines, ['Not relinked: #7 docs/old.md: no home', 'Not relinked: #7 docs/other.md: no home'])
  })

  test('leaves a link that is gone from the issue since the listing, and prints it', () => {
    const { edits, lines, options } = effects({ 7: 'edited since' })
    const homes = [{ issue: 7, link: 'docs/old.md', home: 'docs/guide.md' }]
    relinkIssues([{ issue: 7, link: 'docs/old.md' }], homes, options)
    assert.deepEqual(edits, [])
    assert.deepEqual(lines, ['Not relinked: #7 docs/old.md: link no longer in the issue'])
  })
})

describe('whereWent', () => {
  const commit = (message) => {
    execFileSync('git', ['add', '-A'], { cwd: root })
    execFileSync('git', ['-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', message], { cwd: root })
  }

  beforeEach(() => {
    execFileSync('git', ['init', '-q'], { cwd: root })
    writeFileSync(path.join(root, 'docs/old.md'), '# Old\n\n## Rules\n\nThe rules of the old page, long enough to be found again.\n')
    writeFileSync(path.join(root, 'docs/gone.md'), '# Gone\n')
    commit('Add the pages')
    renameSync(path.join(root, 'docs/old.md'), path.join(root, 'docs/new.md'))
    commit('Rename the old page')
    unlinkSync(path.join(root, 'docs/gone.md'))
    commit('Delete the gone page')
  })

  test('reports where a renamed file went, with its headings for a link with a heading', () => {
    const facts = whereWent('docs/old.md#rules', root)
    assert.equal(facts.renamedTo, 'docs/new.md')
    assert.match(facts.deletedIn, / Rename the old page$/)
    assert.deepEqual(facts.headings, ['old', 'rules'])
  })

  test('reports the commit that deleted a file', () => {
    const facts = whereWent('docs/gone.md', root)
    assert.match(facts.deletedIn, /^[0-9a-f]{40} Delete the gone page$/)
    assert.equal(facts.renamedTo, undefined)
  })

  test('relinks a link to a section of a renamed page, from the listing through to the edit', () => {
    const body = `See [the rules](${base}/docs/old.md#rules).`
    const links = brokenLinks([{ number: 7, body }], root).map((l) => ({ ...l, ...whereWent(l.link, root) }))
    assert.deepEqual(links, [
      { issue: 7, link: 'docs/old.md#rules', deletedIn: links[0].deletedIn, renamedTo: 'docs/new.md', headings: ['old', 'rules'] },
    ])
    const edits = []
    const lines = []
    const options = { root, view: () => body, edit: (n, text) => edits.push({ n, text }), log: (line) => lines.push(line) }
    relinkIssues(links, [{ issue: 7, link: 'docs/old.md#rules', home: 'docs/new.md#rules' }], options)
    assert.deepEqual(edits, [{ n: 7, text: `See [the rules](${base}/docs/new.md#rules).` }])
    assert.deepEqual(lines, ['Relinked #7: docs/old.md#rules -> docs/new.md#rules'])
  })

  test('reports the headings a file has now when its heading is missing', () => {
    const facts = whereWent('docs/guide.md#gone-heading', root)
    assert.deepEqual(facts, { headings: ['guide', 'out-of-scope', 'rules', 'rules-1'] })
  })
})
