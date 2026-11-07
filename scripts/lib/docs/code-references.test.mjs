import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'
import { checkCodeReferences } from './code-references.mjs'
import { useTempRoot } from './testing.mjs'

const fixture = useTempRoot()
const { write } = fixture
const messages = () => fixture.messages(checkCodeReferences).sort()

const notInForce = (number) =>
  `D${number} is not a decision in force in docs/decisions.md: cite the decision in force, or link the reference section that holds the rule`

describe('code references', () => {
  beforeEach(() => {
    write(
      'docs/decisions.md',
      [
        '# Decisions',
        '',
        '| Decision | Title | Status |',
        '| --- | --- | --- |',
        '| [D1](decisions/d1-files-first.md) | Files first | **Agreed** |',
        '| D2 | Typed expressions | Superseded by [D3](decisions/d3-typed-expressions-again.md), [last text](https://github.com/phuongnse/axis/blob/0123456789abcdef0123456789abcdef01234567/docs/decisions/d2-typed-expressions.md) |',
        '| [D3](decisions/d3-typed-expressions-again.md) | Typed expressions again | **Agreed** |',
      ].join('\n') + '\n',
    )
    write('docs/reference/guide.md', '# Guide\n\n## Rules\n\nText.\n')
  })

  test('reports a citation of a decision that is not in force, in code and in docs prose', () => {
    write('src/Rules.cs', '// Kept by D2.\nvar holds = 1; // D1 holds.\n')
    write('web/src/view.ts', '// Sets D9 in the list.\n')
    write('docs/reference/guide.md', '# Guide\n\n## Rules\n\nSee D2 for the rule.\n')

    assert.deepEqual(messages(), [
      `docs/reference/guide.md:5: ${notInForce(2)}`,
      `src/Rules.cs:1: ${notInForce(2)}`,
      `web/src/view.ts:1: ${notInForce(9)}`,
    ])
  })

  test('reports a docs reference to a missing file or section, and a milestone in code', () => {
    write('src/Links.cs', '// See docs/reference/guide.md#missing and docs/reference/absent.md#rules.\n// Milestone M12 is done.\n')

    assert.deepEqual(messages(), [
      'src/Links.cs:1: docs/reference/absent.md does not exist',
      'src/Links.cs:1: docs/reference/guide.md#missing: no such section',
      'src/Links.cs:2: M12 names a milestone: say what the code means instead',
    ])
  })

  test('passes a section that exists, a decision in force and a format specifier', () => {
    write('src/Valid.cs', '// See docs/reference/guide.md#rules, which says D1 holds.\nvar text = $"{k:D9}"; var label = "D9";\n')

    assert.deepEqual(messages(), [])
  })

  test('passes a decision file that cites the decision it superseded, and reports one that cites another', () => {
    write('docs/decisions/d1-files-first.md', '# D1. Files first\n\nStatus: **Agreed**\n\nReplaces D2.\n')
    write('docs/decisions/d3-typed-expressions-again.md', '# D3. Typed expressions again\n\nStatus: **Agreed**\n\nReplaces D2.\n')

    assert.deepEqual(messages(), ['docs/decisions/d1-files-first.md:5: ' + notInForce(2)])
  })

  test('skips the checks own tests, fixtures, build output and binary files', () => {
    write('scripts/check-thing.test.mjs', '// D2 and M12\n')
    write('scripts/fixtures/sample.txt', 'D2 M12\n')
    write('src/bin/Stale.cs', '// D2\n')
    write('src/Binary.dat', 'D2\0 M12\n')
    write('scripts/check-other.mjs', '// D2\n')

    assert.deepEqual(messages(), ['scripts/check-other.mjs:1: ' + notInForce(2)])
  })
})
