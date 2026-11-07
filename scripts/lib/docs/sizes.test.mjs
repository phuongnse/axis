import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'
import { checkSizes, maxDocLines, maxGrownDocLines, maxSkillWords } from './sizes.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = (options) => fixture.messages(checkSizes, options)

const doc = (n) => '# Guide\n' + 'line\n'.repeat(n - 1)

describe('sizes', () => {
  beforeEach(() => {
    write('docs/reference/guide.md', '# Guide\n')
  })

  test('reports a doc over its size limit', () => {
    write('docs/reference/guide.md', '# Guide\n' + 'line\n'.repeat(maxDocLines))

    const found = messages()
    assert.equal(found.length, 1)
    assert.match(found[0], /^docs\/reference\/guide\.md:1001: 1001 lines, over the limit of 1000/)
  })

  test('reports a doc over 1,000 lines once, with or without a base', () => {
    write('docs/reference/guide.md', doc(maxDocLines + 1))

    for (const options of [undefined, { baseLineCount: () => maxDocLines + 1 }]) {
      const found = messages(options)
      assert.equal(found.length, 1)
      assert.match(found[0], /^docs\/reference\/guide\.md:1001: 1001 lines, over the limit of 1000/)
    }
  })

  test('reports a doc that a change grows past 900 lines', () => {
    write('docs/reference/guide.md', doc(910))

    const found = messages({ baseLineCount: () => 880 })
    assert.equal(found.length, 1)
    assert.match(found[0], /^docs\/reference\/guide\.md:901: 910 lines, up from 880.*move whole ## sections/)
  })

  test('passes a doc over 900 lines that a change does not grow', () => {
    write('docs/reference/guide.md', doc(950))

    assert.deepEqual(messages({ baseLineCount: () => 950 }), [])
  })

  test('passes a doc over 900 lines that a change shrinks', () => {
    write('docs/reference/guide.md', doc(940))

    assert.deepEqual(messages({ baseLineCount: () => 960 }), [])
  })

  test('reports a doc over 900 lines that is missing at the base', () => {
    write('docs/reference/guide.md', doc(maxGrownDocLines + 1))

    const found = messages({ baseLineCount: () => 0 })
    assert.equal(found.length, 1)
    assert.match(found[0], /^docs\/reference\/guide\.md:901: 901 lines, up from 0/)
  })

  test('passes on a skill at its word limit', () => {
    write('.claude/skills/runbook/SKILL.md', 'word\n'.repeat(maxSkillWords))

    assert.deepEqual(messages(), [])
  })

  test('reports a skill over its word limit', () => {
    write('.claude/skills/runbook/SKILL.md', 'word\n'.repeat(maxSkillWords + 1))

    assert.deepEqual(messages(), [
      '.claude/skills/runbook/SKILL.md:1: 1001 words, over the limit of 1000: link the rules in docs/ and move a workflow few runs need to a file of its own',
    ])
  })

  test('ignores the size of a workflow file beside a skill', () => {
    write('.claude/skills/runbook/SKILL.md', 'A short skill.\n')
    write('.claude/skills/runbook/workflow.md', 'word\n'.repeat(2 * maxSkillWords))

    assert.deepEqual(messages(), [])
  })
})
