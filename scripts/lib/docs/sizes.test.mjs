import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'
import { checkSizes, maxDocWords, maxGrownDocWords, maxSkillWords } from './sizes.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = (options) => fixture.messages(checkSizes, options)

const doc = (n) => '# Guide\n' + 'word\n'.repeat(n - 1)

describe('sizes', () => {
  beforeEach(() => {
    write('docs/reference/guide.md', '# Guide\n')
  })

  test('reports a doc over 12,000 words once, with or without a base', () => {
    write('docs/reference/guide.md', doc(maxDocWords + 1))

    for (const options of [undefined, { baseWordCount: () => maxDocWords + 1 }]) {
      const found = messages(options)
      assert.equal(found.length, 1)
      assert.match(found[0], /^docs\/reference\/guide\.md:1: 12001 words, over the limit of 12000/)
      assert.match(found[0], /move the whole ## sections of one topic/)
      assert.match(found[0], /leaving each page at or under 8000 words/)
      assert.match(found[0], /do not shorten, rewrap or delete existing text to fit$/)
    }
  })

  test('passes a doc at exactly 12,000 words with no base', () => {
    write('docs/reference/guide.md', doc(maxDocWords))

    assert.deepEqual(messages(), [])
  })

  test('reports a doc that a change grows past 10,000 words', () => {
    write('docs/reference/guide.md', doc(10100))

    const found = messages({ baseWordCount: () => 9800 })
    assert.equal(found.length, 1)
    assert.match(found[0], /^docs\/reference\/guide\.md:1: 10100 words, up from 9800, over 10000 for a change that grows a doc/)
    assert.match(found[0], /move the whole ## sections of one topic/)
    assert.match(found[0], /do not shorten, rewrap or delete existing text to fit$/)
  })

  test('passes a doc that a change grows to exactly 10,000 words', () => {
    write('docs/reference/guide.md', doc(maxGrownDocWords))

    assert.deepEqual(messages({ baseWordCount: () => 9900 }), [])
  })

  test('reports a doc over 10,000 words that a change grows by one word', () => {
    write('docs/reference/guide.md', doc(maxGrownDocWords + 1))

    const found = messages({ baseWordCount: () => maxGrownDocWords })
    assert.equal(found.length, 1)
    assert.match(found[0], /^docs\/reference\/guide\.md:1: 10001 words, up from 10000/)
  })

  test('passes a doc over 10,000 words that a change does not grow', () => {
    write('docs/reference/guide.md', doc(11000))

    assert.deepEqual(messages({ baseWordCount: () => 11000 }), [])
  })

  test('passes a doc over 10,000 words that a change shrinks', () => {
    write('docs/reference/guide.md', doc(10400))

    assert.deepEqual(messages({ baseWordCount: () => 10600 }), [])
  })

  test('reports a doc over 10,000 words that is missing at the base', () => {
    write('docs/reference/guide.md', doc(maxGrownDocWords + 1))

    const found = messages({ baseWordCount: () => 0 })
    assert.equal(found.length, 1)
    assert.match(found[0], /^docs\/reference\/guide\.md:1: 10001 words, up from 0/)
  })

  test('passes a doc of thousands of short lines under both word limits, new at the base or not', () => {
    write('docs/reference/guide.md', '# Guide\n' + 'a b\n'.repeat(4000))

    assert.deepEqual(messages({ baseWordCount: () => 0 }), [])
    assert.deepEqual(messages(), [])
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
